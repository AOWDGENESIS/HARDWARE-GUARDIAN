#!/usr/bin/env python3
"""Localisation key checker (heuristic, like tools/check-contracts.py).

Extracts every localisation key that the sources reference and compares it with the keys defined
in the resource files. Reports:

  * keys used in code but missing in a language file  (would show as a visible marker in the UI)
  * keys defined in one language but not in the other (half translated)
  * placeholders that do not match between languages ({0} dropped or renumbered by a translation,
    or an unescaped brace that string.Format would read as a placeholder)
  * keys defined but never used                        (dead strings, reported as information)

The extractor is deliberately conservative: only string literals that look like a key
(UpperCamelCase segments joined by '_', optionally dotted) count. Sentences, file paths, WMI class
names and command lines do not match, so the report stays readable.

Usage:
    python3 tools/check-localization.py            # compare code against the resource files
    python3 tools/check-localization.py --list     # also list every string
    python3 tools/check-localization.py --emit-en FILE   # template for missing keys
"""

from __future__ import annotations

import argparse
import contextlib
import fnmatch
import io
import shutil
import tempfile
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src"
RESOURCE_DIRS = [p for p in SRC.rglob("Resources") if p.is_dir()]

KEY_PATTERN = re.compile(r'"([A-Z][A-Za-z0-9]*(?:_[A-Za-z0-9]+)+(?:\.[A-Za-z0-9_]+)*)"')

# Members whose value is a localisation key by contract. They are recognised as key positions even
# when a segment is written in capitals (Vendor_NVIDIA), which the generic pattern above rejects.
# ModuleKey is deliberately not in this list: module identifiers are not localised.
KEY_POSITION_PATTERN = re.compile(
    r"\b(?:DisplayName|Display|Name|Title|Step|Action|Reason|Message|Summary|SkipReason|"
    r"MeasurementPoint|Description|Category|Status|Section|Label|Tooltip|Unit)Key"
    r"\s*[:=]\s*\"([^\"]+)\""
)
OF_PATTERN = re.compile(r"LocalizedText\.Of\(\s*\"([^\"]+)\"")

# Literals that match the pattern but are not localisation keys:
# WMI/CIM class names, registry roots and the machine readable block reason codes
# (which are ALL_CAPS by design, while every real key is UpperCamelCase_Segments).
EXCLUDED_PREFIXES = (
    "SIMULATION", "HKEY", "HKLM", "HKCU", "SOFTWARE", "SYSTEM",
    "Win32", "MSFT", "MSAcpi", "CIM", "StandardCimv2", "ROOT",
)


def looks_like_key(key: str) -> bool:
    """UpperCamelCase segments: at least one lower case letter in every segment."""
    if key.startswith(EXCLUDED_PREFIXES):
        return False
    for segment in key.split("."):
        for part in segment.split("_"):
            if not part or not any(c.islower() for c in part):
                return False
    return True


# WMI property names look like keys ("IsEnabled_InitialValue") but are not: they are the property
# names that the firmware or the operating system publishes. They only ever appear as the argument
# of a WMI accessor, so those arguments are blanked before the key search runs.
WMI_PROPERTY_ARGUMENT = re.compile(
    r"\.(?:TryGetString|GetString|TryGetBool|TryGetUInt|TryGetULong|TryGetInt|TryGetDateTime|"
    r"GetUIntArray|GetStringArray)\(\s*\"[^\"]*\""
)

# Keys that are assembled from a prefix and a value at runtime ("Safety_" + item.SafetyClass).
# They cannot be found by a static search, so the prefix is collected and the matching keys are
# reported as dynamic instead of dead.
DYNAMIC_KEY_PATTERN = re.compile(r'"([A-Z][A-Za-z0-9]*(?:_[A-Za-z0-9]+)*_)\"\s*\+')


def blank_wmi_property_names(text: str) -> str:
    """Replaces `GetX("Prop_Name")` arguments so they cannot be mistaken for localisation keys."""
    return WMI_PROPERTY_ARGUMENT.sub(
        lambda match: match.group(0).split("(")[0] + "(", text
    )


def dynamic_key_prefixes(text: str) -> set[str]:
    """Collects prefixes of keys that are concatenated at runtime."""
    return {match.group(1) for match in DYNAMIC_KEY_PATTERN.finditer(text)}


def strip_comments(text: str) -> str:
    """Removes comments, keeps string literals (keys live in strings)."""
    out = []
    i = 0
    length = len(text)
    while i < length:
        two = text[i:i + 2]
        if two == "//":
            j = text.find("\n", i)
            i = length if j < 0 else j
            continue
        if two == "/*":
            j = text.find("*/", i)
            i = length if j < 0 else j + 2
            continue
        if text[i] == '"':
            j = i + 1
            while j < length:
                if text[j] == "\\":
                    j += 2
                    continue
                if text[j] == '"':
                    break
                j += 1
            out.append(text[i:j + 1])
            i = j + 1
            continue
        out.append(text[i])
        i += 1
    return "".join(out)


# XAML resolves a key through the markup extension: Text="{services:Loc Dashboard_Title}"
XAML_LOC = re.compile(r"\{services:Loc\s+([A-Za-z0-9_]+)\s*\}")


def used_keys() -> tuple[dict[str, set[str]], set[str]]:
    """Returns the keys found in the sources and the prefixes of dynamically built keys."""
    result: dict[str, set[str]] = {}
    dynamic: set[str] = set()
    files = sorted(SRC.rglob("*.cs"))
    if (ROOT / "tests").is_dir():
        files += sorted((ROOT / "tests").rglob("*.cs"))
    for path in files:
        text = strip_comments(path.read_text(encoding="utf-8", errors="replace"))
        dynamic |= dynamic_key_prefixes(text)
        text = blank_wmi_property_names(text)
        for match in KEY_PATTERN.finditer(text):
            key = match.group(1)
            if not looks_like_key(key):
                continue
            result.setdefault(key, set()).add(str(path.relative_to(ROOT)))
        for pattern in (KEY_POSITION_PATTERN, OF_PATTERN):
            for match in pattern.finditer(text):
                key = match.group(1)
                if key.startswith(EXCLUDED_PREFIXES):
                    continue
                result.setdefault(key, set()).add(str(path.relative_to(ROOT)))

    for path in sorted(SRC.rglob("*.xaml")):
        text = path.read_text(encoding="utf-8", errors="replace")
        for match in XAML_LOC.finditer(text):
            result.setdefault(match.group(1), set()).add(str(path.relative_to(ROOT)))
    return result, dynamic


# A placeholder of the catalogue format: {0}, {1:...}. Braces are literal only when doubled ({{).
PLACEHOLDER = re.compile(r"\{(?P<index>\d+)(?::[^}]*)?\}")
# Any single brace, for the report of a brace that string.Format would read as a placeholder.
SINGLE_BRACE = re.compile(r"(?<!\{)\{(?!\{)|(?<!\})\}(?!\})")


def placeholders(text: str) -> set[int]:
    """The placeholder indices of a template ({} and {name} are not part of this format)."""
    return {int(match.group("index")) for match in PLACEHOLDER.finditer(text)}


def unescaped_brace(text: str) -> bool:
    """True when a single (unescaped) brace stands in the text - string.Format would fail on it."""
    stripped = PLACEHOLDER.sub("", text)
    return bool(SINGLE_BRACE.search(stripped.replace("{{", "").replace("}}", "")))


def check_placeholders(resources: dict[str, dict[str, str]], languages: list[str]) -> int:
    """Compares the placeholders of every key across the languages. Returns the exit code."""
    exit_code = 0
    first = languages[0]
    mismatched: list[str] = []
    for language in languages[1:]:
        for key, text in resources[language].items():
            reference = resources[first].get(key)
            if reference is None:
                continue  # a key that exists in one language only is reported by the symmetry check
            expected, found = placeholders(reference), placeholders(text)
            if expected != found:
                mismatched.append(
                    f"  - [{language}] {key}: placeholders {sorted(expected)} in {first}, {sorted(found)} here"
                )
    if mismatched:
        exit_code = 1
        print(f"\n{len(mismatched)} key(s) whose placeholders differ between languages:")
        for line in mismatched[:40]:
            print(line)
        if len(mismatched) > 40:
            print(f"  ... and {len(mismatched) - 40} more")

    # A stray brace is wrong in every language, including the first one: string.Format throws on it.
    stray: list[str] = []
    for language in languages:
        for key, text in resources[language].items():
            if unescaped_brace(text):
                stray.append(f"  - [{language}] {key}: `{text}` carries a single brace")
    if stray:
        exit_code = 1
        print(f"\n{len(stray)} string(s) with a brace that string.Format would read as a placeholder:")
        for line in stray[:40]:
            print(line)
        if len(stray) > 40:
            print(f"  ... and {len(stray) - 40} more")
    return exit_code


EMBEDDED_RESOURCE = re.compile(
    r"<EmbeddedResource[^>]*Include\s*=\s*\"(?P<pattern>[^\"]+)\"", re.IGNORECASE)


def embedded_patterns(project: Path) -> list[str]:
    """The EmbeddedResource patterns of a project file, with forward slashes."""
    text = project.read_text(encoding="utf-8", errors="replace")
    return [match.group("pattern").replace("\\", "/") for match in EMBEDDED_RESOURCE.finditer(text)]


def check_embedding() -> int:
    """Every catalogue must be embedded by the project it sits in.

    A resource file that exists in the source tree but is not matched by an `EmbeddedResource` entry is
    not shipped: `LanguageCatalog` would not see it, the interface would not offer the language, and
    nothing in the build would complain. That is the `en.json`/`de.json` list problem in its original
    form - this check is what keeps the project file and the folder in step.
    """
    exit_code = 0
    problems: list[str] = []
    for directory in RESOURCE_DIRS:
        project = next((candidate for candidate in directory.parent.glob("*.csproj")), None)
        if project is None:
            problems.append(f"  - {directory}: no .csproj next to it, so nothing can be checked")
            continue
        patterns = embedded_patterns(project)
        for path in sorted(directory.glob("*.json")):
            relative = path.relative_to(project.parent).as_posix()
            matched = any(
                fnmatch.fnmatch(relative, pattern) or fnmatch.fnmatch(relative.lower(), pattern.lower())
                for pattern in patterns
            )
            if not matched:
                problems.append(
                    f"  - {relative}: not matched by any EmbeddedResource entry of {project.name} "
                    f"(patterns: {', '.join(patterns) or 'none'}) - this catalogue would not ship"
                )
    if problems:
        exit_code = 1
        print(f"\n{len(problems)} catalogue(s) that the build does not embed:")
        for line in problems:
            print(line)
    return exit_code


def load_resources() -> dict[str, dict[str, str]]:
    resources: dict[str, dict[str, str]] = {}
    for directory in RESOURCE_DIRS:
        for path in sorted(directory.glob("*.json")):
            try:
                data = json.loads(path.read_text(encoding="utf-8"))
            except json.JSONDecodeError as ex:
                raise SystemExit(f"{path}: invalid JSON ({ex})")
            if not isinstance(data, dict) or not isinstance(data.get("strings"), dict):
                raise SystemExit(f"{path}: expected an object with a 'strings' object")
            language = data.get("language") or path.stem
            resources.setdefault(language, {}).update(data["strings"])
    return resources


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--list", action="store_true", help="list every string")
    parser.add_argument("--emit-en", metavar="FILE", help="write a template with only the missing keys")
    parser.add_argument("--self-test", action="store_true",
                        help="prove that a translation with wrong placeholders is reported")
    args = parser.parse_args()

    if args.self_test:
        # Two languages, one key, one dropped placeholder and one stray brace: both have to be reported.
        probe = {
            "en": {"Format_OK": "Copied {0} of {1}", "Format_Clean": "Nothing to replace"},
            "xx": {"Format_OK": "Copied {1} of {2}", "Format_Clean": "Nothing to replace {"},
        }
        captured = io.StringIO()
        with contextlib.redirect_stdout(captured):
            code = check_placeholders(probe, ["en", "xx"])
        output = captured.getvalue()
        reported = "Format_OK" in output and "Format_Clean" in output
        print(output.strip())
        if code != 0 and reported:
            print("self-test ok: a dropped placeholder and a stray brace are both reported")
        else:
            print("self-test FAILED: the placeholder check stays silent")
            return 1

        # The embedding check: a project that does not match the catalogue has to be reported.
        probe_root = Path(tempfile.mkdtemp(prefix="wmc-l10n-selftest-"))
        try:
            project_dir = probe_root / "src" / "Probe"
            resources = project_dir / "Resources"
            resources.mkdir(parents=True)
            (resources / "en.json").write_text('{"language": "en", "strings": {}}', encoding="utf-8")
            (resources / "xy.json").write_text('{"language": "xy", "strings": {}}', encoding="utf-8")
            (project_dir / "Probe.csproj").write_text(
                '<Project><ItemGroup><EmbeddedResource Include="Resources\\en.json" /></ItemGroup></Project>',
                encoding="utf-8",
            )
            global RESOURCE_DIRS
            original_dirs = RESOURCE_DIRS
            RESOURCE_DIRS = [resources]
            captured = io.StringIO()
            with contextlib.redirect_stdout(captured):
                embedding_code = check_embedding()
            RESOURCE_DIRS = original_dirs
        finally:
            shutil.rmtree(probe_root, ignore_errors=True)
        print(captured.getvalue().strip())
        if embedding_code == 0 or "xy.json" not in captured.getvalue():
            print("self-test FAILED: a catalogue the project does not embed was accepted")
            return 1
        print("self-test ok: a catalogue the project does not embed is reported")
        return 0

    used, dynamic_prefixes = used_keys()
    resources = load_resources()

    if not resources:
        print("no resource files found under src/**/Resources")
        return 1

    languages = sorted(resources)
    print(f"{len(used)} key(s) referenced in code, resource file(s): {', '.join(languages)}")

    exit_code = 0
    for language in languages:
        missing = sorted(k for k in used if k not in resources[language])
        print(f"\n[{language}] {len(resources[language])} string(s) defined, {len(missing)} missing")
        for key in missing[:60]:
            print(f"  - {key}  (used in {', '.join(sorted(used[key])[:2])})")
        if len(missing) > 60:
            print(f"  ... and {len(missing) - 60} more")
        if missing:
            exit_code = 1

    placeholder_code = check_placeholders(resources, languages)
    if placeholder_code:
        exit_code = 1

    if check_embedding():
        exit_code = 1

    if len(languages) > 1:
        first, *rest = languages
        for other in rest:
            only_first = sorted(set(resources[first]) - set(resources[other]))
            only_other = sorted(set(resources[other]) - set(resources[first]))
            if only_first or only_other:
                exit_code = 1
                print(f"\n[{first}] vs [{other}] not symmetric:")
                for key in only_first[:40]:
                    print(f"  - only {first}: {key}")
                for key in only_other[:40]:
                    print(f"  - only {other}: {key}")

    unused = sorted(
        key for key in set().union(*resources.values()) - set(used)
        if not key.startswith(tuple(dynamic_prefixes))
    )
    dynamic_used = sorted(
        key for key in set().union(*resources.values()) - set(used)
        if key.startswith(tuple(dynamic_prefixes))
    )
    if dynamic_used:
        print(f"\n{len(dynamic_used)} key(s) reachable through a built prefix "
              f"({', '.join(sorted(dynamic_prefixes))}) - not dead:")
        for key in dynamic_used[:40]:
            print(f"  - {key}")
    if unused:
        print(f"\n{len(unused)} defined but not referenced statically (dead strings):")
        for key in unused[:40]:
            print(f"  - {key}")
        if len(unused) > 40:
            print(f"  ... and {len(unused) - 40} more")

    if args.list:
        for language in languages:
            print(f"\n===== {language} =====")
            for key in sorted(resources[language]):
                print(f"  {key} = {resources[language][key]}")

    if args.emit_en:
        target = Path(args.emit_en)
        missing = {k: "" for k in sorted(used) if k not in resources.get(languages[0], {})}
        target.write_text(json.dumps({"language": languages[0], "strings": missing}, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print(f"\ntemplate with {len(missing)} key(s) written to {target}")

    print("\nresult:", "clean" if exit_code == 0 else "findings above")
    return exit_code


if __name__ == "__main__":
    raise SystemExit(main())

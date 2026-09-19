#!/usr/bin/env python3
"""Localisation key checker (heuristic, like tools/check-contracts.py).

Extracts every localisation key that the sources reference and compares it with the keys defined
in the resource files. Reports:

  * keys used in code but missing in a language file  (would show as a visible marker in the UI)
  * keys defined in one language but not in the other (half translated)
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


def used_keys() -> dict[str, set[str]]:
    result: dict[str, set[str]] = {}
    files = sorted(SRC.rglob("*.cs"))
    if (ROOT / "tests").is_dir():
        files += sorted((ROOT / "tests").rglob("*.cs"))
    for path in files:
        text = strip_comments(path.read_text(encoding="utf-8", errors="replace"))
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
    return result


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
    args = parser.parse_args()

    used = used_keys()
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

    unused = sorted(set().union(*resources.values()) - set(used))
    if unused:
        print(f"\n{len(unused)} defined but not referenced statically (dynamic keys or dead strings):")
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

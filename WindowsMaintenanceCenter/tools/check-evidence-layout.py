#!/usr/bin/env python3
"""Checks the evidence directory against chapter 71 of the specification.

The specification fixes the layout:

    test-results/
    ├── unit/  integration/  safety/  security/  recovery/
    ├── installer/  localization/  offline/  regression/  release/

and every report in it has to carry `timestamp`, `version`, `build`, `environment`, `result`. An
evidence directory that has grown its own structure is a problem in a project whose whole release
verdict rests on evidence: a report in a folder nobody looks at is not a report, and a folder that is
empty because git does not keep empty folders is worse - it looks like "nothing to record" while it
means "nothing was recorded".

What this tool checks:

1. The ten folders of chapter 71 exist.
2. No folder invents a name outside that list. `ci/` is the single documented exception: it holds the
   raw logs of the Windows CI runs, which are build and test evidence rather than an acceptance area
   of their own (documented in `test-results/README.md`).
3. No evidence folder is empty. An area without evidence keeps a `.gitkeep` and a note; a folder with
   neither is reported, because it would silently disappear from the repository.
4. `test-results/README.md` names every one of the ten folders, so the layout and its documentation
   cannot drift apart.

Exit codes: 0 = clean, 1 = findings, 2 = the evidence directory itself is missing.

This is a layout check, not a truth check: it cannot tell whether a report is honest or whether a test
was really run. It can only tell whether a report could be found at all.
"""

from __future__ import annotations

import shutil
import sys
import tempfile
from fnmatch import fnmatch
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EVIDENCE = ROOT / "test-results"

# Chapter 71, in the order the specification lists them.
AREAS = (
    "unit",
    "integration",
    "safety",
    "security",
    "recovery",
    "installer",
    "localization",
    "offline",
    "regression",
    "release",
)

# Documented exceptions. `ci` holds the raw logs of the CI runs; it is evidence, but no acceptance
# area of its own. An exception only exists when the README names it.
EXTRAS = ("ci",)

README = "README.md"

# A probe file per area. The extension is chosen so that a rule aimed at build output (`*.trx`,
# `*.json`) hits it: that is exactly how the unit test evidence was about to become uncommittable.
PROBE = "probe.trx"

# Files that only exist to keep a folder in the repository. A folder holding nothing else is allowed,
# but only deliberately.
PLACEHOLDERS = (".gitkeep", ".gitignore", "README.md", "NOTE.md", "note.md")


def ignore_rules(repository_root: Path) -> list[tuple[Path, str, bool]]:
    """Collects the rules of every .gitignore as (folder, pattern, negated).

    The two mistakes this project made on 2026-09-23 were both gitignore rules: `release/` swallowed
    the evidence area of the same name, and `*.trx` would have kept every future test result file out
    of the repository. Both were invisible because the folders holding already-committed files look
    fine. This reader exists so the layout check can see them.

    It understands what the rules in this repository use: plain names, directory patterns with a
    trailing slash, and simple globs (`*.trx`). A rule it cannot interpret is returned as written and
    matched literally - a checker that silently ignores a rule it does not understand would be the
    same kind of blind spot that produced the two defects.
    """
    rules: list[tuple[Path, str, bool]] = []
    for ignore_file in sorted(repository_root.rglob(".gitignore")):
        if any(part in {".git", "bin", "obj", "node_modules", "__pycache__"} for part in ignore_file.parts):
            continue
        folder = ignore_file.parent
        for line in ignore_file.read_text(encoding="utf-8", errors="replace").splitlines():
            entry = line.strip()
            if not entry or entry.startswith("#"):
                continue
            negated = entry.startswith("!")
            pattern = entry[1:].strip() if negated else entry
            if pattern:
                rules.append((folder, pattern, negated))
    return rules


def _matches(relative: str, pattern: str, is_directory: bool) -> bool:
    """Does one rule apply to this path? Only the shapes this repository uses."""
    directory_only = pattern.endswith("/")
    cleaned = pattern.rstrip("/")
    if not cleaned:
        return False

    if directory_only and not is_directory:
        return False

    if "/" in cleaned:
        return fnmatch(relative, cleaned) or fnmatch(relative, cleaned + "/**")

    # No slash: the rule matches on every level, so any segment may carry the name.
    return any(fnmatch(segment, cleaned) for segment in relative.split("/"))


def is_ignored(path: Path, rules: list[tuple[Path, str, bool]]) -> bool:
    """True when git would ignore this path.

    Two steps, because git does two: a file below an excluded **directory** stays excluded no matter
    how many `!` rules name the file - "it is not possible to re-include a file if a parent directory
    of that file is excluded". That is not a detail: the repository lost the evidence area `release/`
    exactly this way, and a checker that only looked at the file would have called it fine.
    """
    for folder, _, _ in rules:
        try:
            relative = path.relative_to(folder).as_posix()
        except ValueError:
            continue

        parts = relative.split("/")

        # 1. Would one of the parent directories be excluded?
        for depth in range(1, len(parts)):
            directory = "/".join(parts[:depth])
            ignored = False
            for rule_folder, pattern, negated in rules:
                if rule_folder != folder:
                    continue
                if _matches(directory, pattern, is_directory=True):
                    ignored = not negated
            if ignored:
                return True

        # 2. Would the file itself be excluded?
        ignored = False
        for rule_folder, pattern, negated in rules:
            if rule_folder != folder:
                continue
            if _matches(relative, pattern, is_directory=False):
                ignored = not negated
        return ignored

    return False


def inspect(root: Path, repository_root: Path | None = None) -> list[str]:
    """Returns every layout finding under `root`. An empty list means the layout is as specified."""
    findings: list[str] = []

    if not root.is_dir():
        return [f"{root} does not exist - there is no evidence directory at all"]

    allowed = set(AREAS) | set(EXTRAS)

    for area in AREAS:
        if not (root / area).is_dir():
            findings.append(f"the area '{area}' of chapter 71 is missing under {root.name}/")

    for entry in sorted(root.iterdir()):
        if not entry.is_dir():
            continue

        if entry.name not in allowed:
            findings.append(
                f"'{entry.name}/' is not one of the ten areas of chapter 71 and not a documented extra "
                f"({', '.join(EXTRAS)}) - evidence in an invented folder is evidence nobody looks for")
            continue

        # An area folder may be empty, but then it has to say so (otherwise git drops it and the area
        # disappears from the repository without anybody noticing).
        own_files = [item for item in entry.iterdir() if item.is_file()]
        subfolders = [item for item in entry.iterdir() if item.is_dir()]

        if not own_files and not subfolders:
            findings.append(
                f"'{entry.name}/' is empty and has no placeholder - add a .gitkeep or a note, or the "
                f"folder disappears from the repository")
            continue

        if not subfolders:
            # Only placeholder files: the area has no evidence (allowed, but visible).
            if all(item.name in PLACEHOLDERS for item in own_files):
                continue

        for run in subfolders:
            if not any(run.iterdir()):
                findings.append(f"'{entry.name}/{run.name}/' is empty - a run folder without content proves nothing")

    # Would a fresh evidence file in each area be committable? A rule that hides it means the area
    # can only ever hold what was added before that rule existed.
    if repository_root is not None:
        rules = ignore_rules(repository_root)
        for area in AREAS:
            probe = root / area / PROBE
            if is_ignored(probe, rules):
                findings.append(
                    f"'{area}/{PROBE}' would be ignored by .gitignore - evidence written into "
                    f"'{area}/' could never be committed, so the area would look empty on every clone")

    readme = root / README
    if not readme.is_file():
        findings.append(f"{root.name}/{README} is missing - the layout has no explanation")
    else:
        text = readme.read_text(encoding="utf-8", errors="replace")
        for area in AREAS:
            if f"`{area}/`" not in text and f"`{area}`" not in text and f"{area}/" not in text:
                findings.append(f"{root.name}/{README} does not name the area '{area}' - documentation and layout drifted apart")

    return findings


def self_test() -> int:
    """Builds a broken tree and requires the tool to report it, then a valid one that must stay silent."""
    root = Path(tempfile.mkdtemp(prefix="wmc-evidence-selftest-"))
    try:
        broken = root / "broken"
        for area in AREAS:
            (broken / area).mkdir(parents=True)
            (broken / area / ".gitkeep").write_text("", encoding="utf-8")

        # 1. an area folder disappears, 2. a folder invents a name, 3. a run folder is empty,
        # 4. the README forgets an area.
        shutil.rmtree(broken / "recovery")
        (broken / "misc").mkdir()
        (broken / "unit" / "20260101T000000Z").mkdir()
        (broken / README).write_text(" ".join(f"`{area}/`" for area in AREAS if area != "offline"), encoding="utf-8")

        findings = inspect(broken)
        expected = ("recovery", "misc", "20260101T000000Z", "offline")
        missing = [needle for needle in expected if not any(needle in finding for finding in findings)]
        if missing:
            print(f"self-test FAILED: these deliberate defects were not reported: {missing}")
            return 1

        clean = root / "clean"
        for area in AREAS:
            (clean / area).mkdir(parents=True)
            (clean / area / ".gitkeep").write_text("", encoding="utf-8")
        (clean / "ci" / "run-1").mkdir(parents=True)
        (clean / "ci" / "run-1" / "build.log").write_text("ok", encoding="utf-8")
        (clean / README).write_text(" ".join(f"`{area}/`" for area in AREAS) + " `ci/`", encoding="utf-8")

        # 5. an area that a .gitignore rule would hide
        (root / "ignored").mkdir()
        (root / "ignored" / ".gitignore").write_text("release/\n", encoding="utf-8")
        broken_ignored = inspect(root / "ignored", root / "ignored")
        if not any("would be ignored by .gitignore" in finding for finding in broken_ignored):
            print("self-test FAILED: an evidence area hidden by .gitignore was not reported")
            return 1

        clean_findings = inspect(clean)
        if clean_findings:
            print("self-test FAILED: the valid tree was reported:")
            for finding in clean_findings:
                print(f"  - {finding}")
            return 1

        print("self-test: every deliberate defect was reported, the valid tree stayed silent")
        return 0
    finally:
        shutil.rmtree(root, ignore_errors=True)


def main() -> int:
    if "--self-test" in sys.argv[1:]:
        return self_test()

    findings = inspect(EVIDENCE, ROOT)
    print(f"evidence directory: {EVIDENCE.relative_to(ROOT) if EVIDENCE.is_relative_to(ROOT) else EVIDENCE}")
    print(f"areas of chapter 71: {', '.join(AREAS)}")

    if findings:
        print(f"\n{len(findings)} finding(s):")
        for finding in findings:
            print(f"  - {finding}")
        return 1

    print("\nresult: clean - the ten areas exist, no folder invents a name, and the README names all of them")
    print("note: this proves the layout, not the truth of any report in it")
    return 0


if __name__ == "__main__":
    sys.exit(main())

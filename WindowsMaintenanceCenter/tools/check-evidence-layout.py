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

# Files that only exist to keep a folder in the repository. A folder holding nothing else is allowed,
# but only deliberately.
PLACEHOLDERS = (".gitkeep", ".gitignore", "README.md", "NOTE.md", "note.md")


def inspect(root: Path) -> list[str]:
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

    findings = inspect(EVIDENCE)
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

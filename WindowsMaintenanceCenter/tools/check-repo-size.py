#!/usr/bin/env python3
"""Keeps the repository (and with it every diff and every patch set) small enough to be reviewable.

Why this exists: this project files its own evidence - run records, TRX files, logs, reports - into the
branch, because the CI machine is the only place where the program is built, tested and installed, and
the log download of that machine is not reachable from where the project is developed. That is correct
and it has a cost: each run adds about a megabyte, and a diff that carries hundreds of megabytes is the
end of working with it at all. The promise "the diff never runs full" is therefore not a matter of
discipline but of a limit that is checked.

Limits and their reasoning:

  * single tracked file  <= 4 MB   - the TRX of the unit run is ~0.5 MB; anything clearly larger is a
                                     binary somebody added by accident (a published exe, an installer,
                                     a disk image), and those belong in a release, not in the tree
  * all tracked files    <= 64 MB  - a fresh clone, a diff and a patch set stay ordinary
  * tracked file count   <= 4000   - a diff with tens of thousands of files cannot be read
  * test-results/        <= 48 MB  - the evidence budget of chapter 71

What to do when a limit is reached: prune the *oldest* run records and unit proofs (the newest proof of
each test is the one that counts), and keep the summary files of the pruned runs in an index next to
them, so nothing that was proven becomes invisible. The CI record step already drops files above 2 MB
(run folder) and 8 MB (unit evidence) for the same reason.

Usage:
    python3 tools/check-repo-size.py            # check this repository
    python3 tools/check-repo-size.py --self-test # prove that the limits still fire
"""

from __future__ import annotations

import pathlib
import shutil
import subprocess
import sys
import tempfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
EVIDENCE = "WindowsMaintenanceCenter/test-results"

SINGLE_FILE_LIMIT = 4 * 1024 * 1024
TOTAL_LIMIT = 64 * 1024 * 1024
FILE_COUNT_LIMIT = 4000
EVIDENCE_LIMIT = 48 * 1024 * 1024


def tracked_files(root: pathlib.Path) -> list[pathlib.Path]:
    """Every file git knows about, as absolute paths. A non-repository is an error, not an empty list."""
    result = subprocess.run(
        ["git", "ls-files", "-z"],
        cwd=root,
        capture_output=True,
        check=True,
    )
    names = [name for name in result.stdout.decode("utf-8", errors="replace").split("\0") if name]
    return [root / name for name in names]


def measure(files: list[pathlib.Path]) -> tuple[int, list[tuple[int, str]]]:
    total = 0
    sized: list[tuple[int, str]] = []
    for path in files:
        try:
            size = path.stat().st_size
        except OSError:
            continue
        total += size
        sized.append((size, str(path)))
    return total, sized


def evaluate(files: list[pathlib.Path], root: pathlib.Path) -> list[str]:
    findings: list[str] = []
    total, sized = measure(files)

    for size, path in sorted(sized, reverse=True):
        if size > SINGLE_FILE_LIMIT:
            findings.append(
                f"{path}: {size / 1048576:.1f} MB is above the limit of {SINGLE_FILE_LIMIT / 1048576:.0f} MB "
                f"for a single tracked file - a binary belongs into a release, not into the tree"
            )

    if total > TOTAL_LIMIT:
        findings.append(
            f"the repository carries {total / 1048576:.1f} MB in {len(files)} tracked files, more than the "
            f"budget of {TOTAL_LIMIT / 1048576:.0f} MB - prune the oldest run records (see the header)"
        )

    if len(files) > FILE_COUNT_LIMIT:
        findings.append(
            f"{len(files)} tracked files are more than the budget of {FILE_COUNT_LIMIT} - a diff of this "
            f"size is not reviewable"
        )

    evidence_total = sum(size for size, path in sized if EVIDENCE in path.replace("\\", "/"))
    if evidence_total > EVIDENCE_LIMIT:
        findings.append(
            f"{EVIDENCE} carries {evidence_total / 1048576:.1f} MB, more than the evidence budget of "
            f"{EVIDENCE_LIMIT / 1048576:.0f} MB - prune the oldest records, keep the newest proof per test"
        )

    print(f"inspected {len(files)} tracked file(s), {total / 1048576:.1f} MB in total")
    print(f"  evidence {EVIDENCE}: {evidence_total / 1048576:.1f} MB of {EVIDENCE_LIMIT / 1048576:.0f} MB budget")
    heftiest = sorted(sized, reverse=True)[:5]
    if heftiest:
        print("  heaviest files:")
        for size, path in heftiest:
            shown = path.replace(str(root) + "/", "")
            print(f"    {size / 1024:8.0f} KB  {shown}")
    return findings


def self_test() -> int:
    """Proves that the limits fire: a synthetic tree with an oversized file has to be reported."""
    workspace = pathlib.Path(tempfile.mkdtemp(prefix="wmc-size-selftest-"))
    try:
        big = workspace / "big.bin"
        big.write_bytes(b"0" * (SINGLE_FILE_LIMIT + 1024))
        small = workspace / "small.txt"
        small.write_text("small", encoding="utf-8")

        findings = evaluate([big, small], workspace)
        if not any("above the limit" in finding for finding in findings):
            print("SELFTEST FAILED: an oversized file was not reported", file=sys.stderr)
            return 1

        findings = evaluate([small], workspace)
        if findings:
            print(f"SELFTEST FAILED: a small tree was reported: {findings}", file=sys.stderr)
            return 1

        print("self-test: an oversized file is reported, a small tree is not")
        return 0
    finally:
        shutil.rmtree(workspace, ignore_errors=True)


def main() -> int:
    if "--self-test" in sys.argv:
        return self_test()

    try:
        files = tracked_files(ROOT)
    except (subprocess.CalledProcessError, FileNotFoundError) as error:
        print(f"could not read the tracked files: {error}", file=sys.stderr)
        return 2

    findings = evaluate(files, ROOT)
    if findings:
        print(f"\n{len(findings)} finding(s):")
        for finding in findings:
            print("  -", finding)
        return 1

    print("the repository stays inside its size budget (diff, clone and patch set stay ordinary)")
    return 0


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""Parses every PowerShell script of this project and reports syntax problems.

Why this exists
---------------
The scripts in `scripts/` are the measuring instruments of the acceptance kit: build, release, tests
and the five VM scripts under `scripts/vm/`. When one of them is broken the failure appears in a CI run
minutes later - and a run that dies inside the evidence collection produces *no evidence at all*,
which is how a whole installation cycle ended without a report (2026-09-22). C# is checked before every
commit (`check-*`), the scripts were not checked at all.

What it is not
--------------
**This is a pre-filter, not the PowerShell parser.** `tools/psparse/pscheck.py` in the workspace root
documents, from measurements, that the tree-sitter grammar this uses both reports constructs that are
valid PowerShell and misses constructs the real parser rejects. The binding instance is the real parser
on a Windows machine:

    $e = $null; $t = $null
    [System.Management.Automation.Language.Parser]::ParseFile($file, [ref]$t, [ref]$e) | Out-Null

So a green run here means "nothing obvious", never "the script runs". The known false positives are
named below and filtered, with the source line in the output, so that a reader can always see what was
suppressed and why. Nothing is filtered silently.

Exit codes: 0 = nothing found, 1 = at least one finding, 3 = the grammar is not installed (which is
explicitly *not* a pass).
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# The script files of this project, by directory. Kept as a list so a new directory does not slide in
# unnoticed: an unchecked evidence script is exactly what this tool exists for.
SCRIPT_DIRS = ("scripts", "installer", "eng")

# Known blind spots of the tree-sitter PowerShell grammar, each one with the measurement that produced
# it. A finding whose text or line touches one of these is reported as "known noise", never as a
# finding - and the count of suppressed items is printed, because a filter that hides everything looks
# like a clean repository.
KNOWN_NOISE: tuple[tuple[str, str], ...] = (
    (r"^\s*#", "comment line: the grammar breaks on some comment texts (see pscheck.py)"),
    (r"^\d+(\.\d+)?(GB|MB|KB|TB|PB)$", "number with a unit suffix: 1GB is valid PowerShell"),
    (r"\d+(\.\d+)?(GB|MB|KB|TB|PB)\b", "number with a unit suffix in this line: 1GB is valid PowerShell"),
    (r"^(GB|MB|KB|TB|PB)$", "tail of a number with a unit suffix"),
    (r"switch\s*\(", "switch statement with several cases on one line"),
    (r"2>\$null", "redirection inside a command expression: `& git ... 2>$null` is valid"),
    (r'--pretty=format:', "format string handed to git: the grammar reads it as code"),
    (r"-Format\s|Format-Table|Format-List", "bare comma argument list (Format-Table Name, Length) is valid"),
    (r'"\s*\d+\s*\{', "switch case inside a braced block"),
)


def load_grammar(verbose: bool = False):
    """The tree-sitter PowerShell grammar, or None when it is not installed."""
    try:
        from tree_sitter import Language, Parser  # noqa: PLC0415 - optional dependency
        import tree_sitter_powershell as tsp  # noqa: PLC0415

        return Parser(Language(tsp.language()))
    except Exception as error:  # noqa: BLE001 - any import problem means "no grammar"
        if verbose:
            print(f"  the PowerShell grammar could not be loaded: {type(error).__name__}: {error}")
        return None


def script_files() -> list[Path]:
    files: list[Path] = []
    for directory in SCRIPT_DIRS:
        base = ROOT / directory
        if base.is_dir():
            files.extend(sorted(base.rglob("*.ps1")))
    return files


def walk(node):
    """Every node of the tree, the root first."""
    yield node
    for child in node.children:
        yield from walk(child)


def noise_reason(text: str, line_text: str, window: str) -> str | None:
    """Why this parse problem is a known blind spot of the grammar, or None.

    Three views are tested: the node itself, its line, and the statement it belongs to (the line plus
    the six before it). The window is needed for the switch statements in
    `Invoke-VmHardwareEvidence.ps1`, where one statement is spread over several lines and the grammar
    stops at a case in the middle of it.
    """
    for pattern, reason in KNOWN_NOISE:
        if re.search(pattern, text) or re.search(pattern, line_text) or re.search(pattern, window):
            return reason
    return None


def check(path: Path, parser) -> tuple[list[str], list[str]]:
    """Returns (findings, suppressed) for one file."""
    source = path.read_bytes()
    tree = parser.parse(source)
    lines = source.decode("utf-8", errors="replace").splitlines()
    findings: list[str] = []
    suppressed: list[str] = []
    for node in walk(tree.root_node):
        if not (node.is_error or node.is_missing):
            continue
        text = source[node.start_byte : node.end_byte].decode("utf-8", errors="replace").strip()
        line_number = node.start_point[0]
        line_text = lines[line_number] if line_number < len(lines) else ""
        where = f"{path.relative_to(ROOT)}:{line_number + 1}:{node.start_point[1] + 1}"
        excerpt = " ".join(text.split())[:90]
        window = "\n".join(lines[max(0, line_number - 6) : line_number + 1])
        reason = noise_reason(excerpt, line_text, window)
        if reason:
            suppressed.append(f"{where}: {reason} - suppressed: `{excerpt}`")
        elif node.is_missing:
            findings.append(f"{where}: missing `{node.type}` - the file is probably truncated there")
        else:
            findings.append(f"{where}: parse error around `{excerpt}`")
    return findings, suppressed


def main() -> int:
    parser_args = argparse.ArgumentParser(description="Parse every PowerShell script of this project.")
    parser_args.add_argument("--self-test", action="store_true", help="prove that a broken script is reported")
    parser_args.add_argument("--show-suppressed", action="store_true", help="list every suppressed parse problem")
    arguments = parser_args.parse_args()

    parser = load_grammar(verbose=True)
    if parser is None:
        print("the tree-sitter PowerShell grammar is not installed (`pip install tree-sitter tree-sitter-powershell`)")
        print("this is NOT a pass - no script was checked")
        return 3

    files = script_files()
    if not files:
        print("no PowerShell script was found - the search paths are wrong, not the scripts")
        return 1

    if arguments.self_test:
        # A deliberately broken script must be reported, otherwise the check proves nothing.
        broken = "function Broken {\n    param(\n}\n"
        temp = ROOT / "scripts" / ".check-powershell-selftest.ps1"
        temp.write_text(broken, encoding="utf-8")
        try:
            findings, _ = check(temp, parser)
        finally:
            temp.unlink()
        if findings:
            print(f"self-test ok: the deliberate defect is reported ({findings[0]})")
            return 0
        print("self-test FAILED: a script with an unclosed construct was accepted")
        return 1

    findings: list[str] = []
    suppressed: list[str] = []
    for path in files:
        file_findings, file_suppressed = check(path, parser)
        findings.extend(file_findings)
        suppressed.extend(file_suppressed)

    print(f"parsed {len(files)} PowerShell file(s) with the tree-sitter grammar")

    if suppressed and arguments.show_suppressed:
        print(f"{len(suppressed)} known blind spot(s) of the grammar were suppressed:")
        for line in suppressed:
            print(f"  - {line}")
    elif suppressed:
        print(f"{len(suppressed)} known blind spot(s) of the grammar were suppressed (--show-suppressed lists them)")

    if findings:
        print(f"\n{len(findings)} finding(s):")
        for line in findings:
            print(f"  - {line}")
        return 1

    print("no parse problem outside the known blind spots")
    print("this is a pre-filter: the real parser on a Windows machine is the authority")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""Self-test of the heuristic checkers: break the sources on purpose and require a finding.

None of these checks can prove that the program compiles - that needs the .NET SDK. What they can
do is fail when something is wrong, and that is what this script verifies. For every mutation the
copy is scanned with the tool that owns the rule, and the mutation only counts as detected when the
tool exits with a finding. A checker that stays silent after a deliberate break is worse than no
checker at all, because it looks like proof.

The repository itself is never touched: every mutation is applied to a temporary copy, one at a
time, and the file is restored from the repository before the next mutation.

Usage:  python3 tools/check-mutation.py
Exit code 1 when a mutation is not reported (checker regression) or when an anchor is gone
(the mutation has become stale after a code change and must be updated together with it).
"""

from __future__ import annotations

import shutil
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

IGNORED = ("bin", "obj", ".git", "artifacts", "__pycache__", "node_modules")


@dataclass(frozen=True)
class Mutation:
    """One deliberate defect: the tool that must notice it and the replacement that creates it."""

    name: str
    tool: str
    path: str
    original: str
    broken: str


MUTATIONS: tuple[Mutation, ...] = (
    Mutation(
        "contract: property in an object initialiser",
        "check-contracts",
        "src/HardwareGuardian.Maintenance/MaintenanceService.cs",
        "SafetyClass = item.SafetyClass,",
        "SafetyClassX = item.SafetyClass,",
    ),
    Mutation(
        "contract: enum member",
        "check-contracts",
        "src/HardwareGuardian.Maintenance/MaintenanceService.cs",
        "RiskLevel.High",
        "RiskLevel.Hig",
    ),
    Mutation(
        "contract: member on a field",
        "check-contracts",
        "src/HardwareGuardian.Reporting/ReportGenerator.cs",
        '_clock.Now.ToString("o"',
        '_clock.NowX.ToString("o"',
    ),
    Mutation(
        "contract: member on a method parameter",
        "check-contracts",
        "src/HardwareGuardian.Reporting/ReportGenerator.cs",
        "options.IncludeEvidence ? p.Evidence",
        "options.IncludeEvidenceX ? p.Evidence",
    ),
    Mutation(
        "contract: member inside a LINQ lambda",
        "check-contracts",
        "src/HardwareGuardian.Reporting/ReportGenerator.cs",
        'p.Evidence : "(evidence omitted by settings)"',
        'p.EvidenceX : "(evidence omitted by settings)"',
    ),
    Mutation(
        "contract: member inside an interpolated string",
        "check-contracts",
        "src/HardwareGuardian.Reporting/ReportGenerator.cs",
        "{entry.Kind,-8}",
        "{entry.KindTypo,-8}",
    ),
    Mutation(
        "localisation: key that is not defined",
        "check-localization",
        "src/HardwareGuardian.Diagnostics/WindowsHealthModule.cs",
        'LocalizedText.Of("Problem_PendingReboot_Title"',
        'LocalizedText.Of("Problem_PendingReboot_TitleX"',
    ),
    Mutation(
        "xaml: hard-coded visible text",
        "check-xaml",
        "src/HardwareGuardian.App/Views/DashboardView.xaml",
        'Text="{services:Loc Section_Sensors}"',
        'Text="Sensors"',
    ),
    Mutation(
        "xaml: localisation key that is not defined",
        "check-xaml",
        "src/HardwareGuardian.App/Views/DashboardView.xaml",
        "{services:Loc Section_Sensors}",
        "{services:Loc Section_SensorsX}",
    ),
    Mutation(
        "binding: member that the view model does not have",
        "check-bindings",
        "src/HardwareGuardian.App/Views/HardwareView.xaml",
        'ItemsSource="{Binding Components}"',
        'ItemsSource="{Binding ComponentsTypo}"',
    ),
)


def copy_workspace() -> Path:
    target = Path(tempfile.mkdtemp(prefix="hardwareguardian-mutation-"))
    shutil.copytree(ROOT, target / "tree", ignore=shutil.ignore_patterns(*IGNORED))
    return target / "tree"


def main() -> int:
    workspace = copy_workspace()
    print(f"mutation self-test in {workspace}")
    print(f"{len(MUTATIONS)} deliberate defect(s), every one has to be reported by its tool\n")

    undetected: list[str] = []
    stale: list[str] = []

    for mutation in MUTATIONS:
        source = ROOT / mutation.path
        target = workspace / mutation.path
        text = source.read_text(encoding="utf-8")
        if mutation.original not in text:
            stale.append(mutation.name)
            print(f"  STALE  {mutation.name}: the original text is no longer in {mutation.path}")
            continue

        target.write_text(text.replace(mutation.original, mutation.broken, 1), encoding="utf-8")
        result = subprocess.run(
            [sys.executable, f"tools/{mutation.tool}.py"],
            cwd=workspace,
            capture_output=True,
            text=True,
        )
        target.write_text(text, encoding="utf-8")

        if result.returncode != 0:
            print(f"  ok     {mutation.name}  ->  {mutation.tool} reports it")
        else:
            undetected.append(mutation.name)
            print(f"  MISSED {mutation.name}  ->  {mutation.tool} stays silent")

    shutil.rmtree(workspace.parent, ignore_errors=True)

    print()
    if not undetected and not stale:
        print(f"every mutation was reported ({len(MUTATIONS)}/{len(MUTATIONS)}) - the checkers still work")
        return 0
    if undetected:
        print(f"{len(undetected)} mutation(s) not reported: the rule behind them is broken")
    if stale:
        print(f"{len(stale)} mutation(s) are stale: update the anchor or the tool")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())

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
    original: str = ""
    broken: str = ""
    # Some defects are the absence of a file. `Directory.Build.props` imports `eng/Version.props`;
    # without that file every project fails to load (MSB4019), which is worth a check of its own.
    delete_file: bool = False
    # Tools that need arguments (for example to stay offline) get them here.
    args: tuple[str, ...] = ()


MUTATIONS: tuple[Mutation, ...] = (
    Mutation(
        "contract: property in an object initialiser",
        "check-contracts",
        "src/WindowsMaintenanceCenter.Maintenance/MaintenanceService.cs",
        "SafetyClass = item.SafetyClass,",
        "SafetyClassX = item.SafetyClass,",
    ),
    Mutation(
        "contract: enum member",
        "check-contracts",
        "src/WindowsMaintenanceCenter.Maintenance/MaintenanceService.cs",
        "RiskLevel.High",
        "RiskLevel.Hig",
    ),
    Mutation(
        "contract: member on a field",
        "check-contracts",
        "src/WindowsMaintenanceCenter.Reporting/ReportGenerator.cs",
        '_clock.Now.ToString("o"',
        '_clock.NowX.ToString("o"',
    ),
    Mutation(
        # The blind spot found on 2026-09-22: a member chain broken over two lines was read as its
        # receiver, so a wrong member on the awaited result stayed invisible.
        "contract: member on an awaited call broken over two lines",
        "check-contracts",
        "src/WindowsMaintenanceCenter.App/ViewModels/RecoveryViewModel.cs",
        "        ResultLines.Add(L(outcome.Summary));",
        "        ResultLines.Add(L(outcome.SummaryTypo));",
    ),
    Mutation(
        "contract: member on a method parameter",
        "check-contracts",
        "src/WindowsMaintenanceCenter.Reporting/ReportGenerator.cs",
        "options.IncludeEvidence ? p.Evidence",
        "options.IncludeEvidenceX ? p.Evidence",
    ),
    Mutation(
        "contract: member inside a LINQ lambda",
        "check-contracts",
        "src/WindowsMaintenanceCenter.Reporting/ReportGenerator.cs",
        'p.Evidence : "(evidence omitted by settings)"',
        'p.EvidenceX : "(evidence omitted by settings)"',
    ),
    Mutation(
        "contract: member inside an interpolated string",
        "check-contracts",
        "src/WindowsMaintenanceCenter.Reporting/ReportGenerator.cs",
        "{entry.Kind,-8}",
        "{entry.KindTypo,-8}",
    ),
    Mutation(
        "contract: missing interface member in a test double",
        "check-contracts",
        "tests/WindowsMaintenanceCenter.Tests/InventoryFailureTests.cs",
        "        public Task<IReadOnlyList<DriverRecord>> GetDriversAsync(CancellationToken cancellationToken) => _inner.GetDriversAsync(cancellationToken);\n\n",
        "",
    ),
    Mutation(
        "build: imported MSBuild file is missing",
        "check-projects",
        "eng/Version.props",
        delete_file=True,
    ),
    Mutation(
        "localisation: key that is not defined",
        "check-localization",
        "src/WindowsMaintenanceCenter.Diagnostics/WindowsHealthModule.cs",
        'LocalizedText.Of("Problem_PendingReboot_Title"',
        'LocalizedText.Of("Problem_PendingReboot_TitleX"',
    ),
    Mutation(
        "xaml: hard-coded visible text",
        "check-xaml",
        "src/WindowsMaintenanceCenter.App/Views/DashboardView.xaml",
        'Text="{services:Loc Section_Sensors}"',
        'Text="Sensors"',
    ),
    Mutation(
        "xaml: localisation key that is not defined",
        "check-xaml",
        "src/WindowsMaintenanceCenter.App/Views/DashboardView.xaml",
        "{services:Loc Section_Sensors}",
        "{services:Loc Section_SensorsX}",
    ),
    Mutation(
        "sources: a third-party driver portal as an update source",
        "check-source-urls",
        "src/WindowsMaintenanceCenter.Manufacturer/ManufacturerSources.cs",
        'LandingUrl = "https://www.realtek.com/Download/List?cate_id=584",',
        'LandingUrl = "https://www.driverguide.com/driver/",',
        args=("--offline", "--quiet"),
    ),
    Mutation(
        "binding: member that the view model does not have",
        "check-bindings",
        "src/WindowsMaintenanceCenter.App/Views/HardwareView.xaml",
        'ItemsSource="{Binding Components}"',
        'ItemsSource="{Binding ComponentsTypo}"',
    ),
)


def copy_workspace() -> Path:
    target = Path(tempfile.mkdtemp(prefix="windowsmaintenancecenter-mutation-"))
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
        if not source.exists():
            stale.append(mutation.name)
            print(f"  STALE  {mutation.name}: {mutation.path} does not exist")
            continue

        text = source.read_text(encoding="utf-8")
        if mutation.delete_file:
            target.unlink()
        elif mutation.original not in text:
            stale.append(mutation.name)
            print(f"  STALE  {mutation.name}: the original text is no longer in {mutation.path}")
            continue
        else:
            target.write_text(text.replace(mutation.original, mutation.broken, 1), encoding="utf-8")
        result = subprocess.run(
            [sys.executable, f"tools/{mutation.tool}.py", *mutation.args],
            cwd=workspace,
            capture_output=True,
            text=True,
        )
        if mutation.delete_file:
            target.parent.mkdir(parents=True, exist_ok=True)
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

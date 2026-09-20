#!/usr/bin/env python3
"""Writes WindowsMaintenanceCenter.sln from the projects that actually exist.

The old ".sln" GUIDs are generated deterministically from the project path (uuid5), so running this
again does not produce a diff unless a project was added or removed. That keeps the solution file
reproducible instead of hand edited.

Usage:
    python3 tools/generate-solution.py            # write WindowsMaintenanceCenter.sln
    python3 tools/generate-solution.py --check    # fail if the file is out of date
"""

from __future__ import annotations

import argparse
import pathlib
import sys
import uuid

ROOT = pathlib.Path(__file__).resolve().parent.parent
SOLUTION = ROOT / "WindowsMaintenanceCenter.sln"
CSharpProjectType = "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC"  # classic C# project type GUID
SolutionFolderType = "2150E333-8FDC-42A3-9474-1A3956D46DE8"

FOLDER_ORDER = ["src", "tests", "tools"]


def project_guid(relative: str) -> str:
    return "{" + str(uuid.uuid5(uuid.NAMESPACE_URL, "windowsmaintenancecenter/" + relative.lower())).upper() + "}"


def folder_guid(name: str) -> str:
    return "{" + str(uuid.uuid5(uuid.NAMESPACE_URL, "windowsmaintenancecenter/folder/" + name.lower())).upper() + "}"


def collect() -> tuple[list[str], dict[str, list[str]]]:
    """Returns (folders, projects per folder). Only existing projects are listed."""
    folders: list[str] = []
    per_folder: dict[str, list[str]] = {}
    for folder in FOLDER_ORDER:
        directory = ROOT / folder
        if not directory.is_dir():
            continue
        projects = sorted(str(p.relative_to(ROOT)).replace("/", "\\") for p in directory.rglob("*.csproj"))
        if projects:
            folders.append(folder)
            per_folder[folder] = projects

    # Projects that live directly under the repository root (none expected, kept for completeness).
    root_projects = sorted(str(p.relative_to(ROOT)).replace("/", "\\") for p in ROOT.glob("*.csproj"))
    if root_projects:
        folders.insert(0, "")
        per_folder[""] = root_projects
    return folders, per_folder


def build() -> str:
    folders, per_folder = collect()
    lines = [
        "Microsoft Visual Studio Solution File, Format Version 12.00",
        "# Visual Studio Version 17",
        "VisualStudioVersion = 17.0.31903.59",
        "MinimumVisualStudioVersion = 10.0.40219.1",
    ]

    for folder in folders:
        if folder:
            lines.append(f'Project("{SolutionFolderType}") = "{folder}", "{folder}", "{folder_guid(folder)}"')
            lines.append("EndProject")

    for folder in folders:
        for project in per_folder[folder]:
            name = pathlib.Path(project).stem
            lines.append(f'Project("{CSharpProjectType}") = "{name}", "{project}", "{project_guid(project)}"')
            lines.append("EndProject")

    lines.append("Global")
    lines.append("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution")
    for configuration in ("Debug|x64", "Release|x64", "Debug|Any CPU", "Release|Any CPU"):
        lines.append(f"\t\t{configuration} = {configuration}")
    lines.append("\tEndGlobalSection")
    lines.append("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution")
    for folder in folders:
        for project in per_folder[folder]:
            guid = project_guid(project)
            for configuration, target in (
                ("Debug|x64", "Debug|Any CPU"),
                ("Release|x64", "Release|Any CPU"),
                ("Debug|Any CPU", "Debug|Any CPU"),
                ("Release|Any CPU", "Release|Any CPU"),
            ):
                lines.append(f"\t\t{guid}.{configuration}.ActiveCfg = {target}")
                if configuration in ("Debug|x64", "Release|x64"):
                    lines.append(f"\t\t{guid}.{configuration}.Build.0 = {target}")
    lines.append("\tEndGlobalSection")
    lines.append("\tGlobalSection(SolutionProperties) = preSolution")
    lines.append("\t\tHideSolutionNode = FALSE")
    lines.append("\tEndGlobalSection")
    lines.append("\tGlobalSection(NestedProjects) = preSolution")
    for folder in folders:
        if not folder:
            continue
        for project in per_folder[folder]:
            lines.append(f"\t\t{project_guid(project)} = {folder_guid(folder)}")
    lines.append("\tEndGlobalSection")
    lines.append("EndGlobal")
    return "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()

    content = build()
    current = SOLUTION.read_text(encoding="utf-8") if SOLUTION.exists() else None

    if args.check:
        if current != content:
            print("WindowsMaintenanceCenter.sln is out of date - run tools/generate-solution.py")
            return 1
        print("WindowsMaintenanceCenter.sln is up to date")
        return 0

    SOLUTION.write_text(content, encoding="utf-8")
    folders, per_folder = collect()
    print(f"wrote {SOLUTION.name} with {sum(len(v) for v in per_folder.values())} project(s):")
    for folder in folders:
        for project in per_folder[folder]:
            print(f"  {project}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

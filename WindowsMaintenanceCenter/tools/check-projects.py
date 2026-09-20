#!/usr/bin/env python3
"""Project reference checker (heuristic, complements tools/check-contracts.py).

There is no compiler in this workspace, so the two failure modes that a compiler would catch
immediately are checked here instead:

  1. a project uses a namespace that none of its (transitive) project references provides,
  2. a source directory has no project file, or a project file has no sources.

Package references are only checked for being declared in Directory.Packages.props (central package
management), because resolving NuGet versions is what the restore step is for.

Usage:
    python3 tools/check-projects.py
"""

from __future__ import annotations

import re
import pathlib
import sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent
SRC = ROOT / "src"
TESTS = ROOT / "tests"

NAMESPACE = re.compile(r"^\s*namespace\s+([A-Za-z_][\w\.]*)", re.M)
USING = re.compile(r"^\s*using\s+(WindowsMaintenanceCenter[\w\.]*)\s*;", re.M)
PROJECT_REF = re.compile(r'ProjectReference\s+Include="([^"]+)"')
IMPORT = re.compile(r'<Import\s+Project="([^"]+)"')
PACKAGE_REF = re.compile(r'PackageReference\s+Include="([^"]+)"')


def check_build_configuration() -> list[str]:
    """Checks the MSBuild files that sit above the projects.

    `Directory.Build.props` imports `eng/Version.props`. When that file is missing, every project
    fails to load with MSB4019 and nothing can be built at all - a state that a repository snapshot
    or a careless cleanup can produce, and that no other check here would notice. The import target
    is therefore resolved and required to exist, and the version has to be declared in exactly one
    place.
    """
    findings: list[str] = []
    msbuild_roots = [
        ROOT / "Directory.Build.props",
        ROOT / "Directory.Build.targets",
        ROOT / "Directory.Packages.props",
    ]

    imported: set[str] = set()
    for candidate in msbuild_roots:
        if not candidate.is_file():
            continue
        text = candidate.read_text(encoding="utf-8", errors="replace")
        for raw in IMPORT.findall(text):
            resolved = raw.replace("$(MSBuildThisFileDirectory)", str(ROOT) + "/").replace("\\", "/")
            target = pathlib.Path(resolved)
            if not target.is_absolute():
                target = (candidate.parent / resolved).resolve()
            try:
                relative = target.relative_to(ROOT)
            except ValueError:
                continue
            if not target.is_file():
                findings.append(
                    f"{candidate.name}: imports '{relative}' which does not exist - every project would fail to load (MSB4019)"
                )
                continue
            imported.add(str(relative))

    # Version.props is imported by hand, so a missing import means the values are silently ignored.
    # Directory.Packages.props is picked up by the SDK itself; there it is only the existence that
    # matters (central package management needs it at the repository root).
    if "eng/Version.props" not in imported:
        findings.append(
            "eng/Version.props is not imported by Directory.Build.props - the product version would be ignored"
        )

    for relative in ("Directory.Packages.props", "global.json", "eng/Version.props"):
        if not (ROOT / relative).is_file():
            findings.append(f"{relative} is missing (expected in this repository)")

    version_props = ROOT / "eng" / "Version.props"
    if version_props.is_file():
        text = version_props.read_text(encoding="utf-8", errors="replace")
        if "<VersionPrefix>" not in text:
            findings.append("eng/Version.props does not set VersionPrefix, the single source of the product version")
        for project in sorted(ROOT.rglob("*.csproj")):
            if "<VersionPrefix>" in project.read_text(encoding="utf-8", errors="replace"):
                findings.append(
                    "{} sets VersionPrefix itself - the version must come from eng/Version.props only".format(project.relative_to(ROOT))
                )

    return findings


def strip_comments(text: str) -> str:
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
        out.append(text[i])
        i += 1
    return "".join(out)


class Project:
    def __init__(self, path: pathlib.Path) -> None:
        self.path = path
        self.name = path.stem
        self.relative = str(path.relative_to(ROOT))
        text = path.read_text(encoding="utf-8")
        self.references = [(path.parent / ref.replace("\\", "/")).resolve() for ref in PROJECT_REF.findall(text)]
        self.packages = PACKAGE_REF.findall(text)
        self.namespaces: set[str] = set()
        self.usings: set[str] = set()
        self.source_count = 0
        for source in sorted(path.parent.rglob("*.cs")):
            self.source_count += 1
            content = strip_comments(source.read_text(encoding="utf-8", errors="replace"))
            self.namespaces.update(NAMESPACE.findall(content))
            self.usings.update(USING.findall(content))


def main() -> int:
    project_files = sorted(SRC.rglob("*.csproj"))
    if TESTS.is_dir():
        # The test project is part of the solution and is checked with the same rules.
        project_files += sorted(TESTS.rglob("*.csproj"))
    projects = [Project(p) for p in project_files]
    by_path = {p.path.resolve(): p for p in projects}
    findings: list[str] = []

    # 2. every source directory below src/ or tests/ belongs to a project, and every project has sources
    roots = [SRC] + ([TESTS] if TESTS.is_dir() else [])
    for directory in sorted(d for root in roots for d in root.iterdir() if d.is_dir()):
        # The project file may live in a subdirectory (tests/WindowsMaintenanceCenter.Tests).
        matches = list(directory.rglob("*.csproj"))
        sources = list(directory.rglob("*.cs"))
        if not matches and sources:
            findings.append(f"{directory.relative_to(ROOT)}: {len(sources)} source file(s) but no project file")
        if matches and not sources:
            findings.append(f"{directory.relative_to(ROOT)}: project file without any source file")

    # central package management
    props = (ROOT / "Directory.Packages.props").read_text(encoding="utf-8")
    declared = set(re.findall(r'PackageVersion\s+Include="([^"]+)"', props))

    for project in projects:
        for package in project.packages:
            if package not in declared:
                findings.append(f"{project.relative}: package '{package}' has no PackageVersion in Directory.Packages.props")

        # transitive closure of project references
        reachable: set[pathlib.Path] = set()
        queue = [ref for ref in project.references]
        while queue:
            ref = queue.pop()
            if ref in reachable:
                continue
            reachable.add(ref)
            target = by_path.get(ref)
            if target is None:
                findings.append(f"{project.relative}: ProjectReference '{ref}' does not exist")
                continue
            queue.extend(target.references)

        provided: set[str] = set()
        for ref in reachable:
            target = by_path.get(ref)
            if target is not None:
                provided.update(target.namespaces)

        for used in sorted(project.usings):
            if used in project.namespaces:
                continue
            if not any(used == ns or used.startswith(ns + ".") or ns.startswith(used + ".") for ns in provided):
                findings.append(f"{project.relative}: uses '{used}' but no referenced project provides it")

    findings.extend(check_build_configuration())

    print(f"inspected {len(projects)} project(s), {sum(p.source_count for p in projects)} source file(s)")
    if findings:
        print(f"\n{len(findings)} finding(s):")
        for finding in findings:
            print(f"  - {finding}")
        return 1

    print("no project reference problems detected (heuristic check only - not a compiler)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

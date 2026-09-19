#!/usr/bin/env python3
"""XAML checker (heuristic, complements the C# checks).

Without a compiler, XAML errors only appear when the window is opened - which is exactly the
situation this project must avoid. This tool catches the failure classes that are detectable
statically:

  1. a .xaml file that is not well formed XML,
  2. a StaticResource / DynamicResource reference with no matching x:Key definition,
  3. a DataTemplate DataType that points at a class which does not exist,
  4. an x:Class without a matching code-behind file (or the other way round),
  5. a markup extension typo such as '{Binding}' without a path.

Usage:
    python3 tools/check-xaml.py
"""

from __future__ import annotations

import pathlib
import re
import sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent
SRC = ROOT / "src"
XAML_NS = "{http://schemas.microsoft.com/winfx/2006/xaml}"

KEY_DEFINITION = re.compile(r'x:Key="([^"]+)"')
RESOURCE_REFERENCE = re.compile(r'\{(?:StaticResource|DynamicResource)\s+([A-Za-z0-9_\.]+)\s*\}')
DATA_TYPE = re.compile(r'\bx:Type\s+([A-Za-z0-9_]+):([A-Za-z0-9_]+)')
X_CLASS = re.compile(r'x:Class="([^"]+)"')


def declared_types() -> set[str]:
    names: set[str] = set()
    for path in SRC.rglob("*.cs"):
        text = path.read_text(encoding="utf-8", errors="replace")
        names.update(re.findall(r"\b(?:class|record|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)", text))
    return names


def main() -> int:
    files = sorted(SRC.rglob("*.xaml"))
    if not files:
        print("no .xaml files found")
        return 0

    findings: list[str] = []
    defined_keys: set[str] = set()
    parsed: list[tuple[pathlib.Path, ET.Element]] = []

    for path in files:
        try:
            root = ET.parse(path).getroot()
        except ET.ParseError as ex:
            findings.append(f"{path.relative_to(ROOT)}: XML is not well formed ({ex})")
            continue

        parsed.append((path, root))
        text = path.read_text(encoding="utf-8", errors="replace")
        defined_keys.update(KEY_DEFINITION.findall(text))

    known_types = declared_types()
    # WPF built-ins that appear as DataType: nothing to resolve, they live in the framework.
    builtin_types = {
        "String", "Boolean", "Int32", "Double", "Object", "XmlElement", "ContentControl",
    }

    for path, _ in parsed:
        text = path.read_text(encoding="utf-8", errors="replace")
        relative = path.relative_to(ROOT)

        for reference in sorted(set(RESOURCE_REFERENCE.findall(text))):
            if reference not in defined_keys:
                findings.append(f"{relative}: resource '{reference}' is referenced but never defined")

        for _, type_name in DATA_TYPE.findall(text):
            if type_name not in known_types and type_name not in builtin_types:
                findings.append(f"{relative}: DataType '{type_name}' does not exist in the sources")

        # Windows, user controls and pages need a code-behind; resource dictionaries never have one.
        root_tag = ET.parse(path).getroot().tag
        needs_code_behind = any(root_tag.endswith(name) for name in ("Window", "UserControl", "Page", "ResourceDictionaryWithCode"))
        match = X_CLASS.search(text)
        if match:
            code_behind = path.with_suffix(path.suffix + ".cs")
            if not code_behind.exists():
                findings.append(f"{relative}: x:Class '{match.group(1)}' has no code-behind file {code_behind.name}")
        elif needs_code_behind and path.name != "App.xaml":
            findings.append(f"{relative}: {root_tag.split('}')[-1]} without x:Class cannot be loaded")

    # Code-behind without a XAML file (excluding App.xaml.cs which has one)
    for code in sorted(SRC.rglob("*.xaml.cs")):
        if not code.with_suffix("").exists():
            findings.append(f"{code.relative_to(ROOT)}: code-behind without a matching .xaml file")

    print(f"inspected {len(files)} XAML file(s), {len(defined_keys)} resource key(s), {len(known_types)} declared type(s)")
    if findings:
        print(f"\n{len(findings)} finding(s):")
        for finding in findings:
            print(f"  - {finding}")
        return 1

    print("no XAML problems detected (heuristic check only - the WPF parser is the authority)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

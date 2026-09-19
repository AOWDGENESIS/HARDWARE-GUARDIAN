#!/usr/bin/env python3
"""Parses every C# file with tree-sitter and reports files that do not parse.

This is a grammar check, not a compilation: it catches unbalanced braces, broken string literals
and similar damage that a text patch can produce. Tree-sitter is optional - when it is not
installed the check reports that it was skipped instead of pretending to have passed.
"""

from __future__ import annotations

import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent


def main() -> int:
    try:
        from tree_sitter import Language, Parser
        import tree_sitter_c_sharp as grammar
    except ImportError:
        print("tree-sitter / tree_sitter_c_sharp not installed - syntax check skipped (not passed)")
        return 0

    parser = Parser(Language(grammar.language()))
    files = sorted((ROOT / "src").rglob("*.cs")) + sorted((ROOT / "tests").rglob("*.cs")) if (ROOT / "tests").is_dir() else sorted((ROOT / "src").rglob("*.cs"))
    broken = []
    for path in files:
        tree = parser.parse(path.read_bytes())
        if tree.root_node.has_error:
            broken.append(path)

    print(f"parsed {len(files)} file(s) with the tree-sitter C# grammar")
    if broken:
        for path in broken:
            print(f"  - syntax error: {path.relative_to(ROOT)}")
        return 1

    print("no syntax errors found (grammar level only - not a compiler)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

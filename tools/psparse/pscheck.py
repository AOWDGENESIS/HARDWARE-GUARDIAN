#!/usr/bin/env python3
"""PowerShell-Syntaxpruefung mit tree-sitter.

Ersatz fuer [System.Management.Automation.Language.Parser]::ParseFile auf
Systemen ohne PowerShell (z. B. Linux-Sandbox). Meldet ERROR- und
MISSING-Knoten mit Zeile, Spalte und Kontext.

Voraussetzungen:
    pip install --target .arena/tools/py-ps tree-sitter tree-sitter-powershell
    PYTHONPATH=.arena/tools/py-ps python3 tools/psparse/pscheck.py <datei.ps1> ...

Rueckgabecode: 0 = sauber, 1 = mindestens ein Problemknoten.
"""
import sys

from tree_sitter import Language, Parser
import tree_sitter_powershell as tsp

PS = Language(tsp.language())
parser = Parser(PS)


def check(path):
    src = open(path, "rb").read()
    tree = parser.parse(src)
    lines = src.decode("utf-8", errors="replace").replace("\r\n", "\n").split("\n")
    problems = []

    def walk(node):
        if node.type == "ERROR" or node.is_missing:
            problems.append(node)
        for child in node.children:
            walk(child)

    walk(tree.root_node)

    print("=" * 72)
    print("FILE:", path)
    print("BYTES:", len(src), " LINES:", len(lines))
    print("=" * 72)

    if not problems:
        print("PARSE: OK (keine ERROR/MISSING-Knoten)")
        return 0

    seen = set()

    for node in problems:
        r0, c0 = node.start_point
        r1, c1 = node.end_point

        if not node.is_missing and (r0, c0, r1, c1) in seen:
            continue

        seen.add((r0, c0, r1, c1))
        snippet = "\n".join(lines[r0:min(r1 + 1, r0 + 6)])
        snippet = "\n".join("   " + line for line in snippet.split("\n"))
        kind = "MISSING" if node.is_missing else "ERROR"
        print(f"\n[{kind}] {node.type}  Zeile {r0+1} Spalte {c0+1} .. Zeile {r1+1} Spalte {c1+1}")
        print(snippet)

    print(f"\nGESAMT: {len(problems)} Problemknoten")
    return 1


if __name__ == "__main__":
    rc = 0

    for path in sys.argv[1:]:
        rc |= check(path)

    sys.exit(rc)

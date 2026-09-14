#!/usr/bin/env python3
"""Statisches Gate: Aufrufe interner Funktionen gegen ihre Signaturen pruefen.

Findet vor der Ausfuehrung genau die beiden Fehlerklassen, die im
LocalCloudCode-Patchprojekt zweimal zugeschlagen haben:

    UNKNOWN-PARAMETER   Aufruf mit einem Parameternamen, den die Funktion nicht hat
                        (Befund F-03: Get-WorkspacePath -Workspace)
    MISSING-MANDATORY   Pflichtparameter wird nicht uebergeben
                        (Befund F-02: Get-WorkspaceContent ohne -Path)

Beruecksichtigt: benannte Parameter, positionale Bindung (die ersten N
deklarierten Parameter), aufgeloeste Splat-Hashtabellen ($h = @{...} und
$h['Key'] = ...) sowie die CommonParameters.

Voraussetzungen:
    pip install --target .arena/tools/py-ps tree-sitter tree-sitter-powershell
    PYTHONPATH=.arena/tools/py-ps python3 tools/psparse/paramgate.py <datei.ps1> ...
"""
import re
import sys

from tree_sitter import Language, Parser
import tree_sitter_powershell as tsp

PS = Language(tsp.language())
parser = Parser(PS)

COMMON = {
    "Verbose", "Debug", "ErrorAction", "WarningAction", "InformationAction",
    "ErrorVariable", "WarningVariable", "InformationVariable", "OutVariable",
    "OutBuffer", "PipelineVariable", "ProgressAction",
}


def funcs(src):
    tree = parser.parse(src)
    out = {}

    def walk(node):
        if node.type == "function_statement":
            name_node = next((c for c in node.children if c.type == "function_name"), None)

            if name_node:
                name = src[name_node.start_byte:name_node.end_byte].decode()
                params, mandatory = [], []

                def find_paramlist(m):
                    if m.type == "parameter_list":
                        for sp in m.children:
                            if sp.type != "script_parameter":
                                continue

                            direct_vars = [c for c in sp.children if c.type == "variable"]

                            if not direct_vars:
                                continue

                            pname = src[direct_vars[0].start_byte:direct_vars[0].end_byte].decode().lstrip("$")
                            params.append(pname)
                            text = src[sp.start_byte:sp.end_byte].decode()

                            if "Mandatory" in text and not re.search(r"Mandatory\s*=\s*\$[Ff]alse", text):
                                mandatory.append(pname)

                        return True

                    for c in m.children:
                        if find_paramlist(c):
                            return True

                    return False

                find_paramlist(node)
                out[name] = {"params": params, "mandatory": mandatory, "line": node.start_point[0] + 1}

        for c in node.children:
            walk(c)

    walk(tree.root_node)
    return out, tree


def splat_index_keys(text, varname):
    return set(re.findall(re.escape(varname) + r"\s*\[\s*['\"]([^'\"]+)['\"]\s*\]\s*=", text))


def splat_keys(src, tree):
    keys = {}

    def walk(node):
        if node.type == "assignment_expression":
            left = src[node.children[0].start_byte:node.children[0].end_byte].decode() if node.children else ""
            right = src[node.start_byte:node.end_byte].decode()

            if left.startswith("$") and re.search(r"=\s*@\{", right):
                body = right[right.index("@{") + 2:]
                found = set(re.findall(r"(?:^|[;{\s])([A-Za-z_][A-Za-z0-9_]*)\s*=", body))
                keys[left] = {"keys": found, "line": node.start_point[0] + 1}

        for c in node.children:
            walk(c)

    walk(tree.root_node)
    return keys


def commands(src, tree):
    found = []

    def walk(node):
        if node.type == "command":
            name_node = next((c for c in node.children if c.type == "command_name"), None)

            if name_node:
                name = src[name_node.start_byte:name_node.end_byte].decode().strip("\"'")
                params, splats, positional = [], [], 0
                elements = []

                for ch in node.children:
                    if ch.type == "command_elements":
                        elements = ch.children

                for el in elements:
                    if el.type == "command_parameter":
                        params.append(src[el.start_byte:el.end_byte].decode().split(":")[0].lstrip("-"))
                    elif el.type == "variable" and src[el.start_byte:el.end_byte].decode().startswith("@"):
                        splats.append("$" + src[el.start_byte:el.end_byte].decode().lstrip("@"))
                    elif el.type == "array_literal_expression":
                        inner = src[el.start_byte:el.end_byte].decode()

                        if re.match(r"^@[A-Za-z_]", inner):
                            splats.append("$" + inner.lstrip("@"))
                        else:
                            positional += 1
                    elif el.type != "command_argument_sep":
                        positional += 1

                found.append({"name": name, "params": params, "splats": splats,
                              "positional": positional, "line": node.start_point[0] + 1})

        for c in node.children:
            walk(c)

    walk(tree.root_node)
    return found


def check(path):
    src = open(path, "rb").read()
    text = src.decode("utf-8", errors="replace")
    sig, tree = funcs(src)
    splats = splat_keys(src, tree)

    for name in splats:
        splats[name]["keys"] |= splat_index_keys(text, name)

    calls = commands(src, tree)
    problems = []

    for call in calls:
        if call["name"] not in sig:
            continue

        spec = sig[call["name"]]

        for param in call["params"]:
            if param not in spec["params"] and param not in COMMON:
                problems.append((call["line"], "UNKNOWN-PARAMETER",
                                 f"{call['name']} -{param}  (Funktion kennt: {', '.join(spec['params']) or '-'})"))

        order = spec["params"]
        provided = set(call["params"]) | COMMON | set(order[: call["positional"]])
        unresolved = False

        for splat in call["splats"]:
            if splat in splats:
                provided |= splats[splat]["keys"]
            else:
                unresolved = True

        missing = [m for m in spec["mandatory"] if m not in provided]

        if missing:
            how = "Splat-Quelle unbekannt" if unresolved else "Splatting (Schluessel statisch bekannt)" if call["splats"] else "direkter Aufruf"
            kind = "WARN-SPLAT" if unresolved else "MISSING-MANDATORY"
            problems.append((call["line"], kind,
                             f"{call['name']} Zeile {call['line']}: Pflichtparameter fehlen: {', '.join(missing)}  [{how}]"))

    internal = sum(1 for c in calls if c["name"] in sig)

    print("=" * 74)
    print("DATEI:", path)
    print(f"Funktionen: {len(sig)} | Aufrufe interner Funktionen: {internal}")
    print("=" * 74)

    if not problems:
        print("PARAMETER-GATE: PASS (keine Auffaelligkeiten)")

    for line, kind, msg in sorted(problems):
        print(f"[{kind}] Zeile {line}: {msg}")

    return len([p for p in problems if not p[1].startswith("WARN")])


if __name__ == "__main__":
    rc = 0

    for path in sys.argv[1:]:
        rc += check(path)

    print("\nGESAMT PROBLEME:", rc)
    sys.exit(0 if rc == 0 else 1)

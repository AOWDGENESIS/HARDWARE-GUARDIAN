#!/usr/bin/env python3
"""PowerShell-Syntaxpruefung mit tree-sitter (VORFILTER, nicht verbindlich).

Ersatz fuer [System.Management.Automation.Language.Parser]::ParseFile auf
Systemen ohne PowerShell (z. B. Linux-Sandbox). Meldet ERROR- und
MISSING-Knoten mit Zeile, Spalte und Kontext.

ACHTUNG - BEKANNTE FALSCHMELDUNGEN (am 14.09.2026 empirisch nachgewiesen):
Dieser Parser ist NICHT der echte PowerShell-Parser. Belegt sind mindestens
diese Klassen - alles davon ist GUELTIGES PowerShell:

  1. Zahl-Literale mit Einheitensuffix:  1GB, 1MB, 1KB, 1TB     -> ERROR
  2. Komma-getrennte nackte Argumente:   Select-Object Name,DriverVersion
  3. Bestimmte Kommentarzeilen am Dateirand (endend auf " 7.")

Beweis: "# Nur ein Kommentar" parst fehlerfrei; "# ASCII-only. Windows
PowerShell 5.1 / PowerShell 7." erzeugt einen ERROR-Knoten. Der Fehler liegt
im Parser, nicht im Code.

UMGEKEHRT - BEKANNTE UEBERSEHUNGEN (am 14.09.2026 durch den echten Parser
auf GitHub nachgewiesen, CI-Lauf 34878640404):
tree-sitter meldet "PARSE: OK", wo der echte Parser hart ablehnt:

  4. "$name:" in einer Zeichenkette, wenn nach dem Doppelpunkt kein
     gueltiges Namenszeichen folgt:
         $md += "- $k: $($apps[$k])"      # Fehler: ':' not followed by
                                          # a valid variable name character
     Gueltig sind Scope-/Laufwerkspraefixe ($env:PATH, $script:x) und die
     geklammerte Form ("${k}: ..."). Fuer diese Klasse gibt es unten eine
     Musterpruefung, die als "VERDACHT" gekennzeichnet ist - nicht als
     Parserfehler, weil sie ein Muster prueft und keinen Syntaxbaum.

Konsequenz dieser Uebersehung: "PARSE: OK" dieses Skripts ist KEIN Beweis fuer
gueltige Syntax. In dem genannten CI-Lauf fand der echte Parser drei Fehler in
Dateien, die hier als OK galten (zwei Dateien mit "$k:", eine abgeschnittene
Transkription). Der echte Parser ist die einzige verbindliche Instanz.

Konsequenz: Dieses Werkzeug ist ein VORFILTER. Verbindlich ist ausschliesslich
der echte Parser auf dem Zielsystem:

    $e=$null;$t=$null
    [System.Management.Automation.Language.Parser]::ParseFile(
        $Datei,[ref]$t,[ref]$e) | Out-Null
    if (@($e).Count -eq 0) { 'SYNTAX: OK' } else { $e | Select-Object -First 5 }

Die Klassen 1-3 filtert dieses Skript vorab heraus.

Voraussetzungen:
    pip install --target .arena/tools/py-ps tree-sitter tree-sitter-powershell
    PYTHONPATH=.arena/tools/py-ps python3 tools/psparse/pscheck.py <datei.ps1> ...

Rueckgabecode: 0 = sauber, 1 = mindestens ein Problemknoten.
"""
import re
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

    def is_known_false_positive(node):
        line = lines[node.start_point[0]] if node.start_point[0] < len(lines) else ""

        if re.search(r"\b\d+([KMGT]i?B)\b", line):
            return True
        if node.is_missing and re.search(r"\b[A-Za-z_][A-Za-z0-9_]*,[A-Za-z_]", line):
            return True
        if node.type == "ERROR" and line.lstrip().startswith("#"):
            return True

        return False

    def walk(node):
        if node.type == "ERROR" or node.is_missing:
            if not is_known_false_positive(node):
                problems.append(node)
        for child in node.children:
            walk(child)

    walk(tree.root_node)

    # Musterpruefung Klasse 4: "$name:" ohne gueltiges Namenszeichen danach.
    # Bekannte Scope- und Laufwerkspraefixe sind erlaubt.
    allowed_prefixes = {
        "env", "script", "global", "local", "private", "using", "variable",
        "function", "alias", "workflow", "hk lm", "hklm", "hkey_local_machine",
    }
    pattern = re.compile(r"(?<!\{)\$([A-Za-z_][A-Za-z0-9_]*):(?![A-Za-z0-9_])")
    suspects = []

    def strip_line(line):
        """Einzelquotierte Bereiche und Kommentare ausserhalb von Quotes entfernen."""
        out, i, quote = [], 0, None
        while i < len(line):
            ch = line[i]
            if quote == "'":
                if ch == "'":
                    quote = None
                i += 1
                continue
            if ch in "\"'":
                if ch == "'":
                    quote = "'"
                out.append(ch)
                i += 1
                continue
            if ch == "#":
                break
            out.append(ch)
            i += 1
        return "".join(out)

    for idx, raw in enumerate(lines, start=1):
        code = strip_line(raw)
        for match in pattern.finditer(code):
            name = match.group(1)
            if name.lower() in allowed_prefixes:
                continue
            suspects.append((idx, name, raw.strip()))

    print("=" * 72)
    print("FILE:", path)
    print("BYTES:", len(src), " LINES:", len(lines))
    print("=" * 72)

    for line_no, name, text in suspects:
        print(f"\n[VERDACHT] Zeile {line_no}: '${name}:' ohne gueltiges Namenszeichen danach")
        print(f"   {text[:160]}")
        print("   Der echte Parser lehnt das ab.")
        print("   Gueltig waere:  \"${" + name + "}: ...\"   oder   \"$(" + name + "): ...\"")

    if not problems and not suspects:
        print("PARSE: OK (keine ERROR/MISSING-Knoten)")
        return 0

    if not problems:
        print("\nPARSE: keine Baumfehler - aber siehe VERDACHT oben.")
        print(f"\nGESAMT: {len(suspects)} Verdachtsstelle(n)")
        return 1

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

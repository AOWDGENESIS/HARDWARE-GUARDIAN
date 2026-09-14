# Entwicklungen

Arbeits- und Prüf-Repository rund um **LocalCloudCode** (lokaler Cloud-Code-Ersatz
auf Basis von Ollama / LM Studio, PowerShell 7, Windows).

Alles in diesem Repository ist darauf ausgelegt, **nachweisbar** zu sein:
Jeder Befund hat einen Beleg, jedes Werkzeug prüft sich selbst, und es werden
keine Ergebnisse behauptet, die nicht tatsächlich ausgeführt wurden.

## Inhalt

| Pfad | Inhalt |
|---|---|
| `review/PRUEFBERICHT-v9.2.md` | **Tiefenprüfung des aktuellen Stands.** 36 Befunde (P0/P1/P2/P3) mit Beweis, Ursache und konkreter Lösung. Einstiegspunkt. |
| `review/LocalCloudCode.v9.2.stand-2026-09-13.ps1` | Der geprüfte Code-Auszug (Transkription aus dem Chat, 822 Zeilen). Referenz für die Befundnummern. |
| `patches/Get-DiscoveryResult.v1.2.ps1` | Korrigierter, vollständiger Ersatz für `Get-DiscoveryResult` — behebt die Blocker F-02, F-03, F-04, F-08, F-09, F-10, F-13c, F-14. |
| `tools/Update-PsFunctionBlock.ps1` | Sicheres Patch-Werkzeug: ersetzt eine Funktion per **AST-Extent** statt per `-replace`, sichert, prüft, rollt automatisch zurück. |
| `tools/psparse/` | PowerShell-Parser und Parameter-Gate als Python-Werkzeug (tree-sitter) — prüft Syntax und interne Aufrufe, wenn kein PowerShell verfügbar ist. Findet F-02/F-03 vor der Ausführung. |
| `tests/Invoke-ContractTests.ps1` | Vertragstests, die **ohne** Start der Runtime laufen: Syntax, Smart-Quotes, Parameter-Gate (findet F-02/F-03 vor der Ausführung), Workspace-Containment, Content-/Discovery-Vertrag, Hash-Determinismus. |
| `tools/Collect-LocalCloudCodeSnapshot.ps1` | **READ-ONLY** Snapshot des echten Systems: Hashes, Parsergebnisse, Content-Schema, Backend-Status, Marker zu allen Befunden — ein ZIP für die Datenübergabe. |
| `review/GATEWAY-UND-DATENUEBERGABE.md` | Warum das lokale Gateway hier nicht erreichbar ist (und nicht erreichbar sein soll), Sicherheitscheck in 5 Prüfungen, Übergabewege. |

## Reihenfolge

**Die Dateien liegen im Repository, nicht auf dem PC** — zuerst holen (das Repo ist
privat, ein Raw-Link funktioniert daher nicht ohne Token):

```powershell
# Option A: im Browser  Code -> Download ZIP  (Branch: arena/01a09c80-entwicklungen)
#           entpacken nach  C:\Users\aowdg\Desktop\KI\Entwicklungen
# Option B: mit git
git clone -b arena/01a09c80-entwicklungen `
  https://github.com/AOWDGENESIS/Entwicklungen.git `
  "$env:USERPROFILE\Desktop\KI\Entwicklungen"
```

Dann in PowerShell 7 (7.2 oder neuer):

```powershell
$D = "$env:USERPROFILE\Desktop\KI\Entwicklungen"
Get-ChildItem $D -Recurse -File | Unblock-File          # heruntergeladene Skripte freigeben
Set-ExecutionPolicy -Scope Process Bypass -Force        # nur fuer diese Sitzung

# 1. Ist-Zustand belegen (findet die Blocker, ohne etwas zu veraendern)
& "$D\tests\Invoke-ContractTests.ps1" -ScriptPath "$env:USERPROFILE\Desktop\KI\LocalCloudCode\LocalCloudCode.ps1"

# 2. Fakten sammeln, wenn kein Dateitransfer moeglich ist (kurze Ausgabe zum Kopieren)
& "$D\tools\Get-LccQuickFacts.ps1"

# 3. Vollstaendiger Snapshot (JSON + Markdown + ZIP)
& "$D\tools\Collect-LocalCloudCodeSnapshot.ps1" -WhatIf
& "$D\tools\Collect-LocalCloudCodeSnapshot.ps1"

# 4. Aenderung vorher ansehen, dann anwenden (Backup + Auto-Rollback)
& "$D\tools\Update-PsFunctionBlock.ps1" `
    -TargetFile  "$env:USERPROFILE\Desktop\KI\LocalCloudCode\LocalCloudCode.ps1" `
    -NewBlockFile "$D\patches\Get-DiscoveryResult.v1.2.ps1" -WhatIf
```

## Warum kein Copy-Paste mehr

Zwei Ausfälle in kurzer Folge hatten dieselbe Ursache: Code wurde über Chat
kopiert und per `-replace` in eine 1.200-Zeilen-Produktivdatei geschrieben.
Dabei gilt:

- Markdown/Chat ersetzt Anführungszeichen gerne durch typografische Zeichen
  (`„ " "`) — PowerShell erkennt dann nicht einmal mehr die Zeilenstruktur.
- `-replace` interpretiert im Ersetzungstext `$1`, `$&`, `` $` ``, `$_` als
  Ersetzungsanweisungen und zerstört damit jeden PowerShell-Code, der
  `$($_.Exception.Message)` o. Ä. enthält.

Deshalb: Änderungen nur über `tools/Update-PsFunctionBlock.ps1` (AST-basiert,
mit Backup, Parser-Gate und automatischem Rollback) und Verifikation nur über
`tests/Invoke-ContractTests.ps1`.

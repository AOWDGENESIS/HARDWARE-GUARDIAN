# Entwicklungen

Arbeits- und Prüf-Repository rund um **LocalCloudCode** (lokaler Cloud-Code-Ersatz
auf Basis von Ollama / LM Studio, PowerShell 7, Windows).

Alles in diesem Repository ist darauf ausgelegt, **nachweisbar** zu sein:
Jeder Befund hat einen Beleg, jedes Werkzeug prüft sich selbst, und es werden
keine Ergebnisse behauptet, die nicht tatsächlich ausgeführt wurden.

## Inhalt

| Pfad | Inhalt |
|---|---|
| `review/PRUEFBERICHT-v9.2.md` | **Tiefenprüfung des aktuellen Stands.** 35 Befunde (P0/P1/P2/P3) mit Beweis, Ursache und konkreter Lösung. Einstiegspunkt. |
| `review/LocalCloudCode.v9.2.stand-2026-09-13.ps1` | Der geprüfte Code-Auszug (Transkription aus dem Chat, 822 Zeilen). Referenz für die Befundnummern. |
| `patches/Get-DiscoveryResult.v1.2.ps1` | Korrigierter, vollständiger Ersatz für `Get-DiscoveryResult` — behebt die Blocker F-02, F-03, F-04, F-08, F-09, F-10, F-13c, F-14. |
| `tools/Update-PsFunctionBlock.ps1` | Sicheres Patch-Werkzeug: ersetzt eine Funktion per **AST-Extent** statt per `-replace`, sichert, prüft, rollt automatisch zurück. |
| `tests/Invoke-ContractTests.ps1` | Vertragstests, die **ohne** Start der Runtime laufen: Syntax, Smart-Quotes, Parameter-Gate (findet F-02/F-03 vor der Ausführung), Workspace-Containment, Content-/Discovery-Vertrag, Hash-Determinismus. |

## Reihenfolge

```powershell
# 1. Ist-Zustand belegen (findet die Blocker, ohne etwas zu veraendern)
.\tests\Invoke-ContractTests.ps1 -ScriptPath "C:\Users\aowdg\Desktop\KI\LocalCloudCode\LocalCloudCode.ps1"

# 2. Aenderung vorher ansehen
.\tools\Update-PsFunctionBlock.ps1 `
    -TargetFile  "C:\Users\aowdg\Desktop\KI\LocalCloudCode\LocalCloudCode.ps1" `
    -NewBlockFile ".\patches\Get-DiscoveryResult.v1.2.ps1" `
    -WhatIf

# 3. Aenderung anwenden (legt automatisch ein Backup an)
.\tools\Update-PsFunctionBlock.ps1 `
    -TargetFile  "C:\Users\aowdg\Desktop\KI\LocalCloudCode\LocalCloudCode.ps1" `
    -NewBlockFile ".\patches\Get-DiscoveryResult.v1.2.ps1"

# 4. Gegenprüfen
.\tests\Invoke-ContractTests.ps1 -ScriptPath "C:\Users\aowdg\Desktop\KI\LocalCloudCode\LocalCloudCode.ps1"
```

Voraussetzung: **PowerShell 7.2 oder neuer.** Windows PowerShell 5.1 wird
bewusst nicht unterstützt (Befund F-16 im Prüfbericht).

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

# KI Programmierreferenz Gesamtpaket 2026

PROGRAMMING_LANGUAGE: MULTI-LANGUAGE
DOCUMENT_TYPE: Consolidated Engineering Reference
DATE: 2026-09-14

Dieses Paket vereinigt die bisherigen Programmier- und KI-Engineering-Referenzen
mit einer erweiterten Sprachabdeckung.

## Enthalten

Top-10-Arbeitsliste:
1. Python
2. JavaScript
3. Java
4. C++
5. C#
6. Go
7. PHP
8. TypeScript
9. Rust
10. R

Zusaetzlich:
11. Kotlin
12. Android Platform Reference

## Android

Kotlin ist als primaere moderne Android-Sprache aufgenommen.
Java bleibt fuer Android weiterhin relevant.
C++ ist fuer native Android-Komponenten ueber das NDK aufgenommen.

## Alte Inhalte

Die bisherigen Pakete bleiben vollstaendig unter `legacy/` erhalten:
- KI_Programmierreferenz_GitHub
- KI_Engineering_Memory

Damit geht kein bisheriger Inhalt verloren.

## Sprachkennzeichnung

Jede neue Sprachreferenz enthaelt:
`PROGRAMMING_LANGUAGE: ...`

Die Datei `docs/DOCUMENT_LANGUAGE_MAP.md` beschreibt die Zuordnung
fuer das gesamte Gesamtpaket.

## Ranking-Hinweis

Die Top-10-Reihenfolge wurde aus der vom Benutzer gelieferten Liste uebernommen.
Das Paket behauptet nicht, dass diese Reihenfolge hier unabhaengig neu verifiziert wurde.

## Grundregel

Die Referenz ersetzt keine aktuelle offizielle Dokumentation. Versionsabhaengige
Details sollen vor einer produktiven Umsetzung gegen die aktuelle Dokumentation
und das lokale Tooling geprueft werden.

---

## Inhalt

| Pfad | Inhalt |
|---|---|
| `review/PRUEFBERICHT-v9.2.md` | **Tiefenprüfung des aktuellen Stands.** 36 Befunde (P0/P1/P2/P3) mit Beweis, Ursache und konkreter Lösung. Einstiegspunkt. |
| `review/LocalCloudCode.v9.2.stand-2026-09-13.ps1` | Der geprüfte Code-Auszug (Transkription aus dem Chat, 822 Zeilen). Referenz für die Befundnummern. |
| `patches/Get-DiscoveryResult.v1.2.ps1` | Korrigierter, vollständiger Ersatz für `Get-DiscoveryResult` — behebt die Blocker F-02, F-03, F-04, F-08, F-09, F-10, F-13c, F-14. |
| `tools/Update-PsFunctionBlock.ps1` | Sicheres Patch-Werkzeug: ersetzt eine Funktion per **AST-Extent** statt per `-replace`, sichert, prüft, rollt automatisch zurück. |
| `reference/` | **Dauerreferenzen** (Regelwerke, Leitlinien) plus `reference/manifest.json` mit gepinntem SHA256 aller Dokumente — keine Copy-Paste-Übernahme. |
| `tools/Register-ReferenceDocument.ps1` | Registriert Referenzen (`-Path`), prüft sie (`-Verify`), listet sie (`-List`); verweigert stilles Überschreiben ohne `-Force`. |
| `tools/psparse/` | PowerShell-Parser und Parameter-Gate als Python-Werkzeug (tree-sitter) — prüft Syntax und interne Aufrufe, wenn kein PowerShell verfügbar ist. Findet F-02/F-03 vor der Ausführung. |
| `tests/Invoke-ContractTests.ps1` | Vertragstests, die **ohne** Start der Runtime laufen: Syntax, Smart-Quotes, Parameter-Gate (findet F-02/F-03 vor der Ausführung), Workspace-Containment, Content-/Discovery-Vertrag, Hash-Determinismus. |
| `tools/Collect-LocalCloudCodeSnapshot.ps1` | **READ-ONLY** Snapshot des echten Systems: Hashes, Parsergebnisse, Content-Schema, Backend-Status, Marker zu allen Befunden — ein ZIP für die Datenübergabe. |
| `legacy/KORREKTUREN.md` | **Protokoll der Korrekturen** an den Legacy-Paketen: welche Datei, welche Zeile, Vorher-/Nachher-SHA256, Rückrollweg. |
| `review/BEFUNDE-REFERENZPAKET-2026-09-14.md` | **Prüfung des hochgeladenen Referenzpakets** (Commit 2501970): 11 Befunde inkl. 2 harter Werkzeugfehler und einer CI, die nicht läuft. |
| `.github/workflows/reference-package.yml` | **Lauffähiger** GitHub-Actions-Workflow an der Stelle, die GitHub tatsächlich ausführt: Pflichtdateien, SHA256-Pins, Kodierung/Copy-Paste-Schäden, PowerShell-Syntax mit dem **echten** Parser. Schlägt bei Fehlern fehl. |
| `.gitattributes` | Hält Zeilenenden auf LF, damit die SHA256-Pins auf Windows wie auf Linux identisch sind. |
| `reference/manifest.json` | Gepinnte Hashes der Referenzdokumente (SHA256, Bytes, Zeichen, Zeilen, BOM) — prüfbar mit `Register-ReferenceDocument.ps1 -Verify`. |
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

# Korrekturen an den Legacy-Paketen

Diese Datei dokumentiert jede Änderung an Dateien unter `legacy/**` — mit Vorher-Hash,
Nachher-Hash und Begründung. Die Legacy-Ordner sind historische Paketstände; sie werden
nur dort angefasst, wo eine Datei nachweislich nicht lauffähig war. Jede Änderung ist
einzeln nachvollziehbar und einzeln umkehrbar.

**Wichtig zu den Hashes:** Angegeben ist der SHA256 der **im Repository gespeicherten**
Bytes (Zeilenenden LF). Die `.gitattributes` der Legacy-Ordner setzen `*.ps1 text eol=crlf`
— auf einem Windows-Checkout liegen die Dateien daher mit CRLF auf der Platte, und
`Get-FileHash` liefert dort **andere** Werte. Das ist keine Beschädigung, sondern die
Zeilenenden-Regel des Pakets selbst. Zum Vergleich immer die Repository-Fassung verwenden:

```powershell
# 1) Stimmt die Arbeitskopie mit dem Repository ueberein? (leere Ausgabe = identisch)
git diff --stat -- legacy/KI_Engineering_Memory/tools/

# 2) Byte-exakter Vergleich mit dem REPOSITORY-Inhalt (LF), nicht der Arbeitskopie:
#    cmd-Redirection schreibt die Bytes unveraendert (PowerShell-Operatoren wuerden
#    die Zeilenenden auf CRLF umstellen).
cmd /c "git show HEAD:legacy/KI_Engineering_Memory/tools/Update-KI-MachineProfile.ps1 > %TEMP%\probe.ps1"
Get-FileHash "$env:TEMP\probe.ps1" -Algorithm SHA256
# erwartet: 54E4C06B4DFE8D60720195F8C33E38C3C6144AB563D06DF57962DE81335AA4F4
```

---

## 1 — W-01: Komma-Ausdruck in der Argumentliste (2 Dateien)

**Dateien**

- `legacy/KI_Engineering_Memory/tools/Install-KI-Dauerreferenz-AutoUpdate.ps1`
- `legacy/KI_Engineering_Memory/tools/Install-KI-Engineering-Memory-AutoUpdate.ps1`

(beide byte-identisch, auch nach der Korrektur)

| | |
|---|---|
| vorher | `6012FD39DB05AB706D8E6B2FE2A1AFD61BD75C15F87D7822DD6358F85DC7C786`, 874 Bytes |
| nachher | `6D47C983F7E821E2C1B680E76E0F0E3C6BE81D79B8A7240428AE56E8720EB7A`, 913 Bytes |

**Befund.** Zeile 9:

```powershell
$triggers=@(New-ScheduledTaskTrigger -AtLogOn, New-ScheduledTaskTrigger -Daily -At 03:15)
```

Das Komma steht **innerhalb** der Argumentliste. In PowerShell trennt es dort keine zwei
Befehle, sondern erzeugt ein Array als Parameterwert — hier für den Schalter `-AtLogOn`.
Der echte Parser meldet: `Missing argument in parameter list.` Die Datei war damit nicht
laden- und nicht ausführbar.

**Korrektur.** Die Trigger einzeln bauen und als Array übergeben — Muster wörtlich aus
`legacy/KI_Programmierreferenz_GitHub/tools/Install-KI-Dauerreferenz-AutoUpdate.ps1`
(Zeilen 20, 21, 23), nicht neu erfunden:

```powershell
$trigger1=New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$trigger2=New-ScheduledTaskTrigger -Daily -At 03:15
Register-ScheduledTask ... -Trigger @($trigger1,$trigger2) ...
```

**Nicht geändert:** Taskname, Zielskript, Beschreibung, Installationspfad. Die Datei
registriert weiterhin genau dieselbe Aufgabe wie zuvor.

---

## 2 — W-12: ungültige Variablenreferenz in einer Zeichenkette (2 Dateien)

**Dateien**

- `legacy/KI_Engineering_Memory/tools/Update-KI-Dauerreferenz.ps1`
- `legacy/KI_Engineering_Memory/tools/Update-KI-MachineProfile.ps1`

(beide byte-identisch, auch nach der Korrektur)

| | |
|---|---|
| vorher | `C355E90F5F04564585A90555B08EE9BAEBD9396F0015D0A9543B63E0FFF6817D`, 2941 Bytes |
| nachher | `54E4C06B4DFE8D60720195F8C33E38C3C6144AB563D06DF57962DE81335AA4F4`, 2945 Bytes |

**Befund.** Zeile 29 und Zeile 31, jeweils dasselbe Muster:

```powershell
foreach($k in $apps.Keys){$md += "- $k: $($apps[$k])"}
foreach($k in $apis.Keys){$md += "- $k: online=$($apis[$k].online), ..."}
```

In PowerShell ist der Doppelpunkt nach einer Variablen die Scope- oder Laufwerksangabe
(`$env:PATH`, `$script:x`). Nach dem Doppelpunkt muss ein Namenszeichen folgen — in
`"- $k: "` folgt ein Leerzeichen. Der echte Parser meldet:
`Variable reference is not valid. ':' was not followed by a valid variable name character.`
Beide Dateien ließen sich nicht laden.

Der echte Parser meldet nur die **erste** fehlerhafte Zeile je Datei. Es sind zwei — beide
wurden korrigiert.

**Korrektur.** Geschweifte Klammern begrenzen den Namen eindeutig:

```powershell
$md += "- ${k}: $($apps[$k])"
```

**Nicht geändert:** Reihenfolge, Aufbau und Inhalt des erzeugten Maschinenprofils. Die
Ausgabe enthält weiterhin eine Zeile je Eintrag, nur mit korrekter Interpolation.

---

## 3 — Was ausdrücklich NICHT geändert wurde

| Punkt | Warum nicht |
|---|---|
| Die beiden Legacy-`.gitignore`/`.gitattributes` und die Legacy-Workflows | Unverändert aus der SAFE-ZIP zurückgelegt. |
| Alle übrigen Legacy-Dateien | Unverändert. Von 19 PowerShell-Dateien im Repository waren genau vier defekt (W-01, W-12). |
| `KI_Engineering_Memory/reference/*` und die Legacy-Dokumente | Unverändert — Belegstand. Die dort genannten Skriptnamen betreffen das Dauerreferenz-Paket, dessen Dateien unangetastet bleiben. |

Die Punkte **W-02** und **W-06** sind am 14.09.2026 ebenfalls entschieden und umgesetzt —
siehe Abschnitte 4 und 5.

---

## 4 — Prüfstand nach der Korrektur

| Prüfung | Ergebnis |
|---|---|
| tree-sitter-Vorfilter (`tools/psparse/pscheck.py`) | 17 von 17 Dateien ohne Problemknoten, Rückgabecode 0 |
| SHA256-Pins (`reference/manifest.json`) | aktualisiert nach der Änderung an `LOAD_INSTRUCTION.txt` |
| Parameter-Gate (`tools/psparse/paramgate.py`) | unverändert; der einzige Fund ist der bereits dokumentierte F-03 im Prüf-Auszug |
| **Echter PowerShell-Parser** | **PASS** — CI-Lauf 34879488698 (Commit 107e317): der Schritt „PowerShell - Syntax mit dem echten Parser" ist grün, alle 19 Dateien ohne Fehler |
| Zeichenkodierung | alle vier Dateien ASCII-only, UTF-8 ohne BOM, Zeilenenden LF |

**Rückrollen** einer einzelnen Korrektur:

```powershell
git checkout <commit-vor-der-korrektur> -- legacy/KI_Engineering_Memory/tools/Update-KI-MachineProfile.ps1
```

---

## 4 — W-06: `pwsh.exe` wird jetzt aufgelöst statt hart verdrahtet

**Datei:** `legacy/KI_Engineering_Memory/tools/Install-KI-Engineering-Memory-AutoUpdate.ps1`

| | |
|---|---|
| vorher | `6D47C983F7E821E2C1B680E76E0F0E3E6CBE81D79B8A7240428AE56E8720EB7A`, 913 Bytes |
| nachher | `580EC934BCBF84D34C5C86670329E6A8E9DD365D5E07BDD39F884099BF82E262`, 1079 Bytes |

**Befund.** Zeile 8 übergab `-Execute 'pwsh.exe'` als bloßen Namen. Die geplante Aufgabe
löst den Namen in ihrem eigenen Kontext auf — liegt `pwsh.exe` dort nicht im `PATH`,
schlägt der Lauf fehl, ohne dass die Ursache sichtbar wird.

**Korrektur.** Auflösung über `Get-Command`, mit Rückfall auf `powershell.exe` — Muster
wörtlich aus `legacy/KI_Programmierreferenz_GitHub/tools/Install-KI-Dauerreferenz-AutoUpdate.ps1`
(Zeilen 17 bis 19):

```powershell
$pwsh=(Get-Command pwsh.exe -ErrorAction SilentlyContinue).Source
if ([string]::IsNullOrWhiteSpace($pwsh)) { $pwsh=(Get-Command powershell.exe -ErrorAction Stop).Source }
$action=New-ScheduledTaskAction -Execute $pwsh -Argument (...)
```

**Nicht geändert:** Taskname, Zielskript, Beschreibung, Trigger-Logik und Installationspfad.
Die Datei registriert weiterhin dieselbe Aufgabe mit denselben Argumenten.

---

## 5 — W-02: die zwei falsch benannten Kopien wurden entfernt

Entschieden am 14.09.2026: **löschen** statt kennzeichnen. Die betroffenen Dateien hatten
einen Dateinamen, der nicht zu ihrem Inhalt passte; die korrekt benannte Fassung bleibt.

| entfernte Datei | Inhalt war tatsächlich | SHA256 (identisch mit der behaltenen Datei) | Größe |
|---|---|---|---|
| `legacy/KI_Engineering_Memory/tools/Update-KI-Dauerreferenz.ps1` | Maschinenprofil-Inventar | `54E4C06B4DFE8D60720195F8C33E38C3C6144AB563D06DF57962DE81335AA4F4` | 2945 B |
| `legacy/KI_Engineering_Memory/tools/Install-KI-Dauerreferenz-AutoUpdate.ps1` | Engineering-Memory-Task | `6D47C983F7E821E2C1B680E76E0F0E3E6CBE81D79B8A7240428AE56E8720EB7A` | 913 B |

**Warum das gefahrlos ist.** Beide Dateien waren byte-identische Kopien ihrer korrekt
benannten Nachbarn (`Update-KI-MachineProfile.ps1`, `Install-KI-Engineering-Memory-AutoUpdate.ps1`),
die beide erhalten bleiben. Nach der Löschung gibt es im Ordner `tools/` **kein Duplikat**
mehr — nachgeprüft über einen Hash-Vergleich aller verbleibenden Dateien.

Die echte Dauerreferenz-Logik liegt unverändert im Nachbarpaket:
`legacy/KI_Programmierreferenz_GitHub/tools/Update-KI-Dauerreferenz.ps1` (10.489 B) und
`.../Install-KI-Dauerreferenz-AutoUpdate.ps1` (1425 B) — beide unangetastet.

**Zurückholen** (die Bytes liegen unverändert in der Git-Historie):

```powershell
git checkout <commit-vor-der-loeschung> -- legacy/KI_Engineering_Memory/tools/Update-KI-Dauerreferenz.ps1
```

---

## 6 — W-05: die `.md`-Fassung ist jetzt ausdrücklich kanonisch

**Datei:** `reference/LOAD_INSTRUCTION.txt`

| | |
|---|---|
| vorher | `EA6B25AB2B265EC5D80094A15DB1B303C6E5E7376B5CFD11A1FF035EF6769FD2`, 1704 Bytes |
| nachher | `7F013059E6D7C44D9006AA5802E7EBB2FE38C19A77B698C38DADE0B71777288B`, 2118 Bytes |

*(Nachher-Wert gegen `reference/manifest.json` und den Dateiinhalt gegengeprüft — identisch.)*

Eingefügt nach der Titelzeile, in der Sprache der Datei (Englisch), ASCII-only:

```
Canonical reference document:
reference/KI_Dauerreferenz_Programmierung_und_Softwarequalitaet_DE.md
This is the authoritative version. Load it whenever a programming reference is required.

Reduced variant - NOT canonical, do not load it instead of the canonical file:
reference/KI_Dauerreferenz_Programmierung_und_Softwarequalitaet_DE.txt
It contains no code-block markers and no AUTO-MACHINE-INVENTORY section.
```

**Wichtig:** Diese Datei ist hash-gepinnt. Der Pin in `reference/manifest.json` wurde im
selben Schritt aktualisiert — sonst hätte die Integritätsprüfung zu Recht Alarm geschlagen.

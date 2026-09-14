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
| **W-02** — `Update-KI-Dauerreferenz.ps1` ist eine byte-identische Kopie von `Update-KI-MachineProfile.ps1` (Name ≠ Inhalt) | Das ist eine Benennungsentscheidung, keine Fehlfunktion. Beide Dateien sind nach der Korrektur lauffähig; welche Namen bleiben sollen, entscheidet der Eigentümer. |
| **W-06** — `-Execute 'pwsh.exe'` fest verdrahtet | Robustheitsfrage, kein Syntaxfehler. Die Datei ist jetzt ausführbar. Die robustere Auflösung über `Get-Command` steht in der Nachbardatei bereit. |
| Die beiden Legacy-`.gitignore`/`.gitattributes` und die Legacy-Workflows | Unverändert aus der SAFE-ZIP zurückgelegt. |
| Alle übrigen Legacy-Dateien | Unverändert. Von 19 PowerShell-Dateien im Repository waren genau diese vier defekt. |

---

## 4 — Prüfstand nach der Korrektur

| Prüfung | Ergebnis |
|---|---|
| tree-sitter-Vorfilter (`tools/psparse/pscheck.py`) | 19 von 19 Dateien ohne Problemknoten, Rückgabecode 0 |
| Parameter-Gate (`tools/psparse/paramgate.py`) | unverändert; der einzige Fund ist der bereits dokumentierte F-03 im Prüf-Auszug |
| **Echter PowerShell-Parser** | **PASS** — CI-Lauf 34879488698 (Commit 107e317): der Schritt „PowerShell - Syntax mit dem echten Parser" ist grün, alle 19 Dateien ohne Fehler |
| Zeichenkodierung | alle vier Dateien ASCII-only, UTF-8 ohne BOM, Zeilenenden LF |

**Rückrollen** einer einzelnen Korrektur:

```powershell
git checkout <commit-vor-der-korrektur> -- legacy/KI_Engineering_Memory/tools/Update-KI-MachineProfile.ps1
```

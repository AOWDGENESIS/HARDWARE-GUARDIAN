# Befunde: KI-Programmierreferenz-Paket — Prüfung vom 14.09.2026

**Prüfgegenstand:** `AOWDGENESIS/Entwicklungen`, Branch `main`, Commit `2501970` („Add files via upload", 14.09.2026 17:51:59 UTC)
**Umfang:** 51 Dateien im Repository, 55 in der beiliegenden ZIP
**Prüfmethode:** Byte-exakter Abgleich (SHA256 je Datei), echter `git clone`, ZIP-Analyse, Referenz-Lint (dieselbe Logik wie `Review-ReferenceDocument.ps1`, kalibriert an echten Dokumenten), tree-sitter-Vorfilter **plus** Gegenprüfung jedes Verdachtsfalls.

---

## 0. Zusammenfassung

**Die Dokumente sind gut. Die Werkzeuge haben zwei echte Defekte. Die CI läuft nicht.**

| | Bewertung |
|---|---|
| Referenzdokumente (Inhalt, Kodierung, Konsistenz) | **sauber** — 0 Smart-Quotes, 0 Dubletten, 0 Widersprüche, 0 Platzhalter, UTF-8 ohne BOM, Codeblöcke ausbalanciert |
| PowerShell-Werkzeuge | **2 harte Fehler** (W-01, W-02) + 2 kleinere (W-06, W-07) |
| Automatische Prüfung (CI) | **läuft nicht** (W-03) — und würde auch dann nichts prüfen |
| Repository-Vollständigkeit | **6 Dateien fehlen** gegenüber der ZIP (W-04) |
| Integritätsnachweis (Hashes) | **fehlt vollständig** (W-05) |

---

## 1. Was gut ist (und bleiben soll)

Das ist keine Höflichkeit, sondern gemessen:

- **Referenzdokument `…DE.md` (43.293 B, 1.183 Zeilen):** 0 typografische Anführungszeichen, 0 geschützte Leerzeichen, 0 Dubletten, 0 ähnliche Zeilenpaare, 0 Widerspruchskandidaten, 0 Platzhalter, 40 Codeblock-Marker (gerade = alle geschlossen), gültiges UTF-8 ohne BOM. Ein Dokument in diesem Zustand ist die Ausnahme, nicht die Regel.
- **`LOAD_INSTRUCTION.txt`:** 15 Prioritätsregeln, die genau die Fehlerklassen adressieren, die wir in dieser Sitzung gefunden haben — Regel 4 („keine erfundenen Testergebnisse"), 10 („Regression vor Erfolgsmeldung"), 11 („fail closed"), 12 („keine Secrets ins Repository"), 13 („UTF-8 ohne BOM"), 15 („stabile Policy von volatilen Maschinendaten trennen"). Das ist die richtige Systematik.
- **`.gitignore`** (in der ZIP, siehe W-04): exkludiert korrekt `runtime/KI_Maschinenprofil.json|md`, Logs, Caches, `.env`. Die Kommentarzeile „Local machine data. Never publish this." sitzt an der richtigen Stelle.
- **`Publish-ToGitHub.ps1`:** Standard-Sichtbarkeit `private`, nutzt `gh` (keine Tokens im Skript). Richtig entschieden.
- **`.gitattributes`:** erzwingt `eol=lf` für `.md`/`.txt`/`.json`/`.yml` und `eol=crlf` nur für `.ps1`. Das stabilisiert Hashes — die Voraussetzung dafür, dass ein Hash-Pin überhaupt funktioniert.
- **Trennung stabil/volatil:** Der AUTO-MACHINE-INVENTORY-Abschnitt wird ausschließlich vom Updater erzeugt. Genau die Trennung, die Regel 15 fordert und die in der Praxis fast nie gemacht wird.

---

## 2. Harte Befunde

### W-01 — Syntaxfehler: Komma-Ausdruck in der Argumentliste (2 Dateien)

**Dateien:**
- `legacy/KI_Engineering_Memory/tools/Install-KI-Dauerreferenz-AutoUpdate.ps1`
- `legacy/KI_Engineering_Memory/tools/Install-KI-Engineering-Memory-AutoUpdate.ps1`
(beide byte-identisch, SHA256 `6012FD39DB05AB70…`)

**Zeile 9:**
```powershell
$triggers=@(New-ScheduledTaskTrigger -AtLogOn, New-ScheduledTaskTrigger -Daily -At 03:15)
```

**Problem:** Das Komma steht **innerhalb** der Argumentliste eines Befehls. In PowerShell erzeugt ein Komma dort ein **Array als Parameterwert** — es trennt keine zwei Befehle. Gemeint waren zwei Trigger, gebaut wird einer, der ein Array an einen Switch-Parameter (`-AtLogOn`) übergeben soll.

**Belegt durch:** Fehlertext des Parsers `', New-ScheduledTaskTrigger -Daily -At 03'`, und durch die **korrigierte Fassung im Nachbarordner**, die genau dasselbe anders (richtig) löst:

```powershell
$trigger1=New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$trigger2=New-ScheduledTaskTrigger -Daily -At 03:15
Register-ScheduledTask ... -Trigger @($trigger1,$trigger2) ...
```

**Lösung:** Zeile 9 durch die drei Zeilen aus `legacy/KI_Programmierreferenz_GitHub/tools/Install-KI-Dauerreferenz-AutoUpdate.ps1` ersetzen (Zeilen 20, 21, 23 dieser Datei).

**Verifikation auf deinem PC** (verbindlich — mein Parser ist nur Vorfilter):
```powershell
$f = "$env:USERPROFILE\Desktop\KI\...\Install-KI-Engineering-Memory-AutoUpdate.ps1"
$e=$null;$t=$null
[System.Management.Automation.Language.Parser]::ParseFile($f,[ref]$t,[ref]$e) | Out-Null
"Fehler: $(@($e).Count)"
@($e) | Select-Object @{n='Zeile';e={$_.Extent.StartLineNumber}}, Message
```

---

### W-02 — Dateiname ≠ Dateiinhalt: zwei falsch benannte Kopien

Im Ordner `legacy/KI_Engineering_Memory/tools/` liegen **zwei Dateipaare mit identischem Inhalt**:

| Datei | Größe | SHA256 (16) | Inhalt ist tatsächlich |
|---|---|---|---|
| `Update-KI-Dauerreferenz.ps1` | 2.941 B | `c355e90f5f045645` | **Maschinenprofil-Inventar** |
| `Update-KI-MachineProfile.ps1` | 2.941 B | `c355e90f5f045645` | Maschinenprofil-Inventar |
| `Install-KI-Dauerreferenz-AutoUpdate.ps1` | 874 B | `6012fd39db05ab70` | **Engineering-Memory-Task** |
| `Install-KI-Engineering-Memory-AutoUpdate.ps1` | 874 B | `6012fd39db05ab70` | Engineering-Memory-Task |

**Belege für die Fehlbenennung:**
- Im Skript `Update-KI-Dauerreferenz.ps1` kommt das Wort „Dauerreferenz" **0×** vor, „Maschinenprofil/MachineProfile" **2×**. Es schreibt nach `$env:LOCALAPPDATA\KI-Engineering-Memory\runtime` und erzeugt JSON (`ConvertTo-Json -Depth 8`).
- Das „Dauerreferenz"-AutoUpdate registriert den Task `KI-Engineering-Memory-Runtime-Update` und startet `tools\Update-KI-MachineProfile.ps1`.

**Konsequenz:** Wer im Ordner `KI_Engineering_Memory` das Skript mit dem Namen „Dauerreferenz" aufruft, bekommt Maschinenprofil-Verhalten. Zwei von vier Dateien sind irreführend benannt.

**Lösung:** Im Legacy-Ordner je Datei genau eine Fassung behalten — die echte Dauerreferenz-Logik liegt korrekt in `legacy/KI_Programmierreferenz_GitHub/tools/Update-KI-Dauerreferenz.ps1` (10.489 B). Die beiden falsch benannten Kopien entweder löschen oder in der README des Legacy-Ordners ausdrücklich als „historischer Doppelname" kennzeichnen.

---

### W-03 — Die CI läuft nicht (und würde nichts prüfen)

**Zwei Befunde in einem:**

**(a) Falscher Ort.** Die Workflows liegen unter
`legacy/KI_Programmierreferenz_GitHub/.github/workflows/validate.yml` und
`legacy/KI_Engineering_Memory/.github/workflows/validate.yml`.

GitHub führt **ausschließlich** Workflows unter `.github/workflows/` im **Repository-Wurzelverzeichnis** aus. Workflows in Unterordnern werden ignoriert — sie laufen nie. Im Root liegt nur `.github/dependabot.yml`. Es findet also **keine** automatische Prüfung statt.

**(b) Wirkungslose Parserprüfung.** Im Workflow steht:
```yaml
- name: Check PowerShell parser where available
  shell: pwsh
  run: |
    [System.Management.Automation.Language.Parser]::ParseFile('tools/Update-KI-Dauerreferenz.ps1',[ref]$null,[ref]$null) | Out-Null
```

`ParseFile` schreibt Parserfehler in den `$Errors`-Parameter. Hier wird er verworfen (`[ref]$null`, Ergebnis in `Out-Null`) und der Exit-Code nicht geprüft. **Dieser Schritt kann nie fehlschlagen** — die Prüfung ist reine Dekoration. Ein grüner Haken, der nichts bedeutet: dieselbe Fehlerklasse wie „PATCH: PASS" aus deinem letzten Patchlauf.

**Lösung — fertig geliefert:** `.github/workflows/validate.yml` in diesem Repository.
- Gehört nach `<repo-root>/.github/workflows/validate.yml`
- Prüft: Pflichtdateien, Hash-Manifest, Dokumentintegrität (BOM, Smart-Quotes in Codebereichen, unbalancierte Codeblöcke), **PowerShell-Syntax mit dem echten Parser** — und **schlägt fehl**, wenn etwas nicht stimmt.
- `STRICT_LEGACY: 'true'` (Default): Verstöße in `legacy/**` brechen den Lauf ab. Auf `'false'` setzen, wenn die Legacy-Dateien während der Übergangszeit nur gewarnt werden sollen.

**Erwartung:** Der Workflow wird **rot**, solange W-01 nicht behoben ist — genau das ist sein Zweck. Das ist kein Nebeneffekt, den man wegkonfigurieren sollte, sondern der erste ehrliche Prüflauf in diesem Projekt.

---

### W-04 — 6 Dateien fehlen im Repository

Beim Upload über die GitHub-Weboberfläche wurden die Dotfiles nicht mitgenommen. Die ZIP enthält sie:

| Datei | nur in ZIP | Im Repo vorhanden |
|---|---|---|
| `legacy/KI_Programmierreferenz_GitHub/.gitignore` | ✔ | ✖ |
| `legacy/KI_Engineering_Memory/.gitignore` | ✔ | ✖ |
| `legacy/KI_Programmierreferenz_GitHub/.gitattributes` | ✔ | ✖ |
| `legacy/KI_Engineering_Memory/.gitattributes` | ✔ | ✖ |
| `legacy/KI_Programmierreferenz_GitHub/.github/workflows/validate.yml` | ✔ | ✖ |
| `legacy/KI_Engineering_Memory/.github/workflows/validate.yml` | ✔ | ✖ |

**Warum das zählt:** Die `.gitignore` ist der Schutz davor, lokale Maschinendaten (`runtime/KI_Maschinenprofil.json`) zu veröffentlichen. `Publish-ToGitHub.ps1` macht `git add .` — **ohne `.gitignore` wird alles gestaged, was im Ordner liegt.** Die Schutzwirkung existiert nur, wenn die Datei im jeweiligen Arbeitsbaum vorhanden ist.

**Lösung:**
```powershell
# Dotfiles in ein neues Repository hochladen geht per Web-Upload nicht zuverlaessig.
# Stattdessen: die ZIP-Variante verwenden, oder
git clone https://github.com/AOWDGENESIS/<repo>.git
# Dateien aus der ZIP hineinkopieren, dann
git add .gitignore .gitattributes .github
git commit -m "Restore .gitignore, .gitattributes and CI workflow"
git push
```
Prüfen, ob es angekommen ist:
```powershell
git ls-files | Select-String '\.gitignore|\.gitattributes|workflows'
```

---

### W-05 — Zwei Dokumentvarianten ohne Kanonik-Regel

Es liegen zwei Fassungen desselben Dokuments vor, mit **unterschiedlichem Inhalt** (nicht nur anderer Endung):

| | `…DE.md` | `…DE.txt` |
|---|---|---|
| Bytes | 43.293 | 42.182 |
| Zeilen | 1.183 | 1.174 |
| Codeblock-Marker | 40 | **0** |
| Abschnitt `AUTO-MACHINE-INVENTORY` | vorhanden | **fehlt** |

`LOAD_INSTRUCTION.txt` sagt **nicht**, welche Fassung zu laden ist. Ein Modell, das nur die `.txt` bekommt, sieht Codeblöcke nicht als Blöcke und verliert den Inventarabschnitt — ohne dass irgendwo dokumentiert wäre, dass das beabsichtigt ist.

**Lösung — eine Zeile in `LOAD_INSTRUCTION.txt`:**
```
Canonical document: reference/KI_Dauerreferenz_Programmierung_und_Softwarequalitaet_DE.md
Reduced variant:   reference/KI_Dauerreferenz_Programmierung_und_Softwarequalitaet_DE.txt
                   (keine Codeblock-Marker, kein AUTO-MACHINE-INVENTORY-Abschnitt)
```
Und alle Hashes in `reference/hashes.json` eintragen (Vorlage: `review/reference-hashes-2026-09-14.json`).

---

## 3. Kleinere Befunde

| ID | Befund | Lösung |
|---|---|---|
| **W-06** | `Install-KI-Engineering-Memory-AutoUpdate.ps1`, Zeile 8: `-Execute 'pwsh.exe'` hart verdrahtet. Die geplante Aufgabe schlägt fehl, wenn `pwsh.exe` im Task-Kontext nicht im `PATH` liegt. | Auflösen wie in der neueren Fassung: `$pwsh=(Get-Command pwsh.exe -ErrorAction SilentlyContinue).Source` mit Fallback auf `powershell.exe` |
| **W-07** | `Publish-ToGitHub.ps1`, Zeile 32: `git config user.email ((gh api user --jq .email) \| Out-String).Trim()`. Bei privater GitHub-E-Mail liefert das leer; mit `$ErrorActionPreference = 'Stop'` bricht das Skript ab, bevor committet wird. | Auf leeren Wert prüfen und einen Ersatz setzen: `if (-not $email) { $email = "$owner@users.noreply.github.com" }` |
| **W-08** | `Publish-ToGitHub.ps1` macht `git add .` **vor** jeder Prüfung auf sensible Inhalte. | Vor dem `git add` prüfen: `git status --short` anzeigen und auf `runtime/`, `*.log`, `.env`, `*Maschinenprofil*` in der Ausgabe bestehen bleiben lassen |
| **W-09** | Die AutoUpdate-Skripte registrieren eine **geplante Aufgabe** (bei Anmeldung + täglich 03:15, `-ExecutionPolicy Bypass`). Das ist ein dauerhafter Mechanismus im Benutzerkontext. | Bewusste Entscheidung dokumentieren; Entfernen ist vorgesehen (`-Remove`), das ist gut gelöst |
| **W-10** | `KI_Engineering_Memory/tools/Publish-ToGitHub.ps1`: `Set-Location $Root` ohne Rückstellung (kein `Push-Location`/`Pop-Location`). | `Push-Location`/`finally { Pop-Location }` wie in der neueren Fassung |
| **W-11** | `docs/DOCUMENT_LANGUAGE_MAP.md` verweist auf `*.py`, `*.js` usw. „if present in future projects" — die Sprachreferenzen existieren, aber es gibt keine einzige echte Code-Datei im Paket, gegen die die Zuordnung geprüft werden könnte. | Nicht falsch, aber die Tabelle ist derzeit nicht überprüfbar. Beim ersten echten Projekt gegenprüfen |

---

## 4. Was ausdrücklich **kein** Befund ist

Ehrlichkeit in beide Richtungen — das habe ich geprüft und **verworfen**:

- **„UTF-8" als doppelte Regel-ID** (Zeilen 225/319): Falschmeldung meiner ID-Regex. „UTF-8" sieht wie `ABC-123` aus, ist aber eine Zeichenkodierung, keine Regel-ID.
- **`1GB`-Literale, `Select-Object Name,DriverVersion,AdapterRAM`**: Mein tree-sitter-Parser meldet diese als Fehler — **beides ist gültiges PowerShell**. Nachgewiesen mit Minimaltests; der Parser ist entsprechend dokumentiert und vorgefiltert (`tools/psparse/pscheck.py`).
- **Kommentar `# ASCII-only. Windows PowerShell 5.1 / PowerShell 7.`**: vom Parser als Fehler gemeldet, ebenfalls ein Artefakt. Ein deutschsprachiger Kommentar an derselben Stelle parst fehlerfrei — der Fehler liegt im Parser, nicht im Code.
- **Zeilenenden**: `.gitattributes` erzwingt `eol=lf` für Textdateien; alle hier berechneten Hashes gelten für diese Normalisierung. Auf deinem Windows-Rechner müssen die Dateien nach einem frischen `git clone`/Checkout dieselben Bytes haben — bitte mit `Get-FileHash` gegen `review/reference-hashes-2026-09-14.json` gegenprüfen.

---

## 5. Nächste Schritte, in dieser Reihenfolge

1. **W-04 zuerst:** `.gitignore`, `.gitattributes` und die Workflows ins Repository bringen. Ohne `.gitignore` droht beim nächsten `Publish-ToGitHub.ps1`-Lauf das Veröffentlichen lokaler Maschinendaten.
2. **`.github/workflows/validate.yml`** nach `.github/workflows/validate.yml` kopieren → erster echter CI-Lauf.
3. **W-01 beheben** (Zeile 9) → der CI-Lauf wird grün.
4. **W-05:** kanonische Fassung in `LOAD_INSTRUCTION.txt` festlegen + `reference/hashes.json` anlegen. Vorlage: `review/reference-hashes-2026-09-14.json`.
5. **W-02:** die zwei falsch benannten Kopien im Legacy-Ordner bereinigen.
6. Danach: W-06 bis W-11.

**Bewusst noch nicht angefasst:** Ich habe in deinen Repositories ausschließlich **gelesen**. Es wurde nichts geändert, nichts gemergt, kein Branch angelegt — nur Klon, Analyse, Bericht. Deine `main` ist unverändert bei `2501970`.

---

## 6. Nachtrag: Umsetzungsstand

Branch `arena/01a09c80-entwicklungen`, Pull Request nach `main` offen.

| Befund | Status | Beleg |
|---|---|---|
| W-03 CI läuft nicht (falscher Ort) | **behoben** | `.github/workflows/validate.yml` liegt jetzt dort, wo GitHub ausführt |
| W-03 Parserprüfung wirkungslos | **behoben** | Der Schritt wertet `$Errors` aus, gibt `::error` aus und endet mit Exit 1 |
| W-04 sechs Dateien fehlen | **behoben** | alle sechs byte-identisch aus der SAFE-ZIP zurückgelegt (`cmp` gegen die ZIP-Dateien: identisch) |
| W-05 Hash-Pins fehlen | **behoben** | `reference/manifest.json` mit vier Einträgen: SHA256, Bytes, Zeichen, Zeilen, BOM |
| W-05 Kanonik `.md`/`.txt` | **offen** | braucht deine Entscheidung — der CI prüft derzeit beide Fassungen |
| W-01 Syntaxfehler | **behoben** (14.09.2026, Variante A) | Trigger einzeln gebaut; Vorher/Nachher-Hash in `legacy/KORREKTUREN.md` |
| W-12 ungültige Variablenreferenz | **behoben** (14.09.2026, Variante A) | `"${k}: "` statt `"$k: "`, Zeile 29 und 31; siehe `legacy/KORREKTUREN.md` |
| W-02 falsch benannte Kopien | **offen** | braucht deine Entscheidung; der Bericht nennt die korrekte Fassung |

### Erwartetes Ergebnis des ersten CI-Laufs

Rot. Zwei Dateien, beide mit demselben Fehler in Zeile 9:

```
FEHLER  legacy/KI_Engineering_Memory/tools/Install-KI-Dauerreferenz-AutoUpdate.ps1
FEHLER  legacy/KI_Engineering_Memory/tools/Install-KI-Engineering-Memory-AutoUpdate.ps1
```

Das ist beabsichtigt: der Baum enthält tatsächlich zwei Dateien, die PowerShell nicht
parsen kann. Zwei Wege, damit umzugehen — **deine Entscheidung**:

- **A (empfohlen):** Zeile 9 korrigieren. Die korrekte Fassung steht in
  `legacy/KI_Programmierreferenz_GitHub/tools/Install-KI-Dauerreferenz-AutoUpdate.ps1`.
- **B:** in `.github/workflows/validate.yml` `STRICT_LEGACY: 'true'` auf `'false'` setzen.
  Dann bleiben die Legacy-Fehler als Warnung sichtbar, der Lauf wird grün.

**Entschieden und umgesetzt:** Variante A. Die vier defekten Dateien sind korrigiert
(Details, Vorher-/Nachher-Hashes und Rückrollweg in `legacy/KORREKTUREN.md`). Variante B
wurde nicht gewählt — `STRICT_LEGACY` bleibt auf `'true'`, damit neue Fehler weiterhin
hart auffallen.

### Vier Dinge, die beim Umsetzen aufgefallen sind

1. **`main` und dieser Branch hatten keinen gemeinsamen Vorfahren.**
   `git merge` brach mit `refusing to merge unrelated histories` ab. Ursache: `main`
   beginnt heute bei `55677ce` („Add GitHub Actions to Dependabot updates") — ein
   Root-Commit; der frühere Anfang `90a1540` ist nicht mehr Teil der Historie.
   Der Merge wurde deshalb mit `--allow-unrelated-histories` geführt.

2. **`README.md` wurde beim Upload überschrieben.** Aus dem 16-Byte-`# Entwicklungen`
   wurde die 1.415-Byte-Paket-README. Beim Merge (add/add-Konflikt) ist der Pakettext
   **byte-identisch** übernommen und nur angehängt worden — die ersten 1.415 Bytes der
   neuen Datei sind per `cmp` als identisch nachgewiesen (Ergebnis oben in Abschnitt 1
   dieses Nachtrags dokumentiert, Prüfung reproduzierbar mit
   `git show origin/main:README.md | head -c 1415 | cmp - <(head -c 1415 README.md)`).

3. **Zeilenenden entscheiden über die Gültigkeit der Hashes.** Es lag keine
   Wurzel-`.gitattributes` vor. Ohne eine solche wandelt Git beim Checkout unter
   Windows LF in CRLF; jede Hashprüfung gegen `reference/manifest.json` hätte dort
   fehlgeschlagen, obwohl inhaltlich nichts falsch ist. Die neue Datei setzt deshalb
   `* text=auto eol=lf` und `*.zip binary` — bewusst **ohne** Sonderregel für `.ps1`,
   damit `Get-FileHash` auf jedem System dieselben Werte liefert.
   **Folge, die du kennen musst:** Für `legacy/**` gilt weiterhin die dort mitgelieferte
   Regel `*.ps1 text eol=crlf`. Die `.ps1`-Dateien unter `legacy/**` werden auf deinem
   PC also mit CRLF ausgecheckt; ihre `Get-FileHash`-Werte weichen dann von den hier
   dokumentierten ab (diese beziehen sich auf die im Repository gespeicherten Bytes, und
   die sind LF — nachgemessen, nicht angenommen). Die gepinnten Referenzdokumente sind
   nicht betroffen: sie liegen unter `reference/` und bleiben LF.

4. **Zwei Fehler in meiner eigenen Arbeit, die die Probe gefunden hat.**
   - `reference/README.md` enthielt die verbotenen typografischen Anführungszeichen als
     *Beispiel* im Inline-Code — also genau das, was die Prüfung sucht. Statt die
     Prüfung aufzuweichen, sind die Zeichen jetzt als Codepoint benannt
     (U+201E, U+201C, U+201D). Das Dokument hält damit die Regel ein, die es beschreibt.
   - Die Zusammenfassung des Workflows schrieb nach `$GITHUB_STEP_SUMMARY` ohne
     Fallback und brach ab, wenn die Variable nicht gesetzt ist. Jetzt:
     `${GITHUB_STEP_SUMMARY:-/dev/null}`.

   Die Probe selbst: die `bash`-Schritte des Workflows wurden aus der YAML-Datei
   extrahiert und auf einer Kopie des Baums ausgeführt. Der `pwsh`-Schritt konnte hier
   nicht laufen (im Sandkasten ist kein PowerShell installiert) — dort ist der echte
   Parser auf GitHub die Prüfinstanz, und ich behaupte nichts anderes.

---

## 7. Nachtrag 2: Was der erste echte CI-Lauf gefunden hat

Der Workflow ist auf PR #4 gelaufen (Lauf `34878640404`) und war **rot** — wie angekündigt.
Aber **nicht nur** wegen W-01: der echte PowerShell-Parser hat drei Fehler gefunden, die mein
tree-sitter-Vorfilter als „OK" gemeldet hatte.

**Korrektur meiner eigenen Ankündigung:** Ich hatte geschrieben, der Lauf zeige „genau diesen
Fehler" in zwei Dateien. Es sind **fünf**. Meine Angabe war falsch, der echte Parser hat sie
korrigiert:

| Datei | Zeile | Meldung des echten Parsers | Status |
|---|---|---|---|
| `legacy/…/Install-KI-Dauerreferenz-AutoUpdate.ps1` | 9 | `Missing argument in parameter list` | W-01, bekannt |
| `legacy/…/Install-KI-Engineering-Memory-AutoUpdate.ps1` | 9 | `Missing argument in parameter list` | W-01, bekannt |
| `legacy/…/Update-KI-MachineProfile.ps1` | 29 | `Variable reference is not valid. ':' was not followed by a valid variable name character` | **W-12, neu** |
| `legacy/…/Update-KI-Dauerreferenz.ps1` | 29 | dieselbe Meldung | **W-12, neu** |
| `review/LocalCloudCode.v9.2.stand-2026-09-13.ps1` | 818 | `Missing closing '}' in statement block` | **W-13, kein Defekt** |

### W-12 — zwei weitere Dateien sind nicht ausführbar

Zeile 29 (und 31) beider 2941-Byte-Dateien:

```powershell
foreach($k in $apps.Keys){$md += "- $k: $($apps[$k])"}
```

`$k:` — in PowerShell ist der Doppelpunkt nach einer Variablen die Scope- bzw.
Laufwerksangabe (`$env:PATH`, `$script:x`). `"$k: "` ist deshalb kein gültiger Ausdruck:
nach dem Doppelpunkt müsste ein Namenszeichen folgen, es folgt ein Leerzeichen. Beide Dateien
lassen sich damit **nicht einmal laden** — sie sind also nicht nur falsch benannt (W-02),
sondern zusätzlich defekt. Der Updater hat auf diesem Rechner nie gearbeitet.

**Lösung** — geschweifte Klammern setzen, beide Zeilen:

```powershell
foreach($k in $apps.Keys){$md += "- ${k}: $($apps[$k])"}
foreach($k in $apis.Keys){$md += "- ${k}: online=$($apis[$k].online), ..."}
```

Der echte Parser meldet nur die **erste** fehlerhafte Zeile je Datei. Es sind zwei — deshalb
gibt der Workflow jetzt bis zu fünf Meldungen je Datei aus.

### W-13 — die Transkription ist am Ende abgeschnitten (kein Defekt im Code)

`review/LocalCloudCode.v9.2.stand-2026-09-13.ps1` endet mitten in einem Ausdruck; die letzte
Zeile lautet `).TrimS`. Nachgemessen: die Klammerbilanz endet bei **+3** — drei Blöcke sind
offen, weil der Auszug dort abbricht. Die Datei ist ein **Beleg**, kein ausführbarer Code
(Transkription eines Chat-Auszugs) und kann eine Syntaxprüfung nicht bestehen.

**Konsequenz im Workflow:** `review/**` ist von der Syntaxprüfung ausgenommen, mit Begründung
und Nachweis direkt im Workflow. **Wichtig:** Zeilennummern in dieser Datei sind die Nummern
des Auszugs — über die echte `LocalCloudCode.ps1` auf dem PC sagen sie nichts.

### W-14 — mein Vorfilter hat drei Fehler übersehen (Methodik)

Das ist der wichtigste Punkt dieses Nachtrags. `tools/psparse/pscheck.py` hatte für drei
Dateien „PARSE: OK" gemeldet, die der echte Parser ablehnt — nachgewiesen durch den CI-Lauf,
nicht durch eine Vermutung.

Konsequenz, umgesetzt:

- Die Grenze ist in der Docstring des Werkzeugs dokumentiert, **mit der Lauf-ID als Beleg**.
- Es gibt eine zusätzliche Musterprüfung für die Klasse `"$name:"`. Sie ist ausdrücklich als
  `VERDACHT` gekennzeichnet (Musterprüfung, kein Syntaxbaum) und **nicht** als Parserfehler.
- Gegenprobe: die Prüfung findet genau die 4 echten Stellen (Zeile 29 und 31 in beiden
  Dateien) und erzeugt bei den übrigen 17 Dateien **keinen** Fehlalarm.

Und daraus die Regel, die jetzt ohne Einschränkung gilt: **„PARSE: OK" dieses Vorfilters ist
kein Beweis für gültige Syntax. Verbindlich ist ausschließlich der echte Parser.**

### Zusätzlich umgesetzt

| Änderung | Grund |
|---|---|
| Workflow umbenannt `validate.yml` → `reference-package.yml` | Auf einem anderen Branch dieses Repositories liegt bereits eine `validate.yml`. Zwei gleichnamige Dateien mit verschiedenem Inhalt kollidieren beim Zusammenführen. Der neue Name sagt außerdem, was geprüft wird. |
| `actions/checkout` v4 → v5 | Die v4-Warnung („Node.js 20 is deprecated") ist damit weg. |
| `review/**` von der Syntaxprüfung ausgenommen | siehe W-13 |
| Bis zu 5 Fehler je Datei statt nur der erste | siehe W-12 (zwei betroffene Zeilen) |

### Erwartung des nächsten Laufs

Rot, **vier** Dateien: 2 × W-01 (Zeile 9) und 2 × W-12 (Zeilen 29 und 31). Sonst nichts.
Die Prüfungen für Pflichtdateien, SHA256-Pins und Kodierung sind im ersten Lauf **grün**
gewesen — die Hash-Pins funktionieren also nachweislich.

### Bestätigung — nachgemessen, nicht behauptet

Der Lauf nach diesen Änderungen (Commit `8ed4fec`):

| Schritt | Ergebnis |
|---|---|
| Struktur - Pflichtdateien | **success** |
| Dokumentintegritaet - SHA256-Pins | **success** |
| Dokumente - Kodierung und Copy-Paste-Schaeden | **success** |
| PowerShell - Syntax mit dem echten Parser | **failure** (beabsichtigt) |
| Zusammenfassung | **success** |

Fehlerannotationen — genau die vorhergesagte Menge, nichts darüber hinaus:

```
Install-KI-Engineering-Memory-AutoUpdate.ps1:9    Missing argument in parameter list   (W-01)
Install-KI-Dauerreferenz-AutoUpdate.ps1:9         Missing argument in parameter list   (W-01)
Update-KI-MachineProfile.ps1:29 und :31           Variable reference is not valid      (W-12)
Update-KI-Dauerreferenz.ps1:29 und :31            Variable reference is not valid      (W-12)
```

`review/` wird nicht mehr gemeldet (W-13 wirksam). Die Hash-Pins und die Kodierungsprüfung
sind grün — die Integritätskette funktioniert also nachweislich, nicht nur theoretisch.

---

## 8. Bestätigung: Korrekturen vom echten Parser abgenommen

Nach den Korrekturen (Commit , CI-Lauf ):

| Schritt | Ergebnis |
|---|---|
| Struktur - Pflichtdateien | **success** |
| Dokumentintegritaet - SHA256-Pins | **success** |
| Dokumente - Kodierung und Copy-Paste-Schaeden | **success** |
| PowerShell - Syntax mit dem echten Parser | **success** |
| Zusammenfassung | **success** |

**Grün — und zwar nicht theoretisch.** Der echtе PowerShell-Parser hat alle 19 PowerShell-Dateien
im Repository geprüft, einschließlich der vier korrigierten. Damit ist bestätigt:

- W-01 ist behoben (beide )
- W-12 ist behoben (beide , Zeilen 29 und 31)
- die Korrekturen haben **keine** neuen Fehler erzeugt

Vorher/Nachher-Hashes, Begründung und Rückrollweg: .

### Offen

| Befund | Entscheidung nötig |
|---|---|
| W-05 — Kanonik / |  legt nicht fest, welche Fassung gilt |
| W-02 — zwei falsch benannte Legacy-Kopien | löschen oder als Doppelname kennzeichnen |
| W-06 —  fest verdrahtet | Robustheit; Datei ist jetzt lauffähig, also kein Blocker mehr |
| W-07 bis W-11 | kleinere Punkte aus Abschnitt 3 |

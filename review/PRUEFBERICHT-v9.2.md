# Tiefenprüfung LocalCloudCode v9.2 — Befunde, Beweise, Lösungen

**Prüfgegenstand:** `C:\Users\aowdg\Desktop\KI\LocalCloudCode\LocalCloudCode.ps1` (Stand aus dem Share-Chat vom 13.09.2026, ergänzt um den aktuellen Code-Auszug)
**Datum der Prüfung:** 2026-09-13
**Prüfmethode:** Zeilenweise Code-Analyse + echter PowerShell-Parser (`tree-sitter-powershell`) auf dem vorliegenden Code-Auszug + Abgleich mit dem Ausführungsprotokoll aus dem Share-Chat.

---

## 0. Was ich geprüft habe — und was nicht

### Geprüft
- Der komplette Code-Auszug im Umfang von 822 Zeilen (`review/LocalCloudCode.v9.2.stand-2026-09-13.ps1`), von `#requires` bis in `function Get-WorkspaceContent`.
- Der komplette Share-Chat inkl. deiner Konsolenausgaben (Fehlerkaskade, Run-Report, Regressionstest, Report-Diagnose).

### Nicht geprüft (kann ich hier nicht)
- Den **SHA256** deiner echten Datei. Meine Grundlage ist der eingefügte Auszug, nicht das Original.
- Die Teile, die im Auszug **fehlen**: `Get-WorkspaceSummary`, der komplette Main-Runtime-Block, der Runtime-Policy-Compiler, `settings.json`, die Prompt-Datei.
- Alles ab der Zeile, an der der Auszug mitten im Token `.TrimS` endet.

Damit du auf einer belastbaren Basis weiterarbeitest: Am Ende von Abschnitt 2 stehen **4 Prüfkommandos**, mit denen du in 30 Sekunden belegst, welcher Zustand in deiner echten Datei gilt.

### Rekonstruktionshinweis (wichtig für die Beweiskraft)
Der Auszug in `review/` ist eine **Transkription** aus dem Chat. Zeilennummern können minimal abweichen; ich zitiere deshalb fast immer Code-Schnipsel statt Zeilennummern. Die im Chat genannten Zeilennummern (514, 970–1020, 943–951) stammen aus deiner echten Datei und sind als solche gekennzeichnet.

### Nachweis, dass die Prüfung technisch trägt
Ich habe für die Syntaxanalyse einen **echten PowerShell-Parser** verwendet (tree-sitter-powershell). Beispielbefund, reproduzierbar:

| Variante | Parser-Ergebnis |
|---|---|
| Auszug **mit** schließendem `"` in `Write-Log` (Zeilen 1–756) | `PARSE: OK (keine ERROR/MISSING-Knoten)` |
| Auszug **ohne** schließendes `"` (der Zustand, den der Share-Chat zeigt) | `[ERROR] Zeile 57 Spalte 46 .. Zeile 59 Spalte 10` |

Der zweite Fall erzeugt genau die Fehlerkaskade, die in deiner Konsole stand (`Unerwartetes Token`, `Schließende „)" fehlt`, usw.). Damit ist F-01 mechanisch bewiesen und nicht geraten.

Zusätzlich habe ich das **Parameter-Gate aus Abschnitt 5** (dieselbe Logik, die in `tests/Invoke-ContractTests.ps1` läuft) gegen den vorliegenden Code gerichtet. Ergebnis — die beiden Hauptblocker, maschinell gefunden, nicht von Auge:

```
DATEI: LocalCloudCode.ps1 (Auszug)
Funktionen: 12 | Aufrufe interner Funktionen: 18
--------------------------------------------------------------------------
[UNKNOWN-PARAMETER] Zeile 550: Get-WorkspacePath -Workspace  (Funktion kennt: RequestedWorkspace)
[MISSING-MANDATORY] Zeile 571: Get-WorkspaceContent Zeile 571: Pflichtparameter fehlen: Path  [Splatting (Schluessel statisch bekannt)]

GESAMT PROBLEME: 2
```

Zeile 550 = **F-03**, Zeile 571 = **F-02**. Gegenprobe: Dieselbe Prüfung über die drei Dateien in diesem Repo (`tools/`, `tests/`, `patches/`) meldet `PARAMETER-GATE: PASS` — das Gate unterscheidet also korrekt zwischen Fehler und Nichtfehler.

**Wichtiger Hinweis zum Gate selbst:** Die erste Fassung des Gates meldete 11 Fehler in `Write-Log "…"`-Aufrufen — reine Falschmeldungen, weil der Aufruf das Argument **positional** übergibt. Ein Gate, das falsch anschlägt, ist schlimmer als kein Gate (es wird ignoriert). Die ausgelieferte Fassung berücksichtigt daher: benannte Parameter, positionale Bindung (die ersten N deklarierten Parameter) und aufgelöste Splat-Hashtabellen inklusive `$h['Key'] = …`-Zuweisungen.

---

## 1. Zusammenfassung — der ehrliche Befund

**Deine Runtime hat zwei völlig verschiedene Probleme, die im Chat als eines behandelt wurden.**

1. **Das Syntaxproblem** war ein *Kopierproblem*: ein fehlendes `"` am Ende von `Get-Date -Format "yyyy-MM-dd HH:mm:ss"`. Der Fehler ist trivial — aber die Ursache ist strukturell: Du arbeitest per **Copy-Paste aus dem Chat in ein 1.200-Zeilen-Produktivskript**, und Patches werden per `-replace` auf Quelltext angewendet. Das ist die eigentliche Ausfallursache, und sie wird ohne Werkzeugwechsel garantiert wiederkehren.

2. **Das eigentliche Funktionsproblem ist noch offen und wäre direkt nach dem Syntaxfix aufgetreten:** `Get-DiscoveryResult` ist gegen **andere Parameternamen und ein anderes Rückgabe-Schema** geschrieben als die Funktionen, die es aufruft. Die adaptive Parameter-Erkennung (`$ContentCommand.Parameters.ContainsKey(...)`) verdeckt das: sie prüft auf `WorkspacePath` und `Workspace`, während die echte Funktion `-Path` hat — und übergibt am Ende **gar keinen Pfad**. Ebenso ruft sie `Get-WorkspacePath -Workspace` auf, während der Parameter `-RequestedWorkspace` heißt. Beides sind harte Laufzeitfehler.

**Was gut ist und bleibt:** Masterprompt-Validierung, Compiler-Report-Gate, Backend-Auswahl, Run-Report, Workspace-Containment-Logik, Reparse-Point-Blockade, Evidence-Hash-Idee. Die Grundarchitektur ist tragfähig — die Beweisschicht hat Löcher.

**Bilanz: 35 Befunde** — 5 Blocker (P0, F-01…F-05), 16 gravierende Befunde (P1, F-06…F-21), 14 Härtungen/Design/Kleinigkeiten (P2/P3, F-22…F-35).

---

## 2. Blocker (P0) — muss vor dem nächsten Lauf behoben werden

### F-01 — Fehlendes Anführungszeichen in `Write-Log` (Syntaxkaskade)
**Ort:** `function Write-Log`, erste Zeile des Funktionskörpers.
**Status:** Im aktuellen Auszug **vorhanden** (`"yyyy-MM-dd HH:mm:ss"`) → deine Datei ist an dieser Stelle möglicherweise schon korrekt. Das muss F-01 nicht „erledigen“, sondern **verifizieren**.

**Beweis (aus dem Share-Chat, Parser-Ausgabe):**
```
$Stamp = Get-Date -Format "yyyy-MM-dd“ im Ausdruck oder in der Anweisung.
Unerwartetes Token „}“ im Ausdruck oder in der Anweisung.
...
Es fehlt eine schließende „}“ im Anweisungsblock oder der Typdefinition.
```
Der Parser liest alles ab dem fehlenden `"` als offenen String — deshalb bricht er *hunderte* Zeilen später ab und meldet 12+ Folgefehler, obwohl es genau **einer** ist.

**Ursache (mechanisch belegt):** Wird PowerShell-Code über Markdown/Chat kopiert, ersetzt die Darstellungsschicht gerne `"` durch typografische Anführungszeichen (`„ "` U+201E/U+201C/U+201D). PowerShell akzeptiert nur `"` (U+0022) und `'` (U+0027), und neuere PowerShell-Versionen geben dabei auch keine hilfreiche Warnung mehr aus.

**Lösung — sofort, in dieser Reihenfolge:**
```powershell
# 1. Syntaxfakt schaffen (das ist das Gate, das bisher fehlte)
$F = "C:\Users\aowdg\Desktop\KI\LocalCloudCode\LocalCloudCode.ps1"
$Errors = $null
$Tokens = $null
[System.Management.Automation.Language.Parser]::ParseFile($F,[ref]$Tokens,[ref]$Errors) | Out-Null
if ($Errors.Count -eq 0) { "SYNTAX: OK" } else { "SYNTAX: FAIL ($($Errors.Count))"; $Errors | Select-Object -First 5 | ForEach-Object { "{0} : Zeile {1}" -f $_.Message, $_.Extent.StartLineNumber } }

# 2. Smart-Quotes im ganzen Baum finden (der eigentliche Auslöser)
Get-ChildItem "C:\Users\aowdg\Desktop\KI\LocalCloudCode" -Recurse -File -Include *.ps1,*.psm1,*.psd1 |
  ForEach-Object {
    $T = [System.IO.File]::ReadAllText($_.FullName)
    $N = ([regex]::Matches($T,'[\u201C\u201D\u201E\u201A\u2018\u2019]')).Count
    if ($N -gt 0) { "SMART-QUOTES: $N in $($_.FullName)" }
  }
```

**Dauerlösung (das ist der Kern des Problems):**
1. **Kein Code mehr per Chat-Einfügen.** Änderungen laufen ab jetzt über das Werkzeug `tools/Update-PsFunctionBlock.ps1` (siehe Abschnitt 5) — es patcht per **AST-Extent**, nicht per `-replace`, sichert vorher, prüft nachher und rollt bei Fehlern automatisch zurück.
2. `ParseFile` als **Pflicht-Gate** vor jedem `Move-Item` (im Patch-Werkzeug enthalten).
3. Smart-Quote-Scanner als Test (im Testpaket enthalten).

**Warum das zwingend ist:** Genau derselbe Mechanismus hat schon den letzten Patch zerstört:
> „Bei PowerShell wird der zweite Operand von `-replace` als Replacement-String interpretiert. Die `$...`-Variablen innerhalb unseres einzusetzenden Funktionscodes wurden dadurch als Ersetzungen behandelt. Deshalb wurde dein bestehender Quelltext mehrfach und verstümmelt in die temporäre Datei geschrieben.“ *(Share-Chat)*

Das ist ein **bekanntes, vermeidbares** Problem: `-replace` interpretiert `$1`, `$&`, `` $` ``, `$_` usw. im Ersetzungstext. Code, der `$($_.Exception.Message)` enthält, wird dabei zerstört. `String::Replace`/`Insert` oder AST-Patching dürfen niemals mit `-replace` für Code ersetzt werden.

---

### F-02 — `Get-DiscoveryResult` übergibt keinen Pfad → ParameterBindingException
**Ort:** `function Get-DiscoveryResult`, Block `$InvokeParams`.

**Code (Ist-Zustand):**
```powershell
$ContentCommand = Get-Command Get-WorkspaceContent -CommandType Function -ErrorAction Stop
$InvokeParams = @{}

if ($ContentCommand.Parameters.ContainsKey('WorkspacePath')) { $InvokeParams['WorkspacePath'] = $WorkspacePath }
if ($ContentCommand.Parameters.ContainsKey('Workspace'))     { $InvokeParams['Workspace']     = $Workspace }
if ($ContentCommand.Parameters.ContainsKey('MaxFileBytes'))  { $InvokeParams['MaxFileBytes']  = 65536 }
if ($ContentCommand.Parameters.ContainsKey('MaxTotalChars')) { $InvokeParams['MaxTotalChars'] = 24000 }

$RawContent = Get-WorkspaceContent @InvokeParams
```

**Die echte Signatur lautet:**
```powershell
function Get-WorkspaceContent {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [int]$MaxFileBytes = 65536,
        [int]$MaxTotalChars = 24000
    )
```

**Befund:** `Path` wird **nirgends** geprüft und daher **nie** übergeben. `$InvokeParams` enthält ausschließlich `MaxFileBytes` und `MaxTotalChars`. Da `Path` mandatory ist, endet jeder Discovery-Lauf garantierbar mit:
> `Cannot bind argument to parameter 'Path' because it is an empty string.` / ParameterBindingException

Also exakt die Fehlerklasse, die dich schon einmal mit `RequestedWorkspace` getroffen hat (`Das Argument kann nicht an den Parameter „RequestedWorkspace" gebunden werden, da es eine leere Zeichenfolge ist.`). Nur diesmal nicht mit leerem String, sondern mit gar keinem.

**Lösung:** Parameter-Erkennung durch einen festen Vertrag ersetzen. Signatur-Reflection ist bei einer Funktion, die im **selben Skript** definiert ist, reine Fehlerquelle — sie schützt gegen nichts und verdeckt Tippfehler. Der korrigierte, vollständige Funktionsersatz liegt in **`patches/Get-DiscoveryResult.v1.2.ps1`** und übergibt explizit `-Path`.

---

### F-03 — `Get-WorkspacePath` wird mit falschem Parameternamen aufgerufen
**Ort:** `Get-DiscoveryResult`, erste Zeile.
```powershell
$WorkspacePath = Get-WorkspacePath -Workspace $Workspace   # ← Parameter existiert nicht
```
**Die echte Signatur:**
```powershell
function Get-WorkspacePath { param([string]$RequestedWorkspace = "") ... }
```
**Ergebnis (garantiert, immer):** `Ein Parameter mit dem Namen „Workspace" wurde nicht gefunden, der mit den Parametern übereinstimmt.`

**Lösung:** `-RequestedWorkspace`. Zusätzlich (siehe F-11): relative Namen dürfen nicht roh an `Get-WorkspacePath` gehen, sondern werden vorher gegen `$WorkspaceRoot` aufgelöst. Im Ersatz in `patches/` ist beides umgesetzt — inklusive klarer Semantik: `-WorkspaceName ""` = Wurzel, `-WorkspaceName "READONLY_TEST"` = Unterordner.

---

### F-04 — `Get-DiscoveryResult` ist nirgends eingebunden und hat keine definierte Semantik
**Ort:** Runtime-Block (im Chat bei Zeile ~970–1020 deiner Datei).

Der Runtime-Block ruft `Get-WorkspaceSummary` und `Get-WorkspaceContent`, aber **nicht** `Get-DiscoveryResult`. Die Funktion existiert damit als toter bzw. nur im Test erreichbarer Code. Zwei Folgen:
- Der Discovery-Pfad wird nie im echten Lauf geprüft — seine zwei Blocker (F-02, F-03) hätten im Produktivbetrieb sofort zugeschlagen.
- Es ist nirgends definiert, was `$Workspace` bedeuten soll: relativer Ordnername? Absoluter Pfad? Beides ist implizit und damit unzuverlässig.

**Lösung:** Einbettung + eindeutige Semantik, in dieser Reihenfolge im Runtime-Block:
```powershell
$Discovery = Get-DiscoveryResult -WorkspaceName $WorkspaceName   # "" = Wurzel
Write-Log "Discovery: $($Discovery.Inventory.ReadCount) gelesen / $($Discovery.Inventory.BlockedCount) blockiert"
Write-Log "Discovery evidence hash: $($Discovery.EvidenceHash)"
```
Und im Run-Report (`logs/run_*.json`) landen `discovery_version`, `inventory_count`, `read_count`, `blocked_count`, `evidence_hash`, `workspace`. Ohne das ist die Discovery-Arbeit nicht beweisfähig — genau der Punkt, den du beim Run-Report schon erkannt hast („Die Funktion meldet einen Report, den wir anschließend nicht finden können“).

---

### F-05 — Fail-open: `-NoMasterPrompt` schaltet Schutz **und** Policy ab
**Ort:** Runtime-Block.
```powershell
if (-not $NoMasterPrompt) {
    $MasterPromptHash = Validate-MasterPrompt
}
...
$RuntimePolicy = $null
if (-not $NoMasterPrompt) {
    $RuntimePolicy = Load-RuntimePolicy -SelectedMode $Mode -MasterHash $MasterPromptHash
    Write-Log "Runtime policy ready for model execution."
}
```
**Befund:** Mit `-NoMasterPrompt` läuft das Modell **ohne jede Policy** — aber der Rest der Runtime arbeitet unverändert weiter (Workspace-Zugriff, Modellaufruf, Report). Der Run-Report zeigt dann weder `master_prompt_validated: false` noch `unsafe_mode: true`; ein späterer Leser kann einen ungeschützten von einem geschützten Lauf nicht unterscheiden. Für ein System, dessen ganzer Wert die Nachweisbarkeit ist, ist das der gefährlichste Einzelbefund.

**Lösung:**
```powershell
if ($NoMasterPrompt -and -not $Unsafe) {
    throw "-NoMasterPrompt ist nur mit -Unsafe zulaessig. Ein Lauf ohne Policy wird nicht stillschweigend erlaubt."
}
if ($NoMasterPrompt) {
    $RuntimePolicy = [PSCustomObject]@{
        Text   = Get-MinimalSafetyPreamble      # harte, kurze Sicherheitsregeln im Code
        File   = "<builtin>"
        Report = ""
    }
    Write-Log "UNSAFE MODE: Master prompt disabled." "WARN"
}
```
Und im Report: `master_prompt_validated`, `runtime_policy_source`, `unsafe_mode`. **Regel: Kein Lauf ohne Systemprompt.** Die Policy darf reduziert werden, aber nicht verschwinden.

---

## 3. Gravierende Befunde (P1)

### F-06 — Der Masterprompt-Hash wird berechnet, aber nie erzwungen
`Validate-MasterPrompt` berechnet `439443DE…97A8`, protokolliert ihn, und der Compiler-Report muss denselben Wert führen. **Nirgends steht der erwartete Wert.** Eine veränderte `MASTER_PROMPT.txt` (oder eine, die zufällig denselben Header und Marker trägt) ist damit unsichtbar — solange jemand den Compiler neu laufen lässt, wird der neue Hash akzeptiert. Die Integritätskette ist zirkulär.

**Lösung** — gepinnter Erwartungswert, fail-closed:
`prompts\manifest.json`:
```json
{
  "prompt_file": "MASTER_PROMPT.txt",
  "expected_sha256": "439443DEAACD2527756975047B685A380EE58D37665B8412BBAD2A77752B97A8",
  "expected_bytes": 244463,
  "version": "9.2"
}
```
In `Validate-MasterPrompt` direkt nach `Get-Sha256`:
```powershell
$ManifestPath = Join-Path $ScriptRoot "prompts\manifest.json"
$Manifest = [System.IO.File]::ReadAllText($ManifestPath, [System.Text.UTF8Encoding]::new($false,$true)) | ConvertFrom-Json
if ($Manifest.expected_sha256 -and ($Hash -ne [string]$Manifest.expected_sha256)) {
    throw "Master prompt hash mismatch. expected=$($Manifest.expected_sha256) actual=$Hash"
}
if ($Manifest.expected_bytes -and $Bytes.Length -ne [int]$Manifest.expected_bytes) {
    throw "Master prompt size mismatch: $($Bytes.Length) != $($Manifest.expected_bytes)"
}
```
Eskalation (empfohlen, wenn der Agent später Dateien ändert): Manifest mit `-ExpectedHash` übergeben oder Signatur (`Get-AuthenticodeSignature`) prüfen.

---

### F-07 — Die kompilierte Policy hat keinen Hash — Nachträgliche Manipulation bleibt unentdeckt
Der Compiler-Report enthält `SourceSHA256` (Hash des **Masterprompts**) und `CompiledChars`. Er enthält **nicht** den Hash der erzeugten `MASTER_POLICY_<Mode>.txt`. Die Runtime prüft in `Load-RuntimePolicy` daher: Report OK → Policy-Datei existiert → **Regeln aus der Datei werden ungeprüft übernommen.**

**Konsequenz:** `MASTER_POLICY_Inspect.txt` (oder `_Chat`, `_Plan`) kann nach dem Compiliervorgang beliebig geändert werden — die Runtime merkt es nicht. Der Report ist kein Integritätsanker für seinen eigenen Output.

**Lösung:**
1. Compiler schreibt zusätzlich: `OutputFile`, `OutputSHA256`, `OutputBytes`, `CompiledAtUtc`.
2. `Load-RuntimePolicy` prüft:
```powershell
$PolicyHash = (Get-FileHash -LiteralPath $PolicyFile -Algorithm SHA256).Hash.ToUpperInvariant()
if ([string]::IsNullOrWhiteSpace([string]$Report.OutputSHA256)) {
    throw "Compiler report has no OutputSHA256. Recompile required."
}
if ($PolicyHash -ne [string]$Report.OutputSHA256) {
    throw "Runtime policy hash mismatch against compiler report."
}
```
3. Empfohlen zusätzlich: Report-Datei selbst in einem Ordner halten, in dem der Agent (Phase 2) nicht schreiben darf.

---

### F-08 — Der Evidence-Hash ist **kulturabhängig** → „deterministisch“ ist falsch
```powershell
$EvidenceJson = @($NormalizedEvidence | Sort-Object Path | ConvertTo-Json -Depth 20 -Compress) -join ''
```
`Sort-Object` sortiert mit der **aktuellen Kultur**. Dasselbe Verzeichnis mit denselben Dateien ergibt unter `de-DE` und `en-US` eine andere Reihenfolge (Kollation von `ä`, `ü`, `ß`, Ziffern, Groß-/Kleinschreibung) — und damit **einen anderen Hash**. Der Vertrag behauptet aber:
```powershell
DeterministicEvidenceSerialization = $true
```
Das ist genau die Sorte Aussage, die du selbst verboten hast („Erfinde keine Testergebnisse“): eine Behauptung, die die Laufzeit nicht prüft.

**Lösung — ordinal sortieren, nicht kulturell:**
```powershell
$Paths = [string[]]@($NormalizedEvidence | ForEach-Object { [string]$_.Path })
[System.Array]::Sort($Paths, [System.StringComparer]::Ordinal)
```
Danach die Objekte in genau dieser Reihenfolge zusammensetzen (siehe `patches/Get-DiscoveryResult.v1.2.ps1`, dort vollständig implementiert). `[string]::CompareOrdinal`-basierte Sortierung ist auf jedem Rechner und in jeder Kultur identisch.

**Test dazu** (im Paket enthalten): Discovery zweimal laufen lassen, dazwischen `[System.Threading.Thread]::CurrentThread.CurrentCulture` auf `de-DE` bzw. `en-US` setzen → die Hashes **müssen** identisch sein.

---

### F-09 — Die Hash-Eingabe ist **kein gültiges JSON**
```powershell
@($NormalizedEvidence | ConvertTo-Json -Depth 20 -Compress) -join ''
```
- Bei **1** Datei: Pipeline liefert ein Objekt → `"{...}"` 
- Bei **n** Dateien: Pipeline liefert n Ergebnisse → `"{...}{...}..."`
- Bei **0** Dateien: `""`

Das ist kein JSON, sondern eine Aneinanderreihung. Nachvollziehbar ist der Hash damit nur, wenn man exakt diesen Quirk nachbaut — und er hängt am Pipeline-Verhalten, das sich mit PowerShell-Versionen ändern kann. Deine Evidence-Kette ist damit **nicht unabhängig prüfbar**, und genau das war ihr Zweck.

**Lösung (PS 7.0+, `-AsArray` genau für diesen Fall):**
```powershell
$EvidenceJson = ConvertTo-Json -InputObject @($SortedEvidence) -Depth 20 -Compress -AsArray
if (-not (Test-Json -Json $EvidenceJson)) { throw "EVIDENCE_JSON_INVALID" }
```
`-InputObject` (statt Pipeline) + `-AsArray` garantiert in jedem Fall ein Array: `[]`, `[{...}]`, `[{...},{...}]` — gültiges JSON, von jeder Sprache verifizierbar (z. B. in C#, Python, jq).

---

### F-10 — Der `Contract`-Block behauptet statt zu prüfen
```powershell
Contract = [pscustomobject][ordered]@{
    ReadOnly = $true
    EvidenceHash = $true
    EvidenceSeparatedFromContent = $true
    DeterministicEvidenceSerialization = $true
}
```
Vier Konstanten. Der `ReadOnly`-Wert **wird direkt darüber tatsächlich ermittelt** (`$ReadOnly = [bool]$Content.ReadOnly`) — und im Vertrag steht trotzdem hart `$true`. Wenn `Get-WorkspaceContent` je in einen schreibenden Modus wechselt, behauptet Discovery weiter „ReadOnly“.

**Lösung:** Vertragswerte berechnen, nicht setzen:
```powershell
Contract = [pscustomobject][ordered]@{
    ReadOnly                     = [bool]$ReadOnly                     # aus dem Content-Objekt
    EvidenceHashAlgorithm        = 'SHA256'
    EvidenceJsonValid            = $true                               # nur nach bestandenem Test-Json
    EvidenceSort                 = 'Ordinal'                           # nur nach ordinaler Sortierung
    EvidenceCountMatchesRead     = ($ReadFiles.Count -eq $NormalizedEvidence.Count)
    SchemaVersion                = [string]$Content.SchemaVersion
}
```
Wo etwas nicht geprüft werden kann: `$null` oder der ehrliche Wert — nicht `$true`.

---

### F-11 — Relative `-Workspace`-Angaben werden gegen das Arbeitsverzeichnis aufgelöst
```powershell
$Full = [System.IO.Path]::GetFullPath($RequestedWorkspace)
```
`GetFullPath` löst relative Pfade gegen das **aktuelle Verzeichnis des Prozesses** auf, nicht gegen `$WorkspaceRoot`. Beispiel: Du startest aus `C:\temp` mit `-Workspace "READONLY_TEST"` → wird zu `C:\temp\READONLY_TEST` → Containment-Check schlägt fehl („outside the authorized workspace root“). Startest du aus `Desktop\KI\LocalCloudCode\workspace` → funktioniert es. **Ergebnis hängt davon ab, wo die Konsole stand** — ein reproduzierbarer Lauf ist das nicht.

**Lösung:** Relatives immer gegen die Wurzel auflösen, absolute prüfen:
```powershell
if ([string]::IsNullOrWhiteSpace($WorkspaceName)) {
    $Target = $WorkspaceRoot
} else {
    $Target = Join-Path $WorkspaceRoot $WorkspaceName
}
$WorkspacePath = Get-WorkspacePath -RequestedWorkspace $Target
```
Die Containment-Prüfung bleibt unverändert streng — sie greift dann nur noch bei echten Verstößen.

---

### F-12 — `Get-WorkspaceContent` prüft seine Dateien nicht gegen die autorisierte Wurzel
`Get-WorkspacePath` erzwingt sauber, dass der **Einstiegspunkt** in `workspace\` liegt. `Get-WorkspaceContent` prüft aber nur den übergebenen `-Path` und blockt Reparse Points beim Traversieren. Wird die Funktion **direkt** aufgerufen (Test, künftige Tool-Schicht, Modell-gesteuerter Aufruf), ist `-Path "C:\Users\aowdg"` zulässig und wird gelesen.

**Das ist der wunde Punkt für Phase 2**: sobald der Agent Werkzeuge selbst aufruft, ist „der Aufrufer hat schon geprüft“ keine Sicherheitsgrenze mehr.

**Lösung (fail-closed, in der Funktion selbst):**
```powershell
function Get-WorkspaceContent {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [string]$AuthorizedRoot = "",                 # ← neu
        [int]$MaxFileBytes = 65536,
        [int]$MaxTotalChars = 24000,
        [string[]]$ExcludedDirectoryNames = @('.git','logs','runtime_policy','node_modules','.vs','bin','obj')
    )

    if ([string]::IsNullOrWhiteSpace($AuthorizedRoot)) {
        $AuthorizedRoot = $WorkspaceRoot
    }
    $RootFull = [System.IO.Path]::GetFullPath($AuthorizedRoot).TrimEnd('\')
    $PathFull = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')

    if (-not ($PathFull.Equals($RootFull,[System.StringComparison]::OrdinalIgnoreCase) -or
              $PathFull.StartsWith($RootFull + '\',[System.StringComparison]::OrdinalIgnoreCase))) {
        throw "CONTENT_PATH_OUTSIDE_ROOT: $PathFull"
    }
    ...
}
```
Damit ist die Grenze **in jeder Funktion** wirksam, unabhängig vom Aufrufer.

---

### F-13 — Doppelte Blöcke und widersprüchliche Rückgabe-Schemata
**13a — Doppelungen im Runtime-Block** (deine Datei, Zeilen ~997–1016 laut Chat):
```powershell
997:  $WorkspaceContentEvidence = $null
999:  $WorkspaceContentEvidence = $null
...
1006: try { $WorkspaceContentEvidence = $WorkspaceContent | ConvertFrom-Json } catch { $WorkspaceContentEvidence = $null }
1012: try { $WorkspaceContentEvidence = $WorkspaceContent | ConvertFrom-Json } catch { $WorkspaceContentEvidence = $null }
```
Zweimal identisch → entsteht durch wiederholte Patches. Muss raus, sonst driftet es.

**13b — Und hier steckt ein echter Fehler drin:** `ConvertFrom-Json` funktioniert nur, wenn `$WorkspaceContent` ein **String** ist. Liefert `Get-WorkspaceContent` direkt ein Objekt (was der Code in `Get-DiscoveryResult` mit `-is [string]` sogar berücksichtigt), wirft `ConvertFrom-Json` — und der leere `catch` setzt `$WorkspaceContentEvidence = $null`. **Deine Runtime hätte dann stillschweigend keine Evidence, ohne Fehlermeldung.** Zwei Indizien deuten stark darauf hin, dass genau das passiert: die Doppelung selbst (jemand hat zweimal versucht, es zu retten) und die adaptive Schema-Sonde in Discovery.

**13c — Schema-Mismatch zwischen den beiden Funktionen.** Der Regressionstest aus dem Chat liest:
```powershell
$ContentResult.ContentReadFiles ; $ContentResult.ContentBlockedFiles ; $ContentResult.ContentChars ; $ContentResult.ReadOnly
```
`Get-DiscoveryResult` liest dagegen:
```powershell
$Content.ReadFiles  |  $Content.Files  |  $Content.Read
$Content.BlockedFiles | $Content.Blocked
$Content.Evidence
```
Zwei verschiedene Namenswelten. Da Discovery fehlende Felder **still** als leer behandelt (`@()`), ist die Folge kein Fehler, sondern ein **leeres, „erfolgreiches“ Ergebnis**: `Inventory.Count = 0`, `EvidenceHash` über eine leere Liste, Status OK. Das ist der gefährlichste Zustand überhaupt — ein grüner Bericht über nichts.

**Lösung — ein Schema, eine Version, harte Ablehnung:**
```powershell
# Get-WorkspaceContent gibt IMMER dieses Objekt zurück (kein JSON-String):
[pscustomobject][ordered]@{
    SchemaVersion = '1.0'
    ReadOnly      = $true
    Workspace     = $PathFull
    Limits        = [pscustomobject][ordered]@{ MaxFileBytes=$MaxFileBytes; MaxTotalChars=$MaxTotalChars; MaxFiles=$MaxFiles }
    ReadFiles     = @($ReadRows)     # Path, Size, Extension, Encoding, SHA256, ContentChars, Truncated
    BlockedFiles  = @($BlockedRows)  # Path, Size, Extension, Reason
    TotalChars    = [int]$TotalChars
    Truncated     = [bool]$Truncated
}
```
Und in Discovery:
```powershell
if ([string]$Content.SchemaVersion -ne '1.0') { throw "CONTENT_SCHEMA_MISMATCH: '$($Content.SchemaVersion)'" }
if ($null -eq $Content.ReadFiles)            { throw "CONTENT_SCHEMA_INCOMPLETE: ReadFiles fehlt" }
```
**Nie wieder still degradieren.** Ein fehlendes Feld ist ein Fehler, kein leeres Array.

**Prüfkommando für dich (zeigt den Ist-Zustand in 3 Zeilen):**
```powershell
$C = Get-WorkspaceContent -Path (Join-Path $PSScriptRoot "workspace\READONLY_TEST")
"TYPE: $($C.GetType().FullName)"
"KEYS: $($C.PSObject.Properties.Name -join ', ')"
```

---

### F-14 — Limits werden behauptet, nicht durchgereicht; `MaxFiles` existiert nicht
`Get-DiscoveryResult` schreibt fest:
```powershell
Limits = [pscustomobject][ordered]@{ MaxFileBytes = 65536; MaxTotalChars = 24000; MaxFiles = 500 }
```
Die tatsächlichen Werte sind aber Parameter von `Get-WorkspaceContent` und können beim Aufruf abweichen — Discovery erfindet seine Limits. Schlimmer: **`MaxFiles` gibt es in keiner Signatur.** Ein Verzeichnis mit 5.000 Dateien wird vollständig enumeriert; der Bericht behauptet derweil ein Limit von 500.

**Lösung:**
1. `[int]$MaxFiles = 500` in `Get-WorkspaceContent` ergänzen und im Traversal durchsetzen; beim Erreichen: Rest als `BlockedFiles` mit Reason `MAX_FILES_REACHED` (plus `Truncated = $true`).
2. Discovery reicht **durch**, statt zu setzen: `Limits = $Content.Limits`.
3. Reason `MAX_FILES_REACHED` in `$KnownReasons` aufnehmen (sonst landet er in `Unknown.Reasons`).

---

### F-15 — Pfad-Härtung: Lücken bei Kurznamen, UNC, ADS und TOCTOU
Die Containment-Prüfung ist gut, aber es fehlen vier Fälle:

| Fall | Ist | Soll |
|---|---|---|
| 8.3-Kurznamen (`C:\PROGRA~1\…`) | wird nicht als innerhalb erkannt → Fehlalarm | über realen Pfad vergleichen |
| UNC / `\\?\`-Präfixe | ungeprüfte Sonderform | explizit ablehnen (oder definiert behandeln) |
| ADS (`datei.txt:stream`), Laufwerks-relativ (`D:foo`) | ungeprüft | ablehnen |
| Reparse-Erkennung nur über Attribut-Bitmaske | erkennt **Hardlinks nicht**; `Get-Item`-Eigenschaft `.LinkType` ist präziser (PS 7) | `.LinkType` prüfen |

**Verschärfend: TOCTOU.** Zwischen `Get-WorkspacePath` (Prüfung) und dem Dateizugriff (Nutzung) kann ein Verzeichnis durch eine Junction ersetzt werden. Bei einem Single-User-Lokalsystem ist das Restrisiko klein — aber es gehört ausgesprochen, bevor der Agent schreiben darf.

**Lösung (Ergänzungen):**
```powershell
# Reparse/Hardlink präziser erkennen (PowerShell 7)
$Item   = Get-Item -LiteralPath $Full -Force -ErrorAction Stop
$Link   = $Item.LinkType                       # '', 'SymbolicLink', 'Junction', 'HardLink'
if ($Link -and $Link -ne '') { throw "BLOCKED_LINK_TYPE: $Link ($Full)" }
if ($Item.FullName -like '\\?\*' -or $Item.FullName -like '\\*\*') { throw "UNC_PATH_BLOCKED: $Full" }
if ($Full -match ':' -and $Full -notmatch '^[A-Za-z]:\\') { throw "PATH_FORM_NOT_ALLOWED: $Full" }
```
Zusätzlich für Phase 2 (Schreiben): nach dem Öffnen den **echten** Pfad über das Handle prüfen — damit ist TOCTOU geschlossen:
```powershell
$Stream = [System.IO.File]::Open($File,[System.IO.FileMode]::Open,[System.IO.FileAccess]::Read)
try { $Real = $Stream.Name } finally { $Stream.Dispose() }
# $Real gegen $AuthorizedRoot prüfen
```

---

### F-16 — Der Anspruch „PowerShell 5.1/7“ ist nicht haltbar
`#requires -Version 7.0` steht in der Datei — die Regressionstests prüfen aber „PowerShell 5.1/7 Syntax → PASS“. Das ist widersprüchlich und technisch nicht möglich, allein wegen:

| Konstrukt | Verfügbar ab |
|---|---|
| `[System.Security.Cryptography.SHA256]::HashData(...)` | **.NET 5+ — in 5.1 unmöglich** (dort nur `SHA256.Create()` + `ComputeHash`) |
| `ConvertFrom-Json -Depth` | **PS 6.2** |
| `ConvertTo-Json -AsArray` | **PS 7.0** (von mir für F-09 empfohlen) |
| `Test-Json` | PS 6.1 |

**Lösung:** Anspruch streichen, konsequent auf **PowerShell 7.2+** zielen (7.2 ist die älteste noch unterstützte LTS-Linie). In den Testkopf gehört `#requires -Version 7.2`, und im Run-Report: `$PSVersionTable.PSVersion` + `$PSVersionTable.OS`. Sonst hast du später keine Chance zu wissen, mit welcher Runtime ein Evidence-Hash entstanden ist.

---

### F-17 — `Set-StrictMode -Version Latest` macht den Vertrag nicht reproduzierbar
`Latest` bedeutet: Die Regeln verschärfen sich mit jedem PowerShell-Release. Ein Lauf, der heute grün ist, kann nach einem Update rot sein — ohne dass sich am Skript oder an der Policy etwas geändert hat. Für ein System, dessen Zweck nachweisbare Reproduzierbarkeit ist, ist `Latest` die falsche Festlegung.

**Lösung:** `Set-StrictMode -Version 3.0` pinnen (die stärkste explizit stabilisierte Stufe; sie deckt uninitialisierte Variablen, nicht existierende Eigenschaften und Indexfehler ab — belegt durch Microsoft-Doku). Version im Run-Report protokollieren.

**Wichtig für euren Code:** Bei `-Version 3.0` wirft der Zugriff auf eine **nicht existierende Eigenschaft** einen `PropertyNotFoundException`. Das betrifft direkt das im Auszug endende Fragment `.TrimS` (siehe F-31) und ist der Grund, warum solche Tippfehler sofort auffallen. Behalte StrictMode — aber eben gepinnt.

---

### F-18 — Run-Report: Namen, Umfang, Aussagekraft
Der Ist-Report ist ein guter Anfang, aber er dokumentiert die falschen Dinge:
```json
{ "version": "9.2-runtime", "provider": "LMStudio", "model": "qwen/qwen2.5-coder-14b",
  "mode": "Inspect", "workspace": "...", "master_prompt": true,
  "master_prompt_sha256": "439443…", "status": "RESPONSE_RECEIVED" }
```
Probleme: `master_prompt: true` ist irreführend (validiert ≠ übertragen — im Chat schon erkannt). `status: RESPONSE_RECEIVED` unterscheidet nicht zwischen „Antwort kam an“ und „Antwort ist plausibel“. Evidence fehlt komplett. Es fehlt alles, was du für die Agenten-Phase brauchst.

**Lösung — Ziel-Report (Schema 2.0):**
```json
{
  "run_id": "…", "schema": "2.0", "timestamp_utc": "…", "duration_ms": 41234,
  "runtime": { "version": "9.2-runtime", "ps_version": "7.6.6", "os": "Windows 10.0.19045", "strict_mode": "3.0" },
  "provider": { "name": "LMStudio", "url": "http://127.0.0.1:1234", "model": "qwen/qwen2.5-coder-14b", "requested": "Auto" },
  "policy": { "master_prompt_validated": true, "master_prompt_sha256": "439443…",
              "runtime_policy_file": "runtime_policy\\MASTER_POLICY_Inspect.txt",
              "runtime_policy_sha256": "…", "compiler_report": "runtime_policy\\compile_Inspect_….json",
              "policy_chars": 12345, "unsafe_mode": false },
  "prompt": { "chars": 21000, "estimated_input_tokens": 6200, "budget": 28000,
              "truncated_sections": ["workspace_content"], "max_tokens": 2048 },
  "workspace": { "path": "…\\workspace", "discovery_version": "1.2", "inventory_count": 12,
                 "read_count": 11, "blocked_count": 1, "evidence_hash": "…" },
  "response": { "status": "RECEIVED_VALIDATED", "chars": 812, "sha256": "…", "finish_reason": "stop" },
  "errors": []
}
```
Der Punkt: `evidence_hash` und `response.sha256` machen den Lauf **nachprüfbar** — man kann später belegen, was das Modell gesehen und was es gesagt hat. Ohne diese beiden Felder ist der Report eine Behauptung.

---

### F-19 — Kein Loopback-Zwang, kein Secret-Filter
Die URLs sind lokal (`127.0.0.1`) — gut. Aber sie kommen aus `$OllamaUrl`/`$LMStudioUrl`/`settings.json` und werden **nirgends validiert**. Trägt jemand in `settings.json` eine Remote-URL ein, verlassen Workspace-Inhalte den Rechner, ohne dass irgendetwas widerspricht. Zusätzlich: `.txt`, `.json`, `.ps1` dürfen gelesen werden — dort liegen regelmäßig API-Keys, Tokens oder Zugangsdaten, und die gehen mit an das Modell.

**Lösung:**
```powershell
function Assert-LocalEndpoint {
    param([Parameter(Mandatory)][string]$Url, [switch]$AllowRemote)
    $Uri = [System.Uri]$Url
    $Local = @('127.0.0.1','::1','localhost')
    if ($AllowRemote) { return }
    if ($Local -notcontains $Uri.Host) {
        throw "NON_LOCAL_ENDPOINT_BLOCKED: $($Uri.Host). Nur Loopback ist im lokalen Modus erlaubt."
    }
}
```
Secret-Filter vor dem Senden (Redaction, nicht Blockade — sonst ist das System unbenutzbar):
```powershell
$Patterns = @(
    '(?i)(api[_-]?key|token|secret|passwd|password)\s*[:=]\s*\S+',
    'sk-[A-Za-z0-9]{16,}', 'ghp_[A-Za-z0-9]{20,}', 'AKIA[0-9A-Z]{16}',
    '-----BEGIN [A-Z ]*PRIVATE KEY-----'
)
# Treffer → "[REDACTED]" + Reason 'SECRET_REDACTED' im Report
```
Und den Report um `redacted_count` ergänzen. Das ist genau der Punkt, an dem du später froh sein wirst — ein lokales Modell, das einen Key in eine Log-Datei schreibt, ist kein Sicherheitsgewinn.

---

### F-20 — Der Regressionstest startet die komplette Runtime (Dot-Sourcing-Falle)
Im Test aus dem Chat steht:
```powershell
try { . $LCCScript ; ... } catch { "PRODUCT LOAD: FAIL" ... }
```
`LocalCloudCode.ps1` ist ein **Skript mit Top-Level-Runtime-Code**: Backend-Auswahl, Policy-Laden, Workspace-Enumerierung, **Modellaufruf**. `. $LCCScript` führt genau das aus. Der Test lädt also nicht „das Produkt“, sondern **startet einen Lauf** — mit `$ErrorActionPreference='Stop'` bricht er beim ersten Fehler ab, und im günstigen Fall macht er einen echten Modellaufruf und schreibt einen Run-Report. Genau deshalb war unklar, was der Test eigentlich geprüft hat.

**Lösung — zwei Wege, nimm Weg A:**
- **A (empfohlen):** Runtime in ein Modul `LocalCloudCode.psm1` (nur Funktionen), dazu ein dünnes Entry-Script `LocalCloudCode.ps1` mit `Import-Module` + einem Aufruf `Invoke-LocalCloudCodeRuntime @PSBoundParameters`. Tests laden nur das Modul — die Runtime wird nie gestartet.
- **B (minimal-invasiv, falls du die Datei nicht aufteilen willst):** Guard am Ende der Datei:
```powershell
if ($MyInvocation.InvocationName -ne '.') {
    Invoke-LocalCloudCodeRuntime @PSBoundParameters
}
```
Damit ist Dot-Sourcing für Tests gefahrlos und der Produktivaufruf unverändert.

**Zusatznutzen:** Der Test kann dann die Funktionen per AST extrahieren, ohne die Datei auszuführen — siehe `tests/Invoke-ContractTests.ps1` in diesem Repo (nutzt genau diesen Weg, damit du **vor** dem Umbau testen kannst).

---

### F-21 — Ursachenanalyse: der Parameter-Namens-Wildwuchs
Die Blocker F-02/F-03 sind kein Zufall. Im Code existieren für dieselbe Sache gleichzeitig:

| Name | Verwendet in |
|---|---|
| `-Path` | `Get-WorkspaceContent`, `Get-WorkspaceSummary` |
| `-RequestedWorkspace` | `Get-WorkspacePath` |
| `-Workspace` | `Get-DiscoveryResult` (ruft damit `Get-WorkspacePath` auf → Fehler) |
| `WorkspacePath` | als lokale Variable UND als geprüfter Parameter-Schlüssel |

Da `-replace`-Patches nicht typgeprüft sind und es keine Aufruf-Verifikation gibt, fällt so etwas erst zur Laufzeit auf — und dann mit irreführender Meldung („leere Zeichenfolge“).

**Lösung — Konvention + automatisches Gate:**
1. **Pfad-Parameter heißen immer `-WorkspacePath`** (absoluter, autorisierter Pfad). Relative Selektoren heißen `-WorkspaceName`.
2. Umbenennungen nur per AST-Patch (nicht Textersetzung).
3. Gate im Test: Alle internen Funktionsaufrufe werden per AST gegen die tatsächlichen Funktionssignaturen geprüft — findet jeden F-02/F-03-Fall **vor** der Ausführung. Die vollständige, produktionsreife Implementierung liegt in `tests/Invoke-ContractTests.ps1` (Abschnitt 1.4 `PARAMETER-GATE`): sie berücksichtigt benannte Parameter, positionale Bindung und aufgelöste Splat-Hashtabellen. Genau dieses Gate hat F-02 und F-03 oben maschinell nachgewiesen.

---

## 4. Härtungen, Design und Kleinigkeiten (P2/P3)

| ID | Befund | Lösung |
|---|---|---|
| **F-22** | `Write-Log` hat ein leeres `catch { }` — Logfehler verschwinden vollständig | im catch mindestens `Write-Warning` + in `$script:LogDegraded = $true` setzen; Run-Report vermerkt es |
| **F-23** | Keine Log-Rotation; Logs liegen im Programmordner; bei `-Workspace` auf den Projektordner wandern `logs\`/`runtime_policy\` in die nächste Inventarisierung → Rückkopplung | Rotation (7 Tage / 10 MB), und `logs`, `runtime_policy`, `.git`, `node_modules`, `bin`, `obj` **immer** aus der Inventarisierung ausschließen |
| **F-24** | Kontext-Budget: Der 70.935→32.768-Fehler wurde durch hartes Abschneiden behoben; die Kürzung ist **nicht im Report** | zentrales `Get-PromptBudget` (Modellkontext − Antwortbudget − Sicherheitsreserve), `max_tokens` setzen, `truncated_sections` im Report führen |
| **F-25** | Kein Sitzungs-/Turn-Gedächtnis: jeder Lauf ist kontextfrei; die Agenten-Schleife (ändern → testen → reparieren) braucht Historie | `sessions\<id>\session.json` mit Rollen-Historie + Budgetprüfung + Resume |
| **F-26** | Keine Transaktionsschicht für Schreibzugriffe | `Invoke-TransactionalEdit`: Read-Hash merken → Temp schreiben → parsen/testen → Hash erneut prüfen (Optimistic Concurrency gegen TOCTOU) → Swap + Backup + Journal |
| **F-27** | Modes sind reine Prosa: `Inspect` ist nur eine Textdatei-Auswahl, keine Code-Garantie | Modus-Matrix **im Code**: `$ModeCapabilities = @{ Inspect = @('Read','List','Hash'); Plan = @('Read','List','Hash','Plan'); Chat = @('Read','List','Hash','Plan'); Apply = @('Read','List','Hash','Plan','Write','Execute') }`; jedes Werkzeug prüft am Modus |
| **F-28** | Modellantwort wird als Freitext behandelt — sie könnte Aktionen behaupten, die nie liefen | Antwortformat erzwingen (JSON), mit `Test-Json -SchemaFile` validieren, sonst Retry/Block; „Behauptung ohne Werkzeugbeleg = kein Vollzug“ |
| **F-29** | `Get-WorkspaceSummary` und `Get-WorkspaceContent` enumerieren getrennt → doppelte Arbeit, divergierende Limits | ein Traversal, zwei Projektionen (`Summary` aus `ReadFiles`) |
| **F-30** | Kein Exit-Code-Konzept, keine maschinenlesbare Ausgabe | Codes: 0 OK, 2 Konfiguration, 3 Backend offline, 4 Modell fehlt, 5 Policy, 6 Workspace, 7 Antwort ungültig; plus `-Json`-Ausgabe für Automatisierung |
| **F-31** | Auszug endet in `.TrimS` — falls real vorhanden, wirft StrictMode einen `PropertyNotFoundException` (siehe F-17) | vermutlich gemeint: `.TrimStart('\')`; korrekt und unabhängig von Zeichenklassen: `$CurrentDirectory.FullName.Substring($Path.TrimEnd('\').Length + 1)` |
| **F-32** | Tote/verwaiste Variablen `$Files = @()`, `$ReadRows`, `$BlockedRows` — Reste des `-replace`-Unfalls; Einrückung ab `function Get-WorkspaceContent` auf Spalte 0 | per AST-Block ersetzen (Patch-Werkzeug) und mit `Invoke-Formatter` (PSScriptAnalyzer) normalisieren |
| **F-33** | `UTF8Encoding($false,$true)` bei **BOM**-Dateien: `\uFEFF` bleibt im Text → Marker `^# ULTIMATIVER MASTERPROMPT` scheitert; BOM landet im Content-Hash | defensiv strippen: `if ($Text.Length -gt 0 -and [int]$Text[0] -eq 0xFEFF) { $Text = $Text.Substring(1) }` |
| **F-34** | `ConvertTo-Json -Depth 20` für Evidence ist knapp (Struktur ist derzeit flach, wächst aber mit Verträgen) | auf `-Depth 32` setzen und im Vertrag dokumentieren |
| **F-35** | Auto-Fallback kann das Modell wechseln: LM Studio `qwen/qwen2.5-coder-14b` ↔ Ollama `qwen2.5:14b` (kein Coder) — stiller Qualitätsabfall | gleiches Modell in beiden Backends bereitstellen; wenn ein Ersatzmodell greift: `WARN` + Reportfeld `provider.fallback_from` |

---

## 5. Was im Repo liegt (Lösungen, nicht Beschreibungen)

| Datei | Zweck |
|---|---|
| `review/PRUEFBERICHT-v9.2.md` | dieses Dokument |
| `patches/Get-DiscoveryResult.v1.2.ps1` | vollständiger, korrigierter Ersatz: feste Signatur (`-WorkspacePath`, `-WorkspaceName`), korrekter Aufruf von `Get-WorkspacePath`, ordinalsortierter Hash, gültiges JSON, berechneter Vertrag, durchgereichte Limits, harte Schema-Prüfung |
| `tools/Update-PsFunctionBlock.ps1` | **Ersatz für die `-replace`-Patchtechnik.** Findet die Funktion per AST, ersetzt sie per Zeichenbereich, sichert, prüft (Parse + Funktionszählung + BOM), rollt bei jedem Fehler automatisch zurück, protokolliert Vorher-/Nachher-Hash |
| `tests/Invoke-ContractTests.ps1` | prüft **ohne** die Runtime zu starten: Syntax, Smart-Quotes, Parameter-Namen (AST-Gate gegen F-02/F-03), Workspace-Containment (drinnen/draußen/`..`/nicht existent), Discovery-Vertrag, Hash-Determinismus über Kulturen, Schema-Prüfung |

**Reihenfolge des Vorgehens (so würde ich abarbeiten):**
1. `ParseFile` + Smart-Quote-Scan (F-01) — 1 Minute.
2. `tests/Invoke-ContractTests.ps1` laufen lassen → das zeigt dir sofort, welche der hier behaupteten Fehler in **deiner** Datei wirklich stehen.
3. `tools/Update-PsFunctionBlock.ps1` mit `patches/Get-DiscoveryResult.v1.2.ps1` → F-02, F-03, F-08, F-09, F-10, F-14 sind erledigt.
4. F-07 (OutputSHA256) und F-06 (Manifest) — Integritätskette schließen.
5. F-13 (Schema vereinheitlichen) + F-12 (Root-Prüfung in `Get-WorkspaceContent`) — das ist der Übergang zur Agenten-Phase.
6. Erst danach: Schreibwerkzeuge (F-26, F-27, F-28).

---

## 6. Was nachweislich funktioniert (nicht wegwerfen)

| Nachweis | Belegquelle |
|---|---|
| Masterprompt wird gelesen und validiert (244.463 Bytes / 238.926 Zeichen) | Log deines Laufs |
| Hash-Kette Masterprompt → Compiler-Report greift (Hash-Abgleich) | `Load-RuntimePolicy`-Prüfungen |
| LM-Studio-Backend + Modellwahl funktioniert (`qwen/qwen2.5-coder-14b`) | `POLICY RUNTIME TEST OK`, Run-Report |
| Run-Report wird geschrieben; Pfadauflösung ist okay (das frühere „nicht gefunden“ war kein Codefehler) | Report-Diagnose im Chat |
| Workspace-Containment in `Get-WorkspacePath` ist sauber gebaut (Vergleich, `..`, Reparse Points, Segmentprüfung) | Code-Analyse |
| Kontext-Überlauf ist beseitigt | `POLICY RUNTIME TEST OK` nach der Kürzung |
| Backend-Auswahl mit Auto-Fallback und Modellprüfung ist solide | `Select-Backend` |

---

## 7. Prüfkommandos für deinen Rechner (bitte zuerst ausführen)

**A — Syntaxgate + Hash deiner echten Datei:**
```powershell
$R = "C:\Users\aowdg\Desktop\KI\LocalCloudCode"
Get-ChildItem $R -Recurse -File -Include *.ps1,*.psm1,*.psd1 | ForEach-Object {
    $E = $null; $T = $null
    [System.Management.Automation.Language.Parser]::ParseFile($_.FullName,[ref]$T,[ref]$E) | Out-Null
    "{0,-20} {1,-8} {2}" -f $_.Name, ($(if($E.Count -eq 0){"OK"}else{"FAIL:$($E.Count)"})), (Get-FileHash $_.FullName -Algorithm SHA256).Hash
}
```

**B — Smart-Quotes finden:**
```powershell
Get-ChildItem $R -Recurse -File -Include *.ps1,*.psm1,*.psd1 | ForEach-Object {
    $T = [System.IO.File]::ReadAllText($_.FullName)
    $N = ([regex]::Matches($T,'[\u201C\u201D\u201E\u201A\u2018\u2019]')).Count
    if ($N) { "SMART-QUOTES $N : $($_.FullName)" }
}
```

**C — Rückgabe-Schema von `Get-WorkspaceContent` klären (entscheidet F-13) — ohne die Runtime zu starten:**
```powershell
$R = "C:\Users\aowdg\Desktop\KI\LocalCloudCode"
$F = Join-Path $R "LocalCloudCode.ps1"

# Nur die Funktionsdefinitionen laden (KEIN Dot-Source der ganzen Datei:
# der wuerde einen kompletten Lauf inkl. Modellaufruf starten - Befund F-20).
$Tok = $null; $Err = $null
$Ast = [System.Management.Automation.Language.Parser]::ParseFile($F, [ref]$Tok, [ref]$Err)
$Src = (@($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)) |
        ForEach-Object { $_.Extent.Text }) -join "`n`n"
. ([ScriptBlock]::Create($Src))

$C = Get-WorkspaceContent -Path (Join-Path $R "workspace\READONLY_TEST")
"TYPE : $($C.GetType().FullName)"
"KEYS : $($C.PSObject.Properties.Name -join ', ')"
if ($C -is [string]) { "JSON-KEYS: $((($C | ConvertFrom-Json).PSObject.Properties.Name) -join ', ')" }
```

**D — Hash der Prompt-Datei gegenprüfen (F-06):**
```powershell
Get-ChildItem "$R\prompts","$R\runtime_policy" -File |
  ForEach-Object { "{0,-40} {1}" -f $_.Name, (Get-FileHash $_.FullName -Algorithm SHA256).Hash }
```

Schick mir die Ausgaben von A–D, dann passe ich die Patch-Dateien auf deinen exakten Stand an (statt auf den Auszug).

---

## 8. Roadmap zur Agenten-Engine (mit Nachweiskriterium pro Stufe)

| Stufe | Inhalt | Fertig, wenn nachweisbar ist … |
|---|---|---|
| **B0 — Verträge** | F-01…F-18 abgearbeitet; ein Schema überall; AST-Patch-Werkzeug im Einsatz | `tests/Invoke-ContractTests.ps1` läuft mit 0 FAIL auf deinem Rechner |
| **B1 — Discovery** | Inventar + Evidence pro Lauf im Run-Report; Hash unabhängig nachrechenbar (jq/Notepad) | `evidence_hash` im Report == Nachrechnung aus dem Report selbst |
| **B2 — Plan** | Modell liefert strukturierten Plan (JSON, Schema-validiert), der **keine** Aktion auslöst; Runtime zeigt Plan + erwartete Wirkung | Plan-JSON validiert gegen Schema; keine Schreib-/Exec-Aktion im Log |
| **B3 — Freigabe** | Freigabestufen pro Aktionstyp: `Read` immer, `Write`/`Execute` explizit (`-Approve`) oder per Freigabedatei mit Hash der geplanten Änderung | Log zeigt je Aktion `requires_approval` + `approved_by` + Hash |
| **B4 — Änderung** | `Invoke-TransactionalEdit` (Temp → Test → Swap → Backup → Journal), Optimistic Concurrency | Abbruch mitten im Schreiben hinterlässt die Originaldatei unverändert (Test) |
| **B5 — Verifikation** | Pflicht-Testkommando pro Änderung; Vorher/Nachher-Diff; bei Fehlschlag **automatischer Rollback** | Journal enthält Vorher-/Nachher-Hash + Testergebnis + Rollback-Status |
| **B6 — Regression** | Testsuite als Gate vor jedem Commit; Parser-Gate; Parameter-Gate (F-21) | Gate blockiert einen Lauf mit absichtlich eingebautem Parameternamen-Fehler |
| **B7 — Evidenzbericht** | Ein Bericht pro Lauf: Eingabe, Policy-Hash, Evidence-Hash, Plan, Aktionen, Testergebnisse, Antwort-Hash, Rollbacks | Bericht genügt, um den Lauf **ohne** Konsolenmitschnitt zu rekonstruieren |

**Leitsatz für alle Stufen:** Eine Fähigkeit gilt erst als vorhanden, wenn ein Test sie **widerlegen könnte**. Alles andere ist Prosa im Prompt — und Prosa ist genau das, was v9.2 im Übermaß hat und was die 244 KB Masterprompt nicht schützt.

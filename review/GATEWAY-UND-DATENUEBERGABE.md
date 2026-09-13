# Gateway-Anbindung und Datenübergabe — Stand 13.09.2026

## 1. Ergebnis der Prüfung dieses Zugangs

| Prüfung | Ergebnis | Beleg |
|---|---|---|
| Gateway `http://127.0.0.1:8765` von hier erreichbar? | **Nein** | `curl` → `code=000`, kein TCP-Connect für `/`, `/health`, `/status` |
| Angekündigte Uploads vorhanden? | **Nein** | `/home/user/uploads` existiert nicht; dateisystemweite Suche nach `*MASTERPROMPT*`, `*Snapshot_Summary*`, `*Installed_Modules*` → 0 Treffer |

**Erklärung, ohne Ausreden:** `127.0.0.1` in meiner Sandbox ist ein **anderer Rechner** als deiner. Ein `localhost`-Port auf deinem PC ist für mich prinzipiell nicht erreichbar — das ist keine Fehlkonfiguration deines Gateways, sondern die erwartete Isolierung.

**Und ich werde daran nichts ändern.** Ich baue keinen Tunnel, der deine PowerShell-Sitzung in meine Umgebung durchreicht. Ein Shell-Gateway ist die stärkste Fernsteuerung, die es auf einem Windows-Rechner gibt (dein Skript kann alles ausführen, was dein Benutzer darf). Diese Verbindung gehört ausschließlich in deine Hand, nicht in einen Chat-Kontext. Wenn du sie irgendwann remote brauchst: SSH statt HTTP-Gateway, Token kurzlebig, und nie ein öffentliches Port-Forwarding.

Die gute Nachricht: Für die eigentliche Arbeit brauche ich das Gateway **nicht**. Ich brauche Fakten — und die kann ein Skript einsammeln, das du einmal lokal laufen lässt.

---

## 2. Sicherheitscheck des Gateways (5 Prüfungen, alle read-only)

Ein lokaler HTTP-Server auf `127.0.0.1:8765`, der PowerShell ausführt, hat drei typische Angriffsflächen. Alle drei sind in 2 Minuten prüfbar:

**1. Bind-Adresse — hört er nur auf Loopback?**
```powershell
netstat -ano | Select-String ':8765'
```
Erwartet: `127.0.0.1:8765`. **Alarm bei** `0.0.0.0:8765` oder `[::]:8765` — dann ist er im gesamten LAN erreichbar, inklusive WLAN-Gäste.

**2. Wird das Token auf *jedem* Endpunkt erzwungen?**
```powershell
try { Invoke-WebRequest -Uri "http://127.0.0.1:8765/system.info.read" -TimeoutSec 5 }
catch { "ohne Token: $($_.Exception.Response.StatusCode.value__)" }   # erwartet: 401
try { Invoke-WebRequest -Uri "http://127.0.0.1:8765/system.info.read" -Headers @{ Authorization = "Bearer Buic3gVfi58QaCizgxcDRhrkILDKvn9aEn7F1xyI" } -TimeoutSec 5 }
catch { "mit Token: $($_.Exception.Response.StatusCode.value__)" }    # erwartet: 200
```
Wichtig: `system.info.read` ist der harmloseste Endpunkt. Er darf `200` liefern — aber **kein** Endpunkt darf ohne Token `200` liefern. Prüfe stichprobenartig auch `filesystem.list.read` und `process.read`.

**3. Origin/Host-Validierung — Schutz gegen fremde Webseiten (DNS-Rebinding/CSRF)**
```powershell
$H = @{ Authorization = "Bearer <TOKEN>"; Origin = "https://example.com"; Host = "example.com" }
try { Invoke-WebRequest -Uri "http://127.0.0.1:8765/system.info.read" -Headers $H -TimeoutSec 5 }
catch { "fremder Origin/Host: $($_.Exception.Response.StatusCode.value__)" }  # erwartet: 403
```
Ohne diese Prüfung kann jede Webseite, die du offen hast, im Hintergrund Anfragen an deinen Gateway senden (`fetch('http://127.0.0.1:8765/...')`). Ein READONLY-Gateway leakt dann Dateilisten und Prozessdaten auf deinen Rechner — bei einem Schreibmodus wäre es eine Katastrophe.

**4. Content-Type — erzwingt er CORS-Preflight?**
Ein Server, der `POST` mit `Content-Type: text/plain` annimmt, erlaubt „simple requests" ohne Preflight. Erwartet: `415` oder `400` bei fehlendem `application/json`.

**5. Not-Aus und Zugriffsprotokoll**
- `& "C:\Users\aowdg\Downloads\KI_PowerShell_Gateway_UI.ps1" STOP` muss den Listener wirklich beenden. Nachprüfen: `netstat -ano | Select-String ':8765'` → leer.
- Gibt es eine Zugriffslog-Datei (Methode, Endpunkt, Token-ID, Ergebnis)? Ohne Log ist nicht feststellbar, ob jemand anderes den Port benutzt hat.

### Token behandeln
Das Token steht jetzt im Chatverlauf und im Sitzungskopf. Behandle es nach dieser Session als **verbrannt**: Gateway beenden → Token neu erzeugen → Token nicht mehr in Chats oder Logs, sondern nur in einer Datei mit eingeschränkten Rechten (`icacls <Datei> /inheritance:r /grant:r "$env:USERNAME:(R)"`). Der Sitzungs-Header sagt selbst: *„Do not expose it directly to the public Internet"* — die gleiche Sorgfalt gilt für jeden Kanal, in dem das Token erscheint.

### Empfehlung zum Modus
`READONLY` ist die richtige Einstellung. Zwei Ergänzungen, damit sie belastbar bleibt:
- Der **Server** muss schreiben ablehnen, nicht nur die Anleitung. Test: ein Schreib-Endpunkt muss auch mit gültigem Token `403` liefern, solange `READONLY` aktiv ist.
- Endpunkte, die Dateiinhalte zurückgeben (`filesystem.list.read` liefert bei dir offenbar ganze Ordnerinhalte), sollten auf ein Whitelist-Verzeichnis begrenzt sein. Sonst ist „read-only" gleichbedeutend mit „du kannst jeden Ordner lesen, den dein Benutzer lesen darf".

---

## 3. Schritt 0: Dateien auf den PC holen (das hatte gefehlt)

Die Werkzeuge liegen im Repository, **nicht** auf deinem PC. `.\tools\...` kann deshalb
nicht funktionieren, solange das Repo nicht lokal liegt. Das Repository ist **privat**,
deshalb funktioniert ein direkter `raw.githubusercontent.com`-Link **nicht** ohne Token.

**Weg A — ZIP herunterladen (kein git, kein Token nötig; im Browser angemeldet sein):**

1. Im Browser öffnen:
   `https://github.com/AOWDGENESIS/Entwicklungen/tree/arena/01a09c80-entwicklungen`
2. Rechts oben **Code ▾ → Download ZIP**
3. ZIP entpacken nach: `C:\Users\aowdg\Desktop\KI\`
   (der entpackte Ordner heißt `Entwicklungen-arena-01a09c80-entwicklungen` — umbenennen in `Entwicklungen`)
4. In PowerShell 7:

```powershell
$D = "$env:USERPROFILE\Desktop\KI\Entwicklungen"
# Windows blockiert heruntergeladene Skripte - einmalig freigeben
Get-ChildItem $D -Recurse -File | Unblock-File
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
# Syntaxpruefung VOR dem ersten Lauf
Get-ChildItem "$D\tools","$D\tests" -Recurse -File | ForEach-Object {
    $e = $null; $t = $null
    [System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$t, [ref]$e) | Out-Null
    "{0,-34} {1}" -f $_.Name, $(if ($e.Count) { "SYNTAX FAIL: $($e.Count)" } else { "SYNTAX OK" })
}
```

**Weg B — git clone (falls git installiert und angemeldet ist):**

```powershell
git clone -b arena/01a09c80-entwicklungen `
  https://github.com/AOWDGENESIS/Entwicklungen.git `
  "$env:USERPROFILE\Desktop\KI\Entwicklungen"
```

**Danach starten (aus dem entpackten Ordner heraus):**

```powershell
cd "$env:USERPROFILE\Desktop\KI\Entwicklungen"
.\tools\Collect-LocalCloudCodeSnapshot.ps1 -WhatIf      # Vorschau
.\tools\Collect-LocalCloudCodeSnapshot.ps1              # echter Lauf (nur lesend)
```

Wichtig: `-Root` zeigt standardmäßig auf `C:\Users\aowdg\Desktop\KI\LocalCloudCode`.
Wenn deine Installation woanders liegt, mit `-Root "…"` übergeben.

---

## 4. Datenübergabe — die vollständige Variante

**Empfohlen:** `tools/Collect-LocalCloudCodeSnapshot.ps1` lokal ausführen (reines Lesen, READONLY-kompatibel) und die erzeugte ZIP anhängen.

```powershell
# Vorschau ohne jede Schreibaktion
.\tools\Collect-LocalCloudCodeSnapshot.ps1 -WhatIf

# Lauf (schreibt nur Berichte nach <Desktop>\KI\snapshots\snapshot_<zeitstempel>\)
.\tools\Collect-LocalCloudCodeSnapshot.ps1
```

Das Skript sammelt in einem Durchlauf genau das, was hier fehlt:

| Abschnitt | Inhalt | Beantwortet im Prüfbericht |
|---|---|---|
| A | PS-Version, OS, Kultur, ExecutionPolicy, Profil(Pfad/Hash) | F-16, F-17 |
| B | Umgebungsvariable **namen** (Werte nur auf Wunsch, Secrets maskiert) | F-19 |
| C | PSScriptAnalyzer / Pester / `Test-Json` / `Invoke-Formatter` | Werkzeugvoraussetzungen |
| D | Mehrfach definierte Befehle (Alias/Function schatten Cmdlet) | Risiko für `Get-WorkspacePath` & Co. |
| E | Jede Datei: Bytes + SHA256 + Parse-Fehler + Smart-Quote-Zähler | F-01, F-32, Dateibestand |
| F | Masterprompt: Bytes, Zeichen, SHA256, Abgleich mit `439443DE…` | F-06 |
| G | Policy-Dateien + Hashes + Compiler-Report-Felder | F-07 |
| H | Ollama-/LM-Studio-Modelllisten (nur GET) | F-35 |
| I | Rückgabetyp und Felder von `Get-WorkspaceContent`, Test von `Get-DiscoveryResult` | F-02, F-03, F-13c |
| J | Marker für alle Befunde: gefunden/nicht gefunden + Zeilennummern | F-01…F-20 |
| K | Run-Reports: Felder und Werte, Loggrößen | F-18 |

**Warum das besser ist als die 18 Einzeldateien:** Die alte Sammlung enthielt *alle* Umgebungsvariablen im Klartext, die komplette Alias- und Befehlsliste und die Registry-Exporte. Das ist mehr Angriffsfläche als Nutzen. Der Snapshot liefert Werte nur auf ausdrücklichen Wunsch (`-IncludeEnvironmentValues`) und maskiert alles, was wie ein Schlüssel aussieht.

### Fallback ohne jeden Dateitransfer: die Kurzdiagnose

Wenn Download oder Anhängen nicht klappt, gibt es zwei Wege, die **sofort** funktionieren:

**(a) Als Datei im Repo** (nach Weg A oben, dann einfach):
```powershell
& "$env:USERPROFILE\Desktop\KI\Entwicklungen\tools\Get-LccQuickFacts.ps1"
```

**(b) Ohne jede Datei — Block direkt einfügen**, aber vorher die Zwischenablage prüfen.
Genau dieser Kopiervorgang hat schon zweimal deinen Code beschädigt (typografische
Anführungszeichen). Der Check kostet eine Sekunde und rettet den Lauf:

```powershell
# 1. Block in den Chat kopieren (Strg+C), dann DAS ausfuehren:
$t = Get-Clipboard -Raw; $e = $null
[System.Management.Automation.Language.Parser]::ParseInput($t, [ref]$null, [ref]$e) | Out-Null
if ($e.Count) { "CLIPBOARD SYNTAX FAIL"; $e | Select-Object -First 3 | ForEach-Object { "Zeile $($_.Extent.StartLineNumber): $($_.Message)" } }
else { "CLIPBOARD SYNTAX OK - Block kann eingefuegt werden" }
```
Erst bei `CLIPBOARD SYNTAX OK` den Block in die Konsole einfügen (nicht noch einmal kopieren)
und Enter drücken.

**Die älteren Einzelbefehle** (falls du lieber stückweise arbeitest): die Ausgabe
dieser vier Blöcke direkt in den Chat kopieren — sie ist klein:

```powershell
$R = "C:\Users\aowdg\Desktop\KI\LocalCloudCode"
# 1) Syntax + Smart Quotes
Get-ChildItem $R -Recurse -File -Include *.ps1,*.psm1,*.psd1 | ForEach-Object {
  $E=$null;$T=$null; [System.Management.Automation.Language.Parser]::ParseFile($_.FullName,[ref]$T,[ref]$E)|Out-Null
  $X=[System.IO.File]::ReadAllText($_.FullName)
  "{0,-22} {1,-8} Quotes={2} SHA256={3}" -f $_.Name, $(if($E.Count){'FAIL:'+$E.Count}else{'OK'}), ([regex]::Matches($X,'[\u201C\u201D\u201E\u201A\u2018\u2019]')).Count, (Get-FileHash $_.FullName -Algorithm SHA256).Hash
}
# 2) Rueckgabe-Schema der Content-Funktion
$Tok=$null;$Err=$null
$Ast=[System.Management.Automation.Language.Parser]::ParseFile("$R\LocalCloudCode.ps1",[ref]$Tok,[ref]$Err)
$Src=(@($Ast.FindAll({param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst]},$true))|ForEach-Object{$_.Extent.Text}) -join "`n`n"
. ([ScriptBlock]::Create($Src))
$C = Get-WorkspaceContent -Path "$R\workspace\READONLY_TEST"
"TYPE: $($C.GetType().FullName)"; "KEYS: $($C.PSObject.Properties.Name -join ', ')"
# 3) Prompt- und Policy-Hashes
Get-ChildItem "$R\prompts","$R\runtime_policy" -File -ErrorAction SilentlyContinue | ForEach-Object { "{0,-40} {1}" -f $_.Name,(Get-FileHash $_.FullName -Algorithm SHA256).Hash }
# 4) Backends
(Invoke-RestMethod http://127.0.0.1:11434/api/tags -TimeoutSec 5).models.name
(Invoke-RestMethod http://127.0.0.1:1234/v1/models -TimeoutSec 5).data.id
```

---

## 5. Was danach passiert

Sobald der Snapshot hier ist, ist die Reihenfolge:
1. Befunde gegen den echten Stand abgleichen (nicht mehr gegen den Chat-Auszug) und den Prüfbericht auf „bestätigt / nicht bestätigt / nicht vorhanden" umstellen.
2. `patches/Get-DiscoveryResult.v1.2.ps1` an die echte Signatur und das echte Content-Schema anpassen.
3. `tests/Invoke-ContractTests.ps1` laufen lassen → Ergebnis als Beleg, nicht als Behauptung.
4. Erst dann der nächste Baustein (Modus-Matrix F-27, Transaktionsschreiber F-26).

Bis dahin ändere ich an deinem System nichts — es gilt weiterhin: **READONLY, Schreibzugriffe nur nach ausdrücklicher Freigabe.**

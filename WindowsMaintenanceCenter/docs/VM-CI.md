# Bau- und Prüfumgebung (Windows-VM im CI)

Dieses Dokument beschreibt, **welcher Rechner** den Build, die Tests und die Paketierung ausführt und
**was dieser Rechner nicht kann**. Es ersetzt den Nachweis auf der Zielmaschine nicht — es macht
sichtbar, welcher Teil der Abnahme dort noch offen ist.

## 1. Warum nicht lokal

Auf dem Entwicklungsrechner stehen zur Verfügung:

| Voraussetzung | Zustand |
| --- | --- |
| Betriebssystem | Linux, kein Windows |
| Hardware-Virtualisierung (`/dev/kvm`, `vmx`/`svm`) | **nicht vorhanden** |
| Arbeitsspeicher | 3 GB — für eine Windows-11-VM unbrauchbar |
| Windows-Datenträger | nicht beschaffbar (Bezugsquellen sind aus dieser Umgebung gesperrt) |
| .NET-SDK | nicht installierbar (NuGet und `dotnet.microsoft.com` sind gesperrt) |

Eine Windows-VM auf diesem Rechner wäre also keine VM, sondern eine Behauptung. Deshalb läuft die
Windows-Umgebung dort, wo sie wirklich existiert: als **Windows-VM eines GitHub-gehosteten Runners**
(`windows-latest`, Windows Server mit echtem Kernel, echtem .NET-SDK, echtem WMI, echtem PowerShell).

## 2. Was der CI-Rechner leistet

| Schritt | Werkzeug | Ergebnis |
| --- | --- | --- |
| Wiederherstellen | `dotnet restore` | Abhängigkeiten aus NuGet, Lock-Dateien werden erzeugt und mitgeschrieben |
| Bauen | `dotnet build -c Release` | echte Kompilierung aller 15 Projekte inklusive WPF-XAML |
| Testen | `scripts/test.ps1` (xUnit/MTP) | echte Testausführung; TRX-Datei als Nachweis, sobald die Berichtserweiterung lädt — sonst das Konsolenprotokoll (`test-run.log`) mit ausdrücklichem Vermerk, dass der Nachweis unvollständig ist |
| Portabel | `scripts/build.ps1` | selbstenthaltene `win-x64`-Ausgabe |
| Installer | Inno Setup (`iscc`) | `WindowsMaintenanceCenter-Setup-x64.exe` |
| Prüfsummen | `scripts/release.ps1` | SHA-256 über alle Artefakte, Gegensprüfung durch den Workflow |
| Umgebung | `Get-CimInstance`, `dotnet --info` | Betriebssystem, Hypervisor, CPU, RAM, SDK, Runtimes und **was fehlt** |
| Installationszyklus (Gate 7) | `scripts/vm/Test-InstallerCycle.ps1` | installieren, Layout und SHA-256 gegen den Bau prüfen, starten, **Nutzungssonde** (Datenordner und Logdatei entstehen), beenden, deinstallieren, prüfen was weg ist und was bewusst bleibt — und zum Schluss das portable Artefakt in einen **leeren Ordner** kopieren und nachweisen, dass es seine Daten **neben sich** ablegt. Nachweis unter `test-results/installer/` |

Die Logs jedes Schritts und die TRX-Datei werden vom Lauf zurück in den Zweig geschrieben
(`test-results/ci/run-<Laufnummer>/`). Das ist nötig, weil der Log-Download des Runners von hier aus
nicht erreichbar ist; der Nachweis steht damit im Repository, nicht nur im Runner.

### 2a. Warum der Installationszyklus im CI läuft (Befund vom 2026-09-22)

Bis zum 2026-09-22 stand hier, der CI könne „installieren, reparieren und deinstallieren" **nicht**
belegen. Das war zu streng: die CI-VM *kann* installieren, starten und deinstallieren, sie kann nur
nicht *bedienen*. Die einzige Sache, die bis dahin niemandem auffiel, war genau die, die sie hätte
auffallen lassen: **das ausgelieferte Portable-Artefakt war nicht portabel.** Es besteht aus *einer*
Datei, aber der Portabel-Modus hing an einer Markierungsdatei *daneben* — die der Bau in den
Ausgabebordner schrieb, nicht auf die ausgelieferte Datei. Baulog und Prüfsummen waren korrekt, und
„ist portabel" stand in einem Bericht, der nie ausgeführt wurde. Gefunden wurde das zu Fuß beim Lesen
von `release.ps1`; gefunden hätte es ein einziger Start der ausgelieferten Datei in einem leeren
Ordner. Genau diesen Start macht `Test-InstallerCycle.ps1` jetzt in jedem Lauf.

Was der Zyklus im CI **belegt**: dass der Installer auf einer echten Windows-Maschine installiert,
dass die installierte Datei byteweise die gebaute ist, dass das Programm startet und wirklich läuft
(Datenordner, Logzeile), dass die installierte Kopie ihre Daten **nicht** neben sich schreibt, dass
die Deinstallation Eintrag, Programmdateien und Verknüpfung entfernt und die Daten nach Rückfrage
behält — und dass das portable Artefakt seine Daten neben sich ablegt.
Was er **nicht** belegt: Reparaturinstallation, Upgrade über eine Vorgängerversion, Neustart,
Installation auf D:, E:, F: und „USE" im Sinne von Kapitel 62 (ein Mensch, der die Oberfläche
bedient). Diese Punkte bleiben offen und werden im Bericht des Zyklus als offene Punkte genannt.

## 3. Was dieser Rechner **nicht** belegen kann

Diese Punkte bleiben `BLOCKED`, auch wenn der CI-Lauf grün ist:

1. **Physische Hardware.** Der Runner ist selbst eine VM: kein Wechseldatenträger, kein Gehäuse-,
   Mainboard- oder Sensormodell, keine Thermalzonen, kein Akku, keine diskrete GPU, kein SMART-Wert
   echter Laufwerke. Die Hardware-, Sensor-, Thermal- und SMART-Module (M08–M11, M18, M20) brauchen
   die Zielmaschine.
2. **UAC und erhöhte Ausführung.** Auf dem Runner gibt es keine interaktive Rechteabfrage: ein
   `runas`-Start ist nicht nachweisbar, der Abbruch der Abfrage (Win32 1223) ebenfalls nicht. Die
   Nachweise zu M31-S-001/S-003/S-004 bleiben offen.
3. **Neustart, Ruhezustand, Rollback über einen Neustart.** Die VM wird nach dem Lauf verworfen; es
   gibt keinen Zustand „vorher/nachher" über einen Reboot.
4. **Windows 11 im Desktop-Zuschnitt.** Der Runner ist ein Server-Image. Was ausschließlich an einer
   Windows-11-Desktop-Installation hängt (Store, Widgets, Desktop-Spezifika), ist damit nicht
   abgedeckt.
5. **Zeitverhalten.** Ein VM-Lauf ist keine Messung von Startzeiten oder Leistungsaufnahme auf der
   Zielmaschine; Leistungsaussagen aus dem CI werden nicht als Nachweis verwendet.
6. **Offline-/Netzbetrieb.** Der Runner hat immer Netz. Der Offline-Nachweis (Gate 6) braucht die
   Zielmaschine mit gezogenem Kabel.

Alles, was der CI belegt, wird im Abnahmedokument als „auf der CI-VM gelaufen" geführt — **nie** als
`PASSED` eines Moduls, solange die MUSS-Kriterien der Spezifikation den Nachweis auf der Zielmaschine
verlangen (Kapitel 93/94).

## 3a. Die Läufe vom 2026-09-20 im Einzelnen

Jeder Lauf hat genau das gefunden, was auf diesem Rechner niemand finden konnte. Die Zahlen sind die
Fehler des jeweiligen Bau-Schritts:

| Lauf | Commit | Was der Lauf aufgedeckt hat | Ergebnis |
| --- | --- | --- | --- |
| 1 | `52a0a7c` | Solution baute nichts (Build.0 nur fuer x64), Restore fand kein Projekt, dotnet test lehnte den VSTest-Pfad ab | kein echter Build |
| 2 | `f58b8c2` | Umgebungsabfrage abgestuerzt; Logs im Zweig als Nachweis eingefuehrt | kein echter Build |
| 3 | `1aeb12c` | Solution/Testpfad korrigiert | 2 Fehler |
| 4 | `657229e` | ScanOrchestrator ReadInventoryAsync doppelt, Events.cs ohne using | 5 Fehler |
| 5 | `23aceb6` | Pc() lieferte ValueOrigin statt TextInfo (Ursache von ~300 Fehlern), ProgressReporter, PathGuard, IsKnown, InMemoryStores | 1 Fehler |
| 6 | `080b15e` | Wertetupel-Fehler, letzte Warnungen | 386 Fehler in 10 Dateien |
| 7 | `b07fc0b` | Hardware-, Windows- und Wartungsschicht bereinigt (Defender-Zustaende, Update-Ausgang, Environment.SpecialFolder, Using-Fehler, Iterator-Zaehler) | 22 Fehler |
| 8 | `953a0c5` | Methodengruppen statt Aufrufe (Display), nullable Lesungen, Vorlagenschluessel, JSON-Werte | 10 Fehler |
| 9 | `dee9711` | CultureInfo, blocked-Reihenfolge, out-Parameter im Lambda, WriteStringValue | 7 Fehler |
| 10 | `e06f84e` | TextInfo-Mehrdeutigkeit, generische Einschraenkung, Bedingungsausdruck in der Zeichenkette | 5 Fehler |
| 11 | `b88952c` | Alias, struct-Einschraenkung, Core-Namensraum; unnoetige using-Zeilen zurueckgenommen | 13 Fehler (Oberflaechen- und Testprojekt wurden erst jetzt sichtbar) |
| 12 | `a07550b` | PendingReboot als Wahrheitswert, Storage statt StorageDevices, Testdoppel-Namensraum | 13 Fehler |
| 13 | `8315c29` | ThemePreference, ISystemStateMachine/StateChangedEvent, App.Services-Verdeckung, ValueOrigin.Manufacturer — **der Zugang riss waehrend des Laufs ab** | 2 Fehler (spaeter ablesbar: `ISystemStateMachine` fehlte in `MainViewModel`), behoben in `dd8c551` |
| 14 | `dd8c551` | `using`-Zeilen fuer Hardware.Wmi und System.IO fehlten jetzt dort, wo die vorige Runde sie entfernt hatte; WmiReader-Zeitstempel | 8 Fehler |
| 15 | `6f9aa45` | `WindowsHardwareProvider` wurde ueber einen Namensraum angesprochen, den es nicht gibt | 6 Fehler / 718 Warnungen |
| 16 | `37d34ba` | Berichtskultur, Aliaslisten, Testprojekt-Warnungen — **der Bau ist durch** | **0 Fehler / 50 Warnungen: Bau ✓, Testschritt ✗** |
| 17 | `1f852a7` | Testschritt startet die Testanwendung statt `dotnet test`; sie laeuft, lehnt aber `--report-trx` ab: die TRX-Erweiterung ist im Lauf nicht registriert | Bau ✓, Testlauf ausgefuehrt, Nachweis unvollstaendig |
| 18 | `01eda0c` | `test.ps1` fragt die Anwendung selbst (`--help`): sie faehrt den xUnit-eigenen Runner und nimmt `-result-trx`. **Die Suite laeuft: 314 Faelle, 0 Fehler in der Ausfuehrung, 11 inhaltliche Testfehler.** Kein TRX in diesem Lauf, weil die Option erst danach gesetzt wurde | 314 ausgefuehrt, **11 failed**, Exit 1 |

| 19 | `d9f5645` | Der Fix am `SimulationFixtureTests` rief `TextInfo.Display` als Methode auf: **`TextInfo.Display` ist eine Eigenschaft** (`Measured<T>.Display` ist die Methode). Nur der zweite Vergleich brauchte den Aufruf | 4 Fehler (CS1955), mein eigener Fix, behoben in `bf2e240` |
| 20 | `bf2e240` | Bau ✓, **Tests ✓: 315 ausgefuehrt, 0 failed, 0 skipped** — und die TRX-Datei wird geschrieben. Der Schritt scheiterte am eigenen Auswerter: das TRX traegt einen XML-Namensraum, `//UnitTestResult` findet darin nichts | Bau ✓, Tests ✓ (315/0), Auswertung ✗ |

| 21 | `8e10699` | Bau ✓, **Tests ✓ (315/0), TRX gelesen**, Nachweis unter `test-results/unit/20260920T103645Z-315-of-315` im Zweig. Der Publish scheiterte an PowerShell selbst: `-p:Version=…` wurde als Parameter `-p` der Skriptfunktion gebunden ("parameter name 'p' is ambiguous") | Bau ✓, Tests ✓, Publish ✗ |

| 22 | `b08aaea` | Bau ✓, Tests ✓, jetzt bis zum Publish: `NETSDK1047` — das Projekt wurde ohne RuntimeIdentifier wiederhergestellt, `publish --no-restore` verlangt aber ein Asset-Ziel fuer `net10.0-windows/win-x64` | Bau ✓, Tests ✓, Publish ✗ (Restore fehlt) |

| 25 | `a5e5277` | **Dritter gruener Lauf, 359 Tests**: M35 ist bedienbar geworden (Wiederherstellungsseite, Befund `WMC-M35-###`, Zustandsbuchhaltung im Journal). Bau 0 Fehler, Portable-EXE, Inno-Setup-Installer und Pruefsummendatei wie in den Laeufen 23/24. Nachweisordner `test-results/unit/20260922T062120Z-359-of-359`, CI-Record `run-35694300454-1` | **success** (359/0/0) |

| 24 | `01a0201` | **Zweiter vollstaendig gruener Lauf, jetzt mit 348 Tests**: Bau 0 Fehler, M34/M35 (Zustandsjournal mit Hashkette, Wiederaufnahme nach Abbruch, SEC-12/SEC-14) sind gebaut und getestet, Portable-EXE, Inno-Setup-Installer und Pruefsummendatei wie im Lauf 23. Nachweisordner `test-results/unit/20260920T111746Z-348-of-348`, CI-Record `run-35507347841-1` | **success** (348/0/0) |

| 23 | `8a5c0b9` | **Der komplette Windows-Lauf ist durch**: Bau 0 Fehler, 315/0 Tests, Portable-EXE (62.513.557 Bytes), Inno-Setup-Installer (57.479.929 Bytes), Pruefsummendatei, Release Notes, Artefakt- und SHA-256-Gegenpruefung | **success** |

### Der erste vollstaendig gruene Lauf (35505778032)

Alle 15 Schritte des Windows-Jobs sind gruen. Was das belegt - und was nicht:

| Belegt | Nicht belegt |
| --- | --- |
| 15 Projekte uebersetzen im Release-Zuschnitt, inklusive WPF-XAML | dass die Oberflaeche bedienbar ist (niemand hat sie bedient) |
| 315 Testfaelle laufen und bestehen, TRX und `summary.txt` im Nachweisordner `test-results/unit/` | Tests auf echter Hardware, mit Sensoren, mit UAC-Dialog |
| Portable-EXE und Installer werden erzeugt und ihre SHA-256 stimmen | dass der Installer auf einer Maschine installiert, repariert und deinstalliert |
| Der Lauf schreibt seine Nachweise selbst in den Zweig | dass eine Zielmaschine dieselben Ergebnisse liefert |

Der Nachweisordner des Laufs enthaelt: `build.log`, `build-summary.txt`, `restore.log`,
`environment.txt`, `publish.log`, `innosetup.log`, `release.log`, `test.log`, `test-run.log`,
`help.log`, die TRX-Datei, den HTML-Bericht und `run.txt`.

### Die elf echten Testfunde aus Lauf 18

Erstmals lief nicht die Umgebung schief, sondern die Suite fand elf Sachen. Neun davon sind jetzt
behoben, und die Trennung ist wichtig: **drei waren Fehler im Produkt, sechs in den Testdaten oder
-erwartungen.**

Produktfehler (im Code behoben):

| Fund | Warum das gefaehrlich war |
| --- | --- |
| `PathGuard` liess einen Pfad mit abschliessendem Punkt zu (`cache.`) | Windows entfernt solche Zeichen beim Zugriff: geprueft worden waere ein anderer Pfad als geloescht worden. Der Test `A_segment_that_ends_with_a_dot_or_a_space_is_refused` hat genau das aufgedeckt. |
| `PathGuard` meldete fuer die Wurzel der Allowlist "ausserhalb aller Wurzeln" | Ergebnis gleich (verweigert), Begruendung falsch: der Leser haette einen Pfadfehler gesucht, wo eine Grenze verteidigt wurde. |
| `IsIntegratedGraphics` war ein Wahrheitswert, kein "unbekannt" | Ein Adaptername, der nicht gelesen werden konnte, wurde als "nicht integriert" gemeldet - eine erfundene Hardwareaussage. Jetzt dreiwertig, der Bericht schreibt `UNKNOWN: not reported`. |

Testfehler (Testdaten oder -erwartungen korrigiert, jeweils mit Begruendung im Test):

| Fund | Ursache |
| --- | --- |
| `SmbiosCodesTests` nannte 25 und 29 "undokumentiert" | Beide sind dokumentiert (0x19 = FBD2, 0x1D = LPDDR3). Der Test haette eine falsche Antwort verlangt. |
| `InventoryFailureTests` erwartete die alten Problem-IDs `HW-SYS` / `DRV` | Kapitel 85 verlangt `WMC-<Thema>-<Nummer>`; die Erwartungen folgen jetzt dem Schema. |
| `SystemActionCatalogTests` suchte `/f` als Teilzeichenkette | `/fo list` von `systeminfo.exe` ist eine Formatausgabe, kein Erzwingungsschalter. Der Test prueft jetzt den Schalter selbst - vorher haette er `/F` durchgelassen. |
| `SimulationFixtureTests` verglich `Name.Display` ohne Aufruf | Verglichen wurden zwei frische Delegates, nie die angezeigten Werte. |
| `UpdateDecisionEngineTests` setzte fuer den Kompatibilitaetsfall keinen neueren Kandidaten | Gleiche Version ergibt "aktuell"; die Frage nach der Kompatibilitaet stellt sich erst bei einem neueren Kandidaten. |

**Noch offen aus diesem Lauf:** `Volume.ChkdskScan` mit einem Traversal-Argument meldete
`ACTION_ARGUMENT_INVALID` statt `ACTION_PATH_NOT_ALLOWED`. Die Argumentpruefung antwortete auf den
Wert, statt die Pfadrichtlinie zu befragen; `CheckArgument` fragt jetzt zuerst nach dem Ort, damit
Kapitel 79 die Antwort gibt.

Der Lauf belegt ausserdem: die Testanwendung ist **kein** Microsoft.Testing.Platform-Host, sondern
faehrt den xUnit-eigenen In-Prozess-Runner (`xUnit.net v3 In-Process Runner v4.0.1`). Dessen
Optionsliste (`artifacts/test-results/help.log` im Laufrecord) nennt `-result-trx <Datei>` und
`-result-html`, dazu `-noColor`, `-reporter` und die Filter `-class`, `-method`, `-namespace`,
`-trait`. Die Ausgabe der Suite nennt die Zahlen in einer Zeile
(`Total: 314, Errors: 0, Failed: 11, Skipped: 0`), sodass ein Lauf auch ohne Berichtsdatei
auswertbar bleibt.

### Der TRX-Befund aus Lauf 17

Der Lauf startet die erzeugte Testanwendung und bekommt von ihr

```
error: unknown option: --report-trx
Exception: ... the test run failed with exit code 3.
```

Das ist **kein** Fehler der Testfaelle: die Anwendung laeuft, sie kennt die Option nur nicht. Die
Ursache steht in den Quellen der Testplattform selbst (`microsoft/testfx`): Erweiterungspakete
registrieren sich seit MTP v2 nicht mehr dadurch, dass ihre Datei neben der Anwendung liegt, sondern
ueber einen Build-Hook — das Paket `Microsoft.Testing.Extensions.TrxReport` liefert dafuer ein
`TestingPlatformBuilderHook`-Element mit, das die MSBuild-Aufgabe `TestingPlatformEntryPoint` beim
Erzeugen des Einstiegspunkts einsammelt. Genau diese Einsammlung greift hier nicht, obwohl das Paket
wiederhergestellt wird: deshalb ist die Option unbekannt, waehrend die Anwendung einwandfrei startet.

`scripts/test.ps1` fragt die Testanwendung deshalb jetzt **vor** dem Lauf, welche Optionen sie
wirklich kennt (`--help`, abgelegt als `artifacts/test-results/help.log`), listet die
Testplattform-Dateien im Ausgabeordner mit ihren Versionen auf und benutzt die Berichtsoption, die
tatsaechlich vorhanden ist. Fehlt sie ganz, laeuft die Suite trotzdem, ihre Ausgabe steht in
`test-run.log`, und das Skript sagt ausdruecklich, dass der Nachweis allein auf diesem Protokoll
beruht. Ein Lauf ohne TRX wird **nicht** als bestandene Abnahme gefuehrt.

Lauf 18 hat die Frage beantwortet: Die Anwendung faehrt den **xUnit-eigenen In-Prozess-Runner** und
kennt `-result-trx <Datei>` (siehe oben). `test.ps1` benutzt diese Option, sobald sie in der Hilfe
steht, und legt jeden Lauf zusaetzlich unter `test-results/unit/<Zeitstempel>-<Ergebnis>/` ab -
mit `summary.txt` (timestamp, version, build, environment, result nach Kapitel 71), der TRX-Datei
und dem Konsolenprotokoll. Damit steht der Unit-Nachweis im Nachweisverzeichnis der Spezifikation,
nicht nur im Laufrecord.

Waehrend der fruehere Stand dieses Dokuments den Lauf 13 als „nicht ablesbar\" fuehrte: der Lauf
`35501975574` ist inzwischen aus dem Laufnachweis im Zweig ablesbar (2 Fehler, beide
`ISystemStateMachine`), und die Reparatur `dd8c551` ist gelaufen.


## 3b. Wichtigste echte Fehler, die nur eine echte Kompilierung zeigt

Nicht alles war Formalismus. Diese Funde hätten in der Anwendung zu falschen Aussagen geführt:

| Fund | Warum das gefährlich war |
| --- | --- |
| `Measured<T>.Display` als Methodengruppe statt `Display()` in fünf Aufrufen (`MaintenanceService`) | Plan, Trockenlauf und Zusammenfassung hätten statt der Zahl eine Beschleuniger-Beschreibung angezeigt - eine Aussage ohne Wert. |
| `ScanOrchestrator` rief `Info(...)` mit vier Argumenten; die Schwere gehörte zu `Publish(...)` | Ein Scan mit kritischen Befunden wäre als gewöhnliche Meldung protokolliert worden. |
| `ProgressReporter.Start` meldete `Raise()` ohne Stand | Die Oberfläche hätte für den Start keinen Stand bekommen. |
| `WindowsHealthModule` verglich den Laufausgang der Update-Prüfung mit `UpdateStatus` | „Updates verfügbar" wäre aus dem Ausgang der Abfrage abgeleitet worden, nicht aus einer Zählung. |
| `DefenderStatus` hatte kein `IsEnabled`/`IsSignatureOutdated` | Die Entscheidung „Schutz in Ordnung?" stand auf Eigenschaften, die es nicht gab; jetzt dreiwertig, unbekannt wird als unbekannt gemeldet. |
| `HashService`-Prüfung akzeptierte SHA-1 | Bleibt bewusst (nur zum Prüfen fremd veröffentlichter Hashes); Artefakte werden mit SHA-256 gehasht und gegengeprüft. |
| `PowerShellRunner`: Vorlagenschlüssel `RestorePointStatus` gegen Konstante `SystemRestoreStatus` | Die Vorlage war nicht erreichbar - ein Aufruf wäre mit „unbekannte Vorlage" gescheitert. |
| `InMemoryStores.LoadAsync` las `.Snapshot` von einem `FirstOrDefault`, das null sein kann | Ein nicht gefundener Pfad hätte eine Ausnahme geworfen statt `null` zu liefern. |
| `WindowsHardwareProvider.Pc()` lieferte `ValueOrigin` unter dem Rückgabetyp `TextInfo` | Rund 300 Folgefehler; jede gelesene Herkunft war für den Übersetzer unbrauchbar. |

## 3c. Lauf 35701860625 (2026-09-22, Commit `d759180`) - der erste Lauf mit dem Installationszyklus

| Schritt | Ergebnis | Nachweis |
| --- | --- | --- |
| Bauen und Testen | 15 Projekte, 0 Fehler; **370 von 370 Testfällen bestanden** | `test-results/ci/run-35701860625-1/{build.log,test.log,windowsmaintenancecenter.trx}` |
| Paketieren | Setup, Portable-EXE, Prüfsummendatei, Release Notes | `run-35701860625-1/{release.log,innosetup.log}` |
| Nachweiskit | Selbsttest existiert noch nicht (er entsteht aus diesem Befund) | - |
| Installationszyklus (Gate 7) | **alle Kriterien PASS**: stille Installation Exit-Code 0; installierte Datei SHA-256-identisch mit dem Bau; Startmenüeintrag; installierte Kopie nicht portabel; Programm startet, Datenordner und Logzeile entstehen; **Programm schließt mit Exit-Code 0**; Deinstallation entfernt Eintrag, Programmordner und Verknüpfung und behält die Daten; portables Artefakt startet und legt seine Daten neben sich ab | `run-35701860625-1/installer-cycle.log`, `test-results/installer/20260922T075623Z-INS-CI/` |
| Bericht des Zyklus | **fehlt**: `Complete-WmcEvidenceRun` bricht mit `Argument types do not match` ab (`$report = [ordered]@{`, Zeile 325) - der Lauf ist deshalb rot, obwohl jeder Schritt PASS meldet | `run-35701860625-1/installer-cycle.log` (Ende) |
| Selbsttest der Nachweisbibliothek | entsteht aus diesem Befund; er läuft ab dem nächsten Lauf vor dem Zyklus | `scripts/vm/Test-EvidenceLibrary.ps1` |
| Zweiter Befund | Die Fehlermeldung nannte nur die Zeile. Der Fehlertext trägt jetzt den Aufrufstapel, und `scripts/vm/Test-EvidenceLibrary.ps1` prüft die Bibliothek vor dem Zyklus und grenzt bei einem Fehlschlag den brechenden Ausdruck selbst ein | Workflow-Schritt „Evidence library self-test (chapter 71)" |

Was dieser Lauf belegt: der Startabsturz ist behoben (Exit-Code 0, vorher 1), das portable Artefakt speichert
seine Daten neben sich, die Deinstallation lässt den Rechner sauber zurück, und die Testsuite steht bei 370
Fällen. Was er **nicht** belegt: den Reparatur- und Upgrade-Weg, den Neustart, weitere Laufwerke und die
Bedienung der Oberfläche - und bis der Report-Schreiber läuft, auch keinen vollständigen Nachweisordner.

## 3d. Lauf 35704157557 (2026-09-22, Commit `fd11038`) - der erste vollständig grüne Lauf

| Schritt | Ergebnis | Nachweis |
| --- | --- | --- |
| Selbsttest der Nachweisbibliothek | alle Kriterien PASS; der Bericht wird geschrieben, die fünf Felder aus Kapitel 71 sind da, ein `PASSED` ohne Messwert wird abgelehnt | `run-35704157557-1/evidence-selftest.log` |
| Bauen und Testen | 15 Projekte, 0 Fehler; **370 von 370 Testfällen bestanden** | `run-35704157557-1/{build.log,test.log,windowsmaintenancecenter.trx}` |
| Installationszyklus (Gate 7) | 20 Kriterien PASS, 0 FAIL, Ergebnis `PASSED` im Bericht | `run-35704157557-1/installer-cycle.log`, `test-results/installer/20260922T082333Z-INS-CI/` |
| Bericht | `report.json` und `report.txt` liegen im Nachweisordner, jede Nachweisdatei mit SHA-256, offene Punkte ausdrücklich benannt | `test-results/installer/20260922T082333Z-INS-CI/report.txt` |

Der Grund für den vorherigen Fehlschlag, in einem Satz: **nicht das Literal war kaputt, sondern der Wert.**
Ein Array, das PowerShell aus einer `System.Collections.Generic.List[object]` baut (`@($Run.Evidence)`),
lässt sich dort nicht an ein Hashtable- oder pscustomobject-Literal übergeben - jedes andere Konstrukt
läuft durch. Der Bericht wird jetzt über `OrderedDictionary.Add()` gefüllt, und die Nachweislisten gehen
als .NET-Liste hinein, die `ConvertTo-Json` als Array schreibt. Gefunden hat das der Selbsttest aus dem
vorherigen Lauf, nicht ein Blick in den Code: genau dafür steht er im Workflow.

## 3e. Was die CI-Maschine zusätzlich messen kann (ab 2026-09-22)

| Messung | Warum sie hier läuft | Was sie nicht ersetzt |
| --- | --- | --- |
| Selbsttest der Nachweisbibliothek (`Test-EvidenceLibrary.ps1`) | Der Schreiber des Nachweises ist Werkzeug, nicht Ergebnis - er wird vor dem Zyklus geprüft (Kapitel 71) | nichts; der Selbsttest ist vollständig |
| Exit-Codes der Reparaturwerkzeuge (`Test-RepairToolExitCodes.ps1`, nur lesende Schalter) | Die Registrierung einer Aktion ist keine Freigabe: solange niemand gemessen hat, was DISM/SFC/CHKDSK antworten, darf kein Code als „Lauf fehlgeschlagen" gedeutet werden. Messen kann das jede echte Windows-Maschine | SFC und CHKDSK (auf der Zielmaschine, dort kann ein Mensch warten) und die Reparatur selbst (`/RestoreHealth`, `/scannow`) - die bleibt auf der Zielmaschine |
| Installationszyklus (`Test-InstallerCycle.ps1`) | Installieren, starten, benutzen (maschinell), deinstallieren und der portable Datenspeicherort sind auf einer echten Maschine messbar | die Bedienung der Oberfläche, Reparatur, Upgrade, Neustart, weitere Laufwerke |

## 4. Grenzen des Zugangs

**Die CI kann seit dem 2026-09-22 nicht mehr starten.** Die Läufe zu `c02d86c` (`35761467349`,
`35761468380`) haben keinen einzigen Schritt ausgeführt; die Anmerkung des Runners lautet:

> The job was not started because recent account payments have failed or your spending limit needs to be
> increased. Please check the 'Billing & plans' section in your settings

Das betrifft beide Workflows und damit jede Messung auf einer Windows-Maschine. Es ist kein
Code-Problem und in diesem Repository nicht behebbar: die Zahlung oder das Ausgabenlimit muss im
GitHub-Konto geändert werden. Solange das nicht geschehen ist, erzeugt jeder Push einen Lauf ohne
Schritte, und fehlende Nachweise bleiben `BLOCKED` - nicht widerlegt, sondern unmessbar.


Der Workflow ist der einzige Weg, auf dem hier ein Windows-Rechner benutzt werden kann. Daraus folgt:

* Läufe werden durch einen Push ausgelöst (`on: push`, Zweige `main` und `arena/**`).
* Ein manueller Start (`workflow_dispatch`) ist über den verwendeten Zugang **nicht** möglich
  (`HTTP 403: Resource not accessible by integration` — dem Zugang fehlt `actions: write`). Wer den
  Lauf von Hand starten will, tut das in der Oberfläche von GitHub.
* Es stehen keine Zugangsdaten auf dem Runner; er benutzt ausschließlich den mitgelieferten
  `GITHUB_TOKEN` mit `contents: write` — und zwar nur, um den Laufnachweis in den Zweig zu schreiben.

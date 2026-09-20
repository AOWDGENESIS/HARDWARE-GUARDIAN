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
| Testen | `scripts/test.ps1` (xUnit) | echte Testausführung, TRX-Datei als Nachweis |
| Portabel | `scripts/build.ps1` | selbstenthaltene `win-x64`-Ausgabe |
| Installer | Inno Setup (`iscc`) | `WindowsMaintenanceCenter-Setup-x64.exe` |
| Prüfsummen | `scripts/release.ps1` | SHA-256 über alle Artefakte, Gegensprüfung durch den Workflow |
| Umgebung | `Get-CimInstance`, `dotnet --info` | Betriebssystem, Hypervisor, CPU, RAM, SDK, Runtimes und **was fehlt** |

Die Logs jedes Schritts und die TRX-Datei werden vom Lauf zurück in den Zweig geschrieben
(`test-results/ci/run-<Laufnummer>/`). Das ist nötig, weil der Log-Download des Runners von hier aus
nicht erreichbar ist; der Nachweis steht damit im Repository, nicht nur im Runner.

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
| 13 | `8315c29` | ThemePreference, ISystemStateMachine/StateChangedEvent, App.Services-Verdeckung, ValueOrigin.Manufacturer - **Ergebnis unbekannt: der GitHub-Zugang ist waehrend dieses Laufs ungueltig geworden** | **nicht ablesbar (Zugang abgerissen)** |

Zur letzten Zeile: der Lauf `35501975574` wurde noch gestartet, aber sein Ergebnis ist nicht mehr
abrufbar, weil der GitHub-Zugang dieses Arbeitsplatzes während des Laufs ungültig wurde. Was für den
Commit `8315c29` behoben wurde, ist im nächsten Abschnitt festgehalten; **der Bau gilt bis zum
Gegenbeweis als nicht bestanden.**

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

## 4. Grenzen des Zugangs

Der Workflow ist der einzige Weg, auf dem hier ein Windows-Rechner benutzt werden kann. Daraus folgt:

* Läufe werden durch einen Push ausgelöst (`on: push`, Zweige `main` und `arena/**`).
* Ein manueller Start (`workflow_dispatch`) ist über den verwendeten Zugang **nicht** möglich
  (`HTTP 403: Resource not accessible by integration` — dem Zugang fehlt `actions: write`). Wer den
  Lauf von Hand starten will, tut das in der Oberfläche von GitHub.
* Es stehen keine Zugangsdaten auf dem Runner; er benutzt ausschließlich den mitgelieferten
  `GITHUB_TOKEN` mit `contents: write` — und zwar nur, um den Laufnachweis in den Zweig zu schreiben.

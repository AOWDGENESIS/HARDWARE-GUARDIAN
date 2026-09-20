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

## 4. Grenzen des Zugangs

Der Workflow ist der einzige Weg, auf dem hier ein Windows-Rechner benutzt werden kann. Daraus folgt:

* Läufe werden durch einen Push ausgelöst (`on: push`, Zweige `main` und `arena/**`).
* Ein manueller Start (`workflow_dispatch`) ist über den verwendeten Zugang **nicht** möglich
  (`HTTP 403: Resource not accessible by integration` — dem Zugang fehlt `actions: write`). Wer den
  Lauf von Hand starten will, tut das in der Oberfläche von GitHub.
* Es stehen keine Zugangsdaten auf dem Runner; er benutzt ausschließlich den mitgelieferten
  `GITHUB_TOKEN` mit `contents: write` — und zwar nur, um den Laufnachweis in den Zweig zu schreiben.

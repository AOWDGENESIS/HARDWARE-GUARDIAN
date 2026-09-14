# KI Engineering Memory

Autonome, projektunabhaengige Engineering-Referenz fuer lokale und externe KI-Systeme.

## Grundprinzip

Dieses Repository ist KEIN Projekt und KEIN Jarvis-Modul.
Es ist eine eigenstaendige Wissens- und Qualitaetsschicht, die von beliebigen Projekten verwendet werden kann.

Die Inhalte sind bewusst in zwei Schichten getrennt:

- `reference/`: stabile Regeln, Best Practices, Fehlerbehandlung, Sicherheit, Tests und Engineering-Prinzipien.
- `runtime/`: veraenderliche Maschinen- und Toolfakten. Diese Daten werden lokal erzeugt und gehoeren standardmaessig nicht in Git.

## Autonomie

Der lokale Installer legt die Referenz unabhaengig von einem Projekt unter `%LOCALAPPDATA%\\KI-Engineering-Memory` ab.
Eine geplante Windows-Aufgabe aktualisiert die Runtime-Inventur automatisch.
Die Referenz selbst wird dabei niemals automatisch umgeschrieben.

## Universelle Nutzung

Jede KI oder jedes Projekt kann `connectors/Build-KI-Context.ps1` aufrufen.
Der Connector baut aus stabiler Referenz plus aktuellem Maschinenprofil einen aktuellen Kontext.
Es gibt keine harte Bindung an Jarvis, Ollama, LM Studio, Python-Projekte oder ein bestimmtes GitHub-Repository.

## Sicherheitsregel

Runtime-Daten koennen Benutzername, lokale Pfade, Ports, Modellnamen und Softwarezustand enthalten. Deshalb werden Runtime-Dateien standardmaessig lokal gehalten und durch `.gitignore` ausgeschlossen.

## Dateien

- `reference/KI_Dauerreferenz_Programmierung_und_Softwarequalitaet_DE.md`
- `reference/KI_Dauerreferenz_Programmierung_und_Softwarequalitaet_DE.txt`
- `reference/LOAD_INSTRUCTION.txt`
- `tools/Install-KI-Engineering-Memory.ps1`
- `tools/Update-KI-MachineProfile.ps1`
- `tools/Install-KI-Engineering-Memory-AutoUpdate.ps1`
- `tools/Export-KI-Context.ps1`
- `connectors/Build-KI-Context.ps1`
- `docs/ARCHITECTURE.md`
- `docs/LOCAL_AI_USAGE.md`

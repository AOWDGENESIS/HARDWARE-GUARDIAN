# Python - Programmierreferenz

PROGRAMMING_LANGUAGE: Python
DOCUMENT_TYPE: Language Reference
STATUS: Consolidated 2026-09-14

## Rolle
Python ist besonders geeignet fuer KI, Data Science, Automatisierung, lokale AI-Pipelines, APIs, Datenverarbeitung und Testwerkzeuge.

## Engineering-Regeln
- Virtuelle Umgebungen fuer projektbezogene Abhaengigkeiten bevorzugen.
- Imports, Syntax und Abhaengigkeiten real pruefen.
- subprocess-Aufrufe mit Timeouts, Exitcode-Pruefung und kontrollierter Ausgabe absichern.
- Pfade nicht raten, sondern erkennen und validieren.
- Encoding explizit behandeln.
- Tests reproduzierbar ausfuehren.

## Typische Werkzeuge
- venv
- pip
- pytest
- unittest
- compileall
- typing
- pathlib

## Sicherheitsfokus
Input validieren, Secrets nicht loggen, externe Prozesse begrenzen, Dateizugriffe auf erlaubte Bereiche beschraenken.

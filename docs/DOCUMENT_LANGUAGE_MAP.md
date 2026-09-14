# Dokument- und Sprachkennzeichnung

PROGRAMMING_LANGUAGE: DOCUMENTATION MAP
DATE: 2026-09-14

Jede Sprachreferenz besitzt im Dokumentkopf ein explizites PROGRAMMING_LANGUAGE-Feld.
Bestehende Altpakete bleiben unveraendert als Legacy-Snapshots erhalten.
Fuer allgemeine Dokumente wird Multi-language beziehungsweise Documentation verwendet, weil keine einzelne Programmiersprache korrekt waere.

## PowerShell
- tools/*.ps1
- legacy/**/tools/*.ps1

## Python
- *.py if present in future projects
- language_references/Python.md

## JavaScript
- *.js if present in future projects
- language_references/JavaScript.md

## Java
- *.java if present in future projects
- language_references/Java.md

## C++
- *.cpp/*.h if present in future projects
- language_references/C++.md

## C#
- *.cs if present in future projects
- language_references/C#.md

## Go
- *.go if present in future projects
- language_references/Go.md

## PHP
- *.php if present in future projects
- language_references/PHP.md

## TypeScript
- *.ts/*.tsx if present in future projects
- language_references/TypeScript.md

## Rust
- *.rs if present in future projects
- language_references/Rust.md

## R
- *.r/*.R if present in future projects
- language_references/R.md

## Kotlin
- *.kt/*.kts if present in future projects
- language_references/Kotlin.md

## Android
- Android projects; language_references/Android_Platform.md

## Multi-language / documentation
- README.md, CHANGELOG.md, ARCHITECTURE.md, LOCAL_AI_USAGE.md, reference/*.md, reference/*.txt, docs/*.md

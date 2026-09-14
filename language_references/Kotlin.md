# Kotlin - Programmierreferenz

PROGRAMMING_LANGUAGE: Kotlin
DOCUMENT_TYPE: Language Reference
STATUS: Added for Android coverage
DATE: 2026-09-14

## Rolle
Kotlin ist die primaere moderne Programmiersprache fuer Android-Anwendungsentwicklung und laeuft auf der JVM.

## Android-Schwerpunkt
- Android Studio und Gradle sind der typische Build- und Entwicklungsstack.
- Coroutines eignen sich fuer strukturierte asynchrone Arbeit.
- Jetpack und Jetpack Compose sind zentrale moderne Android-Bausteine.
- Lifecycle und Ressourcen muessen korrekt behandelt werden.
- UI-Thread und Hintergrundarbeit strikt trennen.

## Engineering-Regeln
- Null-Sicherheit konsequent nutzen.
- Exceptions und Coroutine-Fehler nicht verschlucken.
- Build, Lint und Tests real ausfuehren.
- Abhaengigkeiten ueber Gradle reproduzierbar verwalten.
- Keine geheimen Zugangsdaten in APK, Source oder Repository einbetten.

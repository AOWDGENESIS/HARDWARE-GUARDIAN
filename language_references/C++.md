# C++ - Programmierreferenz

PROGRAMMING_LANGUAGE: C++
DOCUMENT_TYPE: Language Reference
STATUS: Consolidated 2026-09-14

## Rolle
C++ wird fuer systemnahe, native und performancekritische Software eingesetzt.

## Engineering-Regeln
- RAII und Smart Pointer bevorzugen.
- Ownership und Lifetime eindeutig machen.
- Undefined Behavior vermeiden.
- Compiler-Warnings als Fehler behandeln, wo sinnvoll.
- Sanitizer und reale Tests fuer kritische Komponenten einsetzen.

## Typische Werkzeuge
- CMake
- Ninja
- MSVC
- Clang
- AddressSanitizer

## Android
C++ kann ueber das Android NDK fuer native Komponenten eingesetzt werden. Die Android-Anwendungslogik sollte nicht ohne Grund komplett auf C++ verlagert werden.

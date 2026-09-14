# Architecture

The project has two layers.

## Stable reference

`reference/`

Contains language-independent and technology-specific engineering rules, quality gates, security guidance, testing practices, error handling patterns, and instructions for local AI systems.

## Volatile runtime profile

`runtime/`

Contains the current local machine and tool snapshot generated on the target computer. Runtime files are deliberately excluded from Git.

## Updater

`tools/Update-KI-Dauerreferenz.ps1`

Reads local facts without installing software or changing system configuration. It refreshes the runtime profile.

## Scheduler

`tools/Install-KI-Dauerreferenz-AutoUpdate.ps1`

Creates a per-user scheduled task for logon and daily refresh.

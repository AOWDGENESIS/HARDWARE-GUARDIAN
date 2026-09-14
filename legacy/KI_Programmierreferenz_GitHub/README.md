# KI Programming Reference and Local Machine Profile

A German-first, local-AI-friendly engineering reference for Windows, PowerShell, Python and local AI tooling.

## What this project contains

- Stable programming and software-quality rules.
- Error handling, logging, validation, security and testing guidance.
- Windows/PowerShell/Python/local-AI working practices.
- A local runtime inventory that updates automatically.
- A scheduled updater for current machine/tool facts.
- GitHub-safe handling of volatile machine data.

## Important separation

The stable reference belongs in `reference/`.

Machine-specific facts belong in `runtime/` and are not published to GitHub. This prevents local usernames, paths, installed tools, model names, ports and environment values from becoming public or stale project documentation.

## Local installation

From the project directory on Windows:

```powershell
& ".\tools\Install-KI-Dauerreferenz-AutoUpdate.ps1"
```

For a manual refresh:

```powershell
& ".\tools\Update-KI-Dauerreferenz.ps1"
```

The updater performs local inventory only. It does not install dependencies or change system configuration.

## Using it with a local AI

Load:

- `reference/KI_Dauerreferenz_Programmierung_und_Softwarequalitaet_DE.md`
- `reference/LOAD_INSTRUCTION.txt`
- `runtime/KI_Maschinenprofil.md`

The first two are stable behavioral guidance. The runtime profile is current-environment evidence only.

## License

MIT

## GitHub publication

The included `tools/Publish-ToGitHub.ps1` can create or update the repository through the GitHub CLI.

1. Install Git and GitHub CLI.
2. Run `gh auth login` once.
3. Run:

```powershell
& ".\tools\Publish-ToGitHub.ps1" -Repository "KI-Programmierreferenz" -Visibility private
```

The default is private. Use `-Visibility public` only when you intentionally want the reference to be public.

The publisher commits and pushes the project and preserves the Git-ignore rule for local runtime data.

#requires -Version 7.2
<#
.SYNOPSIS
    Registriert und prueft Dauerreferenzen mit gepinntem SHA256.

.DESCRIPTION
    Dauerreferenzen (Regelwerke, Leitlinien, Prompt-Bausteine) sind nur dann
    belastbar, wenn belegbar ist, WELCHE Fassung geladen wurde. Dieses Werkzeug
    fuehrt dazu ein Manifest in reference\manifest.json.

    Modi:
      (Standard)   Datei(en) registrieren: Groesse, Zeichenzahl, SHA256, Zeitpunkt
      -List        registrierte Referenzen anzeigen
      -Verify      jede registrierte Datei gegen ihren gepinnten Hash pruefen
      -Remove      Eintraege entfernen (Manifest aendern, Dateien bleiben liegen)

    Schutz gegen stilles Ueberschreiben:
      Aendert sich eine bereits registrierte Datei, verweigert das Werkzeug den
      Eintrag mit INHALT_GEAENDERT und zeigt alt -> neu. Erst -Force aktualisiert
      den Pin. Ein Hash, der sich unbemerkt aendern darf, ist kein Hash.

    Es wird nichts geloescht und nichts ausserhalb von reference\ geschrieben.

.PARAMETER Path
    Eine oder mehrere Referenzdateien (relativ oder absolut).

.PARAMETER Note
    Freitext, der beim Registrieren mitgespeichert wird.

.PARAMETER ReferenceRoot
    Ordner der Referenzen. Standard: <Repo-Wurzel>\reference

.PARAMETER Verify
    Alle registrierten Referenzen gegen ihre gepinnten Hashes pruefen.

.PARAMETER List
    Registrierte Referenzen tabellarisch anzeigen.

.PARAMETER Remove
    Eintraege zu den angegebenen Pfaden aus dem Manifest entfernen.

.PARAMETER Force
    Pin aktualisieren, wenn sich der Inhalt geaendert hat.

.EXAMPLE
    .\tools\Register-ReferenceDocument.ps1 -Path .\reference\regelwerk.md -Note "v1"
.EXAMPLE
    .\tools\Register-ReferenceDocument.ps1 -Verify
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory = $false, Position = 0)]
    [string[]]$Path = @(),

    [Parameter(Mandatory = $false)]
    [string]$Note = "",

    [Parameter(Mandatory = $false)]
    [string]$ReferenceRoot = "",

    [Parameter(Mandatory = $false)]
    [switch]$Verify,

    [Parameter(Mandatory = $false)]
    [switch]$List,

    [Parameter(Mandatory = $false)]
    [switch]$Remove,

    [Parameter(Mandatory = $false)]
    [switch]$Force
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$script:Utf8 = [System.Text.UTF8Encoding]::new($false)
$script:Problems = 0

function Get-TextInfo {
    param([Parameter(Mandatory = $true)][string]$FilePath)

    $Bytes = [System.IO.File]::ReadAllBytes($FilePath)
    $HasBom = ($Bytes.Length -ge 3 -and $Bytes[0] -eq 0xEF -and $Bytes[1] -eq 0xBB -and $Bytes[2] -eq 0xBF)
    $Text = $script:Utf8.GetString($Bytes)

    if ($HasBom) { $Text = $Text.Substring(1) }

    return [pscustomobject]@{
        Bytes  = $Bytes.Length
        Chars  = $Text.Length
        Lines  = (@($Text -split "`r?`n")).Count
        HasBom = $HasBom
        SHA256 = (Get-FileHash -LiteralPath $FilePath -Algorithm SHA256).Hash.ToUpperInvariant()
    }
}

function Read-Manifest {
    param([Parameter(Mandatory = $true)][string]$ManifestPath)

    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        return [pscustomobject]@{ Schema = '1.0'; Documents = @() }
    }

    $Raw = [System.IO.File]::ReadAllText($ManifestPath, $script:Utf8)

    if ([string]::IsNullOrWhiteSpace($Raw)) {
        return [pscustomobject]@{ Schema = '1.0'; Documents = @() }
    }

    $Parsed = $Raw | ConvertFrom-Json -Depth 16

    if (@($Parsed.PSObject.Properties.Name) -notcontains 'Documents') {
        throw "MANIFEST_UNGUELTIG: Feld 'Documents' fehlt in $ManifestPath"
    }

    return $Parsed
}

function Write-Manifest {
    param(
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][object[]]$Documents
    )

    # Ordinal sortieren - nicht kulturabhaengig (siehe Befund F-08 im Pruefbericht).
    $Names = [string[]]@($Documents | ForEach-Object { [string]$_.RelativePath })
    [System.Array]::Sort($Names, [System.StringComparer]::Ordinal)

    $ByName = @{}

    foreach ($Document in $Documents) {
        $ByName[[string]$Document.RelativePath] = $Document
    }

    $Ordered = [object[]]@(
        foreach ($Name in $Names) { $ByName[$Name] }
    )

    $Payload = [ordered]@{
        schema      = '1.0'
        updated_utc = (Get-Date).ToUniversalTime().ToString('o')
        documents   = @($Ordered)
    }

    $Json = $Payload | ConvertTo-Json -Depth 16

    # Bewusst "\n" und kein [Environment]::NewLine: gleiche Bytes auf jedem System.
    $Normalized = ($Json -replace "`r`n", "`n") + "`n"

    [System.IO.File]::WriteAllText($ManifestPath, $Normalized, $script:Utf8)
}

# ---------------------------------------------------------------------------
# Vorbereitung
# ---------------------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($ReferenceRoot)) {
    $ReferenceRoot = Join-Path (Split-Path -Path $PSScriptRoot -Parent) 'reference'
}

if (-not (Test-Path -LiteralPath $ReferenceRoot -PathType Container)) {
    New-Item -ItemType Directory -Path $ReferenceRoot -Force | Out-Null
}

$ReferenceRootFull = (Resolve-Path -LiteralPath $ReferenceRoot).Path.TrimEnd('\')
$ManifestPath = Join-Path $ReferenceRootFull 'manifest.json'
$Manifest = Read-Manifest -ManifestPath $ManifestPath
$Documents = @($Manifest.Documents)

Write-Host ''
Write-Host '============================================'
Write-Host ' DAUERREFERENZEN'
Write-Host '============================================'
Write-Host "Ordner   : $ReferenceRootFull"
Write-Host "Manifest : $ManifestPath"
Write-Host "Eintraege: $($Documents.Count)"
Write-Host ''

# ---------------------------------------------------------------------------
# Modus: auflisten
# ---------------------------------------------------------------------------
if ($List) {
    if ($Documents.Count -eq 0) {
        Write-Host 'Keine Referenzen registriert.' -ForegroundColor Yellow
        exit 0
    }

    foreach ($Document in $Documents) {
        $State = 'OK'
        $Color = 'Green'
        $Absolute = Join-Path $ReferenceRootFull ([string]$Document.FileName)

        if (-not (Test-Path -LiteralPath $Absolute -PathType Leaf)) {
            $State = 'FEHLT'
            $Color = 'Red'
            $script:Problems++
        }
        else {
            $Now = (Get-FileHash -LiteralPath $Absolute -Algorithm SHA256).Hash.ToUpperInvariant()

            if ($Now -ne [string]$Document.SHA256) {
                $State = 'GEAENDERT'
                $Color = 'Red'
                $script:Problems++
            }
        }

        Write-Host ("{0,-10} {1}" -f $State, [string]$Document.RelativePath) -ForegroundColor $Color
        Write-Host ("           SHA256 {0} | {1} Bytes | {2} Zeichen" -f [string]$Document.SHA256, $Document.Bytes, $Document.Chars)

        if ($Document.Note) {
            Write-Host ("           Notiz: {0}" -f $Document.Note)
        }
    }

    Write-Host ''
    Write-Host ("Probleme: {0}" -f $script:Problems)
    exit $(if ($script:Problems -eq 0) { 0 } else { 1 })
}

# ---------------------------------------------------------------------------
# Modus: pruefen
# ---------------------------------------------------------------------------
if ($Verify) {
    if ($Documents.Count -eq 0) {
        Write-Host 'VERIFY: keine registrierten Referenzen.' -ForegroundColor Yellow
        exit 0
    }

    $Ok = 0
    $Bad = 0
    $RegisteredNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

    foreach ($Document in $Documents) {
        $FileName = [string]$Document.FileName
        [void]$RegisteredNames.Add($FileName)
        $Absolute = Join-Path $ReferenceRootFull $FileName

        if (-not (Test-Path -LiteralPath $Absolute -PathType Leaf)) {
            Write-Host ("FEHLT      {0}" -f $FileName) -ForegroundColor Red
            $Bad++
            continue
        }

        $Info = Get-TextInfo -FilePath $Absolute

        if ($Info.SHA256 -ne [string]$Document.SHA256) {
            Write-Host ("GEAENDERT  {0}" -f $FileName) -ForegroundColor Red
            Write-Host ("           erwartet: {0}" -f [string]$Document.SHA256)
            Write-Host ("           gefunden: {0}" -f $Info.SHA256)
            $Bad++
            continue
        }

        if ($Info.Bytes -ne [int64]$Document.Bytes -or $Info.Chars -ne [int]$Document.Chars) {
            Write-Host ("ABWEICHUNG {0} (Groesse/Zeichenzahl trotz gleichem Hash)" -f $FileName) -ForegroundColor Red
            $Bad++
            continue
        }

        Write-Host ("OK         {0}  {1}" -f $FileName, $Info.SHA256) -ForegroundColor Green
        $Ok++
    }

    # Dateien, die im Ordner liegen, aber nicht registriert sind
    $Unregistered = @(
        Get-ChildItem -LiteralPath $ReferenceRootFull -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -ne 'manifest.json' -and -not $RegisteredNames.Contains($_.Name) }
    )

    foreach ($File in $Unregistered) {
        Write-Host ("NICHT REGISTRIERT  {0}" -f $File.Name) -ForegroundColor Yellow
        $Bad++
    }

    Write-Host ''
    Write-Host ("OK: {0} | Probleme: {1}" -f $Ok, $Bad)

    if ($Bad -eq 0) {
        Write-Host 'REFERENZEN: PASS' -ForegroundColor Green
        exit 0
    }

    Write-Host 'REFERENZEN: FAIL' -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------------------
# Modus: entfernen
# ---------------------------------------------------------------------------
if ($Remove) {
    if ($Path.Count -eq 0) {
        throw 'REMOVE_OHNE_PATH: -Remove braucht mindestens eine Datei in -Path.'
    }

    $Kept = [System.Collections.Generic.List[object]]::new()
    $Removed = 0

    foreach ($Document in $Documents) {
        $Match = $false

        foreach ($Candidate in $Path) {
            $Leaf = Split-Path -Path $Candidate -Leaf

            if ($Leaf -eq [string]$Document.FileName) { $Match = $true }
        }

        if ($Match) {
            Write-Host ("ENTFERNT   {0}" -f [string]$Document.RelativePath) -ForegroundColor Yellow
            $Removed++
        }
        else {
            $Kept.Add($Document)
        }
    }

    if ($Removed -eq 0) {
        Write-Host 'Kein passender Eintrag gefunden.' -ForegroundColor Yellow
        exit 1
    }

    if ($PSCmdlet.ShouldProcess($ManifestPath, "$Removed Eintrag/Eintraege entfernen")) {
        Write-Manifest -ManifestPath $ManifestPath -Documents @($Kept)
        Write-Host "Manifest aktualisiert: $ManifestPath"
    }

    exit 0
}

# ---------------------------------------------------------------------------
# Modus: registrieren
# ---------------------------------------------------------------------------
if ($Path.Count -eq 0) {
    Write-Host 'Kein -Path angegeben. Nutze -List oder -Verify, oder gib Referenzdateien an.' -ForegroundColor Yellow
    Write-Host 'Beispiel: .\tools\Register-ReferenceDocument.ps1 -Path .\reference\regelwerk.md'
    exit 2
}

$Inserted = 0
$Updated = 0
$Unchanged = 0
$Refused = 0

foreach ($Candidate in $Path) {
    $Resolved = (Resolve-Path -LiteralPath $Candidate -ErrorAction Stop).Path

    if (-not (Test-Path -LiteralPath $Resolved -PathType Leaf)) {
        throw "NICHT_GEFUNDEN: $Candidate"
    }

    $ReferenceRootPrefix = $ReferenceRootFull + '\'

    if (-not $Resolved.StartsWith($ReferenceRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "AUSSERHALB_DES_REFERENZORDNERS: $Resolved  (erlaubt: $ReferenceRootFull)"
    }

    $FileName = Split-Path -Path $Resolved -Leaf
    $RelativePath = 'reference/' + $FileName
    $Info = Get-TextInfo -FilePath $Resolved

    $Existing = @($Documents | Where-Object { [string]$_.FileName -eq $FileName }) | Select-Object -First 1
    $Entry = [pscustomobject][ordered]@{
        FileName     = $FileName
        RelativePath = $RelativePath
        SHA256       = $Info.SHA256
        Bytes        = [int64]$Info.Bytes
        Chars        = [int]$Info.Chars
        Lines        = [int]$Info.Lines
        HasBom       = [bool]$Info.HasBom
        RegisteredUtc = (Get-Date).ToUniversalTime().ToString('o')
        Note         = $Note
    }

    if ($null -ne $Existing) {
        if ([string]$Existing.SHA256 -eq $Info.SHA256) {
            Write-Host ("UNVERAENDERT {0}" -f $FileName) -ForegroundColor DarkGray
            Write-Host ("             SHA256 {0}" -f $Info.SHA256)
            $Unchanged++
            continue
        }

        if (-not $Force) {
            Write-Host ("INHALT_GEAENDERT {0}" -f $FileName) -ForegroundColor Red
            Write-Host ("             alt: {0}" -f [string]$Existing.SHA256)
            Write-Host ("             neu: {0}" -f $Info.SHA256)
            Write-Host '             Pin bleibt unveraendert. Mit -Force bewusst aktualisieren.' -ForegroundColor Yellow
            $Refused++
            continue
        }

        Write-Host ("AKTUALISIERT {0}" -f $FileName) -ForegroundColor Yellow
        Write-Host ("             alt: {0}" -f [string]$Existing.SHA256)
        Write-Host ("             neu: {0}" -f $Info.SHA256)

        $Documents = @($Documents | Where-Object { [string]$_.FileName -ne $FileName })
        $Documents += $Entry
        $Updated++
        continue
    }

    Write-Host ("REGISTRIERT {0}" -f $FileName) -ForegroundColor Green
    Write-Host ("             SHA256 {0}" -f $Info.SHA256)
    Write-Host ("             {0} Bytes | {1} Zeichen | BOM: {2}" -f $Info.Bytes, $Info.Chars, $Info.HasBom)

    if ($Info.HasBom) {
        Write-Host '             HINWEIS: BOM vorhanden - kann Markerpruefungen und Hashes beeinflussen (Befund F-33).' -ForegroundColor Yellow
    }

    $Documents += $Entry
    $Inserted++
}

if ($Inserted -eq 0 -and $Updated -eq 0) {
    Write-Host ''
    Write-Host ("Keine Aenderung am Manifest (unveraendert: {0}, verweigert: {1})." -f $Unchanged, $Refused)
    exit $(if ($Refused -gt 0) { 1 } else { 0 })
}

if ($PSCmdlet.ShouldProcess($ManifestPath, "Manifest aktualisieren (+$Inserted neu, ~$Updated aktualisiert)")) {
    Write-Manifest -ManifestPath $ManifestPath -Documents $Documents

    Write-Host ''
    Write-Host '============================================'
    Write-Host ' MANIFEST AKTUALISIERT' -ForegroundColor Green
    Write-Host '============================================'
    Write-Host "Datei     : $ManifestPath"
    Write-Host "Neu       : $Inserted"
    Write-Host "Aktualisiert: $Updated"
    Write-Host "Unveraendert: $Unchanged"
    Write-Host "Verweigert : $Refused"
    Write-Host ''
    Write-Host 'Gegenprobe:'
    Write-Host "  & '$PSCommandPath' -Verify"
}

#requires -Version 7.2
<#
.SYNOPSIS
    Ersetzt eine PowerShell-Funktion in einer Produktivdatei sicher per AST.

.DESCRIPTION
    Ersetzt die fehleranfaellige Patchtechnik "Quelltext per -replace austauschen".

    Warum -replace fuer Code ungeeignet ist:
        $Text -replace $Old, $New
    interpretiert im Ersetzungstext $1, $&, $`, $' und $_ als
    Ersetzungsanweisungen. Code, der $($_.Exception.Message) enthaelt, wird
    dadurch zerstoert - genau das ist im LocalCloudCode-Patch passiert
    ("Der zweite Operand von -replace wird als Replacement-String interpretiert").

    Dieses Werkzeug arbeitet stattdessen mit dem echten PowerShell-Parser:
      1. Ziel und neuer Block werden geparst (Syntaxgate vor jeder Aenderung).
      2. Die zu ersetzende Funktion wird per AST gefunden (FunctionDefinitionAst).
      3. Ersetzt wird genau der Zeichenbereich (Extent) der Funktion.
      4. Nach dem Schreiben wird erneut geparst und strukturell verglichen
         (Text vor und nach der Funktion muss bitgenau unveraendert sein).
      5. Bei jedem Fehler wird automatisch aus dem Backup zurueckgerollt.

    Es wird kein Code ausgefuehrt und kein Netzwerk verwendet.

.PARAMETER TargetFile
    Die zu patchende Datei (z. B. LocalCloudCode.ps1).

.PARAMETER NewBlockFile
    Datei, die die neue Funktionsdefinition enthaelt (nur die Funktion).

.PARAMETER FunctionName
    Name der zu ersetzenden Funktion. Wenn leer, wird der Name aus dem neuen
    Block abgeleitet. Sind im neuen Block mehrere Funktionen, muss der Name
    angegeben werden; dann werden alle gleichnamigen Definitionen ersetzt.

.PARAMETER BackupDirectory
    Zielordner fuer Backups. Standard: <Ordner der Zieldatei>\backups

.PARAMETER JournalPath
    JSONL-Journal. Standard: <BackupDirectory>\patch_journal.jsonl

.PARAMETER AppendIfMissing
    Neue Funktion anhaengen, wenn sie in der Zieldatei nicht existiert.

.PARAMETER Force
    Erlaubt Ersetzen, wenn der Name oefter als einmal vorkommt (ersetzt dann
    ALLE Vorkommen; die Anzahl wird protokolliert).

.PARAMETER ExpectedSha256
    Optionaler Baseline-Pin. Stimmt der SHA256 der Zieldatei nicht mit diesem
    Wert ueberein, wird der Patch verweigert. Verhindert das Patchen eines
    unbekannten Zwischenstands. Nach jedem erfolgreichen Patch dient der neu
    ausgegebene Hash als naechster Pin.

.SICHERUNGEN
    Dieses Werkzeug gibt "PATCH: PASS" nur aus, wenn ALLE Bedingungen erfuellt
    sind:
      1. Ziel- und Blockdatei parsen fehlerfrei.
      2. Die Zwischendatei existiert unmittelbar vor dem Tausch noch.
      3. Das Backup existiert unmittelbar vor dem Tausch noch.
      4. Der Text ausserhalb der ersetzten Funktion ist Zeichen fuer Zeichen
         unveraendert.
      5. Die Funktion kommt danach genau einmal vor.
      6. Der SHA256 der Datei hat sich tatsaechlich GEAENDERT.
    Punkt 6 fehlte einer frueheren Fassung von Patchskripten: dort wurde der
    Tausch abgebrochen (Zwischendatei vorher geloescht), danach liefen die
    Pruefungen gegen die unveraenderte Originaldatei und am Ende stand
    "PATCH: PASS", obwohl nichts geschrieben wurde. Ein PASS, das nicht an die
    Aenderung des Artefakts gebunden ist, ist wertlos.

.EXAMPLE
    .\tools\Update-PsFunctionBlock.ps1 `
        -TargetFile "C:\Users\aowdg\Desktop\KI\LocalCloudCode\LocalCloudCode.ps1" `
        -NewBlockFile ".\patches\Get-DiscoveryResult.v1.2.ps1" `
        -WhatIf

.EXAMPLE
    .\tools\Update-PsFunctionBlock.ps1 `
        -TargetFile  "C:\Users\aowdg\Desktop\KI\LocalCloudCode\LocalCloudCode.ps1" `
        -NewBlockFile ".\patches\Get-DiscoveryResult.v1.2.ps1"
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory = $true)]
    [string]$TargetFile,

    [Parameter(Mandatory = $true)]
    [string]$NewBlockFile,

    [Parameter(Mandatory = $false)]
    [string]$FunctionName = "",

    [Parameter(Mandatory = $false)]
    [string]$BackupDirectory = "",

    [Parameter(Mandatory = $false)]
    [string]$JournalPath = "",

    [Parameter(Mandatory = $false)]
    [switch]$AppendIfMissing,

    [Parameter(Mandatory = $false)]
    [switch]$Force,

    [Parameter(Mandatory = $false)]
    [string]$ExpectedSha256 = ""
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$script:Utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$script:SmartQuotePattern = '[\u201C\u201D\u201E\u201A\u2018\u2019]'

function Get-ParseErrors {
    param([Parameter(Mandatory = $true)][string]$Path)

    $Tokens = $null
    $Errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$Tokens, [ref]$Errors) | Out-Null

    return @($Errors)
}

function Get-ParseErrorsFromText {
    param([Parameter(Mandatory = $true)][string]$Text)

    $Tokens = $null
    $Errors = $null
    [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$Tokens, [ref]$Errors) | Out-Null

    return @($Errors)
}

function Get-FunctionAsts {
    param([Parameter(Mandatory = $true)][string]$Text)

    $Tokens = $null
    $Errors = $null
    $Ast = [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$Tokens, [ref]$Errors)

    return @(
        $Ast.FindAll(
            { param($Node) $Node -is [System.Management.Automation.Language.FunctionDefinitionAst] },
            $true
        )
    )
}

function Assert-NoSmartQuotes {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Origin
    )

    $Matches = [regex]::Matches($Text, $script:SmartQuotePattern)

    if ($Matches.Count -gt 0) {
        $Line = 1
        $Index = 0

        foreach ($Ch in $Text.ToCharArray()) {
            if ($Index -lt $Matches[0].Index) {
                if ($Ch -eq "`n") { $Line++ }
            }
            $Index++
        }

        throw "SMART_QUOTES_DETECTED in $Origin (erster Treffer Zeile ~$Line). Typografische Anfuehrungszeichen brechen PowerShell."
    }
}

function Get-HashText {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Write-Journal {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][hashtable]$Entry
    )

    try {
        $Directory = Split-Path -Path $Path -Parent

        if ($Directory -and -not (Test-Path -LiteralPath $Directory -PathType Container)) {
            New-Item -ItemType Directory -Path $Directory -Force | Out-Null
        }

        $Line = ($Entry | ConvertTo-Json -Depth 8 -Compress)
        [System.IO.File]::AppendAllText($Path, $Line + [Environment]::NewLine, $script:Utf8NoBom)
    }
    catch {
        Write-Warning "Journal konnte nicht geschrieben werden: $($_.Exception.Message)"
    }
}

Write-Host ''
Write-Host '============================================'
Write-Host ' UPDATE PS FUNCTION BLOCK'
Write-Host '============================================'

# ---- 0. Pfade pruefen ------------------------------------------------------
$TargetPath = (Resolve-Path -LiteralPath $TargetFile -ErrorAction Stop).Path

if (-not (Test-Path -LiteralPath $TargetPath -PathType Leaf)) {
    throw "TARGET_NOT_A_FILE: $TargetPath"
}

$NewBlockPath = (Resolve-Path -LiteralPath $NewBlockFile -ErrorAction Stop).Path

if (-not (Test-Path -LiteralPath $NewBlockPath -PathType Leaf)) {
    throw "NEW_BLOCK_NOT_A_FILE: $NewBlockPath"
}

if ($BackupDirectory) {
    $BackupRoot = $BackupDirectory
}
else {
    $BackupRoot = Join-Path (Split-Path -Path $TargetPath -Parent) 'backups'
}

if ($JournalPath) {
    $Journal = $JournalPath
}
else {
    $Journal = Join-Path $BackupRoot 'patch_journal.jsonl'
}

if ($TargetPath -eq $NewBlockPath) {
    throw 'TARGET_AND_SOURCE_IDENTICAL'
}

# ---- 1. Ziel einlesen (Encoding/BOM erhalten) ------------------------------
$TargetBytes = [System.IO.File]::ReadAllBytes($TargetPath)
$HasBom = ($TargetBytes.Length -ge 3 -and $TargetBytes[0] -eq 0xEF -and $TargetBytes[1] -eq 0xBB -and $TargetBytes[2] -eq 0xBF)
$TargetText = $script:Utf8NoBom.GetString($TargetBytes)

if ($HasBom) {
    $TargetText = $TargetText.Substring(1)
    Write-Host 'HINWEIS: Zieldatei hat eine UTF-8-BOM (wird erhalten).'
}

$TargetHashBefore = (Get-FileHash -LiteralPath $TargetPath -Algorithm SHA256).Hash.ToUpperInvariant()

Write-Host "Ziel    : $TargetPath"
Write-Host "Groesse : $($TargetBytes.Length) Bytes"
Write-Host "SHA256  : $TargetHashBefore"

if (-not [string]::IsNullOrWhiteSpace($ExpectedSha256)) {
    $ExpectedNorm = $ExpectedSha256.Trim().ToUpperInvariant()

    if ($TargetHashBefore -ne $ExpectedNorm) {
        Write-Host ''
        Write-Host 'BASELINE: FAIL' -ForegroundColor Red
        Write-Host "  erwartet: $ExpectedNorm"
        Write-Host "  gefunden: $TargetHashBefore"
        throw 'BASELINE_MISMATCH: Die Zieldatei entspricht nicht dem gepinnten Stand. Es wird nichts geaendert.'
    }

    Write-Host 'BASELINE: OK (Datei entspricht dem gepinnten Stand)' -ForegroundColor Green
}

Write-Host ''

# ---- 2. Syntaxgate Zieldatei ----------------------------------------------
$TargetErrors = @(Get-ParseErrors -Path $TargetPath)

if ($TargetErrors.Count -ne 0) {
    Write-Host 'SYNTAX ZIELDATEI: FAIL' -ForegroundColor Red

    foreach ($ErrorItem in ($TargetErrors | Select-Object -First 10)) {
        Write-Host ("  Zeile {0}, Spalte {1}: {2}" -f $ErrorItem.Extent.StartLineNumber, $ErrorItem.Extent.StartColumnNumber, $ErrorItem.Message) -ForegroundColor Red
    }

    throw "TARGET_PARSE_FAILED: $($TargetErrors.Count) Fehler. Erst Syntax reparieren, dann patchen."
}

Assert-NoSmartQuotes -Text $TargetText -Origin $TargetPath
Write-Host 'SYNTAX ZIELDATEI: OK' -ForegroundColor Green

# ---- 3. Neuen Block pruefen ------------------------------------------------
$NewBlockBytes = [System.IO.File]::ReadAllBytes($NewBlockPath)
$NewBlockText = $script:Utf8NoBom.GetString($NewBlockBytes)

if ($NewBlockBytes.Length -ge 3 -and $NewBlockBytes[0] -eq 0xEF -and $NewBlockBytes[1] -eq 0xBB -and $NewBlockBytes[2] -eq 0xBF) {
    $NewBlockText = $NewBlockText.Substring(1)
}

Assert-NoSmartQuotes -Text $NewBlockText -Origin $NewBlockPath

$NewBlockErrors = @(Get-ParseErrorsFromText -Text $NewBlockText)

if ($NewBlockErrors.Count -ne 0) {
    foreach ($ErrorItem in $NewBlockErrors) {
        Write-Host ("  Zeile {0}: {1}" -f $ErrorItem.Extent.StartLineNumber, $ErrorItem.Message) -ForegroundColor Red
    }

    throw "NEW_BLOCK_PARSE_FAILED: $($NewBlockErrors.Count) Fehler."
}

$NewFunctions = @(Get-FunctionAsts -Text $NewBlockText)

if ($NewFunctions.Count -eq 0) {
    throw 'NEW_BLOCK_HAS_NO_FUNCTION'
}

foreach ($Function in $NewFunctions) {
    if ($Function.Body.Extent.Text -match '\$\$|TrimS\b') {
        Write-Warning "Verdaechtiges Konstrukt in neuer Funktion $($Function.Name) - bitte pruefen."
    }
}

if ([string]::IsNullOrWhiteSpace($FunctionName)) {
    if ($NewFunctions.Count -ne 1) {
        throw "FUNCTION_NAME_REQUIRED: neuer Block enthaelt $($NewFunctions.Count) Funktionen."
    }

    $FunctionName = $NewFunctions[0].Name
}

if (@($NewFunctions | Where-Object { $_.Name -eq $FunctionName }).Count -eq 0) {
    throw "NEW_BLOCK_FUNCTION_MISMATCH: '$FunctionName' ist im neuen Block nicht enthalten."
}

Write-Host "Neuer Block: $NewBlockPath"
Write-Host "Funktion    : $FunctionName ($($NewFunctions.Count) Definition(en) im Block)"
Write-Host ''

# ---- 4. Ersetzungsbereiche bestimmen --------------------------------------
$TargetFunctions = @(Get-FunctionAsts -Text $TargetText)
$Matching = @($TargetFunctions | Where-Object { $_.Name -eq $FunctionName })

if ($Matching.Count -eq 0) {
    if (-not $AppendIfMissing) {
        throw "FUNCTION_NOT_FOUND: '$FunctionName' existiert in der Zieldatei nicht (mit -AppendIfMissing anhaengen)."
    }

    Write-Host "Funktion nicht gefunden - wird angehaengt (-AppendIfMissing)." -ForegroundColor Yellow

    $Separator = [Environment]::NewLine + [Environment]::NewLine
    $InsertAt = $TargetText.Length
    $Replacement = $Separator + $NewBlockText.TrimEnd() + [Environment]::NewLine
    $NewText = $TargetText + $Replacement
    $StartOffset = $InsertAt
    $EndOffset = $InsertAt
    $ReplacedCount = 0
}
else {
    if ($Matching.Count -gt 1 -and -not $Force) {
        throw "FUNCTION_AMBIGUOUS: '$FunctionName' kommt $($Matching.Count)x vor. Mit -Force alle ersetzen."
    }

    # Von hinten nach vorne ersetzen, damit die Offsets gueltig bleiben.
    $Ordered = @($Matching | Sort-Object { $_.Extent.StartOffset } -Descending)
    $NewText = $TargetText
    $ReplacedCount = $Ordered.Count

    foreach ($Function in $Ordered) {
        $StartOffset = $Function.Extent.StartOffset
        $EndOffset = $Function.Extent.EndOffset
        $NewText = $NewText.Substring(0, $StartOffset) + $NewBlockText.TrimEnd() + $NewText.Substring($EndOffset)
    }

    # Referenzbereich der ERSTEN Definition (niedrigster Offset) fuer die
    # strukturelle Gegenprobe nach dem Schreiben.
    $FirstFunction = @($Matching | Sort-Object { $_.Extent.StartOffset })[0]
    $StartOffset = $FirstFunction.Extent.StartOffset
    $EndOffset = $FirstFunction.Extent.EndOffset
}

if ($NewText -eq $TargetText) {
    Write-Host 'ERGEBNIS: keine Aenderung noetig (Block ist identisch).' -ForegroundColor Yellow

    Write-Journal -Path $Journal -Entry @{
        timestamp   = (Get-Date).ToString('o')
        target      = $TargetPath
        function    = $FunctionName
        result      = 'UNCHANGED'
        hash_before = $TargetHashBefore
        hash_after  = $TargetHashBefore
        tool        = 'Update-PsFunctionBlock.ps1'
    }

    return
}

if (-not $PSCmdlet.ShouldProcess($TargetPath, "Ersetze Funktion '$FunctionName'")) {
    Write-Host ''
    Write-Host 'WHATIF: keine Aenderung geschrieben.' -ForegroundColor Yellow
    Write-Host 'Vorschau der neuen Funktionsdefinition (erste 20 Zeilen):'

    $Preview = ($NewBlockText.TrimEnd() -split "`r?`n" | Select-Object -First 20) -join [Environment]::NewLine
    Write-Host $Preview

    return
}

# ---- 5. Temp-Datei schreiben und pruefen ----------------------------------
$TempPath = Join-Path (Split-Path -Path $TargetPath -Parent) ((Split-Path -Path $TargetPath -Leaf) + '.patchtmp')

try {
    $TempBytes = $script:Utf8NoBom.GetBytes($NewText)

    if ($HasBom) {
        $TempBytes = [byte[]](@(0xEF, 0xBB, 0xBF) + $TempBytes)
    }

    [System.IO.File]::WriteAllBytes($TempPath, $TempBytes)

    $TempErrors = @(Get-ParseErrors -Path $TempPath)

    if ($TempErrors.Count -ne 0) {
        foreach ($ErrorItem in ($TempErrors | Select-Object -First 10)) {
            Write-Host ("  Zeile {0}, Spalte {1}: {2}" -f $ErrorItem.Extent.StartLineNumber, $ErrorItem.Extent.StartColumnNumber, $ErrorItem.Message) -ForegroundColor Red
        }

        throw "TEMP_PARSE_FAILED: $($TempErrors.Count) Fehler."
    }

    $TempText = $script:Utf8NoBom.GetString([System.IO.File]::ReadAllBytes($TempPath))

    if ($HasBom) {
        $TempText = $TempText.Substring(1)
    }

    # Strukturelle Gegenprobe: alles AUSSERHALB des ersetzten Bereichs muss
    # bitgenau identisch sein.
    $BeforeSegment = $TargetText.Substring(0, $StartOffset)
    $AfterSegment = $TargetText.Substring($EndOffset)

    if (-not $TempText.StartsWith($BeforeSegment, [System.StringComparison]::Ordinal)) {
        throw 'STRUCTURE_CHECK_FAILED: Text vor der Funktion wurde veraendert.'
    }

    if (-not $TempText.EndsWith($AfterSegment, [System.StringComparison]::Ordinal)) {
        throw 'STRUCTURE_CHECK_FAILED: Text nach der Funktion wurde veraendert.'
    }

    $TempFunctionCount = @(
        (Get-FunctionAsts -Text $TempText) | Where-Object { $_.Name -eq $FunctionName }
    ).Count

    if ($TempFunctionCount -ne 1 -and $ReplacedCount -le 1) {
        throw "FUNCTION_COUNT_FAILED: '$FunctionName' kommt $TempFunctionCount x vor (erwartet: 1)."
    }

    Write-Host 'TEMP-DATEI: OK (Syntax, Struktur, Funktionsanzahl)' -ForegroundColor Green

    # ---- 6. Backup --------------------------------------------------------
    if (-not (Test-Path -LiteralPath $BackupRoot -PathType Container)) {
        New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null
    }

    $Stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $BackupPath = Join-Path $BackupRoot ("{0}.{1}.{2}.bak" -f (Split-Path -Path $TargetPath -Leaf), $FunctionName, $Stamp)

    Copy-Item -LiteralPath $TargetPath -Destination $BackupPath -Force
    $BackupHash = Get-HashText -Path $BackupPath

    if ($BackupHash -ne $TargetHashBefore) {
        throw "BACKUP_HASH_MISMATCH: $BackupHash (Backup ist nicht identisch mit dem Original)."
    }

    # Unmittelbar vor dem Tausch: beide Dateien muessen noch existieren.
    if (-not (Test-Path -LiteralPath $TempPath -PathType Leaf)) {
        throw 'TEMP_LOST_BEFORE_SWAP: Die Zwischendatei ist vor dem Tausch verschwunden. Es wird nichts geschrieben.'
    }

    if (-not (Test-Path -LiteralPath $BackupPath -PathType Leaf)) {
        throw 'BACKUP_LOST_BEFORE_SWAP: Das Backup ist vor dem Tausch verschwunden. Es wird nichts geschrieben.'
    }

    # ---- 7. Tausch --------------------------------------------------------
    Move-Item -LiteralPath $TempPath -Destination $TargetPath -Force

    # ---- 8. Endkontrolle + Rollback --------------------------------------
    $FinalErrors = @(Get-ParseErrors -Path $TargetPath)

    if ($FinalErrors.Count -ne 0) {
        Copy-Item -LiteralPath $BackupPath -Destination $TargetPath -Force
        throw "FINAL_PARSE_FAILED: $($FinalErrors.Count) Fehler - Rollback ausgefuehrt."
    }

    $FinalText = $script:Utf8NoBom.GetString([System.IO.File]::ReadAllBytes($TargetPath))

    if ($HasBom) {
        $FinalText = $FinalText.Substring(1)
    }

    if (-not $FinalText.StartsWith($BeforeSegment, [System.StringComparison]::Ordinal) -or
        -not $FinalText.EndsWith($AfterSegment, [System.StringComparison]::Ordinal)) {
        Copy-Item -LiteralPath $BackupPath -Destination $TargetPath -Force
        throw 'FINAL_STRUCTURE_CHECK_FAILED - Rollback ausgefuehrt.'
    }

    $FinalFunctionCount = @(
        (Get-FunctionAsts -Text $FinalText) | Where-Object { $_.Name -eq $FunctionName }
    ).Count

    if ($FinalFunctionCount -ne 1) {
        Copy-Item -LiteralPath $BackupPath -Destination $TargetPath -Force
        throw "FINAL_FUNCTION_COUNT_FAILED: $FinalFunctionCount - Rollback ausgefuehrt."
    }

    $FinalBytes = [System.IO.File]::ReadAllBytes($TargetPath)
    $FinalHash = (Get-FileHash -LiteralPath $TargetPath -Algorithm SHA256).Hash.ToUpperInvariant()

    # Beweis der Wirksamkeit: ohne geaenderten Hash darf kein PASS erscheinen.
    if ($FinalHash -eq $TargetHashBefore) {
        Copy-Item -LiteralPath $BackupPath -Destination $TargetPath -Force
        throw 'FINAL_HASH_UNCHANGED: Der Dateiinhalt ist unveraendert - der Patch haette nichts bewirkt. Rollback ausgefuehrt.'
    }

    Write-Host ''
    Write-Host '============================================'
    Write-Host ' PATCH: PASS' -ForegroundColor Green
    Write-Host '============================================'
    Write-Host "Ziel        : $TargetPath"
    Write-Host "Funktion    : $FunctionName (ersetzt: $ReplacedCount)"
    Write-Host "Backup      : $BackupPath"
    Write-Host ("Groesse     : {0} -> {1} Bytes" -f $TargetBytes.Length, $FinalBytes.Length)
    Write-Host ("SHA256 vor  : {0}" -f $TargetHashBefore)
    Write-Host ("SHA256 nach : {0}" -f $FinalHash)
    Write-Host ''
    Write-Host 'Naechster Schritt (immer nach einem Patch):'
    Write-Host '  .\tests\Invoke-ContractTests.ps1 -ScriptPath <Pfad zur Zieldatei>'
    Write-Host ''
    Write-Host 'Neuer Baseline-Pin fuer den naechsten Patch (-ExpectedSha256):'
    Write-Host "  $FinalHash"

    Write-Journal -Path $Journal -Entry @{
        timestamp    = (Get-Date).ToString('o')
        target       = $TargetPath
        function     = $FunctionName
        result       = 'PASS'
        replaced     = $ReplacedCount
        bytes_before = $TargetBytes.Length
        bytes_after  = $FinalBytes.Length
        hash_before  = $TargetHashBefore
        hash_after   = $FinalHash
        backup       = $BackupPath
        tool         = 'Update-PsFunctionBlock.ps1'
    }
}
catch {
    if (Test-Path -LiteralPath $TempPath -PathType Leaf) {
        Remove-Item -LiteralPath $TempPath -Force -ErrorAction SilentlyContinue
    }

    Write-Host ''
    Write-Host '============================================'
    Write-Host ' PATCH: FAIL' -ForegroundColor Red
    Write-Host '============================================'

    Write-Journal -Path $Journal -Entry @{
        timestamp   = (Get-Date).ToString('o')
        target      = $TargetPath
        function    = $FunctionName
        result      = 'FAIL'
        error       = $_.Exception.Message
        hash_before = $TargetHashBefore
        hash_after  = $(if (Test-Path -LiteralPath $TargetPath) { (Get-FileHash -LiteralPath $TargetPath -Algorithm SHA256).Hash.ToUpperInvariant() } else { '' })
        tool        = 'Update-PsFunctionBlock.ps1'
    }

    throw
}

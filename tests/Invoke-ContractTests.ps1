#requires -Version 7.2
<#
.SYNOPSIS
    Vertragstests fuer LocalCloudCode - laufen OHNE die Runtime zu starten.

.DESCRIPTION
    Dieses Skript laedt die Funktionen aus der Zieldatei per AST (nicht per
    Dot-Source der ganzen Datei!) und prueft anschliessend die Vertraege.

    Warum nicht ". $ScriptPath":
        LocalCloudCode.ps1 enthaelt Top-Level-Runtime-Code (Backend-Auswahl,
        Policy-Laden, Modellaufruf). Ein Dot-Source startet einen kompletten
        Lauf - inklusive Netzwerkaufruf und Run-Report. Deshalb werden hier
        ausschliesslich die FunctionDefinitionAst-Knoten extrahiert und
        ausgewertet; der Rest der Datei wird nie ausgefuehrt.

    Es werden keine Ergebnisse erfunden: Jede Zeile PASS/FAIL/SKIP entsteht
    aus einer tatsaechlichen Ausfuehrung mit Angabe des beobachteten Werts.

.PARAMETER ScriptPath
    Pfad zur LocalCloudCode.ps1.

.PARAMETER KeepTestWorkspace
    Testverzeichnis nach dem Lauf behalten (Diagnose).

.EXAMPLE
    .\tests\Invoke-ContractTests.ps1 -ScriptPath "C:\Users\aowdg\Desktop\KI\LocalCloudCode\LocalCloudCode.ps1"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ScriptPath,

    [Parameter(Mandatory = $false)]
    [switch]$KeepTestWorkspace
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$script:Passed = 0
$script:Failed = 0
$script:Skipped = 0
$script:Warned = 0
$script:Utf8 = [System.Text.UTF8Encoding]::new($false)

# Vorbelegung: unter StrictMode 3.0 wirft der Zugriff auf eine nie zugewiesene
# Variable einen Fehler. Alle Variablen, die in bedingten Zweigen gesetzt
# werden, muessen vorher existieren.
$TestWorkspaceRoot = ''
$SubFolder = ''
$ParseErrors = @()

function Write-Section {
    param([Parameter(Mandatory = $true)][string]$Title)

    Write-Host ''
    Write-Host ('-' * 60)
    Write-Host $Title
    Write-Host ('-' * 60)
}

function Add-Result {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][ValidateSet('PASS', 'FAIL', 'SKIP', 'WARN')][string]$Status,
        [Parameter(Mandatory = $false)][string]$Detail = ''
    )

    $Color = switch ($Status) {
        'PASS' { 'Green' }
        'FAIL' { 'Red' }
        'WARN' { 'Yellow' }
        default { 'DarkGray' }
    }

    switch ($Status) {
        'PASS' { $script:Passed++ }
        'FAIL' { $script:Failed++ }
        'SKIP' { $script:Skipped++ }
        'WARN' { $script:Warned++ }
    }

    Write-Host ("{0,-5} {1}" -f $Status, $Name) -ForegroundColor $Color

    if ($Detail) {
        foreach ($Line in ($Detail -split "`r?`n")) {
            Write-Host ("      $Line")
        }
    }
}

function Test-Throws {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [Parameter(Mandatory = $false)][string]$MessagePattern = ''
    )

    try {
        & $Action | Out-Null

        return [pscustomobject]@{ Threw = $false; Message = '' }
    }
    catch {
        $Message = $_.Exception.Message

        if ($MessagePattern -and $Message -notmatch $MessagePattern) {
            return [pscustomobject]@{ Threw = $false; Message = "Falscher Fehler: $Message" }
        }

        return [pscustomobject]@{ Threw = $true; Message = $Message }
    }
}

Write-Host ''
Write-Host '============================================'
Write-Host ' LOCALCLOUDCODE CONTRACT TESTS'
Write-Host '============================================'
Write-Host "Skript : $ScriptPath"
Write-Host "Datum  : $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))"
Write-Host "PS     : $($PSVersionTable.PSVersion)"

$Resolved = (Resolve-Path -LiteralPath $ScriptPath -ErrorAction Stop).Path

if (-not (Test-Path -LiteralPath $Resolved -PathType Leaf)) {
    throw "SCRIPT_NOT_FOUND: $Resolved"
}

# =========================================================================
# ABSCHNITT 1 - STATISCHE PRUEFUNGEN
# =========================================================================
Write-Section 'ABSCHNITT 1 - STATISCHE PRUEFUNG (ohne Ausfuehrung)'

$RawBytes = [System.IO.File]::ReadAllBytes($Resolved)
$RawText = $script:Utf8.GetString($RawBytes)

if ($RawBytes.Length -ge 3 -and $RawBytes[0] -eq 0xEF -and $RawBytes[1] -eq 0xBB -and $RawBytes[2] -eq 0xBF) {
    $RawText = $RawText.Substring(1)
    Add-Result -Name 'UTF8-BOM' -Status 'WARN' -Detail 'Datei hat eine BOM. Markerpruefungen (^# ULTIMATIVER ...) und Content-Hashes koennen dadurch abweichen.'
}
else {
    Add-Result -Name 'UTF8-BOM' -Status 'PASS' -Detail 'Keine BOM vorhanden.'
}

# --- 1.1 Syntaxgate -------------------------------------------------------
$Tokens = $null
$ParseErrors = $null
$Ast = [System.Management.Automation.Language.Parser]::ParseFile($Resolved, [ref]$Tokens, [ref]$ParseErrors)

if (@($ParseErrors).Count -eq 0) {
    Add-Result -Name 'SYNTAX' -Status 'PASS' -Detail "Keine Parserfehler ($($RawBytes.Length) Bytes)."
}
else {
    $Detail = (@($ParseErrors) | Select-Object -First 5 | ForEach-Object {
            "Zeile {0}, Spalte {1}: {2}" -f $_.Extent.StartLineNumber, $_.Extent.StartColumnNumber, $_.Message
        }) -join "`n"

    Add-Result -Name 'SYNTAX' -Status 'FAIL' -Detail ("$(@($ParseErrors).Count) Parserfehler - die Datei ist nicht lauffaehig.`n" + $Detail)
}

# --- 1.2 Smart Quotes -----------------------------------------------------
$SmartMatches = [regex]::Matches($RawText, '[\u201C\u201D\u201E\u201A\u2018\u2019]')

if ($SmartMatches.Count -eq 0) {
    Add-Result -Name 'SMART-QUOTES' -Status 'PASS' -Detail 'Keine typografischen Anfuehrungszeichen.'
}
else {
    $Lines = $RawText.Substring(0, $SmartMatches[0].Index) -split "`n"
    Add-Result -Name 'SMART-QUOTES' -Status 'FAIL' -Detail ("$($SmartMatches.Count) Treffer, erster in Zeile $($Lines.Count). Ursache fuer Syntaxkaskaden nach Copy-Paste.")
}

# --- 1.3 Funktionsinventar ------------------------------------------------
$FunctionAsts = @(
    $Ast.FindAll(
        { param($Node) $Node -is [System.Management.Automation.Language.FunctionDefinitionAst] },
        $true
    )
)

$FunctionNames = @($FunctionAsts | ForEach-Object { $_.Name } | Sort-Object -Unique)
Write-Host ''
Write-Host "Gefundene Funktionen ($($FunctionNames.Count)): $($FunctionNames -join ', ')"

foreach ($Required in @('Get-WorkspacePath', 'Get-WorkspaceContent')) {
    if ($FunctionNames -contains $Required) {
        Add-Result -Name "FUNKTION-VORHANDEN: $Required" -Status 'PASS'
    }
    else {
        Add-Result -Name "FUNKTION-VORHANDEN: $Required" -Status 'FAIL' -Detail 'Pflichtfunktion fehlt.'
    }
}

if ($FunctionNames -contains 'Get-DiscoveryResult') {
    Add-Result -Name 'FUNKTION-VORHANDEN: Get-DiscoveryResult' -Status 'PASS'
}
else {
    Add-Result -Name 'FUNKTION-VORHANDEN: Get-DiscoveryResult' -Status 'SKIP' -Detail 'Discovery ist im Runtime-Pfad nicht vorhanden (Befund F-04: nicht eingebunden).'
}

# --- 1.4 Parameter-Gate (AST gegen Signatur) ------------------------------
# Findet zwei Fehlerklassen, die bisher erst zur Laufzeit aufgefallen sind:
#   F-03  Aufruf mit einem Parameternamen, den die Funktion nicht hat
#   F-02  Pflichtparameter wird nicht uebergeben (auch bei Splatting, wenn die
#         Splat-Hashtabelle im selben Skript statisch aufloesbar ist)
$Signatures = @{}
$MandatoryParams = @{}

foreach ($Function in $FunctionAsts) {
    $Names = @($Function.Parameters | ForEach-Object { $_.Name.VariablePath.UserPath })
    $Signatures[$Function.Name] = $Names

    $ParamBlockParameters = @()

    if ($null -ne $Function.Body.ParamBlock) {
        $ParamBlockParameters = @($Function.Body.ParamBlock.Parameters)
    }

    $Mand = @()

    foreach ($Parameter in $ParamBlockParameters) {
        $AttributeText = (@($Parameter.Attributes) | ForEach-Object { $_.Extent.Text }) -join ' '

        if ($AttributeText -match 'Mandatory' -and $AttributeText -notmatch 'Mandatory\s*=\s*\$false') {
            $Mand += $Parameter.Name.VariablePath.UserPath
        }
    }

    $MandatoryParams[$Function.Name] = $Mand
}

# --- Splat-Hashtabellen statisch aufloesen --------------------------------
$SplatKeys = @{}

function Add-SplatKey {
    param(
        [Parameter(Mandatory = $true)][string]$Variable,
        [Parameter(Mandatory = $true)][string]$Key
    )

    if ([string]::IsNullOrWhiteSpace($Key)) { return }

    if (-not $SplatKeys.ContainsKey($Variable)) {
        $SplatKeys[$Variable] = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    }

    [void]$SplatKeys[$Variable].Add($Key)
}

foreach ($Assignment in $Ast.FindAll({ param($Node) $Node -is [System.Management.Automation.Language.AssignmentStatementAst] }, $true)) {
    $Left = $Assignment.Left

    if ($Left -is [System.Management.Automation.Language.VariableExpressionAst]) {
        if ($Assignment.Right -is [System.Management.Automation.Language.HashtableAst]) {
            $VariableName = '$' + $Left.VariablePath.UserPath

            foreach ($Entry in $Assignment.Right.KeyValuePairs) {
                Add-SplatKey -Variable $VariableName -Key ($Entry.Item1.Extent.Text.Trim().Trim("'").Trim('"'))
            }
        }
    }
    elseif ($Left -is [System.Management.Automation.Language.IndexExpressionAst]) {
        $Target = $Left.Target
        $Index = $Left.Index

        if ($Target -is [System.Management.Automation.Language.VariableExpressionAst] -and
            $Index -is [System.Management.Automation.Language.StringConstantExpressionAst]) {
            Add-SplatKey -Variable ('$' + $Target.VariablePath.UserPath) -Key $Index.Value
        }
    }
}

if ($SplatKeys.Count -gt 0) {
    foreach ($Key in @($SplatKeys.Keys)) {
        Write-Host ("Splat aufgeloest: {0} -> {1}" -f $Key, (@($SplatKeys[$Key]) -join ', '))
    }
}

$CommonParameterNames = @(
    'Verbose', 'Debug', 'ErrorAction', 'WarningAction', 'InformationAction',
    'ErrorVariable', 'WarningVariable', 'InformationVariable', 'OutVariable',
    'OutBuffer', 'PipelineVariable', 'ProgressAction'
)

$UnknownParameterHits = [System.Collections.Generic.List[string]]::new()
$MissingMandatoryHits = [System.Collections.Generic.List[string]]::new()
$UnresolvedSplatHits = [System.Collections.Generic.List[string]]::new()

foreach ($CommandAst in $Ast.FindAll({ param($Node) $Node -is [System.Management.Automation.Language.CommandAst] }, $true)) {
    $CallName = $CommandAst.GetCommandName()

    if (-not $CallName -or -not $Signatures.ContainsKey($CallName)) { continue }

    $Named = @()
    $Splats = @()
    $Positional = 0

    for ($ElementIndex = 1; $ElementIndex -lt $CommandAst.CommandElements.Count; $ElementIndex++) {
        $Element = $CommandAst.CommandElements[$ElementIndex]

        if ($Element -is [System.Management.Automation.Language.CommandParameterAst]) {
            $Named += $Element.ParameterName
        }
        elseif ($Element -is [System.Management.Automation.Language.VariableExpressionAst] -and $Element.Splatted) {
            $Splats += ('$' + $Element.VariablePath.UserPath)
        }
        else {
            $Positional++
        }
    }

    foreach ($ParameterName in $Named) {
        if ($ParameterName -notin $Signatures[$CallName] -and $ParameterName -notin $CommonParameterNames) {
            $UnknownParameterHits.Add("$CallName -$ParameterName  (Zeile $($CommandAst.Extent.StartLineNumber); Funktion kennt: $($Signatures[$CallName] -join ', '))")
        }
    }

    $Provided = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    foreach ($ParameterName in $Named) { [void]$Provided.Add($ParameterName) }
    foreach ($ParameterName in $CommonParameterNames) { [void]$Provided.Add($ParameterName) }

    # Positionale Bindung: die ersten N deklarierten Parameter
    $Order = @($Signatures[$CallName])

    for ($Index = 0; $Index -lt $Positional -and $Index -lt $Order.Count; $Index++) {
        [void]$Provided.Add($Order[$Index])
    }

    $AllSplatsResolved = $true

    foreach ($Splat in $Splats) {
        if ($SplatKeys.ContainsKey($Splat)) {
            foreach ($Key in @($SplatKeys[$Splat])) { [void]$Provided.Add($Key) }
        }
        else {
            $AllSplatsResolved = $false
        }
    }

    $Missing = @($MandatoryParams[$CallName] | Where-Object { -not $Provided.Contains($_) })

    if ($Missing.Count -gt 0) {
        if ($AllSplatsResolved) {
            $MissingMandatoryHits.Add("$CallName (Zeile $($CommandAst.Extent.StartLineNumber)): Pflichtparameter fehlen: $($Missing -join ', ')")
        }
        else {
            $UnresolvedSplatHits.Add("$CallName (Zeile $($CommandAst.Extent.StartLineNumber)): Pflichtparameter nicht statisch pruefbar ($($Missing -join ', ')); Splat-Quelle unbekannt.")
        }
    }
}

if ($UnknownParameterHits.Count -eq 0) {
    Add-Result -Name 'PARAMETER-GATE: unbekannte Parameternamen' -Status 'PASS' -Detail "$($Signatures.Count) Funktionssignaturen geprueft."
}
else {
    Add-Result -Name 'PARAMETER-GATE: unbekannte Parameternamen' -Status 'FAIL' -Detail ("Der Aufruf verwendet Parameternamen, die die Funktion nicht hat (Befund F-03):`n" + ($UnknownParameterHits -join "`n"))
}

if ($MissingMandatoryHits.Count -eq 0) {
    Add-Result -Name 'PARAMETER-GATE: Pflichtparameter' -Status 'PASS' -Detail 'Alle Pflichtparameter werden uebergeben (inkl. aufgeloester Splat-Hashtabellen).'
}
else {
    Add-Result -Name 'PARAMETER-GATE: Pflichtparameter' -Status 'FAIL' -Detail ("Pflichtparameter fehlen (Befund F-02):`n" + ($MissingMandatoryHits -join "`n"))
}

if ($UnresolvedSplatHits.Count -gt 0) {
    Add-Result -Name 'PARAMETER-GATE: Splatting nicht aufloesbar' -Status 'WARN' -Detail ($UnresolvedSplatHits -join "`n")
}

# =========================================================================
# ABSCHNITT 2 - FUNKTIONEN LADEN (nur Definitionen, kein Runtime-Code)
# =========================================================================
Write-Section 'ABSCHNITT 2 - FUNKTIONEN LADEN (AST-Extraktion)'

if (@($ParseErrors).Count -ne 0) {
    Add-Result -Name 'LOAD' -Status 'SKIP' -Detail 'Syntaxfehler verhindern das Laden. Erst Syntax reparieren.'
}
else {
    $Sources = @($FunctionAsts | ForEach-Object { $_.Extent.Text }) -join ([Environment]::NewLine + [Environment]::NewLine)

    $TestWorkspaceRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("lcc-selftest-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $TestWorkspaceRoot -Force | Out-Null

    $SubFolder = Join-Path $TestWorkspaceRoot 'READONLY_TEST'
    New-Item -ItemType Directory -Path $SubFolder -Force | Out-Null

    [System.IO.File]::WriteAllText((Join-Path $SubFolder 'allowed.ps1'), "Write-Host 'test'", $script:Utf8)
    [System.IO.File]::WriteAllText((Join-Path $SubFolder 'notes.md'), "# Test", $script:Utf8)
    [System.IO.File]::WriteAllText((Join-Path $SubFolder 'blocked.exe'), 'MZ', $script:Utf8)
    [System.IO.File]::WriteAllText((Join-Path $SubFolder 'geheim.txt'), "api_key = sk-abcdefghijklmnopqrstuvwxyz", $script:Utf8)

    # Variablen, die die Funktionen erwarten (Skript-Scope)
    $WorkspaceRoot = $TestWorkspaceRoot
    $LogRoot = Join-Path $TestWorkspaceRoot 'logs'
    $OllamaUrl = 'http://127.0.0.1:11434'
    $LMStudioUrl = 'http://127.0.0.1:1237'

    try {
        $FunctionScript = [ScriptBlock]::Create($Sources)
        . $FunctionScript

        Add-Result -Name 'LOAD' -Status 'PASS' -Detail "Geladen: $(@($FunctionAsts | ForEach-Object { $_.Name }) -join ', ')"
        Add-Result -Name 'LOAD (kein Runtime-Start)' -Status 'PASS' -Detail 'Es wurde nur der Funktionscode ausgewertet - kein Backend, kein Modellaufruf, kein Run-Report.'
    }
    catch {
        Add-Result -Name 'LOAD' -Status 'FAIL' -Detail "Funktionen konnten nicht geladen werden: $($_.Exception.Message)"
    }
}

# =========================================================================
# ABSCHNITT 3 - WORKSPACE-CONTAINMENT
# =========================================================================
if (Get-Command -Name Get-WorkspacePath -ErrorAction SilentlyContinue) {
    Write-Section 'ABSCHNITT 3 - WORKSPACE-CONTAINMENT (Get-WorkspacePath)'

    $BeforeCulture = [System.Threading.Thread]::CurrentThread.CurrentCulture

    # 3.1 Root (leerer Name)
    try {
        $ResolvedRoot = Get-WorkspacePath -RequestedWorkspace ''
        $ExpectedRoot = [System.IO.Path]::GetFullPath($TestWorkspaceRoot).TrimEnd('\')

        if ($ResolvedRoot.TrimEnd('\') -eq $ExpectedRoot) {
            Add-Result -Name 'CONTAINMENT: leerer Wert -> Root' -Status 'PASS' -Detail "Aufgeloest zu: $ResolvedRoot"
        }
        else {
            Add-Result -Name 'CONTAINMENT: leerer Wert -> Root' -Status 'FAIL' -Detail "Erwartet: $ExpectedRoot`nErhalten: $ResolvedRoot"
        }
    }
    catch {
        Add-Result -Name 'CONTAINMENT: leerer Wert -> Root' -Status 'FAIL' -Detail $_.Exception.Message
    }

    # 3.2 Unterordner
    try {
        $ResolvedSub = Get-WorkspacePath -RequestedWorkspace $SubFolder

        if ($ResolvedSub.TrimEnd('\') -eq [System.IO.Path]::GetFullPath($SubFolder).TrimEnd('\')) {
            Add-Result -Name 'CONTAINMENT: Unterordner erlaubt' -Status 'PASS' -Detail "Aufgeloest zu: $ResolvedSub"
        }
        else {
            Add-Result -Name 'CONTAINMENT: Unterordner erlaubt' -Status 'FAIL' -Detail "Erhalten: $ResolvedSub"
        }
    }
    catch {
        Add-Result -Name 'CONTAINMENT: Unterordner erlaubt' -Status 'FAIL' -Detail $_.Exception.Message
    }

    # 3.3 Relativer Name wuerde gegen das Arbeitsverzeichnis aufgeloest (Befund F-11)
    $CwdBefore = (Get-Location).Path

    try {
        $RelativeResult = Get-WorkspacePath -RequestedWorkspace 'READONLY_TEST'
        Add-Result -Name 'CONTAINMENT: relativer Name' -Status 'WARN' -Detail "Befund F-11: relativer Name wurde gegen '$CwdBefore' aufgeloest und ergab '$RelativeResult'. Verhalten haengt vom Arbeitsverzeichnis ab - relativer Name sollte gegen den Workspace-Root aufgeloest werden."
    }
    catch {
        Add-Result -Name 'CONTAINMENT: relativer Name' -Status 'WARN' -Detail "Befund F-11: relativer Name schlaegt fehl ('$($_.Exception.Message)'), weil er gegen '$CwdBefore' aufgeloest wird. Relativer Name sollte gegen den Workspace-Root aufgeloest werden."
    }

    # 3.4 Pfad ausserhalb
    $EscapeResult = Test-Throws -Action { Get-WorkspacePath -RequestedWorkspace ([System.IO.Path]::GetTempPath()) } -MessagePattern 'outside|Reparse|outerhalb|authorized'

    if ($EscapeResult.Threw) {
        Add-Result -Name 'CONTAINMENT: Pfad ausserhalb -> BLOCK' -Status 'PASS' -Detail $EscapeResult.Message
    }
    else {
        Add-Result -Name 'CONTAINMENT: Pfad ausserhalb -> BLOCK' -Status 'FAIL' -Detail ("Dateisystem-Wurzel wurde nicht blockiert. $($EscapeResult.Message)")
    }

    # 3.5 ..-Escape
    $DotDotResult = Test-Throws -Action { Get-WorkspacePath -RequestedWorkspace (Join-Path $SubFolder '..\..\..\Windows') } -MessagePattern 'outside|Reparse|existiert|Workspace path'

    if ($DotDotResult.Threw) {
        Add-Result -Name 'CONTAINMENT: ..-Escape -> BLOCK' -Status 'PASS' -Detail $DotDotResult.Message
    }
    else {
        Add-Result -Name 'CONTAINMENT: ..-Escape -> BLOCK' -Status 'FAIL' -Detail 'Pfad mit .. wurde nicht blockiert.'
    }

    # 3.6 Nicht existierender Ordner
    $MissingResult = Test-Throws -Action { Get-WorkspacePath -RequestedWorkspace (Join-Path $TestWorkspaceRoot 'gibt-es-nicht') } -MessagePattern 'existiert|Workspace does not exist'

    if ($MissingResult.Threw) {
        Add-Result -Name 'CONTAINMENT: nicht existierend -> BLOCK' -Status 'PASS' -Detail $MissingResult.Message
    }
    else {
        Add-Result -Name 'CONTAINMENT: nicht existierend -> BLOCK' -Status 'FAIL' -Detail 'Nicht existierender Pfad wurde akzeptiert.'
    }

    # 3.7 Symlink/Junction (nur wenn erstellbar)
    $LinkPath = Join-Path $TestWorkspaceRoot 'link-auf-temp'

    try {
        New-Item -ItemType SymbolicLink -Path $LinkPath -Target $SubFolder -ErrorAction Stop | Out-Null

        $LinkResult = Test-Throws -Action { Get-WorkspacePath -RequestedWorkspace $LinkPath } -MessagePattern 'Reparse|Link|reparse'

        if ($LinkResult.Threw) {
            Add-Result -Name 'CONTAINMENT: Symlink -> BLOCK' -Status 'PASS' -Detail $LinkResult.Message
        }
        else {
            Add-Result -Name 'CONTAINMENT: Symlink -> BLOCK' -Status 'FAIL' -Detail 'Symlink wurde als Workspace akzeptiert.'
        }
    }
    catch {
        Add-Result -Name 'CONTAINMENT: Symlink -> BLOCK' -Status 'SKIP' -Detail "Symlink konnte nicht erstellt werden (Rechte): $($_.Exception.Message)"
    }

    [System.Threading.Thread]::CurrentThread.CurrentCulture = $BeforeCulture
}
else {
    Write-Section 'ABSCHNITT 3 - WORKSPACE-CONTAINMENT'
    Add-Result -Name 'CONTAINMENT' -Status 'SKIP' -Detail 'Get-WorkspacePath nicht geladen.'
}

# =========================================================================
# ABSCHNITT 4 - CONTENT-VERTRAG
# =========================================================================
if (Get-Command -Name Get-WorkspaceContent -ErrorAction SilentlyContinue) {
    Write-Section 'ABSCHNITT 4 - CONTENT-VERTRAG (Get-WorkspaceContent)'

    try {
        $ContentRaw = Get-WorkspaceContent -Path $SubFolder -MaxFileBytes 65536 -MaxTotalChars 24000

        $ContentIsString = $ContentRaw -is [string]
        $Content = if ($ContentIsString) { $ContentRaw | ConvertFrom-Json -Depth 64 } else { $ContentRaw }
        $Keys = @($Content.PSObject.Properties.Name)

        Add-Result -Name 'CONTENT: Rueckgabetyp' -Status 'PASS' -Detail ("Typ: {0} | Felder: {1}" -f $(if ($ContentIsString) { 'string (JSON)' } else { $Content.GetType().Name }), ($Keys -join ', '))

        if ($Keys -contains 'ReadOnly') {
            if ([bool]$Content.ReadOnly -eq $true) {
                Add-Result -Name 'CONTENT: ReadOnly-Vertrag' -Status 'PASS' -Detail 'ReadOnly = true'
            }
            else {
                Add-Result -Name 'CONTENT: ReadOnly-Vertrag' -Status 'FAIL' -Detail "ReadOnly = $($Content.ReadOnly)"
            }
        }
        else {
            Add-Result -Name 'CONTENT: ReadOnly-Vertrag' -Status 'FAIL' -Detail 'Feld ReadOnly fehlt.'
        }

        $ReadProperty = $null

        foreach ($Candidate in @('ReadFiles', 'ContentReadFiles', 'Files', 'Read')) {
            if ($Keys -contains $Candidate) { $ReadProperty = $Candidate; break }
        }

        if ($ReadProperty) {
            $ReadCount = @($Content.$ReadProperty).Count

            if ($ReadCount -ge 2) {
                Add-Result -Name 'CONTENT: Dateien gelesen' -Status 'PASS' -Detail "$ReadCount Eintraege ueber Feld '$ReadProperty' (erwartet: >= 2)."
            }
            else {
                Add-Result -Name 'CONTENT: Dateien gelesen' -Status 'FAIL' -Detail "$ReadCount Eintraege ueber Feld '$ReadProperty' (erwartet: >= 2)."
            }

            if ($ReadProperty -ne 'ReadFiles') {
                Add-Result -Name 'CONTENT: Schema-Name' -Status 'FAIL' -Detail "Befund F-13c: Daten liegen unter '$ReadProperty', Discovery erwartet 'ReadFiles'. Zwei Namenswelten - Schema vereinheitlichen (SchemaVersion 1.0)."
            }
        }
        else {
            Add-Result -Name 'CONTENT: Dateien gelesen' -Status 'FAIL' -Detail "Keine Read-Liste gefunden. Felder: $($Keys -join ', ')"
        }

        $BlockedProperty = $null

        foreach ($Candidate in @('BlockedFiles', 'ContentBlockedFiles', 'Blocked')) {
            if ($Keys -contains $Candidate) { $BlockedProperty = $Candidate; break }
        }

        if ($BlockedProperty) {
            $BlockedList = @($Content.$BlockedProperty)
            $Reasons = @($BlockedList | ForEach-Object { [string]$_.Reason } | Where-Object { $_ } | Sort-Object -Unique)

            Add-Result -Name 'CONTENT: Blockliste' -Status 'PASS' -Detail ("$($BlockedList.Count) blockiert. Gruende: " + $(if ($Reasons.Count) { $Reasons -join ', ' } else { '(keine)' }))

            if ($Reasons -contains 'EXTENSION_NOT_ALLOWED') {
                Add-Result -Name 'CONTENT: .exe blockiert' -Status 'PASS' -Detail 'blocked.exe wurde mit EXTENSION_NOT_ALLOWED abgewiesen.'
            }
            else {
                Add-Result -Name 'CONTENT: .exe blockiert' -Status 'FAIL' -Detail 'blocked.exe wurde nicht mit EXTENSION_NOT_ALLOWED blockiert.'
            }
        }
        else {
            Add-Result -Name 'CONTENT: Blockliste' -Status 'FAIL' -Detail "Keine Blocked-Liste gefunden. Felder: $($Keys -join ', ')"
        }

        if ($Keys -contains 'SecretRedactions') {
            Add-Result -Name 'CONTENT: Secret-Filter' -Status 'PASS' -Detail "SecretRedactions = $($Content.SecretRedactions)"
        }
        else {
            Add-Result -Name 'CONTENT: Secret-Filter' -Status 'WARN' -Detail 'Befund F-19: kein Secret-Filter aktiv. geheim.txt enthaelt einen Beispiel-API-Key, der ungefiltert ans Modell gehen wuerde.'
        }
    }
    catch {
        Add-Result -Name 'CONTENT: Ausfuehrung' -Status 'FAIL' -Detail $_.Exception.Message
    }
}
else {
    Write-Section 'ABSCHNITT 4 - CONTENT-VERTRAG'
    Add-Result -Name 'CONTENT' -Status 'SKIP' -Detail 'Get-WorkspaceContent nicht geladen.'
}

# =========================================================================
# ABSCHNITT 5 - DISCOVERY-VERTRAG
# =========================================================================
if (Get-Command -Name Get-DiscoveryResult -ErrorAction SilentlyContinue) {
    Write-Section 'ABSCHNITT 5 - DISCOVERY-VERTRAG (Get-DiscoveryResult)'

    $DiscoveryCommand = Get-Command -Name Get-DiscoveryResult
    $HasNameParam = $DiscoveryCommand.Parameters.ContainsKey('WorkspaceName')

    Write-Host "Signatur-Modus: $(if ($HasNameParam) { '-WorkspaceName (Fassung 1.2)' } else { 'Altfassung ohne -WorkspaceName' })"

    $CallDiscovery = {
        if ($HasNameParam) {
            return Get-DiscoveryResult -WorkspaceName 'READONLY_TEST'
        }

        return Get-DiscoveryResult -Workspace $TestWorkspaceRoot
    }

    try {
        $Discovery = & $CallDiscovery

        Add-Result -Name 'DISCOVERY: Ausfuehrung' -Status 'PASS' -Detail "Inventory.ReadCount = $($Discovery.Inventory.ReadCount) | BlockedCount = $($Discovery.Inventory.BlockedCount)"

        if ($Discovery.Inventory.ReadCount -ge 2) {
            Add-Result -Name 'DISCOVERY: Dateien im Inventar' -Status 'PASS' -Detail "$($Discovery.Inventory.ReadCount) Eintraege."
        }
        else {
            Add-Result -Name 'DISCOVERY: Dateien im Inventar' -Status 'FAIL' -Detail "Nur $($Discovery.Inventory.ReadCount) Eintraege - stilles Leerergebnis (Befund F-13c)."
        }

        if ([string]$Discovery.EvidenceHash -match '^[0-9A-F]{64}$') {
            Add-Result -Name 'DISCOVERY: EvidenceHash-Format' -Status 'PASS' -Detail $Discovery.EvidenceHash
        }
        else {
            Add-Result -Name 'DISCOVERY: EvidenceHash-Format' -Status 'FAIL' -Detail "Unerwartetes Format: '$($Discovery.EvidenceHash)'"
        }

        # Determinismus: zweiter Lauf
        $Discovery2 = & $CallDiscovery

        if ($Discovery.EvidenceHash -eq $Discovery2.EvidenceHash) {
            Add-Result -Name 'DISCOVERY: Determinismus (2 Laeufe)' -Status 'PASS' -Detail 'Identischer Hash bei unveraendertem Verzeichnis.'
        }
        else {
            Add-Result -Name 'DISCOVERY: Determinismus (2 Laeufe)' -Status 'FAIL' -Detail "$($Discovery.EvidenceHash) != $($Discovery2.EvidenceHash)"
        }

        # Determinismus ueber Kulturen (Befund F-08)
        $CultureBefore = [System.Threading.Thread]::CurrentThread.CurrentCulture

        try {
            [System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::GetCultureInfo('de-DE')
            $HashDe = (& $CallDiscovery).EvidenceHash

            [System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::GetCultureInfo('en-US')
            $HashEn = (& $CallDiscovery).EvidenceHash

            if ($HashDe -eq $HashEn) {
                Add-Result -Name 'DISCOVERY: Kulturunabhaengigkeit (de-DE == en-US)' -Status 'PASS' -Detail "de-DE: $HashDe | en-US: $HashEn"
            }
            else {
                Add-Result -Name 'DISCOVERY: Kulturunabhaengigkeit (de-DE == en-US)' -Status 'FAIL' -Detail ("Befund F-08: Evidence-Hash ist kulturabhaengig.`nde-DE: $HashDe`nen-US: $HashEn`nUrsache: Sort-Object sortiert mit aktueller Kultur. Fix: ordinal sortieren.")
            }
        }
        catch {
            Add-Result -Name 'DISCOVERY: Kulturunabhaengigkeit' -Status 'SKIP' -Detail $_.Exception.Message
        }
        finally {
            [System.Threading.Thread]::CurrentThread.CurrentCulture = $CultureBefore
        }

        # Vertragswerte
        if ($Discovery.PSObject.Properties.Name -contains 'Contract') {
            $ContractJson = $Discovery.Contract | ConvertTo-Json -Depth 4 -Compress
            Add-Result -Name 'DISCOVERY: Contract-Block' -Status 'PASS' -Detail $ContractJson
        }
        else {
            Add-Result -Name 'DISCOVERY: Contract-Block' -Status 'FAIL' -Detail 'Kein Contract-Block im Ergebnis.'
        }

        # CanonicalJson unabhaengig nachrechnen
        if ($Discovery.PSObject.Properties.Name -contains 'EvidenceCanonicalJson') {
            $Json = [string]$Discovery.EvidenceCanonicalJson
            $Bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($Json)
            $Recomputed = ([System.BitConverter]::ToString([System.Security.Cryptography.SHA256]::HashData($Bytes)) -replace '-', '').ToUpperInvariant()

            if ($Recomputed -eq $Discovery.EvidenceHash) {
                Add-Result -Name 'DISCOVERY: Hash unabhaengig nachgerechnet' -Status 'PASS' -Detail "SHA256(EvidenceCanonicalJson) == EvidenceHash == $Recomputed"
            }
            else {
                Add-Result -Name 'DISCOVERY: Hash unabhaengig nachgerechnet' -Status 'FAIL' -Detail "$Recomputed != $($Discovery.EvidenceHash)"
            }

            if (Test-Json -Json $Json) {
                Add-Result -Name 'DISCOVERY: CanonicalJson ist gueltiges JSON' -Status 'PASS'
            }
            else {
                Add-Result -Name 'DISCOVERY: CanonicalJson ist gueltiges JSON' -Status 'FAIL' -Detail 'Kein gueltiges JSON (Befund F-09).'
            }
        }
        else {
            Add-Result -Name 'DISCOVERY: Hash unabhaengig nachgerechnet' -Status 'SKIP' -Detail 'Mit -IncludeCanonicalJson aufrufen, um den Hash unabhaengig nachrechnen zu koennen.'
        }

        if ($Discovery.PSObject.Properties.Name -contains 'Warnings' -and @($Discovery.Warnings).Count -gt 0) {
            Add-Result -Name 'DISCOVERY: Warnungen' -Status 'WARN' -Detail (@($Discovery.Warnings) -join "`n")
        }
    }
    catch {
        Add-Result -Name 'DISCOVERY: Ausfuehrung' -Status 'FAIL' -Detail $_.Exception.Message
    }
}
else {
    Write-Section 'ABSCHNITT 5 - DISCOVERY-VERTRAG'
    Add-Result -Name 'DISCOVERY' -Status 'SKIP' -Detail 'Get-DiscoveryResult nicht geladen.'
}

# =========================================================================
# ABSCHNITT 6 - AUFRAEUMEN UND BILANZ
# =========================================================================
if (-not $KeepTestWorkspace -and $TestWorkspaceRoot) {
    try {
        # Symlink zuerst entfernen (sonst Rekursionsrisiko)
        $LinkPathCheck = Join-Path $TestWorkspaceRoot 'link-auf-temp'

        if (Test-Path -LiteralPath $LinkPathCheck) {
            try {
                [System.IO.Directory]::Delete($LinkPathCheck, $false)
            }
            catch {
                Remove-Item -LiteralPath $LinkPathCheck -Force -Recurse -ErrorAction SilentlyContinue
            }
        }

        Remove-Item -LiteralPath $TestWorkspaceRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    catch {
        Write-Warning "Testverzeichnis konnte nicht entfernt werden: $($_.Exception.Message)"
    }
}
elseif ($TestWorkspaceRoot) {
    Write-Host ''
    Write-Host "Testverzeichnis behalten: $TestWorkspaceRoot"
}

Write-Host ''
Write-Host '============================================'
Write-Host ' BILANZ'
Write-Host '============================================'
Write-Host "PASS    : $script:Passed"
Write-Host "FAIL    : $script:Failed"
Write-Host "WARN    : $script:Warned"
Write-Host "SKIP    : $script:Skipped"
Write-Host ''

if ($script:Failed -eq 0) {
    Write-Host 'CONTRACT TESTS: PASS' -ForegroundColor Green
    exit 0
}

Write-Host 'CONTRACT TESTS: FAIL' -ForegroundColor Red
exit 1

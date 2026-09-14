#requires -Version 7.2
<#
.SYNOPSIS
    Erstellt einen READ-ONLY Uebergabe-Snapshot des LocalCloudCode-Systems.

.DESCRIPTION
    Dieses Skript LIEST ausschliesslich. Es aendert keine Datei des Systems,
    startet keine Runtime, ruft kein Modell auf und loest keinen Lauf aus.
    Geschrieben werden nur die eigenen Berichtsdateien im Zielordner.

    Zweck: In einem einzigen Durchlauf genau die Fakten sammeln, die fuer die
    weitere Arbeit gebraucht werden - damit keine 18 Einzeldateien manuell
    zusammengesucht werden muessen:

      A. Host/Shell: PowerShell-Version, OS, Kultur, ExecutionPolicy, Profil
      B. Umgebung: Variablennamen (Werte nur auf Wunsch, Secrets maskiert)
      C. Module: PSScriptAnalyzer, Pester, PSReadLine (verfuegbar/geladen)
      D. Befehls-Konflikte: mehrfach definierte Namen (Alias/Function/Cmdlet)
      E. Dateien: Groesse + SHA256 + Parse-Ergebnis + Smart-Quote-Zaehler
      F. Masterprompt: Bytes, Zeichen, SHA256, BOM, Abgleich mit Soll-Hash
      G. Runtime-Policy: Dateien, Hashes, Compiler-Report-Felder
      H. Backends: Ollama (Tags/ps) und LM Studio (Models) - nur GETs
      I. Content-Probe: Rueckgabetyp und Felder von Get-WorkspaceContent
      J. Statische Fakten: Marker fuer die Befunde F-01, F-13c, F-16, F-17,
         F-20, F-02/F-03, F-08, F-09, F-14, F-19 (gefunden/nicht gefunden)
      K. Logs: letzte Run-Reports (Felder + Werte) und Loggroessen

.PARAMETER Root
    Wurzel der LocalCloudCode-Installation.

.PARAMETER OutputDirectory
    Zielordner fuer den Snapshot. Standard: <Desktop>\KI\snapshots

.PARAMETER IncludeEnvironmentValues
    Zeigt Umgebungswerte an - Secret-verdaechtige Werte werden trotzdem
    maskiert. Standard: aus (nur Namen).

.PARAMETER NoZip
    Kein ZIP-Archiv erzeugen (nur JSON + Markdown).

.EXAMPLE
    .\tools\Collect-LocalCloudCodeSnapshot.ps1 -WhatIf

.EXAMPLE
    .\tools\Collect-LocalCloudCodeSnapshot.ps1
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Low')]
param(
    [Parameter(Mandatory = $false)]
    [string]$Root = "C:\Users\aowdg\Desktop\KI\LocalCloudCode",

    [Parameter(Mandatory = $false)]
    [string]$OutputDirectory = "",

    [Parameter(Mandatory = $false)]
    [string]$PromptFileName = "MASTER_PROMPT.txt",

    [Parameter(Mandatory = $false)]
    [string]$TestWorkspaceRelative = "workspace\READONLY_TEST",

    [Parameter(Mandatory = $false)]
    [string]$ExpectedPromptSha256 = "439443DEAACD2527756975047B685A380EE58D37665B8412BBAD2A77752B97A8",

    [Parameter(Mandatory = $false)]
    [switch]$IncludeEnvironmentValues,

    [Parameter(Mandatory = $false)]
    [switch]$NoZip
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$script:Utf8 = [System.Text.UTF8Encoding]::new($false)
$script:Stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$script:SecretPattern = '(?i)(api[_-]?key|secret|token|passw|pwd|credential|bearer|private[_-]?key|sk-[A-Za-z0-9]{12,}|ghp_[A-Za-z0-9]{12,}|AKIA[0-9A-Z]{12,})'
$script:Sections = [ordered]@{}
$script:Problems = [System.Collections.Generic.List[string]]::new()

function Get-TextFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    $Bytes = [System.IO.File]::ReadAllBytes($Path)
    $HasBom = ($Bytes.Length -ge 3 -and $Bytes[0] -eq 0xEF -and $Bytes[1] -eq 0xBB -and $Bytes[2] -eq 0xBF)
    $Text = $script:Utf8.GetString($Bytes)

    if ($HasBom) { $Text = $Text.Substring(1) }

    return [pscustomobject]@{ Bytes = $Bytes; Text = $Text; HasBom = $HasBom }
}

function Get-SafeValue {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Value
    )

    if (-not $IncludeEnvironmentValues) { return '<unterdrueckt>' }

    if ($Name -match $script:SecretPattern -or $Value -match $script:SecretPattern) {
        return ('<maskiert, {0} Zeichen>' -f $Value.Length)
    }

    if ($Value.Length -gt 200) { return ($Value.Substring(0, 200) + '...[gekuerzt]') }

    return $Value
}

function New-EmptySection {
    # Platzhalter mit ALLEN Feldern, die die Berichtserzeugung liest.
    # Grund: Unter Set-StrictMode -Version 3.0 wirft der Zugriff auf eine nicht
    # existierende Eigenschaft einen PropertyNotFoundException. Ein fehlgeschlagener
    # Abschnitt darf den Snapshot nicht unbrauchbar machen - er muss als
    # fehlgeschlagen ERKENNBAR sein, aber weiterhin lesbar.
    return [pscustomobject]@{
        Error              = ''
        Failed             = $true
        Reason             = ''
        Found              = $false
        Ran                = $false
        RootExists         = $false
        PolicyRootExists   = $false
        LogRootExists      = $false
        Files              = @()
        Checked            = @()
        Markers            = @()
        CompilerReports    = @()
        PolicyFiles        = @()
        NewestFiles        = @()
        RunReports         = @()
        DiscoveryCallSites = @()
        OllamaModels       = @()
        LMStudioModels     = @()
        OllamaOnline       = $false
        LMStudioOnline     = $false
        ExecutionPolicy    = $null
        Result             = [pscustomobject]@{}
    }
}

function Invoke-Section {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Body
    )

    try {
        $Result = & $Body

        if ($null -eq $Result) {
            $Placeholder = New-EmptySection
            $Placeholder.Error = 'Abschnitt lieferte kein Ergebnis.'
            $script:Sections[$Name] = $Placeholder
            $script:Problems.Add("$Name : kein Ergebnis")
        }
        else {
            $script:Sections[$Name] = $Result
        }
    }
    catch {
        $Placeholder = New-EmptySection
        $Placeholder.Error = $_.Exception.Message
        $script:Sections[$Name] = $Placeholder
        $script:Problems.Add("$Name : $($_.Exception.Message)")
    }
}

Write-Host ''
Write-Host '============================================'
Write-Host ' LOCALCLOUDCODE SNAPSHOT (READ-ONLY)'
Write-Host '============================================'

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $Desktop = [Environment]::GetFolderPath('Desktop')
    $OutputDirectory = Join-Path $Desktop 'KI\snapshots'
}

$Target = Join-Path $OutputDirectory ("snapshot_{0}" -f $script:Stamp)

if (-not $PSCmdlet.ShouldProcess($Target, 'Snapshot-Berichte schreiben (nur eigene Dateien, keine Systemaenderung)')) {
    Write-Host 'WHATIF: nichts geschrieben.' -ForegroundColor Yellow
    return
}

if (-not (Test-Path -LiteralPath $OutputDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

if (-not (Test-Path -LiteralPath $Target -PathType Container)) {
    New-Item -ItemType Directory -Path $Target -Force | Out-Null
}

# =========================================================================
# A. Host und Shell
# =========================================================================
Invoke-Section -Name 'A_host_shell' -Body {
    $Policy = @{}

    try {
        $Policy = [pscustomobject]@{
            MachinePolicy  = [string](Get-ExecutionPolicy -Scope MachinePolicy -ErrorAction SilentlyContinue)
            UserPolicy     = [string](Get-ExecutionPolicy -Scope UserPolicy -ErrorAction SilentlyContinue)
            Process        = [string](Get-ExecutionPolicy -Scope Process -ErrorAction SilentlyContinue)
            CurrentUser    = [string](Get-ExecutionPolicy -Scope CurrentUser -ErrorAction SilentlyContinue)
            LocalMachine   = [string](Get-ExecutionPolicy -Scope LocalMachine -ErrorAction SilentlyContinue)
        }
    }
    catch { }

    $ProfilePath = [string]$PROFILE
    $ProfileExists = $false
    $ProfileHash = ''
    $ProfileChars = 0

    if ($ProfilePath -and (Test-Path -LiteralPath $ProfilePath -PathType Leaf)) {
        $ProfileExists = $true
        $ProfileHash = (Get-FileHash -LiteralPath $ProfilePath -Algorithm SHA256).Hash
        $ProfileChars = (Get-TextFile -Path $ProfilePath).Text.Length
    }

    [pscustomobject]@{
        TimestampLocal   = (Get-Date).ToString('o')
        ComputerName     = $env:COMPUTERNAME
        UserName         = $env:USERNAME
        PSVersion        = $PSVersionTable.PSVersion.ToString()
        PSEdition        = $PSVersionTable.PSEdition
        CLR              = $PSVersionTable.CLRVersion.ToString()
        OS               = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        OSArchitecture   = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        DotNetVersion    = [System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
        Culture          = [System.Threading.Thread]::CurrentThread.CurrentCulture.Name
        UICulture        = [System.Threading.Thread]::CurrentThread.CurrentUICulture.Name
        ExecutionPolicy  = $Policy
        ProfilePath      = $ProfilePath
        ProfileExists    = $ProfileExists
        ProfileSHA256    = $ProfileHash
        ProfileChars     = $ProfileChars
        LanguageMode     = $ExecutionContext.SessionState.LanguageMode.ToString()
        PSScriptRootMode = 'n/a'
    }
}

# =========================================================================
# B. Umgebungsvariablen (Secrets maskiert)
# =========================================================================
Invoke-Section -Name 'B_environment' -Body {
    $Interesting = 'OLLAMA|LMSTUDIO|LM_STUDIO|OPENAI|ANTHROPIC|CLAUDE|GEMINI|AZURE|AWS|HF_|HUGGING|TOKEN|API|KEY|SECRET|PROXY|PSMODULE|POWERSHELL'

    @(
        Get-ChildItem Env: |
            Where-Object { $_.Name -match $Interesting } |
            Sort-Object Name |
            ForEach-Object {
                [pscustomobject]@{
                    Name    = $_.Name
                    Value   = (Get-SafeValue -Name $_.Name -Value ([string]$_.Value))
                    Length  = ([string]$_.Value).Length
                    Secrety = ([bool]($_.Name -match $script:SecretPattern -or ([string]$_.Value) -match $script:SecretPattern))
                }
            }
    )
}

# =========================================================================
# C. Module
# =========================================================================
Invoke-Section -Name 'C_modules' -Body {
    $Wanted = @('PSScriptAnalyzer', 'Pester', 'PSReadLine', 'PSScriptCoverage', 'Microsoft.PowerShell.ConsoleGuiTools')

    $Found = @()

    foreach ($Name in $Wanted) {
        $Available = @(Get-Module -ListAvailable -Name $Name -ErrorAction SilentlyContinue |
                Sort-Object Version -Descending)

        $Loaded = @(Get-Module -Name $Name -ErrorAction SilentlyContinue)

        $Found += [pscustomobject]@{
            Name          = $Name
            Available     = $Available.Count -gt 0
            Version       = $(if ($Available.Count -gt 0) { $Available[0].Version.ToString() } else { '' })
            Path          = $(if ($Available.Count -gt 0) { $Available[0].ModuleBase } else { '' })
            LoadedInSession = $Loaded.Count -gt 0
        }
    }

    [pscustomobject]@{
        Checked       = $Found
        TotalAvailable = @(Get-Module -ListAvailable -ErrorAction SilentlyContinue).Count
        PSScriptAnalyzerCommand = [bool](Get-Command Invoke-ScriptAnalyzer -ErrorAction SilentlyContinue)
        PesterCommand           = [bool](Get-Command Invoke-Pester -ErrorAction SilentlyContinue)
        FormatterCommand        = [bool](Get-Command Invoke-Formatter -ErrorAction SilentlyContinue)
        TestJsonCommand         = [bool](Get-Command Test-Json -ErrorAction SilentlyContinue)
    }
}

# =========================================================================
# D. Befehls-Konflikte (Schattenbildung durch Alias/Function)
# =========================================================================
Invoke-Section -Name 'D_command_conflicts' -Body {
    $Critical = @(
        'Get-WorkspacePath', 'Get-WorkspaceContent', 'Get-DiscoveryResult', 'Get-WorkspaceSummary',
        'Write-Log', 'Select-Backend', 'Load-RuntimePolicy', 'Validate-MasterPrompt',
        'Get-ChildItem', 'Get-Content', 'Get-Item', 'Test-Path', 'Get-FileHash',
        'Sort-Object', 'ConvertTo-Json', 'ConvertFrom-Json', 'Invoke-RestMethod', 'Invoke-WebRequest',
        'Remove-Item', 'Copy-Item', 'Move-Item', 'Set-Content', 'Out-File',
        'Get-Command', 'Get-Module', 'Import-Module', 'Test-Json',
        'Get-LocalCloudCodeSnapshot', 'Update-PsFunctionBlock'
    )

    $Rows = @()

    foreach ($Name in $Critical) {
        $All = @(Get-Command -Name $Name -All -ErrorAction SilentlyContinue)

        if ($All.Count -eq 0) {
            $Rows += [pscustomobject]@{ Name = $Name; Count = 0; Kind = 'not-found'; Source = ''; Definition = '' }
            continue
        }

        foreach ($Entry in $All) {
            $Definition = ''

            switch ($Entry.CommandType) {
                'Function' { $Definition = ([string]$Entry.Definition).Substring(0, [Math]::Min(120, ([string]$Entry.Definition).Length)) }
                'Alias' { $Definition = '-> ' + [string]$Entry.Definition }
                default { $Definition = [string]$Entry.Source }
            }

            $Rows += [pscustomobject]@{
                Name       = $Name
                Count      = $All.Count
                Kind       = $Entry.CommandType.ToString()
                Source     = [string]$Entry.Source
                Definition = $Definition
            }
        }
    }

    $Conflicts = @($Rows | Where-Object { $_.Count -gt 1 } |
            Group-Object Name |
            ForEach-Object {
                [pscustomobject]@{
                    Name  = $_.Name
                    Kinds = (@($_.Group | ForEach-Object { "$($_.Kind):$($_.Source)" }) -join ' | ')
                }
            })

    [pscustomobject]@{
        Conflicts = $Conflicts
        All       = $Rows
    }
}

# =========================================================================
# E. Dateien: Groesse, Hash, Parse, Smart Quotes
# =========================================================================
Invoke-Section -Name 'E_files' -Body {
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        return [pscustomobject]@{ RootExists = $false; Root = $Root; Files = @() }
    }

    $Exclude = '\\(\.git|node_modules|bin|obj|backups)\\'
    $Files = @(
        Get-ChildItem -LiteralPath $Root -Recurse -File -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch $Exclude }
    )

    $Rows = [System.Collections.Generic.List[object]]::new()

    foreach ($File in $Files) {
        $Row = [ordered]@{
            RelativePath = $File.FullName.Substring($Root.Length).TrimStart('\')
            SizeBytes    = $File.Length
            LastWriteUtc = $File.LastWriteTimeUtc.ToString('o')
            SHA256       = (Get-FileHash -LiteralPath $File.FullName -Algorithm SHA256).Hash
            ParseErrors  = $null
            ParseFirstError = ''
            SmartQuotes  = $null
            HasBom       = $null
        }

        if ($File.Extension -in @('.ps1', '.psm1', '.psd1')) {
            $Tokens = $null
            $Errors = $null
            [System.Management.Automation.Language.Parser]::ParseFile($File.FullName, [ref]$Tokens, [ref]$Errors) | Out-Null
            $Row.ParseErrors = @($Errors).Count

            if (@($Errors).Count -gt 0) {
                $First = @($Errors)[0]
                $Row.ParseFirstError = 'Zeile {0}, Spalte {1}: {2}' -f $First.Extent.StartLineNumber, $First.Extent.StartColumnNumber, $First.Message
            }

            $Content = Get-TextFile -Path $File.FullName
            $Row.SmartQuotes = ([regex]::Matches($Content.Text, '[\u201C\u201D\u201E\u201A\u2018\u2019]')).Count
            $Row.HasBom = $Content.HasBom
        }
        elseif ($File.Extension -in @('.txt', '.json', '.md')) {
            $Content = Get-TextFile -Path $File.FullName
            $Row.HasBom = $Content.HasBom
        }

        $Rows.Add([pscustomobject]$Row)
    }

    [pscustomobject]@{
        RootExists = $true
        Root       = $Root
        FileCount  = $Rows.Count
        Files      = $Rows
    }
}

# =========================================================================
# F. Masterprompt
# =========================================================================
Invoke-Section -Name 'F_master_prompt' -Body {
    $Candidates = @(
        (Join-Path $Root ("prompts\{0}" -f $PromptFileName)),
        (Join-Path $Root $PromptFileName)
    )

    $Path = $Candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1

    if (-not $Path) {
        return [pscustomobject]@{ Found = $false; Searched = $Candidates }
    }

    $Content = Get-TextFile -Path $Path
    $Hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    $Lines = @($Content.Text -split "`r?`n")
    $Header = $Lines | Where-Object { $_ -match '\S' } | Select-Object -First 3

    [pscustomobject]@{
        Found                = $true
        Path                 = $Path
        Bytes                = $Content.Bytes.Length
        Chars                = $Content.Text.Length
        Lines                = $Lines.Count
        SHA256               = $Hash
        ExpectedSHA256       = $ExpectedPromptSha256
        HashMatchesExpected  = ($Hash -eq $ExpectedPromptSha256)
        HasBom               = $Content.HasBom
        HeaderMatchesRegex   = [bool]($Content.Text.Substring(0, [Math]::Min(4000, $Content.Text.Length)) -match '(?m)^# ULTIMATIVER MASTERPROMPT v9\.2')
        MarkerGenesisOmega   = [bool]($Content.Text -match 'GENESIS OMEGA SUPREME')
        FirstMeaningfulLines = @($Header)
    }
}

# =========================================================================
# G. Runtime-Policy
# =========================================================================
Invoke-Section -Name 'G_runtime_policy' -Body {
    $PolicyRoot = Join-Path $Root 'runtime_policy'

    if (-not (Test-Path -LiteralPath $PolicyRoot -PathType Container)) {
        return [pscustomobject]@{ PolicyRootExists = $false; PolicyRoot = $PolicyRoot }
    }

    $PolicyFiles = @(
        Get-ChildItem -LiteralPath $PolicyRoot -File -ErrorAction SilentlyContinue |
            ForEach-Object {
                [pscustomobject]@{
                    Name         = $_.Name
                    SizeBytes    = $_.Length
                    LastWriteUtc = $_.LastWriteTimeUtc.ToString('o')
                    SHA256       = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
                }
            }
    )

    $Reports = @()

    foreach ($ReportFile in @(Get-ChildItem -LiteralPath $PolicyRoot -Filter 'compile_*.json' -File -ErrorAction SilentlyContinue)) {
        $Entry = [ordered]@{ Name = $ReportFile.Name; Parsed = $false; Fields = @(); Values = @{} }

        try {
            $Json = (Get-TextFile -Path $ReportFile.FullName).Text | ConvertFrom-Json -Depth 64
            $Entry.Parsed = $true
            $Entry.Fields = @($Json.PSObject.Properties.Name)

            foreach ($Field in @('Status', 'Mode', 'SourceSHA256', 'OutputSHA256', 'CompiledChars', 'RuntimeAllowed', 'MissingRequiredRules')) {
                if ($Json.PSObject.Properties.Name -contains $Field) {
                    $Entry.Values[$Field] = [string]($Json.$Field)
                }
            }
        }
        catch {
            $Entry.Values['Error'] = $_.Exception.Message
        }

        $Reports += [pscustomobject]$Entry
    }

    [pscustomobject]@{
        PolicyRootExists = $true
        PolicyRoot       = $PolicyRoot
        PolicyFiles      = $PolicyFiles
        CompilerReports  = $Reports
        HasOutputSHA256  = [bool](@($Reports | Where-Object { $_.Fields -contains 'OutputSHA256' }).Count -gt 0)
    }
}

# =========================================================================
# H. Backends (nur GET)
# =========================================================================
Invoke-Section -Name 'H_backends' -Body {
    $Result = [ordered]@{}

    try {
        $Tags = Invoke-RestMethod -Uri 'http://127.0.0.1:11434/api/tags' -TimeoutSec 6
        $Result['OllamaOnline'] = $true
        $Result['OllamaModels'] = @($Tags.models | ForEach-Object { [string]$_.name })
    }
    catch {
        $Result['OllamaOnline'] = $false
        $Result['OllamaError'] = $_.Exception.Message
    }

    try {
        $Models = Invoke-RestMethod -Uri 'http://127.0.0.1:1234/v1/models' -TimeoutSec 6
        $Result['LMStudioOnline'] = $true
        $Result['LMStudioModels'] = @($Models.data | ForEach-Object { [string]$_.id })
    }
    catch {
        $Result['LMStudioOnline'] = $false
        $Result['LMStudioError'] = $_.Exception.Message
    }

    [pscustomobject]$Result
}

# =========================================================================
# I. Content-Probe: Rueckgabetyp und Felder von Get-WorkspaceContent
# =========================================================================
Invoke-Section -Name 'I_content_probe' -Body {
    $ScriptFile = Join-Path $Root 'LocalCloudCode.ps1'

    if (-not (Test-Path -LiteralPath $ScriptFile -PathType Leaf)) {
        return [pscustomobject]@{ Ran = $false; Reason = "Runtime-Skript nicht gefunden: $ScriptFile" }
    }

    $Tokens = $null
    $Errors = $null
    $Ast = [System.Management.Automation.Language.Parser]::ParseFile($ScriptFile, [ref]$Tokens, [ref]$Errors)

    if (@($Errors).Count -gt 0) {
        return [pscustomobject]@{
            Ran          = $false
            Reason       = 'Runtime-Skript hat Syntaxfehler - Probe uebersprungen.'
            ParseErrors  = @($Errors).Count
            FirstError   = ('Zeile {0}: {1}' -f @($Errors)[0].Extent.StartLineNumber, @($Errors)[0].Message)
        }
    }

    $Sources = (@($Ast.FindAll({ param($Node) $Node -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)) |
            ForEach-Object { $_.Extent.Text }) -join ([Environment]::NewLine + [Environment]::NewLine)

    $FunctionNames = @($Ast.FindAll({ param($Node) $Node -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true) |
            ForEach-Object { $_.Name })

    $TestWorkspacePath = Join-Path $Root $TestWorkspaceRelative

    $Probe = {
        param($FunctionSources, $WorkspacePath, $RootPath)

        $WorkspaceRoot = Join-Path $RootPath 'workspace'
        $LogRoot = Join-Path $RootPath 'logs'

        . ([ScriptBlock]::Create($FunctionSources))

        $Out = [ordered]@{}

        if (-not (Test-Path -LiteralPath $WorkspacePath -PathType Container)) {
            $Out['Content'] = 'Test-Workspace nicht vorhanden: ' + $WorkspacePath
        }
        else {
            $Raw = Get-WorkspaceContent -Path $WorkspacePath

            $Out['ContentType'] = $Raw.GetType().FullName
            $Out['ContentIsString'] = ($Raw -is [string])

            $Parsed = if ($Raw -is [string]) { $Raw | ConvertFrom-Json -Depth 64 } else { $Raw }

            $Out['ContentKeys'] = @($Parsed.PSObject.Properties.Name)

            foreach ($Key in @('ReadOnly', 'ContentReadFiles', 'ContentBlockedFiles', 'ContentChars', 'ReadFiles', 'BlockedFiles', 'Evidence', 'SchemaVersion', 'Limits')) {
                if (@($Parsed.PSObject.Properties.Name) -contains $Key) {
                    $Value = $Parsed.$Key

                    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
                        $Out["Key_$Key"] = "Sammlung, $((@($Value)).Count) Eintraege"
                    }
                    else {
                        $Out["Key_$Key"] = [string]$Value
                    }
                }
            }
        }

        if (Get-Command -Name Get-DiscoveryResult -ErrorAction SilentlyContinue) {
            $Cmd = Get-Command -Name Get-DiscoveryResult
            $Out['DiscoveryParamNames'] = @($Cmd.Parameters.Keys | Where-Object { $_ -notin [System.Management.Automation.PSCmdlet]::CommonParameters })

            try {
                $Discovery = if ($Cmd.Parameters.ContainsKey('WorkspaceName')) {
                    Get-DiscoveryResult -WorkspaceName (Split-Path -Path $WorkspacePath -Leaf)
                }
                elseif ($Cmd.Parameters.ContainsKey('Workspace')) {
                    Get-DiscoveryResult -Workspace $WorkspacePath
                }
                else {
                    Get-DiscoveryResult
                }

                $Out['DiscoveryKeys'] = @($Discovery.PSObject.Properties.Name)
                $Out['DiscoveryEvidenceHash'] = [string]$Discovery.EvidenceHash
                $Out['DiscoveryReadCount'] = [string]$Discovery.Inventory.ReadCount
            }
            catch {
                $Out['DiscoveryError'] = $_.Exception.Message
            }
        }

        return [pscustomobject]$Out
    }

    $ProbeResult = & $Probe -FunctionSources $Sources -WorkspacePath $TestWorkspacePath -RootPath $Root

    [pscustomobject]@{
        Ran            = $true
        FunctionCount  = $FunctionNames.Count
        FunctionNames  = $FunctionNames
        WorkspaceProbe = $TestWorkspacePath
        Result         = $ProbeResult
    }
}

# =========================================================================
# J. Statische Fakten zu den Befunden des Pruefberichts
# =========================================================================
Invoke-Section -Name 'J_static_facts' -Body {
    $ScriptFile = Join-Path $Root 'LocalCloudCode.ps1'

    if (-not (Test-Path -LiteralPath $ScriptFile -PathType Leaf)) {
        return [pscustomobject]@{ Ran = $false; Reason = 'Runtime-Skript nicht gefunden.' }
    }

    $Text = (Get-TextFile -Path $ScriptFile).Text
    $Lines = @($Text -split "`r?`n")

    $Markers = [ordered]@{
        'F-01_SmartQuotes'            = '[\u201C\u201D\u201E\u201A\u2018\u2019]'
        'F-02_CallWithoutPath'        = 'Get-WorkspaceContent\s+@'
        'F-02_ReflectionProbe'        = "Parameters\.ContainsKey\('WorkspacePath'\)"
        'F-03_WrongParameterName'     = 'Get-WorkspacePath\s+-Workspace\b'
        'F-06_ManifestCheck'          = 'expected_sha256|manifest\.json'
        'F-07_OutputSHA256'           = 'OutputSHA256'
        'F-08_CultureDependentSort'   = 'Sort-Object\s+Path'
        'F-09_JsonJoinQuirk'          = "\}\s*\)\s*-join\s*''"
        'F-09_AsArray'                = 'ConvertTo-Json[^\r\n]*-AsArray'
        'F-12_AuthorizedRootParam'    = '\$AuthorizedRoot'
        'F-13a_DuplicateEvidence'     = '\$WorkspaceContentEvidence\s*=\s*\$null'
        'F-14_MaxFilesParam'          = 'MaxFiles'
        'F-16_RequiresVersion'        = '^\s*#requires\s+-Version'
        'F-17_StrictModeLatest'       = 'Set-StrictMode\s+-Version\s+Latest'
        'F-19_LoopbackAssert'         = 'Assert-LocalEndpoint|NON_LOCAL_ENDPOINT_BLOCKED'
        'F-19_SecretFilter'           = 'SECRET_REDACTED|redact'
        'F-20_DotSourceGuard'         = '\$MyInvocation\.InvocationName'
        'F-20_MainFunction'           = 'function\s+Invoke-LocalCloudCodeRuntime'
        'F-24_PromptBudget'           = 'Get-PromptBudget|truncated_sections'
        'F-25_SessionHistory'         = 'session\.json|SessionHistory'
        'F-27_ModeCapabilities'       = 'ModeCapabilities'
        'G1_NoMasterPrompt'           = '\$NoMasterPrompt'
        'G2_UnsafeSwitch'             = '\$Unsafe|unsafe_mode'
        'G3_RunReportStatus'          = 'RESPONSE_RECEIVED'
        'G4_MasterPromptFlag'         = '"master_prompt"\s*:'
    }

    $Rows = [System.Collections.Generic.List[object]]::new()

    foreach ($Key in $Markers.Keys) {
        $Pattern = $Markers[$Key]
        $Hits = [System.Collections.Generic.List[int]]::new()

        for ($Index = 0; $Index -lt $Lines.Count; $Index++) {
            if ($Lines[$Index] -match $Pattern) { $Hits.Add($Index + 1) }
        }

        $Rows.Add([pscustomobject]@{
            Marker    = $Key
            Pattern   = $Pattern
            Found     = $Hits.Count -gt 0
            HitCount  = $Hits.Count
            Lines     = @($Hits | Select-Object -First 5)
        })
    }

    $CallSites = @(
        for ($Index = 0; $Index -lt $Lines.Count; $Index++) {
            if ($Lines[$Index] -match 'Get-DiscoveryResult') {
                '{0}: {1}' -f ($Index + 1), $Lines[$Index].Trim()
            }
        }
    )

    [pscustomobject]@{
        Ran        = $true
        ScriptFile = $ScriptFile
        LineCount  = $Lines.Count
        Markers    = $Rows
        DiscoveryCallSites = $CallSites
    }
}

# =========================================================================
# K. Logs und Run-Reports
# =========================================================================
Invoke-Section -Name 'K_logs' -Body {
    $LogRoot = Join-Path $Root 'logs'

    if (-not (Test-Path -LiteralPath $LogRoot -PathType Container)) {
        return [pscustomobject]@{ LogRootExists = $false; LogRoot = $LogRoot }
    }

    $LogFiles = @(
        Get-ChildItem -LiteralPath $LogRoot -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 10 |
            ForEach-Object {
                [pscustomobject]@{ Name = $_.Name; SizeBytes = $_.Length; LastWriteUtc = $_.LastWriteTimeUtc.ToString('o') }
            }
    )

    $Reports = @()

    foreach ($ReportFile in @(Get-ChildItem -LiteralPath $LogRoot -Filter 'run_*.json' -File -ErrorAction SilentlyContinue |
                Sort-Object LastWriteTime -Descending | Select-Object -First 3)) {
        $Entry = [ordered]@{ Name = $ReportFile.Name; Parsed = $false; Values = @{} }

        try {
            $Json = (Get-TextFile -Path $ReportFile.FullName).Text | ConvertFrom-Json -Depth 32
            $Entry.Parsed = $true

            foreach ($Field in @('run_id', 'version', 'provider', 'model', 'mode', 'workspace', 'master_prompt', 'master_prompt_sha256', 'status', 'runtime_policy_chars', 'evidence_hash')) {
                if (@($Json.PSObject.Properties.Name) -contains $Field) {
                    $Entry.Values[$Field] = [string]($Json.$Field)
                }
            }

            $Entry.Values['AllFields'] = @($Json.PSObject.Properties.Name) -join ', '
        }
        catch {
            $Entry.Values['Error'] = $_.Exception.Message
        }

        $Reports += [pscustomobject]$Entry
    }

    [pscustomobject]@{
        LogRootExists = $true
        LogRoot       = $LogRoot
        NewestFiles   = $LogFiles
        RunReports    = $Reports
    }
}

# =========================================================================
# Berichte schreiben
# =========================================================================
$Payload = [ordered]@{
    Meta = [ordered]@{
        GeneratedLocal = (Get-Date).ToString('o')
        GeneratedUtc   = (Get-Date).ToUniversalTime().ToString('o')
        Tool           = 'Collect-LocalCloudCodeSnapshot.ps1'
        Mode           = 'READONLY'
        Root           = $Root
        SectionErrors  = @($script:Problems)
    }
    Sections = $script:Sections
}

$JsonPath = Join-Path $Target ("snapshot_{0}.json" -f $script:Stamp)
$MdPath = Join-Path $Target ("snapshot_{0}.md" -f $script:Stamp)

try {
    [System.IO.File]::WriteAllText($JsonPath, ($Payload | ConvertTo-Json -Depth 24), $script:Utf8)
    Write-Host "Rohdaten (JSON) geschrieben: $JsonPath" -ForegroundColor Green
}
catch {
    Write-Host "FEHLER beim Schreiben der JSON-Rohdaten: $($_.Exception.Message)" -ForegroundColor Red
    throw
}

# --- Markdown-Zusammenfassung (Fehler hier duerfen die Rohdaten nicht kosten)
try {

$Md = [System.Collections.Generic.List[string]]::new()
$Md.Add('# LocalCloudCode Snapshot (READ-ONLY)')
$Md.Add('')
$Md.Add("- Erzeugt: $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))")
$Md.Add("- Wurzel : $Root")
$Md.Add("- Quelle : ausschliesslich lesende Abfragen (Parsen, Hashen, GETs)")
$Md.Add('')

$Host1 = $script:Sections['A_host_shell']
if ($Host1 -and -not ($Host1.PSObject.Properties.Name -contains 'Error')) {
    $Md.Add('## A - Host und Shell')
    $Md.Add('')
    $Md.Add('| Feld | Wert |')
    $Md.Add('|---|---|')

    foreach ($Property in @('ComputerName', 'UserName', 'PSVersion', 'PSEdition', 'OS', 'DotNetVersion', 'Culture', 'LanguageMode', 'ProfilePath', 'ProfileExists', 'ProfileSHA256')) {
        $Md.Add(('| {0} | {1} |' -f $Property, $Host1.$Property))
    }

    if ($Host1.ExecutionPolicy) {
        $Md.Add(('| ExecutionPolicy (LocalMachine) | {0} |' -f $Host1.ExecutionPolicy.LocalMachine))
        $Md.Add(('| ExecutionPolicy (CurrentUser) | {0} |' -f $Host1.ExecutionPolicy.CurrentUser))
    }

    $Md.Add('')
}

$Mod1 = $script:Sections['C_modules']
if ($Mod1 -and -not ($Mod1.PSObject.Properties.Name -contains 'Error')) {
    $Md.Add('## C - Module')
    $Md.Add('')
    $Md.Add('| Modul | Verfuegbar | Version | Geladen |')
    $Md.Add('|---|---|---|---|')

    foreach ($Module in $Mod1.Checked) {
        $Md.Add(('| {0} | {1} | {2} | {3} |' -f $Module.Name, $Module.Available, $Module.Version, $Module.LoadedInSession))
    }

    $Md.Add('')
    $Md.Add(('Test-Json: {0} | Invoke-ScriptAnalyzer: {1} | Invoke-Pester: {2} | Invoke-Formatter: {3}' -f $Mod1.TestJsonCommand, $Mod1.PSScriptAnalyzerCommand, $Mod1.PesterCommand, $Mod1.FormatterCommand))
    $Md.Add('')
}

$Files1 = $script:Sections['E_files']
if ($Files1 -and $Files1.RootExists) {
    $ParseFailures = @($Files1.Files | Where-Object { $null -ne $_.ParseErrors -and $_.ParseErrors -ne 0 })
    $SmartHits = @($Files1.Files | Where-Object { $null -ne $_.SmartQuotes -and $_.SmartQuotes -ne 0 })

    $Md.Add('## E - Dateien')
    $Md.Add('')
    $Md.Add(("- Geprueft: {0} Dateien unter {1}" -f $Files1.FileCount, $Files1.Root))
    $Md.Add(("- Syntaxfehler: {0}" -f $ParseFailures.Count))
    $Md.Add(("- Smart-Quote-Treffer: {0}" -f $SmartHits.Count))
    $Md.Add('')
    $Md.Add('### PowerShell-Dateien (Pfad | Bytes | SHA256 | Parsefehler)')
    $Md.Add('')
    $Md.Add('| Datei | Bytes | SHA256 | Parsefehler | SmartQuotes | BOM |')
    $Md.Add('|---|---|---|---|---|---|')

    foreach ($File in @($Files1.Files | Where-Object { $_.RelativePath -match '\.(ps1|psm1|psd1)$' } | Sort-Object RelativePath)) {
        $Md.Add(('| {0} | {1} | {2} | {3} | {4} | {5} |' -f $File.RelativePath, $File.SizeBytes, $File.SHA256, $(if ($null -ne $File.ParseErrors) { $File.ParseErrors } else { '-' }), $(if ($null -ne $File.SmartQuotes) { $File.SmartQuotes } else { '-' }), $File.HasBom))
    }

    $Md.Add('')

    if ($ParseFailures.Count -gt 0) {
        $Md.Add('### Syntaxfehler im Detail')
        $Md.Add('')

        foreach ($File in $ParseFailures) {
            $Md.Add(('- `{0}` -> {1}' -f $File.RelativePath, $File.ParseFirstError))
        }

        $Md.Add('')
    }
}

$Prompt1 = $script:Sections['F_master_prompt']
if ($Prompt1 -and $Prompt1.Found) {
    $Md.Add('## F - Masterprompt')
    $Md.Add('')
    $Md.Add('| Feld | Wert |')
    $Md.Add('|---|---|')
    $Md.Add(('| Pfad | {0} |' -f $Prompt1.Path))
    $Md.Add(('| Bytes | {0} |' -f $Prompt1.Bytes))
    $Md.Add(('| Zeichen | {0} |' -f $Prompt1.Chars))
    $Md.Add(('| Zeilen | {0} |' -f $Prompt1.Lines))
    $Md.Add(('| SHA256 | {0} |' -f $Prompt1.SHA256))
    $Md.Add(('| Soll-Hash erreicht | {0} |' -f $Prompt1.HashMatchesExpected))
    $Md.Add(('| BOM | {0} |' -f $Prompt1.HasBom))
    $Md.Add(('| Header v9.2 erkannt | {0} |' -f $Prompt1.HeaderMatchesRegex))
    $Md.Add(('| Marker GENESIS OMEGA SUPREME | {0} |' -f $Prompt1.MarkerGenesisOmega))
    $Md.Add('')
}

$Policy1 = $script:Sections['G_runtime_policy']
if ($Policy1 -and $Policy1.PolicyRootExists) {
    $Md.Add('## G - Runtime-Policy')
    $Md.Add('')
    $Md.Add('| Datei | Bytes | SHA256 |')
    $Md.Add('|---|---|---|')

    foreach ($File in $Policy1.PolicyFiles) {
        $Md.Add(('| {0} | {1} | {2} |' -f $File.Name, $File.SizeBytes, $File.SHA256))
    }

    $Md.Add('')
    $Md.Add(('Compiler-Report mit OutputSHA256 vorhanden: **{0}** (Befund F-07)' -f $Policy1.HasOutputSHA256))
    $Md.Add('')

    foreach ($Report in $Policy1.CompilerReports) {
        $Md.Add(('- Report `{0}`: geparst={1}; Felder={2}' -f $Report.Name, $Report.Parsed, (@($Report.Fields) -join ', ')))
    }

    $Md.Add('')
}

$Backends = $script:Sections['H_backends']
if ($Backends -and -not ($Backends.PSObject.Properties.Name -contains 'Error')) {
    $Md.Add('## H - Backends')
    $Md.Add('')
    $Md.Add(('- Ollama online: **{0}** | Modelle: {1}' -f $Backends.OllamaOnline, $(if ($Backends.OllamaModels) { (@($Backends.OllamaModels) -join ', ') } else { '-' })))
    $Md.Add(('- LM Studio online: **{0}** | Modelle: {1}' -f $Backends.LMStudioOnline, $(if ($Backends.LMStudioModels) { (@($Backends.LMStudioModels) -join ', ') } else { '-' })))
    $Md.Add('')
}

$Probe = $script:Sections['I_content_probe']
if ($Probe) {
    $Md.Add('## I - Content-Probe')
    $Md.Add('')

    if ($Probe.Ran) {
        $Md.Add(('- Funktionen im Runtime-Skript: {0}' -f $Probe.FunctionCount))
        $Md.Add(('- Test-Workspace: {0}' -f $Probe.WorkspaceProbe))

        foreach ($Property in $Probe.Result.PSObject.Properties) {
            $Value = $Property.Value

            if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
                $Value = (@($Value) -join ', ')
            }

            $Md.Add(('- {0}: {1}' -f $Property.Name, $Value))
        }
    }
    else {
        $Md.Add(('- Nicht ausgefuehrt: {0}' -f $Probe.Reason))
    }

    $Md.Add('')
}

$Facts = $script:Sections['J_static_facts']
if ($Facts -and $Facts.Ran) {
    $Md.Add('## J - Statische Fakten (gegen die Befunde des Pruefberichts)')
    $Md.Add('')
    $Md.Add(('Runtime-Datei: `{0}` ({1} Zeilen)' -f $Facts.ScriptFile, $Facts.LineCount))
    $Md.Add('')
    $Md.Add('| Marker | Gefunden | Treffer | Zeilen |')
    $Md.Add('|---|---|---|---|')

    foreach ($Row in $Facts.Markers) {
        $Md.Add(('| {0} | {1} | {2} | {3} |' -f $Row.Marker, $Row.Found, $Row.HitCount, (@($Row.Lines) -join ', ')))
    }

    $Md.Add('')
    $Md.Add('### Aufrufstellen von Get-DiscoveryResult')
    $Md.Add('')

    if (@($Facts.DiscoveryCallSites).Count -gt 0) {
        foreach ($Site in $Facts.DiscoveryCallSites) { $Md.Add(('- `{0}`' -f $Site)) }
    }
    else {
        $Md.Add('- keine (Befund F-04: Funktion ist im Runtime-Pfad nicht eingebunden)')
    }

    $Md.Add('')
}

$Logs = $script:Sections['K_logs']
if ($Logs -and $Logs.LogRootExists) {
    $Md.Add('## K - Logs und Run-Reports')
    $Md.Add('')
    $Md.Add('| Datei | Bytes |')
    $Md.Add('|---|---|')

    foreach ($File in $Logs.NewestFiles) {
        $Md.Add(('| {0} | {1} |' -f $File.Name, $File.SizeBytes))
    }

    $Md.Add('')

    foreach ($Report in $Logs.RunReports) {
        $Md.Add(('### {0}' -f $Report.Name))
        $Md.Add('')

        foreach ($Key in @($Report.Values.Keys)) {
            $Md.Add(('- {0}: {1}' -f $Key, $Report.Values[$Key]))
        }

        $Md.Add('')
    }
}

if ($script:Problems.Count -gt 0) {
    $Md.Add('## Abschnittsfehler')
    $Md.Add('')

    foreach ($Problem in $script:Problems) {
        $Md.Add(('- {0}' -f $Problem))
    }

    $Md.Add('')
}

[System.IO.File]::WriteAllText($MdPath, ($Md -join [Environment]::NewLine) + [Environment]::NewLine, $script:Utf8)
Write-Host "Zusammenfassung (Markdown) geschrieben: $MdPath" -ForegroundColor Green

}
catch {
    Write-Host "WARNUNG: Markdown-Zusammenfassung fehlgeschlagen: $($_.Exception.Message)" -ForegroundColor Yellow
    $MdPath = ''
}

$ZipPath = ''

if (-not $NoZip -and $MdPath) {
    try {
        $ZipPath = Join-Path $OutputDirectory ("snapshot_{0}.zip" -f $script:Stamp)
        Compress-Archive -LiteralPath $JsonPath, $MdPath -DestinationPath $ZipPath -Force
    }
    catch {
        Write-Host "WARNUNG: ZIP fehlgeschlagen: $($_.Exception.Message)" -ForegroundColor Yellow
        $ZipPath = ''
    }
}

Write-Host ''
Write-Host '============================================'
Write-Host ' SNAPSHOT: FERTIG' -ForegroundColor Green
Write-Host '============================================'
Write-Host "JSON     : $JsonPath"
Write-Host "Markdown : $MdPath"

if ($ZipPath) {
    Write-Host "ZIP      : $ZipPath"
}

Write-Host ''
Write-Host "Abschnittsfehler: $($script:Problems.Count)"

if ($script:Problems.Count -gt 0) {
    foreach ($Problem in $script:Problems) {
        Write-Host ("  - $Problem") -ForegroundColor Yellow
    }
}

Write-Host ''
Write-Host 'Wichtig: Es wurde nichts am System geaendert. Nur gelesen und die'
Write-Host 'Berichtsdateien im Zielordner erzeugt.'
Write-Host ''

# Kurzbefunde direkt auf der Konsole, damit die wichtigsten Fakten sofort sichtbar sind
$Files1 = $script:Sections['E_files']

if ($Files1 -and $Files1.RootExists) {
    $Bad = @($Files1.Files | Where-Object { $null -ne $_.ParseErrors -and $_.ParseErrors -ne 0 })

    if ($Bad.Count -gt 0) {
        Write-Host 'SYNTAXFEHLER:' -ForegroundColor Red

        foreach ($File in $Bad) {
            Write-Host ("  {0} -> {1}" -f $File.RelativePath, $File.ParseFirstError) -ForegroundColor Red
        }
    }
    else {
        Write-Host 'SYNTAX: alle geprueften Skripte fehlerfrei.' -ForegroundColor Green
    }
}

$Prompt1 = $script:Sections['F_master_prompt']

if ($Prompt1 -and $Prompt1.Found) {
    if ($Prompt1.HashMatchesExpected) {
        Write-Host 'MASTERPROMPT: SHA256 stimmt mit dem gepinnten Sollwert ueberein.' -ForegroundColor Green
    }
    else {
        Write-Host ('MASTERPROMPT: SHA256 weicht ab! ist={0} soll={1}' -f $Prompt1.SHA256, $Prompt1.ExpectedSHA256) -ForegroundColor Yellow
    }
}

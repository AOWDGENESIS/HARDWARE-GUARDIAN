#requires -Version 7.0

param(
    [Parameter(Mandatory=$false)]
    [string]$Prompt = "",

    [Parameter(Mandatory=$false)]
    [ValidateSet("Auto","Ollama","LMStudio")]
    [string]$Provider = "Auto",

    [Parameter(Mandatory=$false)]
    [string]$Model = "",

    [Parameter(Mandatory=$false)]
    [string]$Workspace = "",

    [Parameter(Mandatory=$false)]
    [ValidateSet("Chat","Plan","Inspect")]
    [string]$Mode = "Chat",

    [Parameter(Mandatory=$false)]
    [switch]$NoMasterPrompt
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ScriptRoot = $PSScriptRoot

$PromptPath = Join-Path $ScriptRoot "prompts\MASTER_PROMPT.txt"
$ConfigPath = Join-Path $ScriptRoot "config\settings.json"
$LogRoot = Join-Path $ScriptRoot "logs"
$WorkspaceRoot = Join-Path $ScriptRoot "workspace"
$RuntimePolicyRoot = Join-Path $ScriptRoot "runtime_policy"

$OllamaUrl = "http://127.0.0.1:11434"
$OllamaChatUrl = "$OllamaUrl/api/chat"

$LMStudioUrl = "http://127.0.0.1:1234"
$LMStudioChatUrl = "$LMStudioUrl/v1/chat/completions"

$DefaultOllamaModel = "qwen2.5:14b"
$DefaultLMStudioModel = "qwen/qwen2.5-coder-14b"

$script:RunId = [Guid]::NewGuid().ToString("N")

function Write-Log {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Message,

        [ValidateSet("INFO","WARN","ERROR")]
        [string]$Level = "INFO"
    )

    $Stamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $Line = "[$Stamp][$Level][$script:RunId] $Message"

    Write-Host $Line

    try {
        New-Item -ItemType Directory -Path $LogRoot -Force | Out-Null

        $LogFile = Join-Path `
            $LogRoot `
            ("LocalCloudCode_{0}.log" -f (Get-Date -Format "yyyyMMdd"))

        [System.IO.File]::AppendAllText(
            $LogFile,
            $Line + [Environment]::NewLine,
            [System.Text.UTF8Encoding]::new($false)
        )
    }
    catch {
    }
}

function Get-Sha256 {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Path
    )

    return (Get-FileHash `
        -LiteralPath $Path `
        -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Validate-MasterPrompt {
    if ($NoMasterPrompt) {
        Write-Log "Master prompt validation explicitly disabled." "WARN"
        return ""
    }

    if (-not (Test-Path -LiteralPath $PromptPath -PathType Leaf)) {
        throw "Master prompt missing: $PromptPath"
    }

    $Bytes = [System.IO.File]::ReadAllBytes($PromptPath)

    if ($Bytes.Length -lt 1000) {
        throw "Master prompt is implausibly small."
    }

    $Utf8 = [System.Text.UTF8Encoding]::new($false,$true)
    $Text = $Utf8.GetString($Bytes)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        throw "Master prompt is empty."
    }

    $FirstPart = $Text.Substring(
        0,
        [Math]::Min(4000,$Text.Length)
    )

    if ($FirstPart -notmatch '(?m)^# ULTIMATIVER MASTERPROMPT v9\.2') {
        throw "Master prompt v9.2 header not found."
    }

    if ($Text -notmatch 'GENESIS OMEGA SUPREME') {
        throw "GENESIS OMEGA SUPREME marker not found."
    }

    $Hash = Get-Sha256 -Path $PromptPath

    Write-Log "Master prompt source validated."
    Write-Log "Master prompt bytes: $($Bytes.Length)"
    Write-Log "Master prompt chars: $($Text.Length)"
    Write-Log "Master prompt SHA256: $Hash"

    return $Hash
}

function Load-RuntimePolicy {
    param(
        [Parameter(Mandatory=$true)]
        [string]$SelectedMode,

        [Parameter(Mandatory=$true)]
        [string]$MasterHash
    )

    $PolicyFile = Join-Path `
        $RuntimePolicyRoot `
        ("MASTER_POLICY_{0}.txt" -f $SelectedMode)

    if (-not (Test-Path -LiteralPath $PolicyFile -PathType Leaf)) {
        throw "Runtime policy missing for mode ${SelectedMode}: $PolicyFile"
    }

    $Reports = @(
        Get-ChildItem `
            -LiteralPath $RuntimePolicyRoot `
            -Filter ("compile_{0}_*.json" -f $SelectedMode) `
            -File `
            -ErrorAction Stop |
        Sort-Object LastWriteTime -Descending
    )

    if ($Reports.Count -eq 0) {
        throw "Compiler report missing for mode $SelectedMode."
    }

    $ReportFile = $Reports[0].FullName

    $ReportRaw = [System.IO.File]::ReadAllText(
        $ReportFile,
        [System.Text.UTF8Encoding]::new($false,$true)
    )

    if ([string]::IsNullOrWhiteSpace($ReportRaw)) {
        throw "Compiler report is empty: $ReportFile"
    }

    $Report = $ReportRaw | ConvertFrom-Json

    if ([string]$Report.Status -ne "OK") {
        throw "Runtime policy compiler status is not OK."
    }

    if ([string]$Report.Mode -ne $SelectedMode) {
        throw "Runtime policy mode mismatch."
    }

    if ([string]$Report.SourceSHA256 -ne $MasterHash) {
        throw "Runtime policy source hash mismatch."
    }

    if ($false -eq [bool]$Report.RuntimeAllowed) {
        throw "Runtime policy is not allowed."
    }

    $MissingRules = @($Report.MissingRequiredRules)

    if ($MissingRules.Count -gt 0) {
        throw "Runtime policy has missing required rules: $($MissingRules -join ', ')"
    }

    $PolicyBytes = [System.IO.File]::ReadAllBytes($PolicyFile)

    if ($PolicyBytes.Length -eq 0) {
        throw "Runtime policy file is empty."
    }

    $PolicyText = [System.Text.UTF8Encoding]::new($false,$true).GetString($PolicyBytes)

    if ([string]::IsNullOrWhiteSpace($PolicyText)) {
        throw "Runtime policy contains no usable text."
    }

    Write-Log "Runtime policy validated."
    Write-Log "Runtime policy file: $PolicyFile"
    Write-Log "Runtime policy chars: $($PolicyText.Length)"
    Write-Log "Compiler report: $ReportFile"

    return [PSCustomObject]@{
        Text = $PolicyText
        File = $PolicyFile
        Report = $ReportFile
        SourceSHA256 = [string]$Report.SourceSHA256
        CompiledChars = [int]$Report.CompiledChars
    }
}

function Get-Config {
    if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) {
        return [ordered]@{
            ollama_url = $OllamaUrl
            lmstudio_url = $LMStudioUrl
            default_ollama_model = $DefaultOllamaModel
            default_lmstudio_model = $DefaultLMStudioModel
            timeout_seconds = 600
        }
    }

    $Raw = [System.IO.File]::ReadAllText(
        $ConfigPath,
        [System.Text.UTF8Encoding]::new($false,$true)
    )

    if ([string]::IsNullOrWhiteSpace($Raw)) {
        throw "Config file is empty."
    }

    return ($Raw | ConvertFrom-Json)
}

function Test-HttpEndpoint {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Uri
    )

    try {
        $Response = Invoke-WebRequest `
            -Uri $Uri `
            -Method Get `
            -TimeoutSec 5

        return [PSCustomObject]@{
            Online = $true
            StatusCode = [int]$Response.StatusCode
            Error = ""
        }
    }
    catch {
        return [PSCustomObject]@{
            Online = $false
            StatusCode = 0
            Error = $_.Exception.Message
        }
    }
}

function Get-OllamaModels {
    $Response = Invoke-RestMethod `
        -Uri "$OllamaUrl/api/tags" `
        -Method Get `
        -TimeoutSec 10

    if ($null -eq $Response.models) {
        return @()
    }

    return @(
        $Response.models |
        ForEach-Object {
            [string]$_.name
        }
    )
}

function Get-LMStudioModels {
    $Response = Invoke-RestMethod `
        -Uri "$LMStudioUrl/v1/models" `
        -Method Get `
        -TimeoutSec 10

    if ($null -eq $Response.data) {
        return @()
    }

    return @(
        $Response.data |
        ForEach-Object {
            [string]$_.id
        }
    )
}

function Select-Backend {
    param(
        [Parameter(Mandatory=$true)]
        [string]$RequestedProvider,

        [string]$RequestedModel = ""
    )

    $OllamaHealth = Test-HttpEndpoint -Uri $OllamaUrl
    $LMHealth = Test-HttpEndpoint -Uri $LMStudioUrl

    $OllamaModels = @()
    $LMModels = @()

    if ($OllamaHealth.Online) {
        try {
            $OllamaModels = Get-OllamaModels
        }
        catch {
            Write-Log `
                "Ollama model discovery failed: $($_.Exception.Message)" `
                "WARN"
        }
    }

    if ($LMHealth.Online) {
        try {
            $LMModels = Get-LMStudioModels
        }
        catch {
            Write-Log `
                "LM Studio model discovery failed: $($_.Exception.Message)" `
                "WARN"
        }
    }

    if ($RequestedProvider -eq "Ollama") {
        if (-not $OllamaHealth.Online) {
            throw "Ollama was requested but is offline."
        }

        $SelectedModel = if ($RequestedModel) {
            $RequestedModel
        }
        else {
            $DefaultOllamaModel
        }

        if (
            $OllamaModels.Count -gt 0 -and
            $OllamaModels -notcontains $SelectedModel
        ) {
            throw "Requested Ollama model not found: $SelectedModel"
        }

        return [PSCustomObject]@{
            Provider = "Ollama"
            Model = $SelectedModel
            Url = $OllamaChatUrl
        }
    }

    if ($RequestedProvider -eq "LMStudio") {
        if (-not $LMHealth.Online) {
            throw "LM Studio was requested but is offline."
        }

        $SelectedModel = if ($RequestedModel) {
            $RequestedModel
        }
        else {
            $DefaultLMStudioModel
        }

        if (
            $LMModels.Count -gt 0 -and
            $LMModels -notcontains $SelectedModel
        ) {
            throw "Requested LM Studio model not found: $SelectedModel"
        }

        return [PSCustomObject]@{
            Provider = "LMStudio"
            Model = $SelectedModel
            Url = $LMStudioChatUrl
        }
    }

    if ($RequestedModel) {
        if ($OllamaModels -contains $RequestedModel) {
            return [PSCustomObject]@{
                Provider = "Ollama"
                Model = $RequestedModel
                Url = $OllamaChatUrl
            }
        }

        if ($LMModels -contains $RequestedModel) {
            return [PSCustomObject]@{
                Provider = "LMStudio"
                Model = $RequestedModel
                Url = $LMStudioChatUrl
            }
        }

        throw "Requested model was not found locally: $RequestedModel"
    }

    if (
        $LMHealth.Online -and
        ($LMModels -contains $DefaultLMStudioModel)
    ) {
        return [PSCustomObject]@{
            Provider = "LMStudio"
            Model = $DefaultLMStudioModel
            Url = $LMStudioChatUrl
        }
    }

    if (
        $OllamaHealth.Online -and
        ($OllamaModels -contains $DefaultOllamaModel)
    ) {
        return [PSCustomObject]@{
            Provider = "Ollama"
            Model = $DefaultOllamaModel
            Url = $OllamaChatUrl
        }
    }

    if ($LMHealth.Online -and $LMModels.Count -gt 0) {
        return [PSCustomObject]@{
            Provider = "LMStudio"
            Model = $LMModels[0]
            Url = $LMStudioChatUrl
        }
    }

    if ($OllamaHealth.Online -and $OllamaModels.Count -gt 0) {
        return [PSCustomObject]@{
            Provider = "Ollama"
            Model = $OllamaModels[0]
            Url = $OllamaChatUrl
        }
    }

    throw "No usable local AI backend was found."
}

function Get-WorkspacePath {
    param(
        [string]$RequestedWorkspace = ""
    )

    $RootFull = [System.IO.Path]::GetFullPath($WorkspaceRoot)

    if (-not (Test-Path -LiteralPath $RootFull -PathType Container)) {
        throw "Authorized workspace root does not exist: $RootFull"
    }

    $RootItem = Get-Item -LiteralPath $RootFull -Force -ErrorAction Stop

    if (($RootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Authorized workspace root is a reparse point: $RootFull"
    }

    if ([string]::IsNullOrWhiteSpace($RequestedWorkspace)) {
        return $RootFull
    }

    $Full = [System.IO.Path]::GetFullPath($RequestedWorkspace)

    if (-not (Test-Path -LiteralPath $Full -PathType Container)) {
        throw "Workspace does not exist: $Full"
    }

    $RequestedItem = Get-Item -LiteralPath $Full -Force -ErrorAction Stop

    if (($RequestedItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Workspace path is a reparse point: $Full"
    }

    $RootCompare = $RootFull.TrimEnd("\")
    $RootPrefix = $RootCompare + "\"

    $IsRoot = $Full.Equals(
        $RootCompare,
        [System.StringComparison]::OrdinalIgnoreCase
    )

    $IsChild = $Full.StartsWith(
        $RootPrefix,
        [System.StringComparison]::OrdinalIgnoreCase
    )

    if (-not $IsRoot -and -not $IsChild) {
        throw "Workspace path is outside the authorized workspace root: $Full"
    }

    $Relative = ""

    if ($IsChild) {
        $Relative = $Full.Substring($RootPrefix.Length)
    }

    if (-not [string]::IsNullOrWhiteSpace($Relative)) {
        $Parts = $Relative -split "[\\/]"

        $Current = $RootCompare

        foreach ($Part in $Parts) {
            if ([string]::IsNullOrWhiteSpace($Part)) {
                continue
            }

            $Current = Join-Path $Current $Part

            if (-not (Test-Path -LiteralPath $Current -PathType Container)) {
                throw "Workspace path segment does not exist: $Current"
            }

            $SegmentItem = Get-Item -LiteralPath $Current -Force -ErrorAction Stop

            if (($SegmentItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Workspace path contains a reparse point: $Current"
            }
        }
    }

    return $Full
}
function Get-DiscoveryResult {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Workspace
    )

    $WorkspacePath = Get-WorkspacePath -Workspace $Workspace

    $ContentCommand = Get-Command Get-WorkspaceContent -CommandType Function -ErrorAction Stop
    $InvokeParams = @{}

    if ($ContentCommand.Parameters.ContainsKey('WorkspacePath')) {
        $InvokeParams['WorkspacePath'] = $WorkspacePath
    }

    if ($ContentCommand.Parameters.ContainsKey('Workspace')) {
        $InvokeParams['Workspace'] = $Workspace
    }

    if ($ContentCommand.Parameters.ContainsKey('MaxFileBytes')) {
        $InvokeParams['MaxFileBytes'] = 65536
    }

    if ($ContentCommand.Parameters.ContainsKey('MaxTotalChars')) {
        $InvokeParams['MaxTotalChars'] = 24000
    }

    $RawContent = Get-WorkspaceContent @InvokeParams

    if ($RawContent -is [string]) {
        try {
            $Content = $RawContent | ConvertFrom-Json -Depth 100
        }
        catch {
            throw "DISCOVERY_CONTENT_JSON_FAILED: $($_.Exception.Message)"
        }
    }
    else {
        $Content = $RawContent
    }

    if ($null -eq $Content) {
        throw 'DISCOVERY_CONTENT_EMPTY'
    }

    $ReadOnly = $true

    if ($Content.PSObject.Properties.Name -contains 'ReadOnly') {
        $ReadOnly = [bool]$Content.ReadOnly
    }

    if (-not $ReadOnly) {
        throw 'DISCOVERY_READONLY_CONTRACT_FAILED'
    }

    $ReadFiles = @()
    $BlockedFiles = @()
    $Evidence = @()

    if ($Content.PSObject.Properties.Name -contains 'ReadFiles') {
        $ReadFiles = @($Content.ReadFiles)
    }
    elseif ($Content.PSObject.Properties.Name -contains 'Files') {
        $ReadFiles = @($Content.Files)
    }
    elseif ($Content.PSObject.Properties.Name -contains 'Read') {
        $ReadFiles = @($Content.Read)
    }

    if ($Content.PSObject.Properties.Name -contains 'BlockedFiles') {
        $BlockedFiles = @($Content.BlockedFiles)
    }
    elseif ($Content.PSObject.Properties.Name -contains 'Blocked') {
        $BlockedFiles = @($Content.Blocked)
    }

    if ($Content.PSObject.Properties.Name -contains 'Evidence') {
        $Evidence = @($Content.Evidence)
    }

    if ($Evidence.Count -eq 0 -and $ReadFiles.Count -gt 0) {
        $EvidenceList = [System.Collections.Generic.List[object]]::new()

        foreach ($File in $ReadFiles) {
            $EvidenceList.Add(
                [pscustomobject][ordered]@{
                    Path = [string]$File.Path
                    Size = [int64]$File.Size
                    Extension = [string]$File.Extension
                    Encoding = [string]$File.Encoding
                    SHA256 = [string]$File.SHA256
                    ContentChars = [int]$File.ContentChars
                    Truncated = [bool]$File.Truncated
                }
            )
        }

        $Evidence = @($EvidenceList)
    }

    $NormalizedReadFiles = @(
        foreach ($File in $ReadFiles) {
            [pscustomobject][ordered]@{
                Path = [string]$File.Path
                Size = [int64]$File.Size
                Extension = [string]$File.Extension
                Encoding = [string]$File.Encoding
                SHA256 = [string]$File.SHA256
                ContentChars = [int]$File.ContentChars
                Truncated = [bool]$File.Truncated
            }
        }
    )

    $NormalizedBlockedFiles = @(
        foreach ($File in $BlockedFiles) {
            [pscustomobject][ordered]@{
                Path = [string]$File.Path
                Size = if ($null -ne $File.Size) { [int64]$File.Size } else { 0 }
                Extension = [string]$File.Extension
                Reason = [string]$File.Reason
            }
        }
    )

    $NormalizedEvidence = @(
        foreach ($Item in $Evidence) {
            [pscustomobject][ordered]@{
                Path = [string]$Item.Path
                Size = [int64]$Item.Size
                Extension = [string]$Item.Extension
                Encoding = [string]$Item.Encoding
                SHA256 = [string]$Item.SHA256
                ContentChars = [int]$Item.ContentChars
                Truncated = [bool]$Item.Truncated
            }
        }
    )

    if ($NormalizedReadFiles.Count -ne $NormalizedEvidence.Count) {
        throw "DISCOVERY_EVIDENCE_COUNT_MISMATCH: read=$($NormalizedReadFiles.Count) evidence=$($NormalizedEvidence.Count)"
    }

    $KnownReasons = @(
        'REPARSE_POINT_BLOCKED'
        'EXTENSION_NOT_ALLOWED'
        'FILE_SIZE_LIMIT'
        'BINARY_CONTENT_DETECTED'
        'INVALID_TEXT_ENCODING'
        'TOTAL_CONTENT_LIMIT_REACHED'
        'READ_FAILED'
    )

    $UnknownReasons = @(
        $NormalizedBlockedFiles |
            Where-Object {
                $_.Reason -and ($KnownReasons -notcontains $_.Reason)
            } |
            Select-Object -ExpandProperty Reason -Unique
    )

    $SecurityReasons = @(
        $NormalizedBlockedFiles |
            Where-Object { $_.Reason } |
            Select-Object -ExpandProperty Reason -Unique
    )

    $EvidenceJson = @(
        $NormalizedEvidence |
            Sort-Object Path |
            ConvertTo-Json -Depth 20 -Compress
    ) -join ''

    $EvidenceBytes = [System.Text.UTF8Encoding]::new($false).GetBytes($EvidenceJson)
    $EvidenceHashBytes = [System.Security.Cryptography.SHA256]::HashData($EvidenceBytes)
    $EvidenceHash = (
        [System.BitConverter]::ToString($EvidenceHashBytes) -replace '-', ''
    ).ToUpperInvariant()

    $InventoryCount = $NormalizedReadFiles.Count + $NormalizedBlockedFiles.Count

    return [pscustomobject][ordered]@{
        DiscoveryVersion = '1.1'
        Workspace = $WorkspacePath
        Inventory = [pscustomobject][ordered]@{
            Count = $InventoryCount
            ReadCount = $NormalizedReadFiles.Count
            BlockedCount = $NormalizedBlockedFiles.Count
        }
        ReadFiles = @($NormalizedReadFiles)
        BlockedFiles = @($NormalizedBlockedFiles)
        Evidence = @($NormalizedEvidence)
        EvidenceHash = $EvidenceHash
        Limits = [pscustomobject][ordered]@{
            MaxFileBytes = 65536
            MaxTotalChars = 24000
            MaxFiles = 500
        }
        Security = [pscustomobject][ordered]@{
            ReadOnly = $ReadOnly
            Reasons = @($SecurityReasons)
        }
        Unknown = [pscustomobject][ordered]@{
            Reasons = @($UnknownReasons)
        }
        Contract = [pscustomobject][ordered]@{
            ReadOnly = $true
            EvidenceHash = $true
            EvidenceSeparatedFromContent = $true
            DeterministicEvidenceSerialization = $true
        }
    }
}
function Get-WorkspaceContent {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Path,

        [int]$MaxFileBytes = 65536,

        [int]$MaxTotalChars = 24000
    )

    $AllowedExtensions = @(
        ".ps1",
        ".psm1",
        ".psd1",
        ".json",
        ".jsonc",
        ".xml",
        ".yaml",
        ".yml",
        ".md",
        ".txt",
        ".csv",
        ".ini",
        ".cfg",
        ".conf",
        ".toml",
        ".py",
        ".js",
        ".ts",
        ".tsx",
        ".jsx",
        ".html",
        ".htm",
        ".css",
        ".scss",
        ".sql",
        ".sh",
        ".bat",
        ".cmd"
    )

    $Utf8Strict = New-Object System.Text.UTF8Encoding($false, $true)

    $Files = @()
$ReadRows = @()
$BlockedRows = @()

$PendingDirectories = New-Object System.Collections.Queue
$PendingDirectories.Enqueue((Get-Item -LiteralPath $Path -Force -ErrorAction Stop))

while ($PendingDirectories.Count -gt 0) {

    $CurrentDirectory = $PendingDirectories.Dequeue()

    if (($CurrentDirectory.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {

        $RelativeReparsePath = $CurrentDirectory.FullName

        if ($CurrentDirectory.FullName.StartsWith(
            $Path.TrimEnd("\") + "\",
            [System.StringComparison]::OrdinalIgnoreCase
        )) {
            $RelativeReparsePath = $CurrentDirectory.FullName.Substring(
                $Path.TrimEnd("\").Length
            ).TrimS

# ASCII-only. Windows PowerShell 5.1 / PowerShell 7.
# Reads local facts only. No installation. No system configuration changes.
[CmdletBinding()]
param(
    [string]$Root = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Runtime = Join-Path $Root 'runtime'
$OutJson = Join-Path $Runtime 'KI_Maschinenprofil.json'
$OutMd = Join-Path $Runtime 'KI_Maschinenprofil.md'
$LogDir = Join-Path $Runtime 'logs'
New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
$Log = Join-Path $LogDir 'KI_Dauerreferenz_AutoUpdate.log'

function Write-Log([string]$Message) {
    $line = "$(Get-Date -Format s) $Message"
    Add-Content -LiteralPath $Log -Value $line -Encoding UTF8
}
function Get-CmdText([string]$FileName, [string[]]$Args) {
    try {
        $cmd = Get-Command $FileName -ErrorAction Stop
        $o = & $cmd.Source @Args 2>&1
        return (($o | ForEach-Object { $_.ToString() }) -join "`n").Trim()
    } catch { return 'NICHT GEFUNDEN' }
}
function Get-Version([string]$FileName, [string[]]$Args) {
    $x = Get-CmdText $FileName $Args
    if ([string]::IsNullOrWhiteSpace($x)) { return 'UNBEKANNT' }
    return $x
}
function Try-Json([string]$Uri) {
    try { return (Invoke-RestMethod -Uri $Uri -Method Get -TimeoutSec 4 -ErrorAction Stop) } catch { return $null }
}
function Test-Port([int]$Port) {
    try { return [bool](Test-NetConnection -ComputerName '127.0.0.1' -Port $Port -InformationLevel Quiet -WarningAction SilentlyContinue) } catch { return $false }
}
function Get-ProcessFileVersion([string[]]$Names) {
    foreach ($n in $Names) {
        try {
            $p = Get-Process -Name $n -ErrorAction Stop | Select-Object -First 1
            if ($p.Path) {
                $v = (Get-Item -LiteralPath $p.Path -ErrorAction Stop).VersionInfo.FileVersion
                return [ordered]@{ process = $p.ProcessName; path = $p.Path; version = $v }
            }
        } catch { }
    }
    return $null
}

try {
    Write-Log 'START'
    $os = Get-CimInstance Win32_OperatingSystem
    $cs = Get-CimInstance Win32_ComputerSystem
    $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
    $disk = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'" | Select-Object -First 1

    $facts = [ordered]@{
        schema_version = 1
        generated_at_local = (Get-Date).ToString('o')
        machine = [ordered]@{
            computer_name = $env:COMPUTERNAME
            user_name = $env:USERNAME
            user_profile = $env:USERPROFILE
            powershell_process_architecture = $env:PROCESSOR_ARCHITECTURE
        }
        windows = [ordered]@{
            caption = $os.Caption
            version = $os.Version
            build = $os.BuildNumber
            os_architecture = $os.OSArchitecture
            last_boot = $os.LastBootUpTime.ToString('o')
        }
        hardware = [ordered]@{
            cpu = $cpu.Name
            cores = $cpu.NumberOfCores
            logical_processors = $cpu.NumberOfLogicalProcessors
            ram_gb = [math]::Round($cs.TotalPhysicalMemory / 1GB, 2)
            c_drive_free_gb = if ($disk) { [math]::Round($disk.FreeSpace / 1GB, 2) } else { $null }
        }
        powershell = [ordered]@{
            edition = $PSVersionTable.PSEdition
            version = $PSVersionTable.PSVersion.ToString()
            psversion = ($PSVersionTable | Out-String).Trim()
            windows_powershell_5_1 = Get-Version 'powershell.exe' @('-NoProfile','-Command','$PSVersionTable.PSVersion.ToString()')
            pwsh_7 = Get-Version 'pwsh.exe' @('-NoProfile','-Command','$PSVersionTable.PSVersion.ToString()')
        }
        python = [ordered]@{
            python = Get-Version 'python.exe' @('--version')
            py = Get-Version 'py.exe' @('--version')
            pip = Get-Version 'pip.exe' @('--version')
            python_path = try { (Get-Command python.exe -ErrorAction Stop).Source } catch { 'NICHT GEFUNDEN' }
        }
        tools = [ordered]@{
            git = Get-Version 'git.exe' @('--version')
            winget = Get-Version 'winget.exe' @('--version')
            node = Get-Version 'node.exe' @('--version')
            npm = Get-Version 'npm.cmd' @('--version')
        }
        local_ai = [ordered]@{
            ollama_binary = Get-Version 'ollama.exe' @('--version')
            ollama_process = Get-ProcessFileVersion @('ollama')
            ollama_api_port_11434 = (Test-Port 11434)
            ollama_api_version = $null
            ollama_tags_available = $null
            lmstudio_api_port_1234 = (Test-Port 1234)
            lmstudio_api_models = $null
            lmstudio_process = Get-ProcessFileVersion @('LM Studio','lm-studio','lmstudio')
            relevant_env = [ordered]@{
                OLLAMA_HOST = $env:OLLAMA_HOST
                OLLAMA_FLASH_ATTENTION = $env:OLLAMA_FLASH_ATTENTION
                OLLAMA_GPU_OVERHEAD = $env:OLLAMA_GPU_OVERHEAD
                HERMES_MODEL_NAME = $env:HERMES_MODEL_NAME
                HERMES_MODEL_PROVIDER = $env:HERMES_MODEL_PROVIDER
            }
        }
        gpu = [ordered]@{
            nvidia_smi = Get-Version 'nvidia-smi.exe' @('--query-gpu=name,driver_version,memory.total,memory.used,utilization.gpu','--format=csv,noheader')
        }
        validation = [ordered]@{
            source = 'local runtime inspection'
            internet_required = $false
            installs_performed = $false
            system_settings_changed = $false
        }
    }

    $ollamaVersion = Try-Json 'http://127.0.0.1:11434/api/version'
    if ($null -ne $ollamaVersion) { $facts.local_ai.ollama_api_version = $ollamaVersion.version }
    $ollamaTags = Try-Json 'http://127.0.0.1:11434/api/tags'
    if ($null -ne $ollamaTags) {
        $facts.local_ai.ollama_tags_available = @($ollamaTags.models | ForEach-Object { $_.name })
    }
    $lm = Try-Json 'http://127.0.0.1:1234/v1/models'
    if ($null -ne $lm) { $facts.local_ai.lmstudio_api_models = @($lm.data | ForEach-Object { $_.id }) }

    $json = $facts | ConvertTo-Json -Depth 10
    Set-Content -LiteralPath $OutJson -Value $json -Encoding UTF8

    $md = @()
    $md += '# KI Maschinenprofil'
    $md += ''
    $md += "Generated: $($facts.generated_at_local)"
    $md += ''
    $md += 'This file contains volatile local machine and tool facts. It is not a permanent software rule.'
    $md += ''
    $md += '## Windows'
    $md += "- Computer: $($facts.machine.computer_name)"
    $md += "- OS: $($facts.windows.caption)"
    $md += "- Version: $($facts.windows.version)"
    $md += "- Build: $($facts.windows.build)"
    $md += ''
    $md += '## Hardware'
    $md += "- CPU: $($facts.hardware.cpu)"
    $md += "- Cores: $($facts.hardware.cores)"
    $md += "- Logical processors: $($facts.hardware.logical_processors)"
    $md += "- RAM GB: $($facts.hardware.ram_gb)"
    $md += "- C free GB: $($facts.hardware.c_drive_free_gb)"
    $md += ''
    $md += '## PowerShell'
    $md += "- Current process: $($facts.powershell.edition) $($facts.powershell.version)"
    $md += "- Windows PowerShell 5.1: $($facts.powershell.windows_powershell_5_1)"
    $md += "- PowerShell 7: $($facts.powershell.pwsh_7)"
    $md += ''
    $md += '## Python'
    $md += "- python: $($facts.python.python)"
    $md += "- py: $($facts.python.py)"
    $md += "- pip: $($facts.python.pip)"
    $md += "- path: $($facts.python.python_path)"
    $md += ''
    $md += '## Tools'
    $md += "- Git: $($facts.tools.git)"
    $md += "- winget: $($facts.tools.winget)"
    $md += "- Node: $($facts.tools.node)"
    $md += "- npm: $($facts.tools.npm)"
    $md += ''
    $md += '## Local AI'
    $md += "- Ollama binary: $($facts.local_ai.ollama_binary)"
    $md += "- Ollama API 11434: $($facts.local_ai.ollama_api_port_11434)"
    $md += "- Ollama API version: $($facts.local_ai.ollama_api_version)"
    $md += "- Ollama process: $($facts.local_ai.ollama_process | ConvertTo-Json -Compress)"
    $md += "- Ollama models: $([string]::Join(', ', @($facts.local_ai.ollama_tags_available)))"
    $md += "- LM Studio API 1234: $($facts.local_ai.lmstudio_api_port_1234)"
    $md += "- LM Studio models: $([string]::Join(', ', @($facts.local_ai.lmstudio_api_models)))"
    $md += "- LM Studio process: $($facts.local_ai.lmstudio_process | ConvertTo-Json -Compress)"
    $md += "- NVIDIA: $($facts.gpu.nvidia_smi)"
    Set-Content -LiteralPath $OutMd -Value ($md -join "`n") -Encoding UTF8

    $inventoryMd = @()
    $inventoryMd += '# Current local machine/tool snapshot'
    $inventoryMd += ''
    $inventoryMd += "Generated: $($facts.generated_at_local)"
    $inventoryMd += ''
    $inventoryMd += '| Area | Fact | Value |'
    $inventoryMd += '|---|---|---|'
    $rows = @(
        @('Windows','Build', $facts.windows.build),
        @('Windows','Version', $facts.windows.version),
        @('PowerShell','Current', "$($facts.powershell.edition) $($facts.powershell.version)"),
        @('PowerShell','Windows PowerShell 5.1', $facts.powershell.windows_powershell_5_1),
        @('PowerShell','PowerShell 7', $facts.powershell.pwsh_7),
        @('Python','python', $facts.python.python),
        @('Python','py', $facts.python.py),
        @('Python','pip', $facts.python.pip),
        @('Tools','Git', $facts.tools.git),
        @('Tools','winget', $facts.tools.winget),
        @('Tools','Node', $facts.tools.node),
        @('Tools','npm', $facts.tools.npm),
        @('Local AI','Ollama', $facts.local_ai.ollama_binary),
        @('Local AI','Ollama API version', $facts.local_ai.ollama_api_version),
        @('Local AI','Ollama port 11434', $facts.local_ai.ollama_api_port_11434),
        @('Local AI','LM Studio API port 1234', $facts.local_ai.lmstudio_api_port_1234),
        @('Hardware','CPU', $facts.hardware.cpu),
        @('Hardware','RAM GB', $facts.hardware.ram_gb),
        @('Hardware','NVIDIA', $facts.gpu.nvidia_smi)
    )
    foreach ($r in $rows) {
        $v = [string]$r[2]
        $v = $v.Replace('|','/').Replace("`r",' ').Replace("`n",' / ')
        $inventoryMd += "| $($r[0]) | $($r[1]) | $v |"
    }
    $inventoryMd += ''
    $inventoryMd += '**Source:** local runtime inspection. No installation and no system configuration changes were performed.'
    Set-Content -LiteralPath (Join-Path $Runtime 'CURRENT_MACHINE_SNAPSHOT.md') -Value ($inventoryMd -join "`n") -Encoding UTF8

    Write-Log 'SUCCESS runtime profile refreshed'
    exit 0
} catch {
    Write-Log ('ERROR ' + $_.Exception.Message)
    exit 1
}

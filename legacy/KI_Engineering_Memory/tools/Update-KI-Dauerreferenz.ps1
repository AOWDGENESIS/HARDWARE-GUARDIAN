# ASCII-only. Read-only inventory. No software installation.
[CmdletBinding()]
param([string]$InstallRoot = "$env:LOCALAPPDATA\KI-Engineering-Memory")
$ErrorActionPreference='Stop'
$runtime=Join-Path $InstallRoot 'runtime'
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
function Get-CmdVersion($cmd,$args=@('--version')) { try { $p=Get-Command $cmd -ErrorAction Stop; $v=& $p.Source @args 2>&1 | Select-Object -First 1; return [string]$v } catch { return $null } }
$os=Get-CimInstance Win32_OperatingSystem
$cs=Get-CimInstance Win32_ComputerSystem
$cpu=(Get-CimInstance Win32_Processor | Select-Object -First 1)
$gpu=Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion,AdapterRAM
$apps=[ordered]@{}
foreach($n in 'git','python','py','pip','pwsh','powershell','gh','winget','ollama'){ $apps[$n]=Get-CmdVersion $n }
$apis=[ordered]@{}
foreach($name in 'Ollama','LM Studio'){
  if($name -eq 'Ollama'){ $url='http://127.0.0.1:11434/api/tags' } else { $url='http://127.0.0.1:1234/v1/models' }
  try { $r=Invoke-RestMethod -Uri $url -TimeoutSec 3 -ErrorAction Stop; $apis[$name]=[ordered]@{ online=$true; endpoint=$url; models=if($r.models){@($r.models | ForEach-Object { $_.name })}elseif($r.data){@($r.data | ForEach-Object { $_.id })}else{@()} } } catch { $apis[$name]=[ordered]@{ online=$false; endpoint=$url; models=@() } }
}
$data=[ordered]@{
 schema_version='1.0'; generated_utc=(Get-Date).ToUniversalTime().ToString('o'); computer_name=$env:COMPUTERNAME; user_name=$env:USERNAME
 os=[ordered]@{ caption=$os.Caption; version=$os.Version; build=$os.BuildNumber; architecture=$os.OSArchitecture }
 hardware=[ordered]@{ cpu=$cpu.Name; cores=$cpu.NumberOfCores; logical_processors=$cpu.NumberOfLogicalProcessors; ram_bytes=[int64]$cs.TotalPhysicalMemory; gpu=@($gpu) }
 powershell=[ordered]@{ edition=$PSVersionTable.PSEdition; version=$PSVersionTable.PSVersion.ToString(); os=$PSVersionTable.OS }
 tools=$apps; local_ai=$apis
}
$json=$data|ConvertTo-Json -Depth 8
[IO.File]::WriteAllText((Join-Path $runtime 'KI_Maschinenprofil.json'),$json,(New-Object Text.UTF8Encoding($false)))
$md=@("# KI Machine Profile","","Generated UTC: $($data.generated_utc)","Computer: $($data.computer_name)","User: $($data.user_name)","","## OS","$($os.Caption) $($os.Version) build $($os.BuildNumber)","","## PowerShell","$($PSVersionTable.PSEdition) $($PSVersionTable.PSVersion)","","## Hardware","CPU: $($cpu.Name)","Cores: $($cpu.NumberOfCores)","Logical processors: $($cpu.NumberOfLogicalProcessors)","RAM bytes: $([int64]$cs.TotalPhysicalMemory)","","## Tool Versions")
foreach($k in $apps.Keys){$md += "- ${k}: $($apps[$k])"}
$md += "","## Local AI APIs"
foreach($k in $apis.Keys){$md += "- ${k}: online=$($apis[$k].online), endpoint=$($apis[$k].endpoint), models=$([string]::Join(', ',@($apis[$k].models)))"}
[IO.File]::WriteAllLines((Join-Path $runtime 'KI_Maschinenprofil.md'),$md,(New-Object Text.UTF8Encoding($false)))

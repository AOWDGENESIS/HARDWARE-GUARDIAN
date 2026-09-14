#requires -Version 7.2
<#
.SYNOPSIS
    Kompakte READ-ONLY Diagnose des LocalCloudCode-Systems (Ausgabe zum Kopieren).

.DESCRIPTION
    Liest ausschliesslich: Parsergebnisse, Hashes, Parameternamen, GETs an
    Ollama / LM Studio. Es wird NICHTS am System geaendert und nichts
    geschrieben. Die Ausgabe ist bewusst kurz (~50 Zeilen), damit sie direkt
    in einen Chat kopiert werden kann.

    Nutzen, wenn die grosse Snapshot-ZIP nicht uebertragen werden kann.

.EXAMPLE
    & "$env:USERPROFILE\Desktop\KI\Entwicklungen\tools\Get-LccQuickFacts.ps1"

.EXAMPLE
    # Ergebnis direkt in die Zwischenablage legen
    & "...\Get-LccQuickFacts.ps1" | Tee-Object -Variable out | Out-Null; $out | Set-Clipboard
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$Root = "$env:USERPROFILE\Desktop\KI\LocalCloudCode"
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Continue'

$R = $Root
$Main = Get-Content "$R\LocalCloudCode.ps1" -Raw -ErrorAction SilentlyContinue
"=== $R | PS $($PSVersionTable.PSVersion) | $((Get-Culture).Name) ==="
"--- DATEIEN (parse / quotes / hash) ---"
Get-ChildItem $R -Recurse -File -ErrorAction SilentlyContinue |
  Where-Object { @('.ps1', '.psm1', '.psd1') -contains $_.Extension } | ForEach-Object {
  $e = $null; $t = $null
  [System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$t, [ref]$e) | Out-Null
  $q = ([regex]::Matches([System.IO.File]::ReadAllText($_.FullName), '[\u201C\u201D\u201E\u201A\u2018\u2019]')).Count
  "{0,-30} {1,8}B parse={2,-5} quotes={3} {4}" -f $_.Name, $_.Length, $(if ($e.Count) { "FAIL" } else { "OK" }), $q, (Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0, 16)
  if ($e.Count) { "    -> Zeile $($e[0].Extent.StartLineNumber): $($e[0].Message)" }
}
"--- PROMPT / POLICY (SHA256) ---"
Get-ChildItem "$R\prompts", "$R\runtime_policy" -File -ErrorAction SilentlyContinue |
  ForEach-Object { "{0,-42} {1}" -f $_.Name, (Get-FileHash $_.FullName -Algorithm SHA256).Hash }
"--- COMPILER-REPORT (Felder) ---"
Get-ChildItem "$R\runtime_policy" -Filter compile_*.json -File -ErrorAction SilentlyContinue |
  Sort-Object LastWriteTime -Descending | Select-Object -First 1 |
  ForEach-Object { $_.Name; ((Get-Content $_.FullName -Raw | ConvertFrom-Json).PSObject.Properties.Name -join ', ') }
"--- FUNKTIONEN (Parameternamen) ---"
$WorkspaceRoot = "$R\workspace"; $LogRoot = "$R\logs"
$ast = [System.Management.Automation.Language.Parser]::ParseFile("$R\LocalCloudCode.ps1", [ref]$null, [ref]$null)
$src = (@($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)) | ForEach-Object { $_.Extent.Text }) -join "`n`n"
try { . ([ScriptBlock]::Create($src)) } catch { "LOAD-FEHLER: $($_.Exception.Message)" }
foreach ($f in 'Get-WorkspaceContent', 'Get-DiscoveryResult', 'Get-WorkspacePath', 'Get-WorkspaceSummary', 'Select-Backend') {
  $c = Get-Command $f -ErrorAction SilentlyContinue
  if ($c) { "$f : " + (($c.Parameters.Keys | Where-Object { $_ -notmatch '^(Verbose|Debug|Error|Warning|Information|Out|Pipeline|Progress)' }) -join ',') }
  else { "$f : NICHT VORHANDEN" }
}
$Defs = ([regex]::Matches($Main, '(?m)^\s*function\s+Get-DiscoveryResult\b')).Count
$Refs = ([regex]::Matches($Main, 'Get-DiscoveryResult')).Count - $Defs
"Get-DiscoveryResult: $Defs Definition(en), $Refs weitere Verweise"
"AKTUELLER SHA256 (Baseline-Pin): " + (Get-FileHash "$R\LocalCloudCode.ps1" -Algorithm SHA256).Hash
"--- CONTENT-PROBE ---"
$W = "$R\workspace\READONLY_TEST"
if (Test-Path $W) {
  try {
    $C = Get-WorkspaceContent -Path $W
    "TYPE: $($C.GetType().FullName)"
    if ($C -is [string]) { $C = $C | ConvertFrom-Json }
    "KEYS: " + ($C.PSObject.Properties.Name -join ',')
  }
  catch { "FEHLER: $($_.Exception.Message)" }
}
else { "READONLY_TEST fehlt: $W" }
"--- BACKENDS ---"
try { "OLLAMA  : " + ((Invoke-RestMethod http://127.0.0.1:11434/api/tags -TimeoutSec 5).models.name -join ' | ') } catch { "OLLAMA  : offline" }
try { "LMSTUDIO: " + ((Invoke-RestMethod http://127.0.0.1:1234/v1/models -TimeoutSec 5).data.id -join ' | ') } catch { "LMSTUDIO: offline" }
"--- LETZTER RUN-REPORT ---"
Get-ChildItem "$R\logs" -Filter run_*.json -File -ErrorAction SilentlyContinue |
  Sort-Object LastWriteTime -Descending | Select-Object -First 1 |
  ForEach-Object { $_.Name; Get-Content $_.FullName -Raw }

<#
.SYNOPSIS
    Checks the backup chain of chapter 75 on real files: create, read, validate, change, restore, compare.

.DESCRIPTION
    The specification wants the chain proven in a fixed order and it wants the verdict to be literally
    RESTORE VERIFIED - which may only be said when the restored state really equals the backed up one.
    This script is the measuring part of that chain; the application is operated by a person:

      1. -CreateTestData writes a synthetic folder (chapter 72: synthetic files, never real ones).
      2. The examiner lets Windows Maintenance Center back that folder up.
      3. This script validates the backup: every file is read, and every SHA-256 is compared with the
         file it was made from. The result is VALID only when all of them match.
      4. -ChangeOriginals changes one original file on purpose (that is the "Original aendern" step).
      5. The examiner lets the application restore the backup.
      6. This script compares the restored state with the backup. RESTORE VERIFIED is only printed
         when every hash matches; otherwise the report says exactly which file differs.

    A backup that was damaged on purpose (-DamageBackup) is the negative case: the script records the
    damage, and a restore that afterwards leaves the originals untouched is the expected outcome. It
    never repairs anything itself.

.PARAMETER BackupDirectory
    Directory the application wrote the backup into (created by the application).

.PARAMETER OriginalDirectory
    Directory the backup was made from.

.PARAMETER CreateTestData
    Writes a synthetic folder and stops. Nothing else is touched.

.PARAMETER ChangeOriginals
    Changes one file in the original directory (to prove that a restore really restores something).

.PARAMETER DamageBackup
    Damages one file in the backup directory (negative case: a restore must be refused).

.PARAMETER Step
    Which step the report describes: validate, compare or damage.

.EXAMPLE
    pwsh ./scripts/vm/Test-BackupRestore.ps1 -CreateTestData -OriginalDirectory C:\WmcTest\Original
    pwsh ./scripts/vm/Test-BackupRestore.ps1 -Step validate -OriginalDirectory C:\WmcTest\Original -BackupDirectory C:\WmcTest\Backup
#>
[CmdletBinding()]
param(
    [string]$BackupDirectory = '',
    [string]$OriginalDirectory = '',
    [switch]$CreateTestData,
    [switch]$ChangeOriginals,
    [switch]$DamageBackup,
    [ValidateSet('prepare', 'validate', 'compare', 'damage')][string]$Step = 'validate',
    [ValidateSet('unit', 'integration', 'safety', 'security', 'recovery', 'installer',
        'localization', 'offline', 'regression', 'release')][string]$Area = 'recovery'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot/Evidence.ps1"

function Get-HashTable {
    param([Parameter(Mandatory)][string]$Directory)
    $table = @{}
    if (-not (Test-Path $Directory)) { return $table }
    foreach ($file in Get-ChildItem -Path $Directory -Recurse -File) {
        $relative = $file.FullName.Substring($Directory.Length).TrimStart('\', '/')
        $table[$relative] = (Get-FileHash -Algorithm SHA256 -Path $file.FullName).Hash.ToLowerInvariant()
    }
    return $table
}

function Write-PreparedData {
    param([Parameter(Mandatory)][string]$Directory)
    New-Item -ItemType Directory -Force -Path $Directory | Out-Null
    # Synthetic content: a text file, a binary file and a file in a subfolder. No personal data, no
    # file from the machine - chapter 72 forbids using real ones for a test.
    Set-Content -Encoding utf8 (Join-Path $Directory 'notes.txt') @(
        'synthetic test file for the backup matrix',
        'line two',
        'line three')
    $bytes = New-Object byte[] 4096
    for ($i = 0; $i -lt $bytes.Length; $i++) { $bytes[$i] = [byte]($i % 251) }
    [IO.File]::WriteAllBytes((Join-Path $Directory 'binary.bin'), $bytes)
    New-Item -ItemType Directory -Force -Path (Join-Path $Directory 'sub') | Out-Null
    Set-Content -Encoding utf8 (Join-Path $Directory 'sub/configuration.json') '{ "synthetic": true, "entries": 3 }'
}

if ($CreateTestData) {
    if ([string]::IsNullOrWhiteSpace($OriginalDirectory)) { throw '-CreateTestData needs -OriginalDirectory' }
    Write-PreparedData -Directory $OriginalDirectory
    Write-Host "synthetic test data written to $OriginalDirectory" -ForegroundColor Green
    Write-Host 'Let Windows Maintenance Center back it up now, then run this script with -Step validate.' -ForegroundColor Yellow
    return
}

if ([string]::IsNullOrWhiteSpace($OriginalDirectory) -or [string]::IsNullOrWhiteSpace($BackupDirectory)) {
    throw 'this step needs -OriginalDirectory and -BackupDirectory'
}

$testId = switch ($Step) {
    'validate' { 'M34-BAK-001' }
    'compare' { 'M34-BAK-002' }
    'damage' { 'M34-BAK-003' }
    default { 'M34-BAK-000' }
}
$run = New-WmcEvidenceRun -Area $Area -TestId $testId -Title "backup chain, step $Step"

$findings = New-Object System.Collections.Generic.List[string]
$openPoints = New-Object System.Collections.Generic.List[string]
$text = New-Object System.Collections.Generic.List[string]

if ($DamageBackup) {
    # The negative case: one byte of one backup file changes, so the hash chain cannot match any more.
    $victim = Get-ChildItem -Path $BackupDirectory -Recurse -File | Select-Object -First 1
    if (-not $victim) { throw "no file in $BackupDirectory to damage" }
    $stream = [IO.File]::Open($victim.FullName, 'Open', 'Write')
    try {
        $stream.Seek(0, 'Begin') | Out-Null
        $stream.WriteByte([byte]([byte]$stream.ReadByte() -bxor 0xFF))
    } finally {
        $stream.Dispose()
    }
    Add-WmcMeasurement -Run $run -Name 'damagedFile' -Value $victim.FullName.Substring($BackupDirectory.Length) -Source 'one byte flipped on purpose'
    $text.Add("damaged: $($victim.Name) (one byte changed on purpose)")
    $text | Set-Content -Encoding utf8 (Join-Path $run.Folder 'logs/backup-step.txt')
    Add-WmcEvidenceFile -Run $run -Path (Join-Path $run.Folder 'logs/backup-step.txt') -Description 'the damaged file of the negative case'
    Complete-WmcEvidenceRun -Run $run -Status 'PASSED' -Summary 'the negative case is prepared: one backup file is damaged, a restore must now be refused' | Out-Null
    Write-Host 'Now let the application restore that backup. It must refuse, and the originals must stay as they are.' -ForegroundColor Yellow
    return
}

if ($ChangeOriginals) {
    $target = Join-Path $OriginalDirectory 'notes.txt'
    Add-Content -Encoding utf8 $target 'changed after the backup was taken'
    Add-WmcMeasurement -Run $run -Name 'changedFile' -Value 'notes.txt' -Source 'appended one line on purpose'
    Write-Host 'the original file was changed; let the application restore the backup now' -ForegroundColor Yellow
    Complete-WmcEvidenceRun -Run $run -Status 'PASSED' -Summary 'the original was changed after the backup was taken, so a restore has something to prove' | Out-Null
    return
}

$originals = Get-HashTable -Directory $OriginalDirectory
$backup = Get-HashTable -Directory $BackupDirectory

Add-WmcMeasurement -Run $run -Name 'originals.files' -Value $originals.Count -Source "SHA-256 over $OriginalDirectory"
Add-WmcMeasurement -Run $run -Name 'backup.files' -Value $backup.Count -Source "SHA-256 over $BackupDirectory"

$missing = @($originals.Keys | Where-Object { -not $backup.ContainsKey($_) })
$different = @($originals.Keys | Where-Object { $backup.ContainsKey($_) -and $backup[$_] -ne $originals[$_] })
$additional = @($backup.Keys | Where-Object { -not $originals.ContainsKey($_) })

foreach ($file in $missing) { $text.Add("missing in backup : $file") }
foreach ($file in $different) { $text.Add("hash differs      : $file") }
foreach ($file in $additional) { $text.Add("only in backup    : $file") }
if ($missing.Count + $different.Count + $additional.Count -eq 0) {
    $text.Add("all $($originals.Count) file(s) identical in both directories")
}

$status = 'PASSED'
$summary = ''
if ($Step -eq 'validate') {
    # Chapter 75: the backup is VALID only when every file was read and every hash matches its
    # original. Anything else - missing, different, additional - is named.
    if ($missing.Count -gt 0) {
        $findings.Add("$($missing.Count) file(s) of the original are not in the backup")
    }
    if ($different.Count -gt 0) {
        $findings.Add("$($different.Count) file(s) have a different hash than the original")
    }
    if ($additional.Count -gt 0) {
        $openPoints.Add("$($additional.Count) file(s) exist only in the backup and cannot be judged")
    }
    $status = if ($findings.Count -gt 0) { 'FAILED' } elseif ($openPoints.Count -gt 0) { 'NOT VERIFIED' } else { 'PASSED' }
    $summary = if ($status -eq 'PASSED') {
        "backup VALID: all $($originals.Count) file(s) read, all SHA-256 match"
    } else {
        'the backup is not a complete copy of the original (see the report)'
    }
} else {
    # Step compare: after the restore the original directory must equal the backup again. Only then is
    # the sentence RESTORE VERIFIED allowed - it is the literal verdict chapter 75 asks for.
    if ($missing.Count -gt 0 -or $different.Count -gt 0) {
        $status = 'FAILED'
        $findings.Add("$($missing.Count + $different.Count) file(s) do not match the backup after the restore")
        $summary = 'RESTORE NOT VERIFIED: the restored state differs from the backup'
    } elseif ($additional.Count -gt 0) {
        $status = 'NOT VERIFIED'
        $openPoints.Add("$($additional.Count) extra file(s) appeared that the backup does not contain")
        $summary = 'RESTORE NOT VERIFIED: the restored state carries files the backup does not contain'
    } else {
        $summary = 'RESTORE VERIFIED: every hash equals the backed up state'
    }
}

$text | Set-Content -Encoding utf8 (Join-Path $run.Folder 'logs/backup-step.txt')
Add-WmcEvidenceFile -Run $run -Path (Join-Path $run.Folder 'logs/backup-step.txt') -Description 'file by file comparison of original and backup'

Complete-WmcEvidenceRun -Run $run -Status $status -Summary $summary `
    -Findings $findings.ToArray() -OpenPoints $openPoints.ToArray() | Out-Null

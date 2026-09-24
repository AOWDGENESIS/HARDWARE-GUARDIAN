# Windows Maintenance Center - Clean Uninstaller
param(
    [switch]$Silent = $false
)

$AppName = "Windows Maintenance Center"
$InstallDir = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$IsAdmin = $false
try {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
    $IsAdmin = $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
} catch {
    $IsAdmin = $false
}

if (-not $Silent) {
    Add-Type -AssemblyName System.Windows.Forms
    $confirm = [System.Windows.Forms.MessageBox]::Show(
        "Moechten Sie $AppName wirklich von diesem Computer entfernen?",
        "$AppName Deinstallation",
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Question
    )
    if ($confirm -ne [System.Windows.Forms.DialogResult]::Yes) {
        exit 0
    }

    # Chapter 76: Ask user before deleting logs and reports
    $keepData = [System.Windows.Forms.MessageBox]::Show(
        "Sollen Konfigurationsdaten, Berichte und Audit-Protokolle fuer eine spaetere Neuinstallation aufbewahrt werden?",
        "$AppName Datenaufbewahrung",
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Question
    )
} else {
    $keepData = [System.Windows.Forms.DialogResult]::Yes
}

# 1. Remove Desktop Shortcut
$desktopPath = [System.Environment]::GetFolderPath("Desktop")
$desktopLink = Join-Path $desktopPath "$AppName.lnk"
if (Test-Path $desktopLink) {
    Remove-Item $desktopLink -Force -ErrorAction SilentlyContinue
}

# 2. Remove Start Menu Group
$programsPath = if ($IsAdmin) {
    [System.Environment]::GetFolderPath("CommonPrograms")
} else {
    [System.Environment]::GetFolderPath("Programs")
}
$appGroup = Join-Path $programsPath "Windows Maintenance Center"
if (Test-Path $appGroup) {
    Remove-Item $appGroup -Recurse -Force -ErrorAction SilentlyContinue
}

# 3. Remove Registry Entry
$regBase = if ($IsAdmin) {
    "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
} else {
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall"
}
$regKey = Join-Path $regBase "WindowsMaintenanceCenter"
if (Test-Path $regKey) {
    Remove-Item $regKey -Recurse -Force -ErrorAction SilentlyContinue
}

# 4. Remove runtime/logs if user chose not to keep
if ($keepData -eq [System.Windows.Forms.DialogResult]::No) {
    $commonData = Join-Path $env:ProgramData "WindowsMaintenanceCenter"
    if (Test-Path $commonData) {
        Remove-Item $commonData -Recurse -Force -ErrorAction SilentlyContinue
    }
    $localData = Join-Path $env:LOCALAPPDATA "WindowsMaintenanceCenter"
    if (Test-Path $localData) {
        Remove-Item $localData -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if (-not $Silent) {
    [System.Windows.Forms.MessageBox]::Show(
        "$AppName wurde erfolgreich deinstalliert.",
        "Deinstallation abgeschlossen",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information
    )
}

# Delete remaining program files via background task after process exits
Start-Process -FilePath "cmd.exe" -ArgumentList "/c timeout /t 2 >nul & rmdir /s /q `"$InstallDir`"" -WindowStyle Hidden

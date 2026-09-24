# Windows Maintenance Center - Standard Windows Installer
param(
    [string]$InstallDir = "",
    [switch]$DesktopShortcut = $true,
    [switch]$StartMenuShortcut = $true,
    [switch]$RegisterUninstall = $true,
    [switch]$Silent = $false
)

$ErrorActionPreference = "Stop"
$AppName = "Windows Maintenance Center"
$AppVersion = "1.0.0"
$AppPublisher = "AOWDGENESIS"
$SourceRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

# Determine default installation directory
$IsAdmin = $false
try {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
    $IsAdmin = $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
} catch {
    $IsAdmin = $false
}
if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    if ($IsAdmin) {
        $InstallDir = Join-Path $env:ProgramFiles "Windows Maintenance Center"
    } else {
        $InstallDir = Join-Path $env:LOCALAPPDATA "Programs\Windows Maintenance Center"
    }
}

function Install-ApplicationCore {
    param([string]$TargetDir)

    if (-not (Test-Path $TargetDir)) {
        New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
    }

    $excludeFolders = @(".git", "TestResults", "bin", "obj")
    Get-ChildItem -Path $SourceRoot | ForEach-Object {
        if ($excludeFolders -notcontains $_.Name) {
            Copy-Item -Path $_.FullName -Destination $TargetDir -Recurse -Force
        }
    }

    # Generate the main application launcher batch script in TargetDir
    $launcherPath = Join-Path $TargetDir "WindowsMaintenanceCenter.cmd"
    $launcherContent = @"
@echo off
setlocal
cd /d "%~dp0"
title Windows Maintenance Center
where pwsh.exe >nul 2>nul
if %errorlevel% equ 0 (
    set "PS=pwsh.exe"
) else (
    set "PS=powershell.exe"
)
%PS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build.ps1" -Run
if %errorlevel% neq 0 (
    pause
)
"@
    Set-Content -Path $launcherPath -Value $launcherContent -Encoding ASCII

    # Create uninstaller script in target directory
    $uninstScript = Join-Path $TargetDir "Uninstall.cmd"
    $uninstContent = @"
@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\Uninstall-WMC.ps1"
"@
    Set-Content -Path $uninstScript -Value $uninstContent -Encoding ASCII

    # Create Shortcuts
    $wsh = New-Object -ComObject WScript.Shell

    if ($DesktopShortcut) {
        $desktopPath = [System.Environment]::GetFolderPath("Desktop")
        $scDesktop = $wsh.CreateShortcut((Join-Path $desktopPath "$AppName.lnk"))
        $scDesktop.TargetPath = $launcherPath
        $scDesktop.WorkingDirectory = $TargetDir
        $scDesktop.Description = "$AppName v$AppVersion"
        $scDesktop.Save()
    }

    if ($StartMenuShortcut) {
        $programsPath = if ($IsAdmin) {
            [System.Environment]::GetFolderPath("CommonPrograms")
        } else {
            [System.Environment]::GetFolderPath("Programs")
        }
        $appGroup = Join-Path $programsPath "Windows Maintenance Center"
        if (-not (Test-Path $appGroup)) {
            New-Item -ItemType Directory -Path $appGroup -Force | Out-Null
        }

        $scMenu = $wsh.CreateShortcut((Join-Path $appGroup "$AppName.lnk"))
        $scMenu.TargetPath = $launcherPath
        $scMenu.WorkingDirectory = $TargetDir
        $scMenu.Description = "$AppName v$AppVersion"
        $scMenu.Save()

        $scUninst = $wsh.CreateShortcut((Join-Path $appGroup "Uninstall $AppName.lnk"))
        $scUninst.TargetPath = $uninstScript
        $scUninst.WorkingDirectory = $TargetDir
        $scUninst.Description = "Uninstall $AppName"
        $scUninst.Save()
    }

    # Register in Windows Add/Remove Programs (Registry)
    if ($RegisterUninstall) {
        $regBase = if ($IsAdmin) {
            "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
        } else {
            "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall"
        }
        $regKey = Join-Path $regBase "WindowsMaintenanceCenter"
        if (-not (Test-Path $regKey)) {
            New-Item -Path $regKey -Force | Out-Null
        }

        Set-ItemProperty -Path $regKey -Name "DisplayName" -Value $AppName
        Set-ItemProperty -Path $regKey -Name "DisplayVersion" -Value $AppVersion
        Set-ItemProperty -Path $regKey -Name "Publisher" -Value $AppPublisher
        Set-ItemProperty -Path $regKey -Name "InstallLocation" -Value $TargetDir
        Set-ItemProperty -Path $regKey -Name "UninstallString" -Value "`"$uninstScript`""
        Set-ItemProperty -Path $regKey -Name "NoModify" -Value 1 -Type DWord
        Set-ItemProperty -Path $regKey -Name "NoRepair" -Value 1 -Type DWord
    }
}

if ($Silent) {
    Install-ApplicationCore -TargetDir $InstallDir
    exit 0
}

# WinForms Graphical Installer Interface
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$form = New-Object System.Windows.Forms.Form
$form.Text = "$AppName v$AppVersion - Setup"
$form.Size = New-Object System.Drawing.Size(560, 420)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false
$form.MinimizeBox = $true

# Header panel
$header = New-Object System.Windows.Forms.Panel
$header.Size = New-Object System.Drawing.Size(560, 65)
$header.BackColor = [System.Drawing.Color]::White
$form.Controls.Add($header)

$lblTitle = New-Object System.Windows.Forms.Label
$lblTitle.Text = "Willkommen zum Setup von $AppName"
$lblTitle.Font = New-Object System.Drawing.Font("Segoe UI", 11, [System.Drawing.FontStyle]::Bold)
$lblTitle.Location = New-Object System.Drawing.Point(15, 12)
$lblTitle.Size = New-Object System.Drawing.Size(520, 24)
$header.Controls.Add($lblTitle)

$lblSub = New-Object System.Windows.Forms.Label
$lblSub.Text = "Dieser Assistent installiert $AppName v$AppVersion auf Ihrem Computer."
$lblSub.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$lblSub.Location = New-Object System.Drawing.Point(18, 36)
$lblSub.Size = New-Object System.Drawing.Size(520, 20)
$lblSub.ForeColor = [System.Drawing.Color]::Gray
$header.Controls.Add($lblSub)

# Body controls
$lblDir = New-Object System.Windows.Forms.Label
$lblDir.Text = "Zielverzeichnis:"
$lblDir.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$lblDir.Location = New-Object System.Drawing.Point(20, 85)
$lblDir.Size = New-Object System.Drawing.Size(120, 20)
$form.Controls.Add($lblDir)

$txtDir = New-Object System.Windows.Forms.TextBox
$txtDir.Text = $InstallDir
$txtDir.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$txtDir.Location = New-Object System.Drawing.Point(20, 110)
$txtDir.Size = New-Object System.Drawing.Size(410, 24)
$form.Controls.Add($txtDir)

$btnBrowse = New-Object System.Windows.Forms.Button
$btnBrowse.Text = "Durchsuchen..."
$btnBrowse.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$btnBrowse.Location = New-Object System.Drawing.Point(438, 108)
$btnBrowse.Size = New-Object System.Drawing.Size(95, 27)
$btnBrowse.Add_Click({
    $fbd = New-Object System.Windows.Forms.FolderBrowserDialog
    $fbd.SelectedPath = $txtDir.Text
    if ($fbd.ShowDialog() -eq "OK") {
        $txtDir.Text = $fbd.SelectedPath
    }
})
$form.Controls.Add($btnBrowse)

$chkDesktop = New-Object System.Windows.Forms.CheckBox
$chkDesktop.Text = "Desktop-Verknuepfung erstellen"
$chkDesktop.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$chkDesktop.Checked = $true
$chkDesktop.Location = New-Object System.Drawing.Point(20, 155)
$chkDesktop.Size = New-Object System.Drawing.Size(300, 24)
$form.Controls.Add($chkDesktop)

$chkMenu = New-Object System.Windows.Forms.CheckBox
$chkMenu.Text = "Startmenue-Eintrag erstellen"
$chkMenu.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$chkMenu.Checked = $true
$chkMenu.Location = New-Object System.Drawing.Point(20, 185)
$chkMenu.Size = New-Object System.Drawing.Size(300, 24)
$form.Controls.Add($chkMenu)

$chkReg = New-Object System.Windows.Forms.CheckBox
$chkReg.Text = "In Windows 'Programme und Features' eintragen"
$chkReg.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$chkReg.Checked = $true
$chkReg.Location = New-Object System.Drawing.Point(20, 215)
$chkReg.Size = New-Object System.Drawing.Size(350, 24)
$form.Controls.Add($chkReg)

$lblStatus = New-Object System.Windows.Forms.Label
$lblStatus.Text = "Bereit zur Installation."
$lblStatus.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$lblStatus.Location = New-Object System.Drawing.Point(20, 260)
$lblStatus.Size = New-Object System.Drawing.Size(510, 20)
$form.Controls.Add($lblStatus)

$progressBar = New-Object System.Windows.Forms.ProgressBar
$progressBar.Location = New-Object System.Drawing.Point(20, 285)
$progressBar.Size = New-Object System.Drawing.Size(512, 22)
$progressBar.Visible = $false
$form.Controls.Add($progressBar)

# Footer Buttons
$btnInstall = New-Object System.Windows.Forms.Button
$btnInstall.Text = "Installieren"
$btnInstall.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$btnInstall.Location = New-Object System.Drawing.Point(320, 335)
$btnInstall.Size = New-Object System.Drawing.Size(105, 32)
$form.Controls.Add($btnInstall)

$btnCancel = New-Object System.Windows.Forms.Button
$btnCancel.Text = "Abbrechen"
$btnCancel.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$btnCancel.Location = New-Object System.Drawing.Point(432, 335)
$btnCancel.Size = New-Object System.Drawing.Size(100, 32)
$btnCancel.Add_Click({ $form.Close() })
$form.Controls.Add($btnCancel)

$btnInstall.Add_Click({
    $btnInstall.Enabled = $false
    $btnBrowse.Enabled = $false
    $txtDir.Enabled = $false
    $progressBar.Visible = $true
    $progressBar.Style = "Marquee"
    $lblStatus.Text = "Dateien werden kopiert und eingerichtet..."
    $form.Refresh()

    try {
        $DesktopShortcut = $chkDesktop.Checked
        $StartMenuShortcut = $chkMenu.Checked
        $RegisterUninstall = $chkReg.Checked
        Install-ApplicationCore -TargetDir $txtDir.Text

        $progressBar.Style = "Blocks"
        $progressBar.Value = 100
        $lblStatus.Text = "Installation erfolgreich abgeschlossen!"
        $lblStatus.ForeColor = [System.Drawing.Color]::DarkGreen
        $btnInstall.Visible = $false
        $btnCancel.Text = "Fertigstellen"
        $btnCancel.Focus()
        [System.Windows.Forms.MessageBox]::Show("Windows Maintenance Center wurde erfolgreich installiert!", "Setup abgeschlossen", "OK", "Information")
    } catch {
        $lblStatus.Text = "Fehler bei der Installation: $($_.Exception.Message)"
        $lblStatus.ForeColor = [System.Drawing.Color]::Red
        $btnInstall.Enabled = $true
    }
})

$form.ShowDialog() | Out-Null

# Envoy Windows Installer
# Self-contained installer using PowerShell

param(
    [string]$InstallPath = "$env:LOCALAPPDATA\Envoy",
    [switch]$CreateDesktopShortcut = $true,
    [switch]$CreateStartMenuShortcut = $true,
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

# Single-source the version from Directory.Build.props (like publish/build-release).
if (-not $Version) {
    $propsPath = Join-Path $PSScriptRoot "Directory.Build.props"
    if (Test-Path $propsPath) { $Version = ([xml](Get-Content $propsPath -Raw)).Project.PropertyGroup.Version }
    if (-not $Version) { $Version = "1.0.0" }
}

Write-Host "Envoy Installer v$Version" -ForegroundColor Cyan
Write-Host ""

# Check if running as admin
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($isAdmin) {
    Write-Host "Running with administrator privileges." -ForegroundColor Yellow
}

# Determine source path
$ScriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$SourcePath = Join-Path $ScriptPath "publish"

if (-not (Test-Path $SourcePath)) {
    $ZipPath = Join-Path $ScriptPath "Envoy-v$Version-win-x64.zip"
    if (Test-Path $ZipPath) {
        Write-Host "Extracting ZIP package..." -ForegroundColor Yellow
        $TempExtract = Join-Path $env:TEMP "EnvoyExtract_$([Guid]::NewGuid().ToString())"
        Expand-Archive -Path $ZipPath -DestinationPath $TempExtract -Force
        $SourcePath = Join-Path $TempExtract "publish"
        if (-not (Test-Path $SourcePath)) {
            $SourcePath = $TempExtract
        }
    } else {
        Write-Error "Cannot find publish folder or ZIP package."
        exit 1
    }
}

# Create install directory
Write-Host "Installing to: $InstallPath" -ForegroundColor Yellow
if (Test-Path $InstallPath) {
    Write-Host "Removing previous installation..." -ForegroundColor Gray
    Remove-Item -Path $InstallPath -Recurse -Force
}
New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null

# Copy files
Write-Host "Copying files..." -ForegroundColor Yellow
Copy-Item -Path "$SourcePath\*" -Destination $InstallPath -Recurse -Force

# Create shortcuts
if ($CreateDesktopShortcut) {
    Write-Host "Creating desktop shortcut..." -ForegroundColor Yellow
    $WshShell = New-Object -ComObject WScript.Shell
    $Shortcut = $WshShell.CreateShortcut("$env:USERPROFILE\Desktop\Envoy.lnk")
    $Shortcut.TargetPath = "$InstallPath\Envoy.exe"
    $Shortcut.WorkingDirectory = $InstallPath
    $Shortcut.IconLocation = "$InstallPath\Envoy.exe,0"
    $Shortcut.Description = "Envoy - Job Application Agent"
    $Shortcut.Save()
    [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($WshShell)
}

if ($CreateStartMenuShortcut) {
    Write-Host "Creating Start Menu shortcut..." -ForegroundColor Yellow
    $StartMenuPath = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Envoy"
    New-Item -ItemType Directory -Path $StartMenuPath -Force | Out-Null
    
    $WshShell = New-Object -ComObject WScript.Shell
    $Shortcut = $WshShell.CreateShortcut("$StartMenuPath\Envoy.lnk")
    $Shortcut.TargetPath = "$InstallPath\Envoy.exe"
    $Shortcut.WorkingDirectory = $InstallPath
    $Shortcut.IconLocation = "$InstallPath\Envoy.exe,0"
    $Shortcut.Description = "Envoy - Job Application Agent"
    $Shortcut.Save()
    [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($WshShell)
}

# Create uninstall registry entry
Write-Host "Registering uninstall information..." -ForegroundColor Yellow
$UninstallRegPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Envoy"
if (Test-Path $UninstallRegPath) {
    Remove-Item -Path $UninstallRegPath -Recurse -Force
}
New-Item -Path $UninstallRegPath -Force | Out-Null
Set-ItemProperty -Path $UninstallRegPath -Name "DisplayName" -Value "Envoy"
Set-ItemProperty -Path $UninstallRegPath -Name "DisplayVersion" -Value $Version
Set-ItemProperty -Path $UninstallRegPath -Name "Publisher" -Value "LXB Studio LLC"
Set-ItemProperty -Path $UninstallRegPath -Name "InstallLocation" -Value $InstallPath
Set-ItemProperty -Path $UninstallRegPath -Name "DisplayIcon" -Value "$InstallPath\Envoy.exe,0"
Set-ItemProperty -Path $UninstallRegPath -Name "NoModify" -Value 1 -Type DWord
Set-ItemProperty -Path $UninstallRegPath -Name "NoRepair" -Value 1 -Type DWord

# Cleanup — remove only our own extract dir, never its parent (%TEMP%)
if ($TempExtract -and (Test-Path $TempExtract)) {
    Remove-Item -Path $TempExtract -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Installation complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Envoy has been installed to: $InstallPath" -ForegroundColor Cyan
Write-Host ""
Write-Host "Quick Start:" -ForegroundColor Yellow
Write-Host "  1. Install Ollama (optional, for local models): https://ollama.com/download" -ForegroundColor White
Write-Host "  2. Pull a model: ollama pull qwen2.5-coder:14b" -ForegroundColor White
Write-Host "  3. Launch Envoy from your desktop or Start Menu" -ForegroundColor White
Write-Host ""

# Ask to launch
$launch = Read-Host "Launch Envoy now? (Y/N)"
if ($launch -eq 'Y' -or $launch -eq 'y') {
    Start-Process -FilePath "$InstallPath\Envoy.exe"
}

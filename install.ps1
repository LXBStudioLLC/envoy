# Envoy Windows Installer
# Self-contained installer using PowerShell

param(
    [string]$InstallPath = "$env:LOCALAPPDATA\Envoy",
    [switch]$CreateDesktopShortcut = $true,
    [switch]$CreateStartMenuShortcut = $true
)

$ErrorActionPreference = "Stop"

Write-Host "Envoy Installer v1.0.0" -ForegroundColor Cyan
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
    $ZipPath = Join-Path $ScriptPath "Envoy-v1.0.0-win-x64.zip"
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
    $Shortcut.TargetPath = "$InstallPath\Envoy.UI.exe"
    $Shortcut.WorkingDirectory = $InstallPath
    $Shortcut.IconLocation = "$InstallPath\Envoy.UI.exe,0"
    $Shortcut.Description = "Envoy - Job Application Agent"
    $Shortcut.Save()
}

if ($CreateStartMenuShortcut) {
    Write-Host "Creating Start Menu shortcut..." -ForegroundColor Yellow
    $StartMenuPath = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Envoy"
    New-Item -ItemType Directory -Path $StartMenuPath -Force | Out-Null
    
    $WshShell = New-Object -ComObject WScript.Shell
    $Shortcut = $WshShell.CreateShortcut("$StartMenuPath\Envoy.lnk")
    $Shortcut.TargetPath = "$InstallPath\Envoy.UI.exe"
    $Shortcut.WorkingDirectory = $InstallPath
    $Shortcut.IconLocation = "$InstallPath\Envoy.UI.exe,0"
    $Shortcut.Description = "Envoy - Job Application Agent"
    $Shortcut.Save()
}

# Create uninstall registry entry
Write-Host "Registering uninstall information..." -ForegroundColor Yellow
$UninstallRegPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Envoy"
if (Test-Path $UninstallRegPath) {
    Remove-Item -Path $UninstallRegPath -Recurse -Force
}
New-Item -Path $UninstallRegPath -Force | Out-Null
Set-ItemProperty -Path $UninstallRegPath -Name "DisplayName" -Value "Envoy"
Set-ItemProperty -Path $UninstallRegPath -Name "DisplayVersion" -Value "1.0.0"
Set-ItemProperty -Path $UninstallRegPath -Name "Publisher" -Value "Envoy Project"
Set-ItemProperty -Path $UninstallRegPath -Name "InstallLocation" -Value $InstallPath
Set-ItemProperty -Path $UninstallRegPath -Name "DisplayIcon" -Value "$InstallPath\Envoy.UI.exe,0"
Set-ItemProperty -Path $UninstallRegPath -Name "NoModify" -Value 1 -Type DWord
Set-ItemProperty -Path $UninstallRegPath -Name "NoRepair" -Value 1 -Type DWord

# Cleanup
if ($TempExtract) {
    Remove-Item -Path (Split-Path -Parent $TempExtract) -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Installation complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Envoy has been installed to: $InstallPath" -ForegroundColor Cyan
Write-Host ""
Write-Host "Quick Start:" -ForegroundColor Yellow
Write-Host "  1. Install Ollama: https://ollama.com/download" -ForegroundColor White
Write-Host "  2. Pull a model: ollama pull qwen2.5-coder:14b" -ForegroundColor White
Write-Host "  3. Launch Envoy from your desktop or Start Menu" -ForegroundColor White
Write-Host "  4. Open http://localhost:5000 in your browser" -ForegroundColor White
Write-Host ""

# Ask to launch
$launch = Read-Host "Launch Envoy now? (Y/N)"
if ($launch -eq 'Y' -or $launch -eq 'y') {
    Start-Process -FilePath "$InstallPath\Envoy.UI.exe" -ArgumentList "--urls", "http://localhost:5000"
    Start-Sleep 2
    Start-Process "http://localhost:5000"
}

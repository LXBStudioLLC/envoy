# Envoy Release Build Script
# One command: publish (optionally signed) -> build installer -> sign installer -> checksums.
#
# Signing is opt-in and path-configurable so the public repo carries no machine-specific
# paths or secrets. Pass the two signing helpers (or set the env vars):
#   -SignScript      / $env:LXB_SIGN_SCRIPT       signs the publish directory (multi-file)
#   -SignFileScript  / $env:LXB_SIGN_FILE_SCRIPT  signs a single file (the installer)
#
# Examples:
#   .\build-release.ps1                                  # unsigned local build
#   .\build-release.ps1 -Sign `
#       -SignScript      C:\tools\Sign-Release.ps1 `
#       -SignFileScript  C:\tools\Sign-File.ps1

param(
    [string]$Version        = "1.0.0",
    [string]$Configuration  = "Release",
    [string]$Runtime        = "win-x64",
    [string]$OutputPath     = "artifacts",
    [switch]$Sign,
    [string]$SignScript     = $env:LXB_SIGN_SCRIPT,
    [string]$SignFileScript = $env:LXB_SIGN_FILE_SCRIPT,
    [string]$IsccPath       = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    [string]$ProductName    = "Envoy",
    [string]$ProductUrl     = "https://github.com/LXBStudioLLC/envoy"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Push-Location $ScriptDir
try {
    Write-Host "Envoy Release Build (v$Version)" -ForegroundColor Cyan
    Write-Host ""

    # Validate signing prerequisites up front so we fail before the long publish.
    if ($Sign) {
        if (-not $SignScript)     { Write-Error "-Sign requires -SignScript (or `$env:LXB_SIGN_SCRIPT) to sign the publish directory."; exit 1 }
        if (-not $SignFileScript) { Write-Error "-Sign requires -SignFileScript (or `$env:LXB_SIGN_FILE_SCRIPT) to sign the installer."; exit 1 }
    }
    if (-not (Test-Path $IsccPath)) {
        Write-Error "Inno Setup compiler not found: $IsccPath. Install Inno Setup 6 or pass -IsccPath."
        exit 1
    }

    # 1) Publish + (sign binaries) + zip. publish.ps1 cleans $OutputPath first.
    Write-Host "[1/4] Publishing application..." -ForegroundColor Yellow
    $publishArgs = @{
        Version       = $Version
        Configuration = $Configuration
        Runtime       = $Runtime
        OutputPath    = $OutputPath
        ProductName   = $ProductName
        ProductUrl    = $ProductUrl
    }
    if ($Sign) { $publishArgs.Sign = $true; $publishArgs.SignScript = $SignScript }
    & "$ScriptDir\publish.ps1" @publishArgs
    if ($LASTEXITCODE -ne 0) { Write-Error "publish.ps1 failed."; exit 1 }

    # 2) Build the installer from the (signed) publish output.
    Write-Host "[2/4] Building installer..." -ForegroundColor Yellow
    $versionInfo = if ($Version -match '^\d+\.\d+\.\d+$') { "$Version.0" } else { "1.0.0.0" }
    & $IsccPath "/DMyAppVersion=$Version" "/DMyAppVersionInfo=$versionInfo" "setup.iss"
    if ($LASTEXITCODE -ne 0) { Write-Error "Inno Setup compile failed."; exit 1 }

    $installer = Join-Path $OutputPath "Envoy-v$Version-setup.exe"
    if (-not (Test-Path $installer)) { Write-Error "Installer was not produced: $installer"; exit 1 }

    # 3) Sign the installer (single file).
    if ($Sign) {
        Write-Host "[3/4] Signing installer..." -ForegroundColor Yellow
        & $SignFileScript -FilePath (Resolve-Path $installer).Path -ProductName $ProductName -ProductUrl $ProductUrl
        if ($LASTEXITCODE -ne 0) { Write-Error "Installer signing failed."; exit 1 }
    } else {
        Write-Host "[3/4] Skipping signing (-Sign not set)." -ForegroundColor DarkGray
    }

    # 4) Checksums for the distributable artifacts.
    Write-Host "[4/4] Writing checksums..." -ForegroundColor Yellow
    Push-Location $OutputPath
    try {
        $names = @("Envoy-v$Version-win-x64.zip", "Envoy-v$Version-setup.exe") | Where-Object { Test-Path $_ }
        $lines = $names | ForEach-Object { (Get-FileHash $_ -Algorithm SHA256).Hash.ToLower() + "  " + $_ }
        $lines | Out-File -FilePath "SHA256SUMS.txt" -Encoding ascii
        Get-Content "SHA256SUMS.txt"
    } finally { Pop-Location }

    Write-Host ""
    Write-Host "Release build complete." -ForegroundColor Green
    Write-Host "  ZIP:       $(Join-Path $OutputPath "Envoy-v$Version-win-x64.zip")"
    Write-Host "  Installer: $installer"
    Write-Host "  Checksums: $(Join-Path $OutputPath 'SHA256SUMS.txt')"
    if (-not $Sign) {
        Write-Host ""
        Write-Host "  NOTE: build is UNSIGNED. Re-run with -Sign and the signing scripts to sign." -ForegroundColor Yellow
    }
}
finally {
    Pop-Location
}

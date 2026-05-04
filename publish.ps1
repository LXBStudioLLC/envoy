# Envoy Publish Script
# Publishes self-contained .NET app for distribution

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained = $true,
    [string]$OutputPath = "artifacts"
)

$ErrorActionPreference = "Stop"

Write-Host "Envoy Publish Script" -ForegroundColor Cyan
Write-Host ""

# Paths
$ProjectPath = "src/Envoy.UI/Envoy.UI.csproj"
$TemplatesPath = "src/Envoy.Templates"
$DocsPath = "docs"
$DistPath = $OutputPath
$PublishPath = "$DistPath/publish"

# Clean previous builds
Write-Host "Cleaning previous builds..." -ForegroundColor Yellow
if (Test-Path $DistPath) {
    Remove-Item -Path $DistPath -Recurse -Force
}
New-Item -ItemType Directory -Path $DistPath -Force | Out-Null
New-Item -ItemType Directory -Path $PublishPath -Force | Out-Null

# Publish .NET app
Write-Host "Publishing .NET application..." -ForegroundColor Yellow
$selfContainedValue = if ($SelfContained) { "true" } else { "false" }
$publishArgs = @(
    "publish", $ProjectPath,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", $selfContainedValue,
    "-p:PublishSingleFile=true",
    "-p:PublishTrimmed=false",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-o", $PublishPath
)

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed!"
    exit 1
}

# Copy templates
Write-Host "Copying templates..." -ForegroundColor Yellow
$TemplatesDest = "$PublishPath/Templates"
New-Item -ItemType Directory -Path $TemplatesDest -Force | Out-Null
Copy-Item -Path "$TemplatesPath/*.json" -Destination $TemplatesDest -Force

# Copy docs
Write-Host "Copying documentation..." -ForegroundColor Yellow
$DocsDest = "$PublishPath/docs"
New-Item -ItemType Directory -Path $DocsDest -Force | Out-Null
Copy-Item -Path "$DocsPath/*" -Destination $DocsDest -Force -Recurse

# Create start script
Write-Host "Creating launcher script..." -ForegroundColor Yellow
$StartScriptContent = "@echo off`nchcp 65001 >nul`necho.`necho  Envoy Job Application Agent`necho.`necho  Starting Envoy...`necho  Open your browser to: http://localhost:5000`necho.`n`n:: Check if Ollama is running`ncurl -s http://localhost:11434 >nul 2>&1`nif errorlevel 1 (`n    echo  WARNING: Ollama is not detected!`n    echo  Please ensure Ollama is installed and running.`n    echo  Download: https://ollama.com/download`n    echo.`n)`n`n:: Start Envoy`nstart /b `"`" `"%~dp0Envoy.UI.exe`" --urls `"http://localhost:5000`"`necho  Envoy started successfully!`necho.`npause"

$StartScriptContent | Out-File -FilePath "$PublishPath/Start Envoy.bat" -Encoding UTF8

# Create README
Write-Host "Creating package README..." -ForegroundColor Yellow
$PackageReadmeContent = @"
Envoy - Quick Start
===================

Requirements
------------
- Windows 10/11 (64-bit)
- Ollama (https://ollama.com/download)
- Google Chrome or Microsoft Edge

Installation
------------
1. Extract this folder to any location (e.g., C:\Tools\Envoy)
2. Install Ollama and pull a model: ollama pull qwen2.5-coder:14b
3. Run Start Envoy.bat
4. Open http://localhost:5000 in your browser

First Time Setup
----------------
1. Drop your resume PDF on the Dashboard
2. Ensure Chrome is running (Envoy will prompt if not)
3. Paste a job URL and click Initiate Sequence

Support
-------
- GitHub: https://github.com/yourname/Envoy

License
-------
AGPLv3 - Your data stays on your machine.
"@

$PackageReadmeContent | Out-File -FilePath "$PublishPath/README.txt" -Encoding UTF8

# Create ZIP package
Write-Host "Creating ZIP package..." -ForegroundColor Yellow
$ZipPath = "$DistPath/Envoy-v1.0.0-win-x64.zip"
Compress-Archive -Path "$PublishPath/*" -DestinationPath $ZipPath -Force

Write-Host ""
Write-Host "Publish complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Output: $DistPath" -ForegroundColor Cyan
Write-Host "  - ZIP: $ZipPath" -ForegroundColor White
Write-Host "  - Folder: $PublishPath" -ForegroundColor White
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Run Inno Setup on setup.iss to create installer" -ForegroundColor Gray
Write-Host "  2. Or distribute the ZIP directly" -ForegroundColor Gray

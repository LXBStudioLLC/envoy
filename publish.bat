@echo off
echo Starting Envoy publish process...
echo.

:: Check if PowerShell is available
where powershell >nul 2>nul
if errorlevel 1 (
    echo ERROR: PowerShell is required to run the publish script.
    exit /b 1
)

:: Run the publish script
powershell -ExecutionPolicy Bypass -File "%~dp0publish.ps1" %*

if errorlevel 1 (
    echo.
    echo Publish failed! Check the error messages above.
    pause
    exit /b 1
)

echo.
echo Press any key to exit...
pause >nul

@echo off
echo.
echo  ╔═══════════════════════════════════════════╗
echo  ║         ENVOY INSTALLER v1.0.0           ║
echo  ╚═══════════════════════════════════════════╝
echo.

:: Check if running as admin
echo  Checking privileges...
net session >nul 2>&1
if %errorlevel% == 0 (
    echo  Running with administrator privileges.
) else (
    echo  Running without administrator privileges.
    echo  This is fine for a per-user install.
)
echo.

:: Run PowerShell installer
echo  Starting installation...
echo.
powershell -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*

echo.
echo  Press any key to exit...
pause >nul

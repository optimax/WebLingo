@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "script=%~dp0uninstall.ps1"
if not exist "%script%" set "script=%~dp0WebLingo\uninstall.ps1"
if not exist "%script%" (
    echo Extract the WebLingo ZIP first, then double-click Uninstall WebLingo in the extracted folder.
    pause
    exit /b 1
)
rem Release this batch file before running PowerShell so it can remove the launcher and its folder.
(goto) 2>nul & (
    cd /d "%SystemRoot%" || exit /b 1
    "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%script%"
    if errorlevel 1 (
        pause
        "%SystemRoot%\System32\cmd.exe" /d /c exit 1
    ) else (
        pause
        "%SystemRoot%\System32\cmd.exe" /d /c exit 0
    )
)

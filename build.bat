@echo off
setlocal EnableExtensions DisableDelayedExpansion
if /i "%~1"=="/install" goto installOnly
set "buildExit=1"
pushd "%~dp0WebLingo" || goto finish

echo Publishing WebLingo with the .NET 10 SDK...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\publish.ps1"
set "buildExit=%errorlevel%"
if not "%buildExit%"=="0" goto leave

echo Updating the local WebLingo installation...
fltmc >nul 2>&1
if errorlevel 1 goto elevate
call :install
goto installed

:elevate
echo Approve the Windows administrator prompt to install the published files.
set "webLingoBatch=%~f0"
powershell.exe -NoProfile -Command ^
  "$ErrorActionPreference = 'Stop';" ^
  "$installer = Start-Process -FilePath $env:webLingoBatch -ArgumentList '/install' -Verb RunAs -Wait -PassThru;" ^
  "exit $installer.ExitCode"

:installed
set "buildExit=%errorlevel%"
if "%buildExit%"=="0" echo WebLingo installed. VS Code can use weblingo from the installation folder.

:leave
popd
:finish
if not "%buildExit%"=="0" echo Build or installation failed with exit code %buildExit%.
pause
exit /b %buildExit%

:installOnly
call :install
set "installExit=%errorlevel%"
if not "%installExit%"=="0" pause
exit /b %installExit%

:install
pushd "%~dp0Scripts" || exit /b 1
if exist "%SystemRoot%\System32\inetsrv\appcmd.exe" (
    ..\WebLingo\publish\WebLingo\weblingo.exe run install-weblingo.wlg
) else (
    ..\WebLingo\publish\WebLingo\weblingo.exe run install.wlg source=..\WebLingo\publish\WebLingo
)
set "installExit=%errorlevel%"
popd
exit /b %installExit%

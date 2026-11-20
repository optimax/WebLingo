@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "setupExit=1"
pushd "%~dp0" || goto finish
if not exist "WebLingo\weblingo.exe" goto incomplete
if not exist "setup.wlg" goto incomplete
"WebLingo\weblingo.exe" run "setup.wlg"
set "setupExit=%errorlevel%"
goto leave
:incomplete
echo Right-click the downloaded ZIP, choose Extract All, then open Install WebLingo in the extracted folder.
:leave
popd
:finish
if not "%setupExit%"=="0" echo Setup did not finish. See the message above.
pause
exit /b %setupExit%

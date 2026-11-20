@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "buildExit=1"
pushd "%~dp0WebLingo" || goto finish

echo Building the WebLingo debugger with the .NET 10 SDK...
dotnet build "src\WebLingo.Cli\WebLingo.Cli.csproj" -c Release
set "buildExit=%errorlevel%"
if not "%buildExit%"=="0" goto leave

echo(
echo In VS Code User Settings, set WebLingo: Debugger Path to the following file.
for %%F in ("src\WebLingo.Cli\bin\Release\net10.0\weblingo.exe") do echo %%~fF

:leave
popd
:finish
if not "%buildExit%"=="0" echo Build failed. See the message above.
pause
exit /b %buildExit%

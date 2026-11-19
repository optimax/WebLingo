# Publishes WebLingo for a server, into one folder that the server installs once for all its sites, as it installs PHP:
# wasp, WASP's host, which IIS's sites run, weblingo, the scripts' runner, their libraries, and the standard modules, std.
# A Windows server's folder is C:\Program Files\WebLingo, which a site's web.config names (Examples.wasp/README.md).
# Run it on a machine with the .NET 10 SDK, on Windows for a Windows server. -Destination names the folder, publish/WebLingo
# in this folder by default; it may be C:\Program Files\WebLingo itself, from an elevated PowerShell, once the sites that
# run wasp from it are stopped, since a running wasp keeps its files in use.
param([string] $Destination = (Join-Path $PSScriptRoot 'publish/WebLingo'))
$ErrorActionPreference = 'Stop'
# wasp's project references weblingo's, so that its output holds both command lines.
$project = Join-Path $PSScriptRoot 'src/WebLingo.Wasp.Cli/WebLingo.Wasp.Cli.csproj'
dotnet publish $project -c Release -o $Destination
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Published WebLingo to $Destination"

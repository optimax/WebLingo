# Publishes wasp, WASP's host, into this example's wasp folder, so that this folder holds everything an IIS server needs
# to run the example: site\, web.config and wasp\. Run it from anywhere, on a machine with the .NET 10 SDK.
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\..\WebLingo\src\WebLingo.Wasp.Cli\WebLingo.Wasp.Cli.csproj'
dotnet publish $project -c Release -o (Join-Path $PSScriptRoot 'wasp')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Published wasp to $(Join-Path $PSScriptRoot 'wasp')"

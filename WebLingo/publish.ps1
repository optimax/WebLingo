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
$profile = Join-Path $PSScriptRoot 'Runtime.pubxml'
$extras = [IO.Path]::GetTempFileName()
try {
    dotnet publish $project -c Release -o $Destination `
        "-p:PublishProfileFullPath=$profile" "-p:WebLingoPublishExtrasFile=$extras"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    # Remove only files the publish profile excluded from this project's output. Keep other files in existing folders.
    $root = (Get-Item -LiteralPath $Destination).FullName
    foreach ($relative in Get-Content -LiteralPath $extras -Encoding UTF8) {
        $file = Join-Path $root $relative
        $parent = Split-Path -LiteralPath $file
        if (![IO.Directory]::Exists($parent)) { continue }
        if ((Get-Item -LiteralPath $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
        if (Test-Path -LiteralPath $file -PathType Leaf) { Remove-Item -LiteralPath $file }
        if ($parent -ne $root -and [IO.Directory]::GetFileSystemEntries($parent).Length -eq 0) {
            [IO.Directory]::Delete($parent)
        }
    }
} finally {
    Remove-Item -LiteralPath $extras
}
Write-Host "Published WebLingo to $Destination"

# Makes WebLingo's installers for Windows, on x64: two zips of weblingo and the standard modules, each with install.wl,
# which installs them in C:\Program Files\WebLingo and puts that folder on the machine's PATH, and readme.md, which says
# how, for a human:
#   WebLingo-<version>-win-x64-self-contained.zip, which holds .NET's runtime too, for a machine without it;
#   WebLingo-<version>-win-x64-framework-dependent.zip, for a machine with .NET 10's runtime installed.
# In each, the folder WebLingo is what install.wl installs, beside install.wl, windows.wl and weblingo.config, from the
# repository's Scripts folder, and readme.md, from this one. Run it on a machine with the .NET 10 SDK, from any folder:
#   powershell -ExecutionPolicy Bypass -File Installers\Windows\package.ps1
# -Destination names the folder the zips go to, WebLingo/publish/installers in the repository by default.
param([string] $Destination = (Join-Path $PSScriptRoot '../../WebLingo/publish/installers'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$project = Join-Path $repository 'WebLingo/src/WebLingo.Cli/WebLingo.Cli.csproj'
# The solution's one version, which every assembly carries (Directory.Build.props).
$props = [xml](Get-Content (Join-Path $repository 'WebLingo/Directory.Build.props'))
$version = @($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
New-Item -ItemType Directory -Force $Destination | Out-Null
$Destination = (Resolve-Path $Destination).Path
$staging = Join-Path ([IO.Path]::GetTempPath()) ('weblingo-installers-' + [Guid]::NewGuid().ToString('N'))
try {
    foreach ($kind in 'self-contained', 'framework-dependent') {
        $name = "WebLingo-$version-win-x64-$kind"
        $root = Join-Path $staging $name
        $runtime = Join-Path $root 'WebLingo'
        $selfContained = if ($kind -eq 'self-contained') { 'true' } else { 'false' }
        dotnet publish $project -c Release -r win-x64 --self-contained $selfContained -o $runtime
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        # Only what runs: no symbols, nor SqlClient's messages in other languages, each a folder of resources alone.
        Get-ChildItem $runtime -Filter *.pdb -Recurse | Remove-Item
        Get-ChildItem $runtime -Directory |
            Where-Object { -not (Get-ChildItem $_.FullName -File -Recurse | Where-Object { $_.Name -notlike '*.resources.dll' }) } |
            Remove-Item -Recurse
        foreach ($script in 'install.wl', 'windows.wl', 'weblingo.config') {
            Copy-Item (Join-Path $repository "Scripts/$script") $root
        }
        Copy-Item (Join-Path $PSScriptRoot 'readme.md') $root
        # The zip holds the folder's files, not the folder, so that Explorer's Extract All makes one folder, not two.
        $zip = Join-Path $Destination "$name.zip"
        if (Test-Path $zip) { Remove-Item $zip }
        [IO.Compression.ZipFile]::CreateFromDirectory($root, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
        Write-Host ("Made {0}, {1:N1} MB" -f $zip, ((Get-Item $zip).Length / 1MB))
    }
}
finally {
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
}

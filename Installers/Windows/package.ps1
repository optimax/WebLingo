# Makes WebLingo's and WASP's installers for Windows, into zips, six of each: for x64; for x86, 32-bit Windows, or 64-bit
# Windows's 32-bit programs, such as a site that uses a COM component made for 32 bits; and for arm64, Windows on ARM:
#   WebLingo-<version>-win-<architecture>-self-contained.zip, which holds .NET's runtime too, for a machine without it;
#   WebLingo-<version>-win-<architecture>-framework-dependent.zip, for a machine with .NET 10's runtime installed;
#   WASP-<version>-win-<architecture>-self-contained.zip and WASP-<version>-win-<architecture>-framework-dependent.zip,
#   for a machine with WebLingo installed from the zip of the same version, architecture and kind, made by the same run
#   of this script.
# WebLingo's hold the folder WebLingo, weblingo and the standard modules, which install.wlg installs in WebLingo's
# folder, C:\Program Files\WebLingo, or for x86 on 64-bit Windows C:\Program Files (x86)\WebLingo, and puts on the
# machine's PATH; WASP's the folder WASP, what wasp adds to them, which iis-install.wlg installs there, once IIS has what
# it needs to run wasp, with wasp-needs.json, the SHA-256 of each file of WebLingo's that wasp needs, as this run made
# it, and add-wasp-site.wlg, to add a site. The scripts are the repository's, from its Scripts folder, and readme.md, a
# human's steps, this folder's readme.md and wasp-readme.md.
# Run it on a machine with the .NET 10 SDK, from any folder:
#   powershell -ExecutionPolicy Bypass -File Installers\Windows\package.ps1
# -Destination names the folder the zips go to, the repository's Installers folder by default, which git ignores them in.
param([string] $Destination = (Join-Path $PSScriptRoot '..'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$solution = Join-Path $repository 'WebLingo'
# The solution's one version, which every assembly carries (Directory.Build.props).
$props = [xml](Get-Content (Join-Path $solution 'Directory.Build.props'))
$version = @($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
New-Item -ItemType Directory -Force $Destination | Out-Null
$Destination = (Resolve-Path $Destination).Path
$staging = Join-Path ([IO.Path]::GetTempPath()) ('weblingo-installers-' + [Guid]::NewGuid().ToString('N'))

# A project published for Windows on an architecture, keeping only what runs: no symbols, nor SqlClient's messages in
# other languages, each a folder of resources alone.
function Publish-Project([string] $project, [string] $architecture, [string] $kind, [string] $folder) {
    $selfContained = if ($kind -eq 'self-contained') { 'true' } else { 'false' }
    dotnet publish (Join-Path $solution $project) -c Release -r "win-$architecture" --self-contained $selfContained -o $folder
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Get-ChildItem $folder -Filter *.pdb -Recurse | Remove-Item
    Get-ChildItem $folder -Directory |
        Where-Object { -not (Get-ChildItem $_.FullName -File -Recurse | Where-Object { $_.Name -notlike '*.resources.dll' }) } |
        Remove-Item -Recurse
}

# A folder's files, each by its path within it, with forward slashes, and its SHA-256, in lower case.
function Get-Hashes([string] $folder) {
    $hashes = @{}
    foreach ($file in Get-ChildItem $folder -File -Recurse) {
        $path = $file.FullName.Substring($folder.Length + 1).Replace('\', '/')
        $hashes[$path] = (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    return $hashes
}

# The folder's files zipped, not the folder, so that Explorer's Extract All makes one folder, not two; each entry named
# with forward slashes, as the zip format has them, which Windows PowerShell's ZipFile.CreateFromDirectory does not.
function New-Zip([string] $folder, [string] $name) {
    $zip = Join-Path $Destination "$name.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    $archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem $folder -File -Recurse) {
            $entry = $file.FullName.Substring($folder.Length + 1).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry,
                [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally {
        $archive.Dispose()
    }
    Write-Host ("Made {0}, {1:N1} MB" -f $zip, ((Get-Item $zip).Length / 1MB))
}

try {
    foreach ($architecture in 'x64', 'x86', 'arm64') {
        foreach ($kind in 'self-contained', 'framework-dependent') {
            # WebLingo's: weblingo, the standard modules, and the scripts that install them.
            $name = "WebLingo-$version-win-$architecture-$kind"
            $root = Join-Path $staging $name
            $runtime = Join-Path $root 'WebLingo'
            Publish-Project 'src/WebLingo.Cli/WebLingo.Cli.csproj' $architecture $kind $runtime
            foreach ($script in 'install.wlg', 'add-wlg-file-type.wlg', 'windows.wlg', 'weblingo.config') {
                Copy-Item (Join-Path $repository "Scripts/$script") $root
            }
            Copy-Item (Join-Path $PSScriptRoot 'readme.md') $root
            New-Zip $root $name

            # WASP's: what wasp's own publish has that WebLingo's has not, or has otherwise, such as ASP.NET Core's
            # libraries, newer than the packages weblingo takes; and the hashes of the files of WebLingo's it shares.
            $waspName = "WASP-$version-win-$architecture-$kind"
            $waspRoot = Join-Path $staging $waspName
            $published = Join-Path $staging "wasp-$architecture-$kind"
            Publish-Project 'src/WebLingo.Wasp.Cli/WebLingo.Wasp.Cli.csproj' $architecture $kind $published
            $weblingo = Get-Hashes $runtime
            $wasp = Get-Hashes $published
            $needs = [ordered]@{}
            foreach ($path in $wasp.Keys | Sort-Object) {
                if ($weblingo[$path] -eq $wasp[$path]) {
                    $needs[$path] = $wasp[$path]
                } else {
                    $target = Join-Path (Join-Path $waspRoot 'WASP') $path
                    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
                    Copy-Item (Join-Path $published $path) $target
                }
            }
            $needs | ConvertTo-Json | Set-Content -Encoding ASCII (Join-Path $waspRoot 'wasp-needs.json')
            foreach ($script in 'iis-install.wlg', 'add-wasp-site.wlg', 'windows.wlg', 'iis.wlg', 'weblingo.config') {
                Copy-Item (Join-Path $repository "Scripts/$script") $waspRoot
            }
            Copy-Item (Join-Path $PSScriptRoot 'wasp-readme.md') (Join-Path $waspRoot 'readme.md')
            New-Zip $waspRoot $waspName
        }
    }
}
finally {
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
}

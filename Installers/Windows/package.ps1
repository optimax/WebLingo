# Makes WebLingo's and WASP's installers for Windows, into zips, six of each: for x64; for x86, 32-bit Windows, or 64-bit
# Windows's 32-bit programs, such as a site that uses a COM component made for 32 bits; and for arm64, Windows on ARM:
#   WebLingo-<version>-win-<architecture>-self-contained.zip, which holds .NET's runtime too, for a machine without it;
#   WebLingo-<version>-win-<architecture>-framework-dependent.zip, for a machine with .NET 10's runtime installed;
#   WASP-<version>-win-<architecture>-self-contained.zip and WASP-<version>-win-<architecture>-framework-dependent.zip,
#   for a machine with WebLingo installed from the zip of the same version, architecture and kind, made by the same run
#   of this script.
# WebLingo's include Install WebLingo.bat and setup.wlg for interactive setup. Their WebLingo folder holds weblingo,
# the standard modules and VSCode/WebLingo.vsix, which install.wlg installs together in WebLingo's
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
$profile = Join-Path $solution 'Runtime.pubxml'
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
    dotnet publish (Join-Path $solution $project) -c Release -r "win-$architecture" --self-contained $selfContained `
        -o $folder "-p:PublishProfileFullPath=$profile"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
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
function New-Zip([string] $folder, [string] $zip) {
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

function New-VSCodePackage {
    $source = Join-Path $repository 'Editors/VSCode'
    $manifest = Get-Content -Raw -LiteralPath (Join-Path $source 'package.json') | ConvertFrom-Json
    $folder = Join-Path $staging 'vsix'
    $payload = New-Item -ItemType Directory -Path (Join-Path $folder 'extension') -Force
    $assets = @('package.json', $manifest.main) + @($manifest.contributes.languages.configuration) +
        @($manifest.contributes.grammars.path)
    foreach ($asset in $assets | Sort-Object -Unique) {
        $target = Join-Path $payload.FullName $asset
        New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $source $asset) -Destination $target
    }
    # The VSIX identity and compatibility range follow the extension's own manifest.
    $xml = [xml]@'
<PackageManifest Version="2.0.0" xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011">
  <Metadata>
    <Identity Language="en-US"/><DisplayName/><Description xml:space="preserve"/>
    <Properties><Property Id="Microsoft.VisualStudio.Code.Engine"/></Properties>
  </Metadata>
  <Installation><InstallationTarget Id="Microsoft.VisualStudio.Code"/></Installation>
  <Dependencies/>
  <Assets><Asset Type="Microsoft.VisualStudio.Code.Manifest" Path="extension/package.json" Addressable="true"/></Assets>
</PackageManifest>
'@
    $metadata = $xml.PackageManifest.Metadata
    $metadata.Identity.SetAttribute('Id', $manifest.name)
    $metadata.Identity.SetAttribute('Publisher', $manifest.publisher)
    $metadata.Identity.SetAttribute('Version', $manifest.version)
    $metadata.DisplayName = $manifest.displayName
    $metadata.Description.InnerText = $manifest.description
    $metadata.Properties.Property.SetAttribute('Value', $manifest.engines.vscode)
    $xml.Save((Join-Path $folder 'extension.vsixmanifest'))
    @'
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="vsixmanifest" ContentType="text/xml"/>
  <Default Extension="json" ContentType="application/json"/>
  <Default Extension="js" ContentType="application/javascript"/>
</Types>
'@ | Set-Content -LiteralPath (Join-Path $folder '[Content_Types].xml') -Encoding UTF8
    New-Zip $folder (Join-Path $staging 'WebLingo.vsix')
}

try {
    New-VSCodePackage
    foreach ($architecture in 'x64', 'x86', 'arm64') {
        foreach ($kind in 'self-contained', 'framework-dependent') {
            # WebLingo's: weblingo, weblingow, the standard modules, and the scripts that install them; weblingow's project
            # references weblingo's, so that its output holds both programs.
            $name = "WebLingo-$version-win-$architecture-$kind"
            $root = Join-Path $staging $name
            $runtime = Join-Path $root 'WebLingo'
            Publish-Project 'src/WebLingo.Windowed.Cli/WebLingo.Windowed.Cli.csproj' $architecture $kind $runtime
            $editor = New-Item -ItemType Directory -Path (Join-Path $runtime 'VSCode') -Force
            Copy-Item -LiteralPath (Join-Path $staging 'WebLingo.vsix') -Destination $editor.FullName
            foreach ($script in 'setup.wlg', 'install.wlg', 'add-wlg-file-type.wlg', 'windows.wlg', 'weblingo.config') {
                Copy-Item (Join-Path $repository "Scripts/$script") $root
            }
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install WebLingo.bat') -Destination $root
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall WebLingo.bat') -Destination $root
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall WebLingo.bat') -Destination $runtime
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination $runtime
            Copy-Item (Join-Path $PSScriptRoot 'readme.md') $root

            # WASP's: what wasp's own publish has that WebLingo's has not, or has otherwise, such as ASP.NET Core's
            # libraries, newer than the packages weblingo takes; and the hashes of the files of WebLingo's it shares.
            $waspName = "WASP-$version-win-$architecture-$kind"
            $waspRoot = Join-Path $staging $waspName
            $published = Join-Path $staging "wasp-$architecture-$kind"
            Publish-Project 'src/WebLingo.Wasp.Cli/WebLingo.Wasp.Cli.csproj' $architecture $kind $published
            $weblingo = Get-Hashes $runtime
            $wasp = Get-Hashes $published
            [ordered]@{
                architecture = $architecture
                kind = $kind
                files = $weblingo
                # WASP loads weblingo.dll in process. Its own apphost uses wasp's deps and runtimeconfig files; weblingow is
                # WebLingo's alone.
                shared = @($wasp.Keys | Where-Object {
                    $weblingo.ContainsKey($_) -and $_ -notin 'weblingo.exe', 'weblingo.deps.json', 'weblingo.runtimeconfig.json' -and
                    $_ -notlike 'weblingow.*'
                } | Sort-Object)
            } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runtime 'uninstall-files.json') -Encoding UTF8
            New-Zip $root (Join-Path $Destination "$name.zip")
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
            New-Zip $waspRoot (Join-Path $Destination "$waspName.zip")
        }
    }
}
finally {
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
}

# Builds WebLingo's extension for Visual Studio, bin\WebLingo.VisualStudio.vsix (Editors/README.md, "Visual Studio, with
# WebLingo's extension"): WebLingoLaunch.cs compiled for .NET Framework 4.8 with the C# compiler Visual Studio installs,
# against .NET Framework 4.8's reference assemblies and the Visual Studio assemblies it uses, referenced and never shipped;
# then packed with WebLingo.pkgdef and extension.vsixmanifest, with no package of Microsoft's. Needs Visual Studio 2022 or
# 2026 on this machine, and .NET Framework 4.8's targeting pack, which Visual Studio installs.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'Visual Studio is not installed: vswhere.exe is not there.' }
$vs = & $vswhere -latest -version '[17.0,19.0)' -property installationPath
if (-not $vs) { throw 'Visual Studio 2022 or 2026 is not installed.' }
$csc = Join-Path $vs 'MSBuild\Current\Bin\Roslyn\csc.exe'
$ide = Join-Path $vs 'Common7\IDE'
$framework = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
if (-not (Test-Path $framework)) { throw ".NET Framework 4.8's reference assemblies are not installed: $framework." }

$references = @(
    (Join-Path $framework 'mscorlib.dll'),
    (Join-Path $framework 'System.dll'),
    (Join-Path $framework 'System.Core.dll'),
    (Join-Path $framework 'System.ComponentModel.Composition.dll'),
    (Join-Path $framework 'Facades\netstandard.dll'),
    (Join-Path $framework 'Facades\System.Runtime.dll'),
    (Join-Path $ide 'CommonExtensions\Microsoft\OpenFolder\Microsoft.VisualStudio.Workspace.dll'),
    (Join-Path $ide 'CommonExtensions\Microsoft\OpenFolder\Microsoft.VisualStudio.Workspace.Extensions.dll'),
    (Join-Path $ide 'CommonExtensions\Microsoft\OpenFolder\Microsoft.VisualStudio.Workspace.Extensions.VS.dll'),
    (Join-Path $ide 'PublicAssemblies\Microsoft.VisualStudio.Interop.dll')
)
foreach ($reference in $references) {
    if (-not (Test-Path $reference)) { throw "A reference is not there: $reference." }
}

$bin = Join-Path $PSScriptRoot 'bin'
$folder = Join-Path $bin 'vsix'
if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }
New-Item -ItemType Directory -Path $folder -Force | Out-Null

$arguments = @('/nologo', '/noconfig', '/nostdlib+', '/target:library', '/deterministic+', '/optimize+', '/langversion:latest',
               '/warnaserror+', "/out:$(Join-Path $folder 'WebLingo.VisualStudio.dll')") +
             @($references | ForEach-Object { "/reference:$_" }) + @((Join-Path $PSScriptRoot 'WebLingoLaunch.cs'))
& $csc @arguments
if ($LASTEXITCODE -ne 0) { throw "The C# compiler failed, exit code $LASTEXITCODE." }

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'WebLingo.pkgdef') -Destination $folder
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'extension.vsixmanifest') -Destination $folder
@'
<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="vsixmanifest" ContentType="text/xml"/>
  <Default Extension="pkgdef" ContentType="text/plain"/>
  <Default Extension="dll" ContentType="application/octet-stream"/>
</Types>
'@ | Set-Content -LiteralPath (Join-Path $folder '[Content_Types].xml') -Encoding UTF8

# The folder's files zipped, each entry named with forward slashes, as the zip format has them.
$vsix = Join-Path $bin 'WebLingo.VisualStudio.vsix'
if (Test-Path $vsix) { Remove-Item $vsix }
$archive = [IO.Compression.ZipFile]::Open($vsix, [IO.Compression.ZipArchiveMode]::Create)
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
Write-Host ("Made {0}, {1:N1} KB" -f $vsix, ((Get-Item $vsix).Length / 1KB))

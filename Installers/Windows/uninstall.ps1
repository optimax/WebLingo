# Windows PowerShell can remove the installed runtime while this script is running.
# The original account handles its registry and VS Code. Only -Machine crosses UAC.
param([switch] $Machine, [switch] $Remember, [switch] $Association, [string] $Code)
$ErrorActionPreference = 'Stop'
$inventoryName = 'uninstall-files.json'
$pathRecord = 'uninstall-path.json'
$extensionId = 'weblingo.weblingo'
$userState = Join-Path $env:LOCALAPPDATA 'WebLingo\Setup'

function Get-ProgramFolder([string] $architecture) {
    $view = if ([Environment]::Is64BitOperatingSystem) { 'Registry64' } else { 'Registry32' }
    $machineKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine', $view)
    try {
        $windows = $machineKey.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion')
        try {
            $value = if ($architecture -eq 'x86' -and $view -eq 'Registry64') { 'ProgramFilesDir (x86)' } else { 'ProgramFilesDir' }
            return Join-Path $windows.GetValue($value) 'WebLingo'
        } finally { $windows.Dispose() }
    } finally { $machineKey.Dispose() }
}

function Find-Code {
    $local = Join-Path $env:LOCALAPPDATA 'Programs\Microsoft VS Code\bin\code.cmd'
    if (Test-Path -LiteralPath $local -PathType Leaf) { return $local }
    foreach ($arch in 'x64', 'x86') {
        $cli = Join-Path (Split-Path (Get-ProgramFolder $arch)) 'Microsoft VS Code\bin\code.cmd'
        if (Test-Path -LiteralPath $cli -PathType Leaf) { return $cli }
    }
    (Get-Command code.cmd -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1).Source
}

function Test-Extension([string] $cli) {
    $extensions = & $cli --list-extensions
    if ($LASTEXITCODE -ne 0) { throw 'VS Code could not list extensions in this account.' }
    return $extensions -contains $extensionId
}

# Each address ends in the registry value name. A trailing slash denotes the unnamed value.
# These are the individual writes in Scripts/add-wlg-file-type.wlg, including the old .wl migration.
function Get-AssociationValues([string] $folder) {
    $exe = Join-Path $folder 'weblingo.exe'
    $command = '"{0}" "%1" %*' -f $exe
    return [ordered]@{
        '.wlg\' = 'WebLingo.Script'; '.wlg\OpenWithProgids\WebLingo.Script' = ''
        '.wlg\PerceivedType' = 'text'; '.wlg\Content Type' = 'text/plain'
        'WebLingo.Script\' = 'WebLingo script'; 'WebLingo.Script\DefaultIcon\' = ('"{0}",0' -f $exe)
        'WebLingo.Script\shell\open\FriendlyAppName' = 'WebLingo'
        'WebLingo.Script\shell\open\command\' = $command
        'WebLingo.Script\shell\edit\command\' = 'notepad.exe "%1"'
        'Applications\weblingo.exe\FriendlyAppName' = 'WebLingo'
        'Applications\weblingo.exe\SupportedTypes\.wlg' = ''
        'Applications\weblingo.exe\shell\open\command\' = $command
        '.wl\' = $null; '.wl\OpenWithProgids\WebLingo.Script' = $null
        'Applications\weblingo.exe\SupportedTypes\.wl' = $null
    }
}

function Read-ClassValue($classes, [string] $address) {
    if (!$classes) { return }
    $slash = $address.LastIndexOf('\')
    $key = $classes.OpenSubKey($address.Substring(0, $slash))
    if (!$key) { return }
    try {
        $name = $address.Substring($slash + 1)
        if ($key.GetValueNames() -contains $name) {
            return @{ kind = $key.GetValueKind($name).ToString(); data = $key.GetValue($name, $null, 'DoNotExpandEnvironmentNames') }
        }
    } finally { $key.Dispose() }
}

function Restore-ClassValue($classes, [string] $address, $previous) {
    $slash = $address.LastIndexOf('\')
    $path = $address.Substring(0, $slash)
    $name = $address.Substring($slash + 1)
    if ($null -ne $previous) {
        $key = $classes.CreateSubKey($path)
        try {
            $data = switch ($previous.kind) {
                'MultiString' { ,([string[]]$previous.data) }
                'Binary' { ,([byte[]]$previous.data) }
                'None' { ,([byte[]]$previous.data) }
                default { $previous.data }
            }
            $key.SetValue($name, $data, [Microsoft.Win32.RegistryValueKind]$previous.kind)
        } finally { $key.Dispose() }
        return
    }
    $key = $classes.OpenSubKey($path, $true)
    if (!$key) { return }
    try { $key.DeleteValue($name, $false) } finally { $key.Dispose() }
    while ($path -and $path -ne 'Applications') {
        $key = $classes.OpenSubKey($path)
        if (!$key) { break }
        try { $empty = ($key.ValueCount -eq 0 -and $key.SubKeyCount -eq 0) } finally { $key.Dispose() }
        if (!$empty) { break }
        $classes.DeleteSubKey($path, $false)
        $path = Split-Path $path
    }
}

function Save-FirstRecord([string] $file, $record) {
    if (Test-Path -LiteralPath $file) { return }
    [IO.Directory]::CreateDirectory($userState) | Out-Null
    $temporary = Join-Path $userState ([guid]::NewGuid().ToString('N') + '.tmp')
    try {
        Export-Clixml -LiteralPath $temporary -InputObject $record -Depth 8
        try { [IO.File]::Move($temporary, $file) } catch [IO.IOException] {
            if (!(Test-Path -LiteralPath $file)) { throw }
        }
    } finally { [IO.File]::Delete($temporary) }
}

function Remember-User($classes, [string] $folder) {
    if ($Association) {
        $before = @{}
        foreach ($address in (Get-AssociationValues $folder).Keys) {
            $value = Read-ClassValue $classes $address
            if ($value) { $before[$address] = $value }
        }
        Save-FirstRecord (Join-Path $userState 'association.xml') $before
    }
    if ($Code) {
        Save-FirstRecord (Join-Path $userState 'debugger.xml') @{ added = !(Test-Extension $Code); cli = $Code }
    }
}

function Remove-UserIntegrations($classes, [string] $folder, [string] $other, [bool] $legacy) {
    $assocFile = Join-Path $userState 'association.xml'
    $hasRecord = Test-Path -LiteralPath $assocFile
    $values = Get-AssociationValues $folder
    $command = 'WebLingo.Script\shell\open\command\'
    $registered = Read-ClassValue $classes $command
    if (($hasRecord -and !$registered) -or (($hasRecord -or $legacy) -and $registered.data -eq $values[$command])) {
        $before = if ($hasRecord) { Import-Clixml -LiteralPath $assocFile } else { @{} }
        $replacement = if ($other) { Get-AssociationValues $other }
        $oldValues = $null
        if ($before[$command].data -match '^"([^"]+\\weblingo\.exe)" "%1" %\*$') {
            $oldExe = $Matches[1]
            if ($oldExe -eq (Join-Path $folder 'weblingo.exe') -or !(Test-Path -LiteralPath $oldExe -PathType Leaf)) {
                $oldValues = Get-AssociationValues (Split-Path $oldExe)
            }
        }
        foreach ($address in $values.Keys) {
            $now = Read-ClassValue $classes $address
            $expected = $values[$address]
            if ($null -eq $expected) {
                if ($address -eq '.wl\' -and $before[$address].data -ne 'WebLingo.Script') { continue }
                if (!$other -and !$now -and $before[$address]) { Restore-ClassValue $classes $address $before[$address] }
            } elseif ($now -and $now.kind -eq 'String' -and $now.data -eq $expected) {
                if ($other) {
                    if ($replacement[$address] -ne $expected) {
                        Restore-ClassValue $classes $address @{ kind = 'String'; data = $replacement[$address] }
                    }
                } else {
                    $previous = $before[$address]
                    if ($oldValues -and $previous.data -eq $oldValues[$address]) { $previous = $null }
                    Restore-ClassValue $classes $address $previous
                }
            }
        }
        Write-Host 'Updated the .wlg registration for this account.'
    }
    if ($other) { Write-Host "Keeping shared editor integration for $other."; return }
    if ($hasRecord) { [IO.File]::Delete($assocFile) }
    $editorFile = Join-Path $userState 'debugger.xml'
    $record = if (Test-Path -LiteralPath $editorFile) { Import-Clixml -LiteralPath $editorFile }
    if ($record.added -or (!$record -and $legacy)) {
        $cli = Find-Code
        if (!$cli -and $record.cli -and (Test-Path -LiteralPath $record.cli -PathType Leaf)) { $cli = $record.cli }
        if ($cli -and (Test-Extension $cli)) {
            & $cli --uninstall-extension $extensionId | Out-Host
            if ($LASTEXITCODE -ne 0) { throw 'VS Code could not uninstall WebLingo. Re-extract the ZIP and rerun uninstall.' }
        } elseif (!$cli -and $record.added) {
            throw 'VS Code was not found. After reinstalling it, rerun uninstall from the extracted WebLingo ZIP.'
        }
    }
    if ($record) { [IO.File]::Delete($editorFile) }
    if ($hasRecord -or $record) {
        Remove-EmptyDirectory $userState
        Remove-EmptyDirectory (Split-Path $userState)
    }
}

function Remove-EmptyDirectory([string] $path) {
    if ([IO.Directory]::Exists($path) -and ![IO.Directory]::GetFileSystemEntries($path).Length) {
        try { [IO.Directory]::Delete($path, $false) } catch [IO.IOException] {
            if (($_.Exception.GetBaseException().HResult -band 65535) -notin 32,145) { throw }
            Write-Host "Windows kept directory $path because it is in use or has new files."
        }
    }
}

function Assert-NoLink([string] $path) {
    for ($part = $path; $part; $part = [IO.Path]::GetDirectoryName($part)) {
        try { $attributes = [IO.File]::GetAttributes($part) }
        catch [IO.FileNotFoundException] { continue }
        catch [IO.DirectoryNotFoundException] { continue }
        if ($attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Cannot uninstall through a link: $part" }
    }
}

function Get-RemainingPath([string] $path, [string] $folder, $original) {
    $matchesFolder = { param($entry) $entry.Trim().TrimEnd('\') -eq $folder.TrimEnd('\') }
    if ($original -and @($original.path.Split(';') | Where-Object { & $matchesFolder $_ }).Count) { return $path }
    $entries = [Collections.Generic.List[string]]::new([string[]]$path.Split(';'))
    for ($i = $entries.Count - 1; $i -ge 0; $i--) {
        if (& $matchesFolder $entries[$i]) {
            if ($original -and $path -ceq ($original.path.TrimEnd(';') + ';' + $folder)) { return $original.path }
            $entries.RemoveAt($i)
            return $entries -join ';'
        }
    }
    return $path
}

function Remove-PathEntry([string] $folder, $original) {
    $key = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey(
        'SYSTEM\CurrentControlSet\Control\Session Manager\Environment', $true)
    try {
        $path = $key.GetValue('Path', '', 'DoNotExpandEnvironmentNames')
        $updated = Get-RemainingPath $path $folder $original
        if ($updated -cne $path) {
            $key.SetValue('Path', $updated, $key.GetValueKind('Path'))
            Write-Host 'Removed the WebLingo entry from the machine PATH.'
        }
    } finally { $key.Dispose() }
}

function Test-SameVsix([string] $installed, [string] $packaged) {
    # ZIP timestamps change between builds even when all of the editor files are identical.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    function Get-Contents([string] $path) {
        $archive = [IO.Compression.ZipFile]::OpenRead($path)
        $hash = [Security.Cryptography.SHA256]::Create()
        try {
            foreach ($entry in $archive.Entries | Sort-Object FullName) {
                $stream = $entry.Open()
                try { $entry.FullName + ' ' + [Convert]::ToBase64String($hash.ComputeHash($stream)) }
                finally { $stream.Dispose() }
            }
        } finally { $archive.Dispose(); $hash.Dispose() }
    }
    try { return !(Compare-Object @(Get-Contents $installed) @(Get-Contents $packaged) -CaseSensitive) }
    catch [IO.InvalidDataException] { return $false }
}

function Get-FileDigest([string] $file) {
    $stream = [IO.File]::OpenRead($file)
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '') }
    finally { $hash.Dispose(); $stream.Dispose() }
}

function Test-LegacyAssembly([string] $file, [string] $packaged) {
    # Two builds of the same pre-alpha can give the project's DLLs different PDB identifiers and binary hashes.
    # Read metadata without loading or locking the assembly. Third-party DLLs still require their exact digest.
    $reference = [Diagnostics.FileVersionInfo]::GetVersionInfo($packaged)
    if ($reference.ProductName -ne 'weblingo' -and $reference.ProductName -notlike 'WebLingo.*') { return $false }
    $actual = [Diagnostics.FileVersionInfo]::GetVersionInfo($file)
    if ($actual.ProductName -ne $reference.ProductName -or $actual.ProductVersion -ne $reference.ProductVersion -or
        $actual.OriginalFilename -ne $reference.OriginalFilename) { return $false }
    try {
        return [Reflection.AssemblyName]::GetAssemblyName($file).FullName -eq
            [Reflection.AssemblyName]::GetAssemblyName($packaged).FullName
    } catch [BadImageFormatException] { return $false }
}

function Test-LegacyDependencies([string] $file, [string] $packaged) {
    $actual = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
    $reference = Get-Content -LiteralPath $packaged -Raw | ConvertFrom-Json
    $product = @($reference.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'weblingo/*' })
    return $product.Count -eq 1 -and $actual.runtimeTarget.name -eq $reference.runtimeTarget.name -and
        $actual.libraries.PSObject.Properties.Name -contains $product[0]
}

function Remove-Runtime([string] $folder, $inventory, [bool] $legacy) {
    Assert-NoLink $folder
    $controls = @('Uninstall WebLingo.bat', 'uninstall.ps1')
    foreach ($control in @($inventoryName, $pathRecord, 'wasp.exe')) { Assert-NoLink (Join-Path $folder $control) }
    $hasWasp = Test-Path -LiteralPath (Join-Path $folder 'wasp.exe') -PathType Leaf
    $original = if (Test-Path -LiteralPath (Join-Path $folder $pathRecord)) {
        Get-Content -LiteralPath (Join-Path $folder $pathRecord) -Raw | ConvertFrom-Json
    }
    $files = [Collections.Generic.List[string]]::new()
    $dirs = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($item in $inventory.files.PSObject.Properties) {
        $name = $item.Name
        $parts = $name -split '[/\\]'
        if (@($parts | Where-Object { !$_ -or $_ -match '[.: ]$' -or $_.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 }).Count) {
            throw "Invalid filename in the WebLingo inventory: $name"
        }
        $file = [IO.Path]::GetFullPath((Join-Path $folder $name))
        if (!$file.StartsWith($folder + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Invalid install path: $file" }
        Assert-NoLink $file
        if ($hasWasp -and $inventory.shared -contains $name) { continue }
        for ($dir = [IO.Path]::GetDirectoryName($file); $dir -ne $folder; $dir = [IO.Path]::GetDirectoryName($dir)) {
            [void]$dirs.Add($dir)
        }
        if (![IO.File]::Exists($file)) { continue }
        $matchesPackage = (Get-FileDigest $file) -eq $item.Value
        if (!$matchesPackage -and $legacy -and $name -eq 'VSCode/WebLingo.vsix') {
            $matchesPackage = Test-SameVsix $file (Join-Path $PSScriptRoot $name)
        } elseif (!$matchesPackage -and $legacy -and $name -like '*.dll') {
            $matchesPackage = Test-LegacyAssembly $file (Join-Path $PSScriptRoot $name)
        } elseif (!$matchesPackage -and $legacy -and $name -eq 'weblingo.deps.json') {
            $matchesPackage = Test-LegacyDependencies $file (Join-Path $PSScriptRoot $name)
        }
        if (!$matchesPackage) {
            Write-Host "Keeping changed file $file"
            continue
        }
        $files.Add($file)
    }
    $exe = Join-Path $folder 'weblingo.exe'
    if (Get-Process -Name weblingo -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }) {
        throw "Close scripts or debugger sessions running $exe, then run uninstall again."
    }
    # Keep the inventory and launcher available for retry if a file is locked.
    $receipt = Join-Path $folder $inventoryName
    if ($legacy -and ![IO.File]::Exists($receipt)) {
        $inventory | Add-Member -NotePropertyName legacy -NotePropertyValue $true -Force
        [IO.File]::WriteAllText($receipt, ($inventory | ConvertTo-Json -Depth 4))
    }
    foreach ($file in $files) { if ($controls -notcontains [IO.Path]::GetFileName($file)) { [IO.File]::Delete($file) } }
    if ($hasWasp) { Write-Host 'Keeping the WASP runtime, shared files and machine PATH.' }
    else { Remove-PathEntry $folder $original }
    foreach ($dir in $dirs | Sort-Object Length -Descending) { Remove-EmptyDirectory $dir }
    foreach ($control in $controls) {
        $file = Join-Path $folder $control
        if ($files.Contains($file)) { [IO.File]::Delete($file) }
    }
    [IO.File]::Delete((Join-Path $folder $pathRecord))
    [IO.File]::Delete($receipt)
    Remove-EmptyDirectory $folder
}

function Invoke-Uninstall {
    $payload = [IO.Path]::GetFullPath($PSScriptRoot)
    $media = (Test-Path -LiteralPath (Join-Path (Split-Path $payload) 'Install WebLingo.bat') -PathType Leaf) -and
        !(Test-Path -LiteralPath (Join-Path $payload $pathRecord))
    if (!(Test-Path -LiteralPath (Join-Path $payload $inventoryName) -PathType Leaf)) {
        throw 'Open Uninstall WebLingo.bat in the installed folder or in the extracted Windows release ZIP.'
    }
    $inventory = Get-Content -LiteralPath (Join-Path $payload $inventoryName) -Raw | ConvertFrom-Json
    $folder = if ($media) { Get-ProgramFolder $inventory.architecture } else { $payload }
    $record = Join-Path $folder $inventoryName
    Assert-NoLink $folder
    Assert-NoLink $record
    $present = Test-Path -LiteralPath $record -PathType Leaf
    $legacy = !$present -and (Test-Path -LiteralPath (Join-Path $folder 'weblingo.exe') -PathType Leaf)
    if ($present) {
        $inventory = Get-Content -LiteralPath $record -Raw | ConvertFrom-Json
        $legacy = $inventory.legacy -eq $true
    } elseif ($legacy) {
        foreach ($name in 'weblingo.dll', 'weblingo.deps.json', 'weblingo.runtimeconfig.json') {
            $file = Join-Path $folder $name
            Assert-NoLink $file
            $sameBuild = (Test-Path -LiteralPath $file) -and ((Get-FileDigest $file) -eq $inventory.files.$name -or
                ($name -eq 'weblingo.dll' -and (Test-LegacyAssembly $file (Join-Path $payload $name))) -or
                ($name -eq 'weblingo.deps.json' -and (Test-LegacyDependencies $file (Join-Path $payload $name))) -or
                ($name -eq 'weblingo.runtimeconfig.json' -and
                    ([bool](Get-Content -LiteralPath $file -Raw | ConvertFrom-Json).runtimeOptions.includedFrameworks -eq
                     ($inventory.kind -eq 'self-contained'))))
            if (!$sameBuild) {
                throw "Use the ZIP matching the version, processor and runtime installed in $folder."
            }
        }
    }
    if ($Machine) {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        if (!([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole('Administrator')) {
            throw 'Removing the machine installation requires administrator access.'
        }
        if ($present -or $legacy) { Remove-Runtime $folder $inventory $legacy }
        return
    }
    if ($Remember) {
        $classes = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Classes')
        try { Remember-User $classes $folder } finally { if ($classes) { $classes.Dispose() } }
        return
    }
    Write-Host "Uninstall WebLingo from $folder and remove its setup integrations for this account."
    if ($legacy) { Write-Host 'This older install has no setup record. Its registered WebLingo integration will be removed.' }
    if ((Read-Host 'Continue? [y/N]') -notin 'y','yes') { Write-Host 'Uninstall canceled.'; return }
    if ($present -or $legacy) {
        $arguments = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -Machine' -f $PSCommandPath
        $child = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList $arguments -Verb RunAs `
            -WorkingDirectory ([Environment]::SystemDirectory) -Wait -PassThru
        if ($child.ExitCode -ne 0) { throw 'Machine uninstall failed. Resolve the reported problem and retry.' }
    }
    $classes = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Classes')
    try {
        $other = @('x64','x86' | ForEach-Object { Get-ProgramFolder $_ } | Where-Object {
            $_ -ne $folder -and (Test-Path -LiteralPath (Join-Path $_ 'weblingo.exe') -PathType Leaf)
        }) | Select-Object -First 1
        Remove-UserIntegrations $classes $folder $other $legacy
        Write-Host 'WebLingo uninstall complete.'
    } finally { $classes.Dispose() }
}

if ($MyInvocation.InvocationName -ne '.') {
    try { Invoke-Uninstall }
    catch {
        Write-Host $_.Exception.Message
        if ($Machine -and ![Console]::IsInputRedirected) { [void](Read-Host 'Press Enter to close') }
        exit 1
    }
}

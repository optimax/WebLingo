# Targeted Windows PowerShell checks for removal. The registry key and files belong only to this run.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\uninstall.ps1')

function Assert([bool] $condition, [string] $message) { if (!$condition) { throw $message } }
function Remove-PathEntry([string] $folder, $original) { $script:pathCalls++ }
# This account's own VS Code is never the tests': an uninstall that would remove WebLingo's extension finds none.
function Find-Code { }
$id = 'WebLingoUninstallTest-' + [guid]::NewGuid().ToString('N')
$scratch = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) $id))
$scope = 'Software\' + $id
$classes = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($scope)
$folder = Join-Path $scratch 'WebLingo'
$userState = Join-Path $scratch 'profile\WebLingo\Setup'
$script:pathCalls = 0
try {
    $original = @{ path = 'first;;' }
    Assert ((Get-RemainingPath "first;$folder" $folder $original) -ceq 'first;;') 'Restore original PATH delimiters'
    Assert ((Get-RemainingPath "other;first;$folder;last" $folder $original) -ceq 'other;first;last') 'Preserve other PATH edits'
    Assert ((Get-RemainingPath "first;$folder" $folder @{path=$folder}) -ceq "first;$folder") 'Keep preexisting PATH entry'
    Assert ((Get-RemainingPath $folder $folder $null) -ceq '') 'Remove a legacy single-entry PATH'

    $key = $classes.CreateSubKey('.wlg')
    $key.SetValue('', 'Previous.App')
    $key.SetValue('Content Type', '%PREVIOUS%', 'ExpandString')
    $key.Close()
    $key = $classes.CreateSubKey('.wlg\OpenWithProgids')
    $key.SetValue('Other.App', [byte[]]@(2,3), 'Binary')
    $key.Close()
    $Association = $true
    Remember-User $classes $folder
    $values = Get-AssociationValues $folder
    foreach ($address in $values.Keys) {
        if ($null -ne $values[$address]) { Restore-ClassValue $classes $address @{kind='String';data=$values[$address]} }
    }
    Remember-User $classes $folder
    Restore-ClassValue $classes '.wlg\PerceivedType' @{kind='String';data='user edit'}
    $sibling = Join-Path $scratch 'another architecture'
    Remove-UserIntegrations $classes $folder $sibling $false
    Assert ((Read-ClassValue $classes 'WebLingo.Script\shell\open\command\').data.Contains($sibling)) 'Use surviving CLI'
    Remove-UserIntegrations $classes $sibling '' $false
    Assert ((Read-ClassValue $classes '.wlg\').data -ceq 'Previous.App') 'Restore the prior file type'
    Assert ((Read-ClassValue $classes '.wlg\Content Type').kind -ceq 'ExpandString') 'Restore the registry value kind'
    Assert ((Read-ClassValue $classes '.wlg\Content Type').data -ceq '%PREVIOUS%') 'Preserve unexpanded registry data'
    Assert ((Read-ClassValue $classes '.wlg\PerceivedType').data -ceq 'user edit') 'Preserve later edits'
    Assert ((Read-ClassValue $classes '.wlg\OpenWithProgids\Other.App').data[1] -eq 3) 'Preserve foreign registrations'
    Assert (!(Read-ClassValue $classes 'Applications\weblingo.exe\shell\open\command\')) 'Remove owned app registration'
    Remove-UserIntegrations $classes $sibling '' $false
    Assert ((Get-AssociationValues $folder)['WebLingo.Script\shell\open\command\'].Contains('weblingow.exe')) 'Run weblingow'

    # A registration an earlier version made, which ran weblingo.exe, is removed as this version's is.
    $earlier = Get-AssociationValues $folder 'weblingo.exe'
    foreach ($address in $earlier.Keys) {
        if ($null -ne $earlier[$address]) { Restore-ClassValue $classes $address @{kind='String';data=$earlier[$address]} }
    }
    Remove-UserIntegrations $classes $folder '' $true
    Assert (!(Read-ClassValue $classes 'WebLingo.Script\shell\open\command\')) 'Remove an earlier registration'
    Assert (!(Read-ClassValue $classes 'Applications\weblingo.exe\shell\open\command\')) 'Remove its app registration'
    Assert ((Read-ClassValue $classes '.wlg\').data -cne 'WebLingo.Script') 'Leave .wlg to no WebLingo'

    [IO.Directory]::CreateDirectory((Join-Path $folder 'VSCode')) | Out-Null
    $files = @{}
    foreach ($name in 'weblingo.exe','shared.dll','VSCode/WebLingo.vsix','custom.config') {
        $file = Join-Path $folder $name
        [IO.File]::WriteAllText($file, 'packaged')
        $files[$name] = (Get-FileHash -LiteralPath $file).Hash
    }
    [IO.File]::WriteAllText((Join-Path $folder 'custom.config'), 'edited')
    [IO.File]::WriteAllText((Join-Path $folder 'my-script.wlg'), 'owned by the user')
    [IO.File]::WriteAllText((Join-Path $folder 'wasp.exe'), 'WASP')
    $manifest = @{files=$files;shared=@('shared.dll')} | ConvertTo-Json | ConvertFrom-Json
    $outside = Join-Path $scratch 'outside.txt'
    [IO.File]::WriteAllText($outside, 'untouched')
    $files['../outside.txt'] = (Get-FileHash -LiteralPath $outside).Hash
    $invalid = @{files=$files;shared=@()} | ConvertTo-Json | ConvertFrom-Json
    try { Remove-Runtime $folder $invalid $false; throw 'Accepted a traversal' }
    catch { if ($_.Exception.Message -notlike '*Invalid filename*') { throw } }
    Assert ([IO.File]::Exists((Join-Path $folder 'weblingo.exe'))) 'Validate before deleting'
    Assert ([IO.File]::Exists($outside)) 'Stay inside install folder'
    Remove-Runtime $folder $manifest $false
    Assert (![IO.File]::Exists((Join-Path $folder 'weblingo.exe'))) 'Remove the CLI'
    Assert (![IO.Directory]::Exists((Join-Path $folder 'VSCode'))) 'Remove VSIX and its empty directory'
    foreach ($name in 'shared.dll','wasp.exe','my-script.wlg','custom.config') {
        Assert ([IO.File]::Exists((Join-Path $folder $name))) "Preserve $name"
    }
    Assert ($pathCalls -eq 0) 'WASP keeps its PATH'
    [IO.File]::Delete((Join-Path $folder 'wasp.exe'))
    $lock = [IO.File]::Open((Join-Path $folder 'shared.dll'), 'Open', 'ReadWrite', 'None')
    try {
        try { Remove-Runtime $folder $manifest $false; throw 'Deleted a locked file' }
        catch [IO.IOException] { }
        Assert ([IO.File]::Exists((Join-Path $folder 'shared.dll'))) 'Keep locked file for retry'
    } finally { $lock.Dispose() }
    Remove-Runtime $folder $manifest $false
    Assert (![IO.File]::Exists((Join-Path $folder 'shared.dll'))) 'Uninstall succeeds after releasing lock'
    Assert ($pathCalls -eq 1) 'Release the PATH after the last runtime is removed'
    Write-Output 'Windows uninstall regression checks passed.'
} finally {
    $classes.Close()
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($scope, $false)
    if ([IO.Path]::GetDirectoryName($scratch) -eq [IO.Path]::GetTempPath().TrimEnd('\') -and
        [IO.Path]::GetFileName($scratch) -eq $id -and [IO.Directory]::Exists($scratch)) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
}

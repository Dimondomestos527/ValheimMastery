$ErrorActionPreference='Stop'
$devRoot=Split-Path -Parent $PSScriptRoot
$runtimeRoot='C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server'
$evidence=Join-Path $devRoot 'validation\isolated-client-smoke-20261004'
if(Test-Path $evidence){throw 'Evidence destination already exists; do not overwrite'}
New-Item -ItemType Directory -Path $evidence|Out-Null
$usersBefore=@(Get-LocalUser|Select-Object Name,@{Name='SID';Expression={$_.SID.Value}},Enabled)
$usersBefore|Export-Csv (Join-Path $evidence 'accounts-before.csv') -NoTypeInformation
& whoami.exe /all > (Join-Path $evidence 'executor-token.txt')
$principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
[pscustomobject]@{Name='MasteryUnityQC';AlreadyExists=[bool](Get-LocalUser -Name 'MasteryUnityQC' -ErrorAction SilentlyContinue);ElevatedAdministrator=$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator);CreationAttempted=$false;ClientLaunchAttempted=$false}|Export-Csv (Join-Path $evidence 'isolation-preflight.csv') -NoTypeInformation
& reg.exe export 'HKCU\Software\IronGate\Valheim' (Join-Path $evidence 'production-playerprefs-before.reg') /y
if($LASTEXITCODE){throw 'Production PlayerPrefs snapshot failed'}
$oldEvidence=Join-Path $devRoot 'validation\unity-smoke-20261004'
$before=@(Import-Csv (Join-Path $oldEvidence 'protected-before.csv'))
$changes=[Collections.Generic.List[object]]::new();$known=@{}
foreach($entry in $before){
 $known[$entry.Path]=$true
 if(!(Test-Path -LiteralPath $entry.Path)){$changes.Add([pscustomobject]@{Path=$entry.Path;Change='MISSING'});continue}
 $file=Get-Item -LiteralPath $entry.Path
 if($entry.SHA256 -ne (Get-FileHash -LiteralPath $entry.Path).Hash -or $entry.Length -ne $file.Length -or $entry.LastWriteUtc -ne $file.LastWriteTimeUtc.ToString('o')){$changes.Add([pscustomobject]@{Path=$entry.Path;Change='CHANGED'})}
}
$current=@(Get-ChildItem $runtimeRoot -File -Recurse |Where-Object {!$_.FullName.StartsWith((Join-Path $runtimeRoot 'MasteryDev')+'\',[StringComparison]::OrdinalIgnoreCase)})+
 @(Get-ChildItem 'C:\ValheimModDev','C:\Users\Domesos\AppData\LocalLow\IronGate\Valheim' -File -Recurse)+
 @(Get-ChildItem (Join-Path $devRoot 'src-modern'),(Join-Path $devRoot 'artifacts\reference'),(Join-Path $devRoot 'bin\Perks123Client'),(Join-Path $devRoot 'bin\Perks123Server') -File -Recurse)+
 @(Get-Item (Join-Path $devRoot 'ValheimMasteryPoC.csproj'),(Join-Path $devRoot 'NuGet.config'))
foreach($file in $current){if(!$known.ContainsKey($file.FullName)){$changes.Add([pscustomobject]@{Path=$file.FullName;Change='NEW'})}}
if($changes.Count){$changes|Export-Csv (Join-Path $evidence 'protected-changes.csv') -NoTypeInformation}
$usersAfter=@(Get-LocalUser|Select-Object Name,@{Name='SID';Expression={$_.SID.Value}},Enabled)
$usersAfter|Export-Csv (Join-Path $evidence 'accounts-after.csv') -NoTypeInformation
& reg.exe export 'HKCU\Software\IronGate\Valheim' (Join-Path $evidence 'production-playerprefs-after.reg') /y
if($LASTEXITCODE){throw 'After PlayerPrefs snapshot failed'}
$prefsMatch=(Get-FileHash (Join-Path $evidence 'production-playerprefs-before.reg')).Hash -eq (Get-FileHash (Join-Path $evidence 'production-playerprefs-after.reg')).Hash
$accountsMatch=(Get-FileHash (Join-Path $evidence 'accounts-before.csv')).Hash -eq (Get-FileHash (Join-Path $evidence 'accounts-after.csv')).Hash
[pscustomobject]@{ProtectedFiles=$before.Count;FileChanges=$changes.Count;Baseline='prior smoke protected-before.csv; no client test launched in this task';ProductionRegistryUnchanged=$prefsMatch;AccountsUnchanged=$accountsMatch;AccountCreated=$false;SourceCs=@(Get-ChildItem (Join-Path $devRoot 'src-modern') -Recurse -Filter '*.cs').Count}|Export-Csv (Join-Path $evidence 'integrity-result.csv') -NoTypeInformation
Get-FileHash (Join-Path $evidence 'production-playerprefs-before.reg'),(Join-Path $evidence 'production-playerprefs-after.reg')|Select-Object Path,Hash|Export-Csv (Join-Path $evidence 'playerprefs-hashes.csv') -NoTypeInformation
Get-CimInstance Win32_Process|Where-Object Name -match '^valheim.*\.exe$'|Select-Object ProcessId,Name,ExecutablePath|Export-Csv (Join-Path $evidence 'valheim-processes.csv') -NoTypeInformation
Import-Csv (Join-Path $evidence 'integrity-result.csv')|Format-List

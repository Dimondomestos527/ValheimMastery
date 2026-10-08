param([ValidateSet('Baseline','Copy','Integrity')][string]$Stage)
$ErrorActionPreference='Stop'
$devRoot=Split-Path -Parent $PSScriptRoot
$runtimeRoot='C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server'
$clientRoot='C:\ValheimModDev'
$evidence=Join-Path $devRoot 'validation\unity-smoke-20261004'
if(!(Test-Path $evidence)){New-Item -ItemType Directory -Path $evidence|Out-Null}
function ProtectedFiles {
 @(Get-ChildItem $runtimeRoot -File -Recurse | Where-Object {!$_.FullName.StartsWith((Join-Path $runtimeRoot 'MasteryDev')+'\',[StringComparison]::OrdinalIgnoreCase)})+
 @(Get-ChildItem $clientRoot -File -Recurse)+
 @(Get-ChildItem 'C:\Users\Domesos\AppData\LocalLow\IronGate\Valheim' -File -Recurse)+
 @(Get-ChildItem (Join-Path $devRoot 'src-modern'),(Join-Path $devRoot 'artifacts\reference'),(Join-Path $devRoot 'bin\Perks123Client'),(Join-Path $devRoot 'bin\Perks123Server') -File -Recurse)+
 @(Get-Item (Join-Path $devRoot 'ValheimMasteryPoC.csproj'),(Join-Path $devRoot 'NuGet.config'))
}
function Fingerprint($file){[pscustomobject]@{Path=$file.FullName;SHA256=(Get-FileHash -LiteralPath $file.FullName).Hash;Length=$file.Length;LastWriteUtc=$file.LastWriteTimeUtc.ToString('o')}}
if($Stage -eq 'Baseline'){
 $path=Join-Path $evidence 'protected-before.csv';if(Test-Path $path){throw 'Baseline already exists; never overwrite'}
 @(foreach($file in ProtectedFiles){Fingerprint $file})|Export-Csv $path -NoTypeInformation
 Get-CimInstance Win32_Process | Where-Object Name -match '^valheim.*\.exe$' | Select-Object ProcessId,Name,ExecutablePath,CommandLine | Export-Csv (Join-Path $evidence 'processes-before.csv') -NoTypeInformation
 Write-Output "Baseline files=$(@(Import-Csv $path).Count) sourceCs=$(@(Get-ChildItem (Join-Path $devRoot 'src-modern') -Recurse -Filter '*.cs').Count)";exit
}
if($Stage -eq 'Integrity'){
 $before=@(Import-Csv (Join-Path $evidence 'protected-before.csv'));$changes=[Collections.Generic.List[object]]::new();$known=@{}
 foreach($entry in $before){$known[$entry.Path]=$true;if(!(Test-Path -LiteralPath $entry.Path)){$changes.Add([pscustomobject]@{Path=$entry.Path;Change='MISSING'});continue};$now=Fingerprint (Get-Item -LiteralPath $entry.Path);if($entry.SHA256 -ne $now.SHA256 -or $entry.Length -ne $now.Length -or $entry.LastWriteUtc -ne $now.LastWriteUtc){$changes.Add([pscustomobject]@{Path=$entry.Path;Change='CHANGED'})}}
 foreach($file in ProtectedFiles){if(!$known.ContainsKey($file.FullName)){$changes.Add([pscustomobject]@{Path=$file.FullName;Change='NEW'})}}
 [pscustomobject]@{Protected=$before.Count;Changes=$changes.Count;SourceCs=@(Get-ChildItem (Join-Path $devRoot 'src-modern') -Recurse -Filter '*.cs').Count}|Export-Csv (Join-Path $evidence 'integrity-result.csv') -NoTypeInformation
 if($changes.Count){$changes|Export-Csv (Join-Path $evidence 'integrity-changes.csv') -NoTypeInformation}
 Write-Output "Integrity files=$($before.Count) changes=$($changes.Count)";exit
}
if(!(Test-Path (Join-Path $evidence 'protected-before.csv'))){throw 'Baseline required before copy'}
$manifest=[Collections.Generic.List[object]]::new()
foreach($variant in 'client','server'){
 $origin=if($variant -eq 'client'){$clientRoot}else{$runtimeRoot}
 $target=Join-Path $evidence "runtimes\$variant"
 if(Test-Path $target){throw "Destination already exists: $target"}
 $dirs=@('BepInEx\core','BepInEx\patchers','MonoBleedingEdge')
 $dirs+=if($variant -eq 'client'){'valheim_Data';'D3D12'}else{'valheim_server_Data'}
 foreach($dir in $dirs){$source=Join-Path $origin $dir;if(Test-Path $source){foreach($file in Get-ChildItem -LiteralPath $source -File -Recurse){$relative=$file.FullName.Substring($origin.Length+1);$manifest.Add([pscustomobject]@{Variant=$variant;Source=$file.FullName;Destination=(Join-Path $target $relative);SHA256=(Get-FileHash -LiteralPath $file.FullName).Hash})}}}
 $files=@('UnityPlayer.dll','UnityCrashHandler64.exe','winhttp.dll','.doorstop_version','doorstop_config.ini','steam_appid.txt')
 $files+=if($variant -eq 'client'){'valheim.exe'}else{'valheim_server.exe';'steamclient.dll';'steamclient64.dll';'steamwebrtc.dll';'steamwebrtc64.dll';'tier0_s.dll';'tier0_s64.dll';'vstdlib_s.dll';'vstdlib_s64.dll'}
 foreach($name in $files){$source=Join-Path $origin $name;if(!(Test-Path $source)){throw "Required runtime file missing: $source"};$manifest.Add([pscustomobject]@{Variant=$variant;Source=$source;Destination=(Join-Path $target $name);SHA256=(Get-FileHash -LiteralPath $source).Hash})}
 $build=if($variant -eq 'client'){'Perks123Client'}else{'Perks123Server'}
 $source=Join-Path $devRoot "bin\$build\ValheimMastery.dll"
 $manifest.Add([pscustomobject]@{Variant=$variant;Source=$source;Destination=(Join-Path $target 'BepInEx\plugins\ValheimMastery\ValheimMastery.dll');SHA256=(Get-FileHash -LiteralPath $source).Hash})
}
$manifest|Export-Csv (Join-Path $evidence 'copy-manifest-before.csv') -NoTypeInformation
foreach($item in $manifest){$parent=Split-Path -Parent $item.Destination;if(!(Test-Path $parent)){New-Item -ItemType Directory -Path $parent -Force|Out-Null};if(Test-Path -LiteralPath $item.Destination){throw "No overwrite allowed: $($item.Destination)"};Copy-Item -LiteralPath $item.Source -Destination $item.Destination}
$verified=@(foreach($item in $manifest){$hash=(Get-FileHash -LiteralPath $item.Destination).Hash;[pscustomobject]@{Variant=$item.Variant;Destination=$item.Destination;SHA256=$hash;Matches=($hash -eq $item.SHA256)}})
$verified|Export-Csv (Join-Path $evidence 'copy-verification.csv') -NoTypeInformation
if(@($verified|Where-Object {!$_.Matches}).Count){throw 'Copied binary integrity failure'}
foreach($variant in 'client','server'){foreach($dir in 'BepInEx\config','BepInEx\cache','saves','logs'){New-Item -ItemType Directory -Path (Join-Path $evidence "runtimes\$variant\$dir") -Force|Out-Null}}
Write-Output "Copied and SHA256 verified $($verified.Count) runtime files; no production configs/state/worlds copied."

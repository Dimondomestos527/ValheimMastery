param([ValidateSet('Baseline','Integrity','Diagnose')][string]$Stage)
$ErrorActionPreference='Stop'
$devRoot=Split-Path -Parent $PSScriptRoot
$runtimeRoot='C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server'
$evidence=Join-Path $devRoot 'validation\tooling-blockers-20261004'
if(!(Test-Path $evidence)){New-Item -ItemType Directory -Path $evidence|Out-Null}
function ProtectedFiles {
 @(Get-ChildItem $runtimeRoot -File -Recurse|Where-Object {!$_.FullName.StartsWith((Join-Path $runtimeRoot 'MasteryDev')+'\',[StringComparison]::OrdinalIgnoreCase)})+
 @(Get-ChildItem 'C:\ValheimModDev\BepInEx','C:\Users\Domesos\AppData\LocalLow\IronGate\Valheim\worlds_local\DEEEP' -File -Recurse)+
 @(Get-ChildItem (Join-Path $devRoot 'src-modern'),(Join-Path $devRoot 'artifacts\reference') -File -Recurse)+
 @(Get-Item (Join-Path $devRoot 'ValheimMasteryPoC.csproj'),(Join-Path $devRoot 'NuGet.config'))
}
function Fingerprint($file){[pscustomobject]@{Path=$file.FullName;SHA256=(Get-FileHash -LiteralPath $file.FullName).Hash;Length=$file.Length;LastWriteUtc=$file.LastWriteTimeUtc.ToString('o')}}
if($Stage -eq 'Baseline'){
 $path=Join-Path $evidence 'protected-before.csv';if(Test-Path $path){throw 'Baseline exists'}
 @(foreach($file in ProtectedFiles){Fingerprint $file})|Export-Csv $path -NoTypeInformation
 @(Get-ChildItem (Join-Path $devRoot 'bin\Perks123Client'),(Join-Path $devRoot 'bin\Perks123Server') -File|ForEach-Object {Fingerprint $_})|Export-Csv (Join-Path $evidence 'builds-before.csv') -NoTypeInformation
 Write-Output "Baseline protected=$(@(Import-Csv $path).Count); gameplay C#=$(@(Get-ChildItem (Join-Path $devRoot 'src-modern') -Recurse -Filter '*.cs').Count)";exit
}
if($Stage -eq 'Integrity'){
 $before=@(Import-Csv (Join-Path $evidence 'protected-before.csv'));$changes=[Collections.Generic.List[object]]::new();$known=@{}
 foreach($entry in $before){$known[$entry.Path]=$true;if(!(Test-Path $entry.Path)){$changes.Add([pscustomobject]@{Path=$entry.Path;Change='MISSING'});continue};$now=Fingerprint (Get-Item $entry.Path);if($entry.SHA256 -ne $now.SHA256 -or $entry.Length -ne $now.Length -or $entry.LastWriteUtc -ne $now.LastWriteUtc){$changes.Add([pscustomobject]@{Path=$entry.Path;Change='CHANGED'})}}
 foreach($file in ProtectedFiles){if(!$known.ContainsKey($file.FullName)){$changes.Add([pscustomobject]@{Path=$file.FullName;Change='NEW'})}}
 [pscustomobject]@{Protected=$before.Count;Changes=$changes.Count;SourceCs=@(Get-ChildItem (Join-Path $devRoot 'src-modern') -Recurse -Filter '*.cs').Count}|Export-Csv (Join-Path $evidence 'integrity-result.csv') -NoTypeInformation
 if($changes.Count){$changes|Export-Csv (Join-Path $evidence 'integrity-changes.csv') -NoTypeInformation}
 Write-Output "Integrity protected=$($before.Count),changes=$($changes.Count)";exit
}
$oldEnvironment=@{}
$localEnvironment=@{NUGET_PACKAGES=Join-Path $devRoot '.nuget\packages';DOTNET_CLI_HOME=Join-Path $devRoot '.dotnet-home';DOTNET_CLI_TELEMETRY_OPTOUT='1';TEMP=Join-Path $evidence 'temp';TMP=Join-Path $evidence 'temp'}
if(!(Test-Path $localEnvironment.TEMP)){New-Item -ItemType Directory -Path $localEnvironment.TEMP|Out-Null}
foreach($name in $localEnvironment.Keys){$oldEnvironment[$name]=[Environment]::GetEnvironmentVariable($name,'Process');[Environment]::SetEnvironmentVariable($name,$localEnvironment[$name],'Process')}
Push-Location $devRoot
try {
 & dotnet msbuild ValheimMasteryPoC.csproj -t:ResolveReferences -v:diag -p:GameManaged="$runtimeRoot\valheim_server_Data\Managed" -p:BaseIntermediateOutputPath="$devRoot/obj/Perks123Server/" -p:MSBuildProjectExtensionsPath="$devRoot/obj/Perks123Server/" -p:OutputPath='bin\Perks123Server\' -nr:false > (Join-Path $evidence 'server-resolve-before.diag.log') 2>&1
 Write-Output "ResolveReferences diagnostic exit=$LASTEXITCODE"
}finally{Pop-Location;foreach($name in $oldEnvironment.Keys){[Environment]::SetEnvironmentVariable($name,$oldEnvironment[$name],'Process')}}

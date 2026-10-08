param([ValidateSet('Baseline','Compare')][string]$Stage)
$ErrorActionPreference='Stop'
$serverRoot='C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server'
$projectRoot=Split-Path -Parent $PSScriptRoot
$evidence=Join-Path $projectRoot 'validation\independent-build-20261004'
if(!(Test-Path $evidence)){New-Item -ItemType Directory -Path $evidence | Out-Null}
function ProtectedFiles {
    @(Get-ChildItem $serverRoot -File -Recurse | Where-Object {!$_.FullName.StartsWith((Join-Path $serverRoot 'MasteryDev')+'\',[StringComparison]::OrdinalIgnoreCase)}) +
    @(Get-ChildItem 'C:\ValheimModDev\BepInEx' -File -Recurse) +
    @(Get-ChildItem 'C:\Users\Domesos\AppData\LocalLow\IronGate\Valheim\worlds_local\DEEEP' -File -Recurse)
}
function Fingerprint($path){$f=Get-Item -LiteralPath $path;[pscustomobject]@{Path=$f.FullName;Length=$f.Length;LastWriteUtc=$f.LastWriteTimeUtc.ToString('o');SHA256=(Get-FileHash -LiteralPath $path).Hash}}
function ExportNew($data,$path){if(Test-Path $path){throw "Existing evidence: $path"};$data|Export-Csv -LiteralPath $path -NoTypeInformation -Encoding utf8}
if($Stage -eq 'Baseline'){
    if((Test-Path (Join-Path $projectRoot 'obj')) -or (Test-Path (Join-Path $projectRoot 'bin'))){throw 'Not a clean initial workspace.'}
    $baseline=@(foreach($f in ProtectedFiles){Fingerprint $f.FullName})
    ExportNew $baseline (Join-Path $evidence 'protected-before.csv')
    $immutable=@(Get-ChildItem (Join-Path $projectRoot 'src-modern'),(Join-Path $projectRoot 'artifacts\reference') -File -Recurse)+@(Get-Item (Join-Path $projectRoot 'ValheimMasteryPoC.csproj'),(Join-Path $projectRoot 'NuGet.config'))
    ExportNew @(foreach($f in $immutable){Fingerprint $f.FullName}) (Join-Path $evidence 'immutable-dev-before.csv')
    $xml=[xml](Get-Content (Join-Path $projectRoot 'ValheimMasteryPoC.csproj') -Raw)
    $refs=@(foreach($variant in @('client','server')){
        $managed=if($variant -eq 'client'){'C:\ValheimModDev\valheim_Data\Managed'}else{Join-Path $serverRoot 'valheim_server_Data\Managed'}
        foreach($ref in $xml.Project.ItemGroup.Reference){if($ref.HintPath){$path=$ref.HintPath.Replace('$(GameManaged)',$managed);if(!(Test-Path -LiteralPath $path)){throw "Missing dependency $path"};$fp=Fingerprint $path;[pscustomobject]@{Variant=$variant;Path=$fp.Path;Length=$fp.Length;SHA256=$fp.SHA256}}}
    })
    ExportNew $refs (Join-Path $evidence 'managed-reference-inputs.csv')
    Write-Output "BASELINE: protected=$($baseline.Count), immutable-dev=$($immutable.Count), explicit game refs=$($refs.Count), clean bin/obj confirmed."
    exit
}
$changes=[Collections.Generic.List[object]]::new()
foreach($csv in @('protected-before.csv','immutable-dev-before.csv')){
    $baseline=@(Import-Csv (Join-Path $evidence $csv))
    foreach($entry in $baseline){if(!(Test-Path $entry.Path)){$changes.Add([pscustomobject]@{Path=$entry.Path;Change='MISSING'});continue};$now=Fingerprint $entry.Path;if($now.SHA256 -ne $entry.SHA256 -or $now.Length -ne $entry.Length -or $now.LastWriteUtc -ne $entry.LastWriteUtc){$changes.Add([pscustomobject]@{Path=$entry.Path;Change='CHANGED'})}}
}
$known=@{};foreach($entry in Import-Csv (Join-Path $evidence 'protected-before.csv')){$known[$entry.Path]=$true};foreach($f in ProtectedFiles){if(!$known.ContainsKey($f.FullName)){$changes.Add([pscustomobject]@{Path=$f.FullName;Change='NEW_PROTECTED_FILE'})}}
foreach($entry in Import-Csv (Join-Path $evidence 'managed-reference-inputs.csv')){if((Get-FileHash $entry.Path).Hash -ne $entry.SHA256){$changes.Add([pscustomobject]@{Path=$entry.Path;Change='DEPENDENCY_CHANGED'})}}
if($changes.Count){ExportNew $changes (Join-Path $evidence 'integrity-changes.csv')}
Add-Type -Path (Join-Path $serverRoot 'BepInEx\core\Mono.Cecil.dll')
function AllTypes($types){foreach($t in $types){$t;AllTypes $t.NestedTypes}}
function MethodMap($assembly){
    $map=@{}
    foreach($type in AllTypes $assembly.MainModule.Types){foreach($method in $type.Methods){
        $body=if($method.HasBody){($method.Body.Instructions|ForEach-Object {$_.ToString()}) -join "`n"}else{'<no body>'}
        $locals=if($method.HasBody){($method.Body.Variables|ForEach-Object {$_.VariableType.FullName}) -join ';'}else{''}
        $handlers=if($method.HasBody){($method.Body.ExceptionHandlers|ForEach-Object {"$($_.HandlerType):$($_.TryStart.Offset):$($_.TryEnd.Offset):$($_.HandlerStart.Offset):$($_.HandlerEnd.Offset):$($_.CatchType.FullName)"}) -join ';'}else{''}
        $map[$method.FullName]="$($method.Attributes)|$($method.ImplAttributes)|$locals|$handlers|$body"
    }}
    return ,$map
}
function Structure($assembly){
    @(foreach($t in AllTypes $assembly.MainModule.Types){
        "TYPE:$($t.FullName):$($t.Attributes):$($t.BaseType.FullName)"
        foreach($i in $t.Interfaces){"INTERFACE:$($t.FullName):$($i.InterfaceType.FullName)"}
        foreach($f in $t.Fields){"FIELD:$($f.FullName):$($f.Attributes):$($f.Constant)"}
        foreach($p in $t.Properties){"PROPERTY:$($p.FullName):$($p.Attributes)"}
        foreach($e in $t.Events){"EVENT:$($e.FullName):$($e.Attributes)"}
    }) | Sort-Object
}
$comparisons=@(foreach($variant in @('client','server')){
    $folder=if($variant -eq 'client'){'Perks123Client'}else{'Perks123Server'}
    $newPath=Join-Path $projectRoot "bin\$folder\ValheimMastery.dll"
    $oldPath=Join-Path $projectRoot "artifacts\reference\1.4.123\$variant\ValheimMastery.dll"
    $new=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($newPath);$old=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($oldPath)
    try{
        $newMethods=MethodMap $new;$oldMethods=MethodMap $old
        $keys=@(@($newMethods.Keys)+@($oldMethods.Keys)|Sort-Object -Unique)
        $diff=@(foreach($key in $keys){if(!$newMethods.ContainsKey($key)){'REMOVED '+$key}elseif(!$oldMethods.ContainsKey($key)){'ADDED '+$key}elseif($newMethods[$key] -cne $oldMethods[$key]){'CHANGED '+$key}})
        $structureDiff=@(Compare-Object (Structure $old) (Structure $new))
        $oldRefs=@($old.MainModule.AssemblyReferences|ForEach-Object {$_.FullName}|Sort-Object)
        $newRefs=@($new.MainModule.AssemblyReferences|ForEach-Object {$_.FullName}|Sort-Object)
        $plugin=($new.MainModule.Types|Where-Object Name -eq 'MasteryPlugin').Fields|Where-Object Name -eq 'Version'
        $oldPlugin=($old.MainModule.Types|Where-Object Name -eq 'MasteryPlugin').Fields|Where-Object Name -eq 'Version'
        [pscustomobject]@{Variant=$variant;OldPath=$oldPath;NewPath=$newPath;OldVersion=$old.Name.Version.ToString();NewVersion=$new.Name.Version.ToString();OldPluginVersion=$oldPlugin.Constant;NewPluginVersion=$plugin.Constant;OldSHA256=(Get-FileHash $oldPath).Hash;NewSHA256=(Get-FileHash $newPath).Hash;OldSize=(Get-Item $oldPath).Length;NewSize=(Get-Item $newPath).Length;OldMVID=$old.MainModule.Mvid.ToString();NewMVID=$new.MainModule.Mvid.ToString();OldArchitecture=$old.MainModule.Architecture.ToString();NewArchitecture=$new.MainModule.Architecture.ToString();OldAttributes=$old.MainModule.Attributes.ToString();NewAttributes=$new.MainModule.Attributes.ToString();OldRuntime=$old.MainModule.RuntimeVersion;NewRuntime=$new.MainModule.RuntimeVersion;OldTargetFramework=($old.CustomAttributes|Where-Object {$_.AttributeType.FullName -eq 'System.Runtime.Versioning.TargetFrameworkAttribute'}).ConstructorArguments[0].Value;NewTargetFramework=($new.CustomAttributes|Where-Object {$_.AttributeType.FullName -eq 'System.Runtime.Versioning.TargetFrameworkAttribute'}).ConstructorArguments[0].Value;OldReferences=$oldRefs;NewReferences=$newRefs;ReferenceDifferences=@(Compare-Object $oldRefs $newRefs).Count;OldMethodCount=$oldMethods.Count;NewMethodCount=$newMethods.Count;MethodDifferences=$diff;StructureDifferenceCount=$structureDiff.Count;EmbeddedResourceCount=$new.MainModule.Resources.Count}
    }finally{$new.Dispose();$old.Dispose()}
})
$result=[pscustomobject]@{OriginalChanges=$changes.Count;Comparisons=$comparisons}
$jsonPath=Join-Path $evidence 'static-comparison.json';if(Test-Path $jsonPath){throw 'Comparison evidence already exists'};$result|ConvertTo-Json -Depth 8|Out-File -LiteralPath $jsonPath -Encoding utf8
$result|ConvertTo-Json -Depth 8

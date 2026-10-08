$ErrorActionPreference='Stop'
$devRoot=Split-Path -Parent $PSScriptRoot
$evidence=Join-Path $devRoot 'validation\tooling-blockers-20261004'
Add-Type -Path (Join-Path $devRoot '.nuget\packages\mono.cecil\0.11.5\lib\netstandard2.0\Mono.Cecil.dll')
function Types($items){foreach($type in $items){$type;Types $type.NestedTypes}}
function Methods($assembly){
 $map=@{}
 foreach($type in Types $assembly.MainModule.Types){foreach($method in $type.Methods){
  $body=if($method.HasBody){($method.Body.Instructions|ForEach-Object ToString)-join "`n"}else{'<no body>'}
  $locals=if($method.HasBody){($method.Body.Variables|ForEach-Object {$_.VariableType.FullName})-join ';'}else{''}
  $handlers=if($method.HasBody){($method.Body.ExceptionHandlers|ForEach-Object {"$($_.HandlerType):$($_.TryStart.Offset):$($_.TryEnd.Offset):$($_.HandlerStart.Offset):$($_.HandlerEnd.Offset):$($_.CatchType.FullName)"})-join ';'}else{''}
  $map[$method.FullName]="$($method.Attributes)|$($method.ImplAttributes)|$locals|$handlers|$body"
 }}
 return ,$map
}
$results=@(foreach($variant in @('Client','Server')){
 $new=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $devRoot "bin\Perks123$variant\ValheimMastery.dll"))
 $old=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $devRoot "artifacts\reference\1.4.123\$($variant.ToLowerInvariant())\ValheimMastery.dll"))
 try{
  $newMap=Methods $new;$oldMap=Methods $old
  $keys=@(@($newMap.Keys)+@($oldMap.Keys)|Sort-Object -Unique)
  $diff=@(foreach($key in $keys){if(!$newMap.ContainsKey($key)){'REMOVED '+$key}elseif(!$oldMap.ContainsKey($key)){'ADDED '+$key}elseif($newMap[$key] -cne $oldMap[$key]){'CHANGED '+$key}})
  [pscustomobject]@{Variant=$variant;PluginVersion=($new.MainModule.Types|Where-Object Name -eq 'MasteryPlugin').Fields.Where({$_.Name -eq 'Version'})[0].Constant;OldMethods=$oldMap.Count;NewMethods=$newMap.Count;Differences=$diff;ReferenceDifferences=@(Compare-Object @($old.MainModule.AssemblyReferences|ForEach-Object FullName|Sort-Object) @($new.MainModule.AssemblyReferences|ForEach-Object FullName|Sort-Object)).Count}
 }finally{$new.Dispose();$old.Dispose()}
})
$results|ConvertTo-Json -Depth 6|Out-File (Join-Path $evidence 'reference-il-comparison.json') -Encoding utf8
$results|ConvertTo-Json -Depth 6

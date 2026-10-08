param([string]$EvidenceDirectory='')
$ErrorActionPreference='Stop'
$devRoot=Split-Path -Parent $PSScriptRoot
$runtimeRoot='C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server'
$evidence=if($EvidenceDirectory){[IO.Path]::GetFullPath($EvidenceDirectory)}else{Join-Path $devRoot 'validation\tooling-blockers-20261004'}
if(!$evidence.StartsWith((Join-Path $devRoot 'validation')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Evidence must stay under MasteryDev validation'}
Add-Type -Path (Join-Path $devRoot '.nuget\packages\mono.cecil\0.11.5\lib\netstandard2.0\Mono.Cecil.dll')
function Metadata($variant,$kind,$path){
 $module=[Mono.Cecil.ModuleDefinition]::ReadModule($path)
 try{[pscustomobject]@{Variant=$variant;Kind=$kind;Path=$path;SHA256=(Get-FileHash -LiteralPath $path).Hash;MVID=$module.Mvid.ToString();References=(@($module.AssemblyReferences|ForEach-Object FullName|Sort-Object)-join ';')}}finally{$module.Dispose()}
}
$project=[xml](Get-Content (Join-Path $devRoot 'ValheimMasteryPoC.csproj') -Raw)
$records=[Collections.Generic.List[object]]::new();$results=[Collections.Generic.List[object]]::new()
foreach($variant in @('Client','Server')){
 $managed=if($variant -eq 'Client'){'C:\ValheimModDev\valheim_Data\Managed'}else{Join-Path $runtimeRoot 'valheim_server_Data\Managed'}
 $pre=@(Get-Content (Join-Path $evidence "Perks123$variant.pre-publicize.txt"))
 $compiler=@(Get-Content (Join-Path $evidence "Perks123$variant.compiler-references.txt"))
 foreach($ref in $project.Project.ItemGroup.Reference|Where-Object {$_.HintPath}){
  $rawPath=[IO.Path]::GetFullPath($ref.HintPath.Replace('$(GameManaged)',$managed))
  $raw=Metadata $variant 'RAW' $rawPath;$records.Add($raw)
  $resolved=@($pre|Where-Object {($_ -split '\|')[1] -eq $ref.Include})
  if($resolved.Count -ne 1 -or [IO.Path]::GetFullPath(($resolved[0]-split '\|')[0]) -ne $rawPath){throw "Raw reference substitution: $variant $($ref.Include) $resolved"}
  $effective=@($compiler|Where-Object {[IO.Path]::GetFileName(($_ -split '\|')[0]) -eq ($ref.Include+'.dll')})
  if($effective.Count -ne 1){throw "Missing compiler reference: $variant $($ref.Include)"}
  $effectivePath=[IO.Path]::GetFullPath(($effective[0]-split '\|')[0])
  if($ref.Publicize -eq 'true'){
   $expected=Join-Path $devRoot "obj\isolated123\Perks123$variant\Release\netstandard2.1\publicized\$($ref.Include).dll"
   if($effectivePath -ne $expected){throw "Publicizer output substitution: $effectivePath"}
   $generated=Metadata $variant 'PUBLICIZED' $expected;$records.Add($generated)
   if($generated.MVID -ne $raw.MVID -or $generated.References -ne $raw.References){throw "Origin mismatch: $variant $($ref.Include)"}
   $results.Add([pscustomobject]@{Variant=$variant;Assembly=$ref.Include;RawPath=$raw.Path;GeneratedPath=$generated.Path;MvidMatch=$true;ReferenceGraphMatch=$true;CompilerPathMatch=$true})
  }elseif($effectivePath -ne $rawPath){throw "Unpublicized compiler substitution: $variant $effectivePath"}
 }
 $other=if($variant -eq 'Client'){'Server'}else{'Client'}
 if(@($compiler|Where-Object {$_ -like "*Perks123$other*"}).Count){throw "Cross-variant compiler dependency $variant"}
 $records.Add((Metadata $variant 'PLUGIN' (Join-Path $devRoot "bin\Perks123$variant\ValheimMastery.dll")))
}
$records|Export-Csv (Join-Path $evidence 'dependency-fingerprints.csv') -NoTypeInformation
$results|Export-Csv (Join-Path $evidence 'publicizer-provenance.csv') -NoTypeInformation
Write-Output "PASS: $($records.Count) fingerprints; $($results.Count) generated inputs match raw origin; every explicit compiler input verified; no cross-variant generated dependency."

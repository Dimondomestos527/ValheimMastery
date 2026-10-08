param([ValidateSet('Client','Server')][string]$Variant='Client')
$ErrorActionPreference='Stop'
$engine=Join-Path $PSScriptRoot 'mastery_update.py'
if(!(Test-Path -LiteralPath $engine)){throw 'Keep mastery_update.py beside this script (extract the entire updater ZIP).'}
$python=$null
foreach($name in @('py','python','python3')){
  $candidate=Get-Command $name -ErrorAction SilentlyContinue
  if(!$candidate){continue}
  $prefix=@();if($name -eq 'py'){$prefix=@('-3')}
  & $candidate.Source @prefix -c 'import sys; sys.exit(0 if sys.version_info >= (3,9) else 1)' 2>$null
  if($LASTEXITCODE -eq 0){$python=$candidate.Source;break}
}
if(!$python){throw 'Install Python 3.9 or newer, then run this updater again.'}
& $python @prefix $engine --variant $Variant --target $PSScriptRoot
if($LASTEXITCODE -ne 0){throw 'Update failed; read the message above.'}

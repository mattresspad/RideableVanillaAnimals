param([Parameter(Mandatory)][string]$TrialRoot,[Parameter(Mandatory)][string]$FrameworkZip,[Parameter(Mandatory)][string]$PackZip,[string]$Kenshi=$env:KENSHI_PATH)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $TrialRoot){throw 'Choose a new trial root.'}
$kenshiRoot=(Resolve-Path -LiteralPath $Kenshi).Path
$root=(New-Item -ItemType Directory -Path $TrialRoot).FullName
$mods=New-Item -ItemType Directory -Path (Join-Path $root 'install/mods') -Force
New-Item -ItemType Junction -Path (Join-Path $root 'install/data') -Target (Join-Path $kenshiRoot 'data') | Out-Null
foreach($name in 'resources.cfg','fcs.def','controls.cfg','screens.cfg','kenshi.cfg','settings.cfg','currentVersion.txt','__tutorials.data'){
 $file=Join-Path $kenshiRoot $name
 if(Test-Path -LiteralPath $file){Copy-Item -LiteralPath $file -Destination (Join-Path $root 'install')}
}
Expand-Archive -LiteralPath $FrameworkZip -DestinationPath $mods.FullName
Expand-Archive -LiteralPath $PackZip -DestinationPath $mods.FullName
$manifest=Get-Content -Raw -LiteralPath (Join-Path $mods.FullName 'Meitou Transport/meitou.json') | ConvertFrom-Json
if($manifest.id -ne 'meitou.transport' -or $manifest.version -ne '0.3.0-playtest.6' -or $manifest.gameVersion -ne '=0.22.1'){throw 'Expected published Transport playtest.6 for v0.22.1.'}
[IO.File]::WriteAllLines((Join-Path $root 'mods.cfg'),[string[]]@('Meitou Transport.mod','Rideable Vanilla Animals.mod'))
Get-FileHash -Algorithm SHA256 -LiteralPath $FrameworkZip,$PackZip | Select-Object Path,Hash

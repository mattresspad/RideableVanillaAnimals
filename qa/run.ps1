param([Parameter(Mandatory)][string]$Game,[Parameter(Mandatory)][string]$TrialRoot,[Parameter(Mandatory)][string[]]$Cases,[ValidateSet(1,4)][int]$Workers=4)
$ErrorActionPreference='Stop'
$base=(Resolve-Path -LiteralPath $TrialRoot).Path
$game=Join-Path (Resolve-Path -LiteralPath $Game).Path 'MeitouClient.exe'
$product=(Get-Item -LiteralPath $game).VersionInfo.ProductVersion
if($product -ne '0.22.1+9ee4074bbc6fd7fdeb26108a04afc9f90cb1dc01'){throw "Game mismatch $product"}
$env:DOTNET_STARTUP_HOOKS=(Resolve-Path "$PSScriptRoot/Ride/bin/Release/net11.0/RideableAnimals.Verification.dll").Path
$env:DOTNET_PROCESSOR_COUNT=[string]$Workers
$env:KENSHI_PATH="$base\install"
$env:MEITOU_MODS_CFG="$base\mods.cfg"
foreach($name in $Cases){
    if($name -notmatch '^[a-z0-9-]+$'){throw 'Invalid QA case'}
    $t=Join-Path $base $name
    if(Test-Path -LiteralPath $t){throw "Choose a fresh trial $t"}
    $env:RVA_CASES=(Resolve-Path "$PSScriptRoot/fixtures/$name.json").Path
    $env:RVA_MODE=if($name -in 'wild','trader'){$name}else{'roster'}
    $control=New-Item -ItemType Directory -Path (Join-Path $t 'control') -Force
    $scratch=New-Item -ItemType Directory -Path (Join-Path $t 'temp') -Force
    $env:TEMP=$scratch.FullName;$env:TMP=$scratch.FullName
    $log=Join-Path $t 'client.log'
    @{version=2;fullScreen=$false;fpsLimit=24;vSync=$false;splashScreens=$false;simThreads=$Workers;sandboxPanelCollapsed=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $t 'meitou.user.json') -Encoding utf8
    $start=if($name -eq 'wild'){'--sandbox 2 --sandbox-character 1995-gamedata.base'}else{'--new-game "The Wandering Trader"'}
    $arguments="$start --headless --no-load-cache --no-precompile --no-foliage --no-gi --no-audio --water-reflection 0 --shadow-quality 0 --texture-quality 1 --fps-limit 24 --size 1088x704 --control-dir ""$($control.FullName)"" --mods-cfg ""$env:MEITOU_MODS_CFG"""
    $proc=Start-Process -FilePath $game -ArgumentList $arguments -WorkingDirectory $t -WindowStyle Hidden -RedirectStandardOutput $log -RedirectStandardError (Join-Path $t 'errors.log') -PassThru
    try{
        Write-Output "$product $name PID $($proc.Id)"
        $deadline=[DateTime]::UtcNow.AddMinutes(9)
        while(!$proc.HasExited -and [DateTime]::UtcNow -lt $deadline){
            if(Select-String -LiteralPath $log -Pattern '^ROSTER (PASS|FAIL)' -Quiet){break}
            Start-Sleep -Milliseconds 750
        }
        $line=Select-String -LiteralPath $log -Pattern '^ROSTER (PASS|FAIL)' | Select-Object -Last 1
        if($line){Write-Output $line.Line}else{Write-Output "NO RESULT exit=$($proc.HasExited) $log"}
        if(!$line -or $line.Line -notmatch '^ROSTER PASS'){throw "Roster trial failed: $log"}
    }finally{
        if(!$proc.HasExited){[IO.File]::WriteAllText((Join-Path $control.FullName 'cmd'),"qa-roster-end quit"+[Environment]::NewLine)
            if(!$proc.WaitForExit(12000)){$proc.Kill();$proc.WaitForExit()}}
    }
}

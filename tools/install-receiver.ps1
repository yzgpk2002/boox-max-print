param([string]$Adb='E:\codexblender\film-001\tools\platform-tools\adb.exe',[string]$Serial='0123456789ABCDEF')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Set-Location $projectRoot
$installArgs=@('-s',$Serial,'install','-r','-f')
$installArgs+=(Join-Path $projectRoot 'dist\booxmaxprint-wifi.apk')
$result=& $Adb @installArgs 2>&1
Write-Output $result
# KitKat adb install can report failure while returning exit code zero.
if(($result -join "`n") -notmatch '(?m)^Success\s*$'){throw 'APK install failed; see device storage status'}
& $Adb -s $Serial shell am start -n net.booxmax.print/.MainActivity
$env:BOOX_ADB=$Adb
$env:BOOX_SERIAL=$Serial
node tools/pair-wifi.mjs
if($LASTEXITCODE -ne 0){throw 'Pairing failed'}

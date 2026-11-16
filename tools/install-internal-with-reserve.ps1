# KitKat caches the storage reserve threshold at boot. This narrowly scoped
# migration temporarily keeps an 8% reserve, then restores the exact setting.
# It never clears app data or changes the global default install location.
param([string]$Adb='E:\codexblender\film-001\tools\platform-tools\adb.exe',[string]$Serial='0123456789ABCDEF')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Set-Location $projectRoot
$baselinePath=Join-Path $projectRoot '.local\storage-reserve-baseline.json'
function Read-Setting { ((& $Adb -s $Serial shell settings get global sys_storage_threshold_percentage) -join '').Trim() }
function Wait-Boot {
    $deadline=(Get-Date).AddMinutes(4)
    do {
        Start-Sleep -Seconds 3
        $boot=(& $Adb -s $Serial shell getprop sys.boot_completed 2>$null) -join ''
        if($boot.Trim() -eq '1'){return}
    } while((Get-Date) -lt $deadline)
    throw 'BOOX did not complete boot within four minutes; keep USB connected.'
}
$original=Read-Setting
if($original -ne 'null' -and $original -notmatch '^\d+$'){throw 'Cannot read original reserve setting'}
if(Test-Path -LiteralPath $baselinePath){
    $previous=Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
    if($previous.stage -ne 'restored'){throw 'A previous migration needs recovery; inspect .local/storage-reserve-baseline.json before retrying.'}
}
$baseline=[ordered]@{serial=$Serial;original=$original;temporary='8';stage='prepared';started=(Get-Date).ToString('o')}
$baseline | ConvertTo-Json | Set-Content -LiteralPath $baselinePath -Encoding UTF8
$changed=$false
$installSucceeded=$false
try {
    & $Adb -s $Serial shell settings put global sys_storage_threshold_percentage 8
    if((Read-Setting) -ne '8'){throw 'Reserve setting change was not accepted'}
    $changed=$true
    $baseline.stage='temporary-setting';$baseline | ConvertTo-Json | Set-Content -LiteralPath $baselinePath -Encoding UTF8
    Write-Output 'Temporary 8% reserve set. Rebooting BOOX for migration.'
    & $Adb -s $Serial reboot
    Wait-Boot
    & $Adb -s $Serial shell dumpsys devicestoragemonitor | Select-String -Pattern 'mFreeMem=|mMemLowThreshold'
    $output=& $Adb -s $Serial install -r -f (Join-Path $projectRoot 'dist\booxmaxprint-wifi.apk') 2>&1
    Write-Output $output
    if(($output -join "`n") -notmatch '(?m)^Success\s*$'){throw 'Internal upgrade did not succeed'}
    $installed=((& $Adb -s $Serial shell pm path net.booxmax.print) -join '').Trim()
    if($installed -notmatch '^package:/data/app/'){throw 'Receiver is not on internal app storage'}
    Write-Output $installed
    $installSucceeded=$true
} finally {
    if($changed){
        # KitKat's settings CLI has no delete command. Delete only this key.
        if($original -eq 'null'){& $Adb -s $Serial shell content delete --uri content://settings/global/sys_storage_threshold_percentage}
        else {& $Adb -s $Serial shell settings put global sys_storage_threshold_percentage $original}
        if((Read-Setting) -ne $original){throw 'Original reserve was NOT restored; recover from .local/storage-reserve-baseline.json'}
        $baseline.stage='setting-restored-reboot-pending';$baseline | ConvertTo-Json | Set-Content -LiteralPath $baselinePath -Encoding UTF8
        Write-Output 'Original reserve setting restored. Rebooting BOOX for startup validation.'
        & $Adb -s $Serial reboot
        Wait-Boot
        if((Read-Setting) -ne $original){throw 'Reserve differs after final reboot'}
        $baseline.stage='restored';$baseline.installSucceeded=$installSucceeded;$baseline | ConvertTo-Json | Set-Content -LiteralPath $baselinePath -Encoding UTF8
        & $Adb -s $Serial shell dumpsys devicestoragemonitor | Select-String -Pattern 'mFreeMem=|mMemLowThreshold'
    }
}
if($installSucceeded){Write-Output 'Internal receiver upgrade and original reserve restoration completed.'}

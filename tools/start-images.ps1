param([switch]$ServiceOnly)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$ready=$false
try {$health=Invoke-RestMethod http://127.0.0.1:8633/health -TimeoutSec 2;$ready=$health.name -eq 'BOOX Max Images'} catch {}
if(!$ready){
    $logDir=Join-Path $projectRoot 'logs';New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    $node=(Get-Command node.exe).Source
    $process=Start-Process $node -ArgumentList @('"'+(Join-Path $projectRoot 'desktop\image-server.mjs')+'"') -WorkingDirectory $projectRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logDir 'images.log') -RedirectStandardError (Join-Path $logDir 'images-error.log') -PassThru
    $process.Id | Set-Content (Join-Path $logDir 'images.pid')
    for($i=0;$i -lt 25;$i++){
        Start-Sleep -Milliseconds 200
        try {$health=Invoke-RestMethod http://127.0.0.1:8633/health -TimeoutSec 1;if($health.name -eq 'BOOX Max Images' -and (Test-Path (Join-Path $projectRoot '.local\image-client.json'))){$ready=$true;break}} catch {}
        if($process.HasExited){throw 'Image bridge exited; see logs/images-error.log'}
    }
}
if(!$ready){throw 'Image bridge did not become ready'}
if(!$ServiceOnly){
    $exe=Join-Path $projectRoot 'dist\BooxImages.exe'
    if(!(Test-Path $exe)){& (Join-Path $PSScriptRoot 'build-images.ps1')}
    Start-Process $exe -WorkingDirectory $projectRoot
}

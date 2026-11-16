param([int]$Port=8631)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
# Start the image transport at login, but never start screen capture automatically.
try { & (Join-Path $PSScriptRoot 'start-images.ps1') -ServiceOnly } catch { Write-Warning "Image bridge: $($_.Exception.Message)" }
$logDir = Join-Path $projectRoot 'logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
try {
  $health = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/health" -TimeoutSec 2
  if ($health.name -eq 'BOOX Max') { Write-Output 'BOOX Max printer is already running.'; exit 0 }
} catch { }
$nodeExe = (Get-Command node.exe -ErrorAction Stop).Source
$env:BOOX_PORT = "$Port"
$process = Start-Process -FilePath $nodeExe -ArgumentList @('"' + (Join-Path $projectRoot 'desktop\server.mjs') + '"') -WorkingDirectory $projectRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logDir 'printer.log') -RedirectStandardError (Join-Path $logDir 'printer-error.log') -PassThru
$process.Id | Set-Content -LiteralPath (Join-Path $logDir 'printer.pid')
for ($i=0; $i -lt 20; $i++) {
  Start-Sleep -Milliseconds 300
  try {
    $health = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/health" -TimeoutSec 1
    if ($health.name -eq 'BOOX Max') { Write-Output "BOOX Max printer ready on localhost:$Port"; exit 0 }
  } catch { }
  if ($process.HasExited) { throw 'Printer service exited. See logs/printer-error.log.' }
}
throw 'Printer service did not become ready.'

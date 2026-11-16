param([int]$Port=8631)
$ErrorActionPreference = 'Stop'
$printerName = 'BOOX Max'
Start-Transcript -Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'logs\install-printer.log') -Append | Out-Null
$existing = Get-Printer -Name $printerName -ErrorAction SilentlyContinue
if ($existing) { Write-Output 'BOOX Max is already installed.'; $existing | Select-Object Name,DriverName,PortName; exit 0 }
$health = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/health" -TimeoutSec 3
if ($health.name -ne 'BOOX Max') { throw 'Start the BOOX printer service first.' }
Add-Printer -IppURL "http://127.0.0.1:$Port/ipp/print" -ErrorAction Stop
Get-Printer -Name $printerName -ErrorAction Stop | Select-Object Name,DriverName,PortName
Stop-Transcript | Out-Null

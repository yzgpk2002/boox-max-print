$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$pidFile = Join-Path $projectRoot 'logs\printer.pid'
if (!(Test-Path -LiteralPath $pidFile)) { Write-Output 'No BOOX printer process recorded.'; exit 0 }
$printerProcessId = [int](Get-Content -LiteralPath $pidFile)
$process = Get-CimInstance Win32_Process -Filter "ProcessId = $printerProcessId"
if (!$process) { Write-Output 'BOOX printer is already stopped.'; exit 0 }
$expected = Join-Path $projectRoot 'desktop\server.mjs'
if ($process.Name -ne 'node.exe' -or !$process.CommandLine.Contains($expected)) { throw 'PID no longer belongs to BOOX printer; no process was stopped.' }
Stop-Process -Id $printerProcessId
Remove-Item -LiteralPath $pidFile
Write-Output 'BOOX Max printer stopped. The installed printer queue remains available.'

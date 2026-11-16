$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$startup=[Environment]::GetFolderPath('Startup')
$shortcutPath=Join-Path $startup 'BOOX Max Print.lnk'
$shell=New-Object -ComObject WScript.Shell
$shortcut=$shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$shortcut.Arguments='-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "'+(Join-Path $projectRoot 'tools\start-printer.ps1')+'"'
$shortcut.WorkingDirectory=$projectRoot
$shortcut.WindowStyle=7
$shortcut.Description='Start BOOX Max virtual printer after Windows sign-in'
$shortcut.Save()
$check=$shell.CreateShortcut($shortcutPath)
if($check.Arguments -ne $shortcut.Arguments){throw 'Startup shortcut verification failed'}
Write-Output $shortcutPath

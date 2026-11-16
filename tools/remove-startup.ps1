$ErrorActionPreference='Stop'
$target=Join-Path ([Environment]::GetFolderPath('Startup')) 'BOOX Max Print.lnk'
if(Test-Path -LiteralPath $target){Remove-Item -LiteralPath $target}
Write-Output 'BOOX Max login startup removed. Printer and documents are unchanged.'

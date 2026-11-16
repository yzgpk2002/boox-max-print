@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\start-printer.ps1"
if errorlevel 1 goto failed
powershell.exe -NoProfile -Command "Start-Process powershell.exe -Verb RunAs -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File ""%~dp0tools\install-printer.ps1""' -WindowStyle Hidden -Wait"
if errorlevel 1 goto failed
echo Check logs\install-printer.log for the installation result.
pause
exit /b 0
:failed
echo Installation did not complete. Check the logs folder.
pause
exit /b 1

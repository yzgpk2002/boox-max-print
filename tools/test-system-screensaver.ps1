# Interactive test: briefly runs Windows' black screen saver without changing settings or locking.
param([string]$Mode='')
$ErrorActionPreference='Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
 $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
 & $compiler /nologo /optimize+ /r:System.Drawing.dll /out:build\BooxWindowCapture.exe desktop\WindowCaptureHost.cs desktop\ScreenCapture.cs
 if($LASTEXITCODE -ne 0){throw 'Helper compilation failed'}
 & $compiler /nologo /optimize+ /r:System.Drawing.dll /r:System.Windows.Forms.dll /out:build\SystemScreenSaverTests.exe tests\SystemScreenSaverTests.cs desktop\ScreenCapture.cs
 if($LASTEXITCODE -ne 0){throw 'Screen saver test compilation failed'}
 & .\build\SystemScreenSaverTests.exe $Mode
 if($LASTEXITCODE -ne 0){throw 'Real system screensaver test failed'}
}finally{Pop-Location}

$ErrorActionPreference='Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
 $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
 & $compiler /nologo /out:build\StalledCaptureHost.exe tests\StalledCaptureHost.cs
 if($LASTEXITCODE -ne 0){throw 'Stalled capture fixture compilation failed'}
 & $compiler /nologo /optimize+ /r:System.Drawing.dll /r:System.Windows.Forms.dll /out:build\WindowCaptureTests.exe tests\WindowCaptureTests.cs desktop\ScreenCapture.cs
 if($LASTEXITCODE -ne 0){throw 'Window capture tests compilation failed'}
 & .\build\WindowCaptureTests.exe
 if($LASTEXITCODE -ne 0){throw 'Window capture tests failed'}
}finally{Pop-Location}

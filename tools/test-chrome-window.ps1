# Requires a visible dedicated dynamic Chrome page whose title starts BOOX-CAPTURE-FIXTURE-057.
# Runs on the interactive desktop, briefly covers that fixture, saves generated captures under build.
$ErrorActionPreference='Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
 $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
 & $compiler /nologo /optimize+ /r:System.Drawing.dll /out:build\BooxWindowCapture.exe desktop\WindowCaptureHost.cs desktop\ScreenCapture.cs
 if($LASTEXITCODE -ne 0){throw 'Window helper compilation failed'}
 & $compiler /nologo /unsafe+ /optimize+ /r:System.Drawing.dll /r:System.Windows.Forms.dll /out:build\ChromeWindowCaptureTests.exe tests\ChromeWindowCaptureTests.cs desktop\ScreenCapture.cs desktop\ImagePipeline.cs desktop\ImageEnhancement.cs
 if($LASTEXITCODE -ne 0){throw 'Chrome window test compilation failed'}
 & .\build\ChromeWindowCaptureTests.exe
 if($LASTEXITCODE -ne 0){throw 'Chrome window test failed'}
}finally{Pop-Location}

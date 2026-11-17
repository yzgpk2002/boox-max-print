$ErrorActionPreference='Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
  New-Item -ItemType Directory -Force build | Out-Null
  $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
  $uiRefs=@(('/r:'+(Join-Path (Split-Path $compiler) 'WPF\UIAutomationClient.dll')),('/r:'+(Join-Path (Split-Path $compiler) 'WPF\UIAutomationTypes.dll')))
  & $compiler @uiRefs /nologo /unsafe+ /optimize+ /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Net.Http.dll /resource:desktop\assets\boox-images.ico,BooxImages.AppIcon /resource:desktop\assets\boox-tray.ico,BooxImages.TrayIcon /main:ReadingToggleTests /out:build\ReadingToggleTests.exe tests\ReadingToggleTests.cs desktop\ImageSender.cs desktop\ImageEnhancement.cs desktop\ImagePipeline.cs desktop\ImageSettings.cs desktop\ScreenCapture.cs desktop\AutoScroller.cs
  if($LASTEXITCODE -ne 0){throw 'Reading toggle test compilation failed'}
  & .\build\ReadingToggleTests.exe
  if($LASTEXITCODE -ne 0){throw 'Reading toggle regression failed'}
}finally{Pop-Location}

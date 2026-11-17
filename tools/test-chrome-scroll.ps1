param([ValidateSet('lines','page')][string]$Mode='lines',[switch]$Live,[switch]$Focus)
# Explicit interactive test. In Chrome open tests/fixtures/chrome-scroll.html,
# then focus its BODY within 60 seconds. Keep the generated page foreground.
# Live sends only that window's captured rectangle to the paired BOOX.
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
  New-Item -ItemType Directory -Force build | Out-Null
  $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
  & $compiler /nologo /unsafe+ /optimize+ /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Net.Http.dll /resource:desktop\assets\boox-images.ico,BooxImages.AppIcon /resource:desktop\assets\boox-tray.ico,BooxImages.TrayIcon /main:ChromeScrollIntegrationTests /out:build\ChromeScrollIntegrationTests.exe tests\ChromeScrollIntegrationTests.cs desktop\ImageSender.cs desktop\ImageEnhancement.cs desktop\ImagePipeline.cs desktop\ImageSettings.cs desktop\ScreenCapture.cs desktop\AutoScroller.cs
  if($LASTEXITCODE -ne 0){throw 'Chrome integration test compilation failed'}
  $testArgs=@($Mode)
  if($Live){$testArgs+='live'}
  if($Focus){$testArgs+='focus'}
  & .\build\ChromeScrollIntegrationTests.exe @testArgs
  if($LASTEXITCODE -ne 0){throw 'Chrome integration test failed'}
}finally{Pop-Location}

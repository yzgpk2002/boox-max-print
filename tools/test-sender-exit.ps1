param([string]$SenderSource='desktop\ImageSender.cs',[string]$Mode='all')
$ErrorActionPreference='Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
 $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
 $uiRefs=@(('/r:'+(Join-Path (Split-Path $compiler) 'WPF\UIAutomationClient.dll')),('/r:'+(Join-Path (Split-Path $compiler) 'WPF\UIAutomationTypes.dll')))
 & $compiler @uiRefs /nologo /unsafe+ /optimize+ /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Net.Http.dll /resource:desktop\assets\boox-images.ico,BooxImages.AppIcon /resource:desktop\assets\boox-tray.ico,BooxImages.TrayIcon /main:SenderExitTests /out:build\SenderExitTests.exe tests\SenderExitTests.cs $SenderSource desktop\ImageEnhancement.cs desktop\ImagePipeline.cs desktop\ImageSettings.cs desktop\ScreenCapture.cs desktop\AutoScroller.cs
 if($LASTEXITCODE -ne 0){throw 'Exit test compilation failed'}
 $modes=if($Mode -eq 'all'){@('idle','active','offline','idle')}else{@($Mode)}
 foreach($scenario in $modes){& .\build\SenderExitTests.exe $scenario;if($LASTEXITCODE -ne 0){throw "Exit test failed: $scenario"}}
}finally{Pop-Location}

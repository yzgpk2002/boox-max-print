param([string]$OutputPath='')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
New-Item -ItemType Directory -Force -Path (Join-Path $projectRoot 'dist') | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$uiRefs=@(('/r:'+(Join-Path (Split-Path $compiler) 'WPF\UIAutomationClient.dll')),('/r:'+(Join-Path (Split-Path $compiler) 'WPF\UIAutomationTypes.dll')))
if(!$OutputPath){$OutputPath=Join-Path $projectRoot 'dist\BooxImages.exe'}
& $compiler @uiRefs /nologo /target:winexe /optimize+ /unsafe+ /platform:anycpu /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll (('/win32icon:')+(Join-Path $projectRoot 'desktop\assets\boox-images.ico')) (('/resource:')+(Join-Path $projectRoot 'desktop\assets\boox-images.ico')+',BooxImages.AppIcon') (('/resource:')+(Join-Path $projectRoot 'desktop\assets\boox-tray.ico')+',BooxImages.TrayIcon') (('/out:')+$OutputPath) (Join-Path $projectRoot 'desktop\ImageSender.cs') (Join-Path $projectRoot 'desktop\ImageEnhancement.cs') (Join-Path $projectRoot 'desktop\ImagePipeline.cs') (Join-Path $projectRoot 'desktop\ImageSettings.cs') (Join-Path $projectRoot 'desktop\ScreenCapture.cs') (Join-Path $projectRoot 'desktop\AutoScroller.cs')
if($LASTEXITCODE -ne 0){throw 'Image sender build failed'}
Get-Item $OutputPath | Select-Object FullName,Length

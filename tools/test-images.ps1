param([switch]$Desktop)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    New-Item -ItemType Directory -Force build | Out-Null
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    function Compile([string[]]$Sources,[string]$Output,[string[]]$Extra=@()) {
        & $compiler /nologo /unsafe+ /optimize+ /r:System.Drawing.dll /r:System.Web.Extensions.dll "/out:$Output" @Extra @Sources
        if($LASTEXITCODE -ne 0){throw "Compile failed: $Output"}
    }
    function Run([string]$Exe,[string[]]$Arguments=@()) {
        & $Exe @Arguments
        if($LASTEXITCODE -ne 0){throw "Test failed: $Exe"}
    }
    Compile @('tests\ImageEnhancementTests.cs','desktop\ImageEnhancement.cs') 'build\ImageEnhancementTests.exe'
    Run '.\build\ImageEnhancementTests.exe' @('build\enhancement-verification')
    Compile @('tests\ImagePipelineBenchmark.cs','tests\ImageEnhancementReference.cs') 'build\BaselineBenchmark.exe'
    Run '.\build\BaselineBenchmark.exe' @('build\baseline-frames')
    Compile @('tests\ImagePipelineTests.cs','tests\ImageEnhancementReference.cs','desktop\ImageEnhancement.cs','desktop\ImagePipeline.cs','desktop\ImageSettings.cs') 'build\ImagePipelineTests.exe'
    Run '.\build\ImagePipelineTests.exe' @('build\optimized-frames')
    if($Desktop) {
        # Requires the interactive desktop for native cursor/screen reads. Does not move the cursor.
        Compile @('tests\SenderLifecycleTests.cs','desktop\ImageSender.cs','desktop\ImageEnhancement.cs','desktop\ImagePipeline.cs','desktop\ImageSettings.cs','desktop\ScreenCapture.cs') 'build\SenderLifecycleTests.exe' @('/r:System.Windows.Forms.dll','/r:System.Net.Http.dll','/resource:desktop\assets\boox-images.ico,BooxImages.AppIcon','/resource:desktop\assets\boox-tray.ico,BooxImages.TrayIcon','/main:SenderLifecycleTests')
        Run '.\build\SenderLifecycleTests.exe'
    }
} finally {Pop-Location}

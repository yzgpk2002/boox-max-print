# Explicit opt-in native test. Opens only self-owned generated editor windows.
# Do not run as part of ordinary background/offline regression.
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    New-Item -ItemType Directory -Force build | Out-Null
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $uiRefs=@(('/r:'+(Join-Path (Split-Path $compiler) 'WPF\UIAutomationClient.dll')),('/r:'+(Join-Path (Split-Path $compiler) 'WPF\UIAutomationTypes.dll')))
    & $compiler @uiRefs /nologo /optimize+ /r:System.Drawing.dll /r:System.Windows.Forms.dll /out:build\AutoScrollNativeTests.exe tests\AutoScrollNativeTests.cs desktop\AutoScroller.cs
    if($LASTEXITCODE -ne 0){throw 'Native scroll test compilation failed'}
    & .\build\AutoScrollNativeTests.exe
    if($LASTEXITCODE -ne 0){throw 'Native scroll test failed; see message above'}
}finally{Pop-Location}

param([string]$SdkRoot='E:\codex-tools\android-sdk',[string]$JdkRoot='E:\REA\tools\jdk-21.0.12.1+1',[string]$BuildToolsDir='',[string]$AndroidJar='')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Set-Location $projectRoot
$build=Join-Path $projectRoot 'build\android'
$dist=Join-Path $projectRoot 'dist'
$local=Join-Path $projectRoot '.local'
New-Item -ItemType Directory -Force -Path "$build\classes","$build\dex",$dist,$local | Out-Null
$env:JAVA_HOME=$JdkRoot
$env:PATH="$JdkRoot\bin;"+$env:PATH
$bt=if($BuildToolsDir){$BuildToolsDir}else{Join-Path $SdkRoot 'android-15'}
$platformJar=if($AndroidJar){$AndroidJar}else{Join-Path $SdkRoot 'android-35\android.jar'}
function Check-Step {if($LASTEXITCODE -ne 0){throw "Build step failed: $LASTEXITCODE"}}
$sources=@(Get-ChildItem 'android\src' -Filter '*.java' -Recurse | ForEach-Object FullName)
& "$JdkRoot\bin\javac.exe" -encoding UTF-8 -source 8 -target 8 -bootclasspath $platformJar -d "$build\classes" @sources
Check-Step
& "$JdkRoot\bin\jar.exe" cf "$build\classes.jar" -C "$build\classes" .
Check-Step
& "$bt\d8.bat" --min-api 19 --lib $platformJar --output "$build\dex" "$build\classes.jar"
Check-Step
& "$bt\aapt.exe" package -f -M 'android\AndroidManifest.xml' -I $platformJar -F "$build\unsigned.apk"
Check-Step
Copy-Item -LiteralPath "$build\dex\classes.dex" -Destination "$build\classes.dex" -Force
Push-Location $build
try{& "$bt\aapt.exe" add unsigned.apk classes.dex;Check-Step}finally{Pop-Location}
& "$bt\zipalign.exe" -f 4 "$build\unsigned.apk" "$build\aligned.apk"
Check-Step
$key=Join-Path $local 'booxmaxprint.keystore'
if(!(Test-Path -LiteralPath $key)){
    & "$JdkRoot\bin\keytool.exe" -genkeypair -keystore $key -storepass android -keypass android -alias booxmaxprint -keyalg RSA -keysize 2048 -validity 10000 -dname 'CN=BOOX Max Print Local Development'
    Check-Step
}
& "$bt\apksigner.bat" sign --ks $key --ks-key-alias booxmaxprint --ks-pass pass:android --key-pass pass:android --min-sdk-version 19 --v1-signing-enabled true --out "$dist\booxmaxprint-wifi.apk" "$build\aligned.apk"
Check-Step
& "$bt\apksigner.bat" verify --verbose --min-sdk-version 19 "$dist\booxmaxprint-wifi.apk"
Check-Step
Get-Item "$dist\booxmaxprint-wifi.apk" | Select-Object FullName,Length

# HandHeld M0 — install all toolchains to D:\Dev (nothing on C:).
# Run from an elevated PowerShell:
#   powershell -ExecutionPolicy Bypass -File D:\Projects\HandHeld\scripts\setup-all.ps1
$ErrorActionPreference = "Stop"

$DevRoot     = "D:\Dev"
$DotNetDir   = "$DevRoot\dotnet"
$JdkDir      = "$DevRoot\jdks\jdk-17"
$SdkDir      = "$DevRoot\Android\sdk"
$CmdToolsZip = "$DevRoot\cmdline-tools.zip"
$JdkZip      = "$DevRoot\jdk17.zip"

New-Item -ItemType Directory -Force -Path $DevRoot, $DotNetDir, "$DevRoot\jdks", $SdkDir, "$DevRoot\gradle", "$DevRoot\nuget" | Out-Null

# ---- .NET 8 SDK ---------------------------------------------------------
if (-not (Test-Path "$DotNetDir\dotnet.exe")) {
    Write-Host "[1/3] Installing .NET 8 SDK -> D:\Dev\dotnet"
    Invoke-WebRequest -UseBasicParsing "https://dot.net/v1/dotnet-install.ps1" -OutFile "$DevRoot\dotnet-install.ps1"
    & "$DevRoot\dotnet-install.ps1" -Channel 8.0 -InstallDir $DotNetDir -NoPath
} else {
    Write-Host "[1/3] .NET SDK already present"
}

# ---- JDK 17 (Temurin) ---------------------------------------------------
if (-not (Test-Path "$JdkDir\bin\java.exe")) {
    Write-Host "[2/3] Installing Temurin JDK 17 -> D:\Dev\jdks"
    $rel = Invoke-RestMethod -UseBasicParsing "https://api.github.com/repos/adoptium/temurin17-binaries/releases/latest"
    $asset = $rel.assets | Where-Object { $_.name -match "jdk_x64_windows.*\.zip$" } | Select-Object -First 1
    if (-not $asset) { throw "No x64 Windows JDK 17 zip found in latest Temurin release" }
    Invoke-WebRequest -UseBasicParsing $asset.browser_download_url -OutFile $JdkZip
    Expand-Archive -Path $JdkZip -DestinationPath "$DevRoot\jdks" -Force
    $extracted = Get-ChildItem "$DevRoot\jdks" -Directory | Where-Object { $_.Name -like "jdk-17*" } | Select-Object -First 1
    if (-not $extracted) { throw "JDK extraction produced no jdk-17* folder" }
    if ($extracted.FullName -ne $JdkDir) { Move-Item $extracted.FullName $JdkDir -Force }
    Remove-Item $JdkZip -Force
} else {
    Write-Host "[2/3] JDK 17 already present"
}

# ---- Android cmdline-tools + SDK ----------------------------------------
if (-not (Test-Path "$SdkDir\cmdline-tools\latest\bin\sdkmanager.bat")) {
    Write-Host "[3/3] Installing Android cmdline-tools -> D:\Dev\Android\sdk"
    $ver = "11076708"  # cmdline-tools latest
    $url = "https://dl.google.com/android/repository/commandlinetools-win-${ver}_latest.zip"
    Invoke-WebRequest -UseBasicParsing $url -OutFile $CmdToolsZip
    Expand-Archive -Path $CmdToolsZip -DestinationPath "$SdkDir\cmdline-tools" -Force
    Rename-Item "$SdkDir\cmdline-tools\cmdline-tools" "$SdkDir\cmdline-tools\latest" -Force
    Remove-Item $CmdToolsZip -Force
} else {
    Write-Host "[3/3] cmdline-tools already present"
}

# Pre-accept SDK licenses (sdkmanager's interactive prompt can't be piped reliably)
$licensesDir = "$SdkDir\licenses"
New-Item -ItemType Directory -Force -Path $licensesDir | Out-Null
Set-Content -Path "$licensesDir\android-sdk-license" -Value @"
8933bad161af4178b1185d1a37fbf41ea5269c55
d56f5187479451eabf01fb78af6dfcb131a6481e
24333f8a63b6825ea9c5514f83c2829b004d1fee
"@
Set-Content -Path "$licensesDir\android-sdk-preview-license" -Value @"
84831b9409646a918e30573bab4c9c91346d8abd
504667f4c0de7af1a06de9f4b1727b84351f2910
"@
Set-Content -Path "$licensesDir\intel-android-extra-license" -Value "d975f751698a77b662f1254ddbededf42e4ece22"

# Install packages (platform-tools, API 36, build-tools; no emulator/NDK)
$env:ANDROID_HOME = $SdkDir
$env:ANDROID_SDK_ROOT = $SdkDir
$sdkManager = "$SdkDir\cmdline-tools\latest\bin\sdkmanager.bat"
& $sdkManager "platform-tools" "platforms;android-36" "build-tools;36.0.0"

Write-Host ""
Write-Host "Done. Dot-source env for this shell:  . D:\Projects\HandHeld\scripts\env.ps1"
Write-Host "All toolchains are under D:\Dev. Nothing was written to C:."

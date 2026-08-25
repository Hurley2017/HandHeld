# Installs HandHeld-debug.apk to a phone over ADB (USB or wireless).
# Usage:
#   .\install-apk.ps1                # USB-connected device
#   .\install-apk.ps1 -Wireless      # ADB over Wi-Fi (enable Wireless debugging on the phone first)
param(
    [switch]$Wireless
)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\env.ps1"

$adb = "$env:ANDROID_HOME\platform-tools\adb.exe"
$apk = "D:\Projects\HandHeld\artifacts\HandHeld-debug.apk"

if (-not (Test-Path $apk)) {
    Write-Host "APK not found. Run .\build-all.ps1 first." -ForegroundColor Red
    exit 1
}

if ($Wireless) {
    Write-Host "Enable Wireless debugging on the phone (Settings > Developer options),"
    Write-Host "then pair with: adb pair <ip>:<port> <code>"
    Write-Host "and connect with: adb connect <ip>:<port>"
    exit 0
}

$devices = & $adb devices
if (($devices | Select-String "device$").Count -eq 0) {
    Write-Host "No device connected. Plug in via USB (enable USB debugging) or use -Wireless." -ForegroundColor Red
    exit 1
}

& $adb install -r $apk
Write-Host "Installed. First launch may warn about unknown sources — allow it."

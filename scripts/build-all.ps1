# Builds the host, publishes it to artifacts\, and builds the client APK.
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\env.ps1"

Write-Host "=== Building host ==="
Push-Location "$PSScriptRoot\..\host"
dotnet build HandHeld.sln -c Release
dotnet publish src\HandHeld.Host -c Release -o ..\artifacts\host-publish --no-build
Pop-Location

Write-Host "=== Building client APK ==="
Push-Location "$PSScriptRoot\..\client"
& "$env:JAVA_HOME\bin\java" -version
.\gradlew.bat assembleDebug --no-daemon
Pop-Location

Write-Host ""
Write-Host "APK: D:\Projects\HandHeld\artifacts\HandHeld.apk (copy from client\app\build\outputs\apk\debug\)"

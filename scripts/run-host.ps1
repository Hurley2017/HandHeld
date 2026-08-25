# Starts the HandHeld host app (tray). Rebuilds first if needed.
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\env.ps1"

$exe = "D:\Projects\HandHeld\host\src\HandHeld.Host\bin\Release\net8.0-windows\HandHeld.Host.exe"

if (-not (Test-Path $exe)) {
    Write-Host "Building host first..."
    Push-Location "$PSScriptRoot\..\host"
    dotnet build HandHeld.sln -c Release
    Pop-Location
}

if (Get-Process -Name "HandHeld.Host" -ErrorAction SilentlyContinue) {
    Write-Host "Host already running."
    exit 0
}

$env:DOTNET_ROOT = "D:\Dev\dotnet"
Start-Process -FilePath $exe
Write-Host "HandHeld host started (tray icon)."

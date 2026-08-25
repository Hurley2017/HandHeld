@echo off
rem HandHeld Host launcher — hidden console, sets the D:-based .NET root, starts the tray app.
set DOTNET_ROOT=D:\Dev\dotnet
set PATH=D:\Dev\dotnet;%PATH%
start "" /min "D:\Projects\HandHeld\host\src\HandHeld.Host\bin\Release\net8.0-windows\HandHeld.Host.exe"
exit

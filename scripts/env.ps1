# HandHeld environment loader — everything on D:, nothing on C:.
# Dot-source this from PowerShell before building:
#   . D:\Projects\HandHeld\scripts\env.ps1
$env:DOTNET_ROOT   = "D:\Dev\dotnet"
$env:JAVA_HOME     = "D:\Dev\jdks\jdk-17"
$env:ANDROID_HOME  = "D:\Dev\Android\sdk"
$env:ANDROID_SDK_ROOT = "D:\Dev\Android\sdk"
$env:GRADLE_USER_HOME = "D:\Dev\gradle"
$env:NUGET_PACKAGES   = "D:\Dev\nuget"
$env:Path = "D:\Dev\dotnet;D:\Dev\jdks\jdk-17\bin;D:\Dev\Android\sdk\platform-tools;D:\Dev\Android\sdk\cmdline-tools\latest\bin;$env:Path"

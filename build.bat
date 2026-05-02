@echo off
echo ========================================
echo GHOSTWing - Automated Build System
echo ========================================
echo Version: 1.0.4
echo Target: Single-File Executable (win-x64)
echo.

dotnet publish GHOSTWing/GHOSTWing.csproj -r win-x64 -c Release -p:PublishSingleFile=true -p:SelfContained=false -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o E:\ANANDPERSIONAL\DISCORD\publish_single\

echo.
echo ========================================
echo BUILD COMPLETE!
echo Output: E:\ANANDPERSIONAL\DISCORD\publish_single\GHOSTWing.exe
echo ========================================
pause

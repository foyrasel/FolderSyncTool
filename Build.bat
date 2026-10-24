@echo off
rem ============================================================
rem  FolderSync Tool v5.0 - build script
rem  Requires: .NET SDK 8 (https://dotnet.microsoft.com/download)
rem ============================================================
cd /d "%~dp0"

echo Building FolderSyncTool v5.0 (Release)...
dotnet build FolderSyncTool.csproj -c Release
if errorlevel 1 (
  echo.
  echo BUILD FAILED - is the .NET SDK installed?
  pause
  exit /b 1
)

echo.
echo Output: bin\Release\net48\FolderSyncTool.exe

rem --- optional: sign the exe to remove "Unknown publisher" ---
rem Get a code-signing certificate, then uncomment and edit:
rem signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 bin\Release\net48\FolderSyncTool.exe

echo Done.
pause

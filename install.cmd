@echo off
rem Builds SentriPet from source and installs it for the current user, then starts it.
rem Needs the .NET 10 SDK (https://dotnet.microsoft.com/download). No SDK? Download a ready-made package instead:
rem https://github.com/daven55663/SentriPet-AI/releases/latest
setlocal
cd /d "%~dp0"
set DOTNET=dotnet
where dotnet >nul 2>&1 || set DOTNET="%ProgramFiles%\dotnet\dotnet.exe"
%DOTNET% --list-sdks 2>nul | findstr /b "10." >nul || (
  echo The .NET 10 SDK was not found. Install it from https://dotnet.microsoft.com/download
  echo or download a ready-made package from https://github.com/daven55663/SentriPet-AI/releases/latest
  exit /b 1
)
set DOTNET_CLI_TELEMETRY_OPTOUT=1
set DOTNET_NOLOGO=1
rem one self-extracting program file that uses the .NET runtime installed with the SDK
%DOTNET% publish xplat\SentriPet.Desktop\SentriPet.Desktop.csproj -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o bin\publish -v q -nologo || exit /b 1
set DEST=%LOCALAPPDATA%\Programs\SentriPet
taskkill /IM SentriPet.exe /F >nul 2>&1
ping -n 2 127.0.0.1 >nul
if not exist "%DEST%" mkdir "%DEST%"
copy /y bin\publish\SentriPet.exe "%DEST%\" >nul || exit /b 1
xcopy /e /i /y /q examples "%DEST%\examples" >nul
copy /y LICENSE "%DEST%\LICENSE.txt" >nul
rem left over from the WPF version (before 2.0)
if exist "%DEST%\SentriPet.exe.config" del "%DEST%\SentriPet.exe.config"
rem Start it through Explorer: the pet then runs as a normal desktop app even when this script is run from a
rem packaged app's terminal (e.g. the Claude desktop app, which would otherwise virtualize its AppData writes).
start "" explorer.exe "%DEST%\SentriPet.exe"
echo Installed to %DEST%

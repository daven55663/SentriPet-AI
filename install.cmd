@echo off
rem Builds and installs SentriPet for the current user, then starts it.
setlocal
cd /d "%~dp0"
call "%~dp0build.cmd" || exit /b 1
set DEST=%LOCALAPPDATA%\Programs\SentriPet
taskkill /IM SentriPet.exe /F >nul 2>&1
rem versions before 1.1 were called "AI Usage Pet"
taskkill /IM AIUsagePet.exe /F >nul 2>&1
ping -n 2 127.0.0.1 >nul
if exist "%LOCALAPPDATA%\Programs\AIUsagePet\AIUsagePet.exe" rmdir /s /q "%LOCALAPPDATA%\Programs\AIUsagePet"
if not exist "%DEST%" mkdir "%DEST%"
copy /y bin\SentriPet.exe "%DEST%\" >nul || exit /b 1
copy /y bin\SentriPet.exe.config "%DEST%\" >nul
xcopy /e /i /y /q examples "%DEST%\examples" >nul
rem Start it through Explorer: the pet then runs as a normal desktop app even when this script is run from a
rem packaged app's terminal (e.g. the Claude desktop app, which would otherwise virtualize its AppData writes).
start "" explorer.exe "%DEST%\SentriPet.exe"
echo Installed to %DEST%

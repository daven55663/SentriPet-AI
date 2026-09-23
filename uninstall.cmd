@echo off
rem Stops SentriPet, removes the sign-in entry and deletes the program folder.
rem Settings in %APPDATA%\SentriPet are kept unless you delete that folder yourself.
setlocal
set DEST=%LOCALAPPDATA%\Programs\SentriPet
choice /m "Uninstall SentriPet from %DEST%"
if errorlevel 2 exit /b 0
taskkill /IM SentriPet.exe /F >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v SentriPet /f >nul 2>&1
ping -n 2 127.0.0.1 >nul
cd /d "%TEMP%"
rmdir /s /q "%DEST%"
echo Uninstalled. Settings are still in %APPDATA%\SentriPet

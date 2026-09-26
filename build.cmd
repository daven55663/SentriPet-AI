@echo off
rem Builds bin\SentriPet.exe with the C# compiler that ships with Windows (.NET Framework 4.8).
setlocal
cd /d "%~dp0"
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set CSC=%FW%\csc.exe
set WPF=%FW%\WPF
if not exist bin mkdir bin
set ICON=
if exist assets\app.ico set ICON=/win32icon:assets\app.ico
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /utf8output /codepage:65001 /nowarn:1699 ^
 /out:bin\SentriPet.exe %ICON% /win32manifest:src\app.manifest ^
 /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\WindowsBase.dll" ^
 /r:System.Xaml.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
 /r:System.Core.dll /r:Microsoft.CSharp.dll /r:System.Xml.dll ^
 /resource:src\Lang\zh-CN.json,SentriPet.Lang.zh-CN.json /resource:src\Lang\en.json,SentriPet.Lang.en.json ^
 /resource:src\Lang\ja.json,SentriPet.Lang.ja.json /resource:src\Lang\ko.json,SentriPet.Lang.ko.json ^
 /recurse:src\*.cs
if errorlevel 1 exit /b 1
copy /y src\SentriPet.exe.config bin\ >nul
if exist examples xcopy /e /i /y /q examples bin\examples >nul
echo Built bin\SentriPet.exe

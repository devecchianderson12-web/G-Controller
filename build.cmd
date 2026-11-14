@echo off
rem Compila GController.exe con el csc de .NET Framework (viene con Windows).
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
cd /d "%~dp0"
if not exist obj mkdir obj
"%CSC%" -nologo -out:obj\mkicon.exe -r:System.Drawing.dll tools\mkicon.cs src\Art.cs || exit /b 1
obj\mkicon.exe obj\app.ico || exit /b 1
"%CSC%" -nologo -target:winexe -platform:anycpu -optimize+ -win32icon:obj\app.ico -win32manifest:src\app.manifest ^
  -resource:res\g305.png,g305.png -resource:res\g733.png,g733.png ^
  -r:System.Windows.Forms.dll -r:System.Drawing.dll -out:GController.exe src\*.cs || exit /b 1
echo OK: GController.exe

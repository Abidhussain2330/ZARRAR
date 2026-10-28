@echo off
rem Rebuilds Zarrar.exe and Zarrar-Stop.exe in the ZARRAR folder from launcher\Launcher.cs.
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set ICON=
if exist "%~dp0zarrar.ico" set ICON=/win32icon:"%~dp0zarrar.ico"
"%CSC%" /nologo /target:exe /optimize %ICON% /out:"%~dp0..\Zarrar.exe" "%~dp0Launcher.cs" || exit /b 1
copy /y "%~dp0..\Zarrar.exe" "%~dp0..\Zarrar-Stop.exe" >nul
echo Built Zarrar.exe and Zarrar-Stop.exe

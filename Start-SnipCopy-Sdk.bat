@echo off
call "%~dp0Build-Sdk.bat"
if errorlevel 1 exit /b 1
start "" "%~dp0bin\x64\Release\net8.0-windows10.0.19041.0\SnipCopy.exe"

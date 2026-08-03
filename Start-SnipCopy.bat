@echo off
call "%~dp0Build.bat"
if errorlevel 1 exit /b 1
start "" "%~dp0SnipCopy.exe"

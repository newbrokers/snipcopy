@echo off
if not exist "%~dp0SnipCopy.exe" call "%~dp0Build.bat"
start "" "%~dp0SnipCopy.exe"

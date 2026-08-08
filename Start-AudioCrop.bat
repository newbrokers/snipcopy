@echo off
set "APP=%~dp0audiocrop-csharp\bin\x64\Release\net8.0-windows\AudioCrop.exe"
if not exist "%APP%" (
  call "%~dp0Build-AudioCrop.bat"
  if errorlevel 1 exit /b 1
)
start "" "%APP%"

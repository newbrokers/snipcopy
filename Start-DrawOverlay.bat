@echo off
call "%~dp0Build-DrawOverlay.bat"
if errorlevel 1 exit /b 1
start "" "%~dp0draw-overlay-csharp\bin\x64\Release\net8.0-windows\DrawOverlay.exe"

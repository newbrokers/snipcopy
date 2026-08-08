@echo off
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find csc.exe. Install .NET Framework developer tools or the .NET SDK.
  exit /b 1
)
"%CSC%" /nologo /target:exe /optimize+ /out:"%~dp0IconMaker.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll "%~dp0IconMaker.cs"
if errorlevel 1 exit /b 1
"%~dp0IconMaker.exe" "%~dp0SnipCopyIcon.png" "%~dp0SnipCopy.ico"
if errorlevel 1 exit /b 1
"%CSC%" /nologo /target:winexe /optimize+ /win32icon:"%~dp0SnipCopy.ico" /out:"%~dp0SnipCopy.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Numerics.dll /reference:System.Security.dll /reference:System.Windows.Forms.dll "%~dp0SnipCopy.cs"

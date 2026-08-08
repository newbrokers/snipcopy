@echo off
set "DOTNET_EXE=%USERPROFILE%\.dotnet\dotnet.exe"
if not exist "%DOTNET_EXE%" (
  if exist "C:\Users\user\.dotnet\dotnet.exe" (
    set "DOTNET_EXE=C:\Users\user\.dotnet\dotnet.exe"
  ) else (
    set "DOTNET_EXE=dotnet"
  )
)

"%DOTNET_EXE%" --list-sdks | findstr /R "^8\." >nul 2>nul
if errorlevel 1 (
  echo The .NET 8 SDK is not installed. Install it from https://dotnet.microsoft.com/download/dotnet/8.0
  exit /b 1
)

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find csc.exe. Install .NET Framework developer tools or the .NET SDK.
  exit /b 1
)

"%CSC%" /nologo /target:exe /optimize+ /out:"%~dp0IconMaker.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll "%~dp0IconMaker.cs"
if errorlevel 1 exit /b 1

"%~dp0IconMaker.exe" "%~dp0website\public\images\products\snipcopy.png" "%~dp0SnipCopy.ico"
if errorlevel 1 exit /b 1

"%DOTNET_EXE%" build "%~dp0SnipCopy.csproj" -c Release -p:Platform=x64

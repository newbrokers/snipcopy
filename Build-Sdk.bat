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

"%DOTNET_EXE%" build "%~dp0SnipCopy.csproj" -c Release -p:Platform=x64

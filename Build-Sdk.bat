@echo off
where dotnet >nul 2>nul
if errorlevel 1 (
  echo dotnet.exe was not found. Install the .NET 8 SDK first.
  exit /b 1
)

dotnet --list-sdks | findstr /R "^8\." >nul 2>nul
if errorlevel 1 (
  echo The .NET 8 SDK is not installed. Install it from https://dotnet.microsoft.com/download/dotnet/8.0
  exit /b 1
)

dotnet build "%~dp0SnipCopy.csproj" -c Release

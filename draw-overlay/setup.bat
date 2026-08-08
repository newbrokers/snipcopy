@echo off
echo Installing dependencies...
set "PYTHON_CMD="
where py >nul 2>nul && set "PYTHON_CMD=py"
if not defined PYTHON_CMD where python >nul 2>nul && set "PYTHON_CMD=python"
if not defined PYTHON_CMD (
  echo Could not find Python. Install Python 3.11+ and make sure it is on PATH.
  pause
  exit /b 1
)
%PYTHON_CMD% -m pip install -r "%~dp0requirements.txt"
if errorlevel 1 exit /b 1
echo.
echo Generating cursors...
%PYTHON_CMD% "%~dp0generate_cursors.py"
if errorlevel 1 exit /b 1
echo.
echo Done! You can now run: draw.bat
pause

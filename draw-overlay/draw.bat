@echo off
where pyw >nul 2>nul
if not errorlevel 1 (
  start "" pyw "%~dp0draw_overlay.py"
  exit /b 0
)

where pythonw >nul 2>nul
if not errorlevel 1 (
  start "" pythonw "%~dp0draw_overlay.py"
  exit /b 0
)

where py >nul 2>nul
if not errorlevel 1 (
  start "" py "%~dp0draw_overlay.py"
  exit /b 0
)

where python >nul 2>nul
if not errorlevel 1 (
  start "" python "%~dp0draw_overlay.py"
  exit /b 0
)

echo Could not find Python. Run setup.bat after installing Python 3.11+.
pause
exit /b 1

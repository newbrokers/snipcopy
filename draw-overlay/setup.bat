@echo off
echo Installing dependencies...
pip install PyQt6>=6.6.0
echo.
echo Generating cursors...
python "%~dp0generate_cursors.py"
echo.
echo Done! You can now run: draw.bat
pause

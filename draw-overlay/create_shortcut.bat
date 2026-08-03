@echo off
echo Creating desktop shortcut...
powershell -NoProfile -Command ^
  "$ws = New-Object -ComObject WScript.Shell; ^
   $s = $ws.CreateShortcut('%USERPROFILE%\Desktop\Draw Overlay.lnk'); ^
   $s.TargetPath = '%~dp0draw.bat'; ^
   $s.WorkingDirectory = '%~dp0'; ^
   $s.Description = 'Desktop Drawing Overlay'; ^
   $s.WindowStyle = 7; ^
   $s.Save()"
echo.
echo Shortcut "Draw Overlay" created on your Desktop!
pause

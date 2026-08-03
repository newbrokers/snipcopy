# SnipCopy

SnipCopy is a tiny Windows tray utility for quick screenshots:

- Press `Ctrl+Shift+S`, drag an area, release.
- The snip is copied to the clipboard immediately.
- No editor window opens unless you enable it from the tray menu.
- Double-click the tray icon to start a snip.
- Right-click the tray icon to open the last snip in the editor.
- Open the editor and use the `History` tab to browse recent snips.
- Right-click the tray icon to open the full screenshot history window.

The editor supports color choice, pen drawing, arrows, text, undo, redo, copy, and PNG save.
The Pro toolbar adds blur, redaction, and numbered step callouts.

## Free and Pro

The Free version keeps the last 5 snips in history. Pro keeps an expanded local history and enables the Pro editor tools.

This local build includes a temporary test license key for development:

`SNIPCOPY-PRO-LOCAL`

Use `Settings / About` from the tray menu to activate or deactivate the local Pro state.

## Run

Double-click `Build.bat` once, then double-click `Start-SnipCopy.bat`.

The build uses the C# compiler that ships with Windows .NET Framework.

## Exit

Right-click the tray icon and choose `Exit`.

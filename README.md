# SnipCopy

SnipCopy is a tiny Windows tray utility for quick screenshots:

- Press `Ctrl+Shift+S`, drag an area, release.
- The snip is copied to the clipboard immediately.
- No editor window opens unless you enable it from the tray menu.
- Press `Ctrl+E` to open the editor.
- Double-click the tray icon to start a snip.
- Right-click the tray icon to open the editor.
- Open the editor and use the `History` tab to browse recent snips.
- Open the editor and use the `Shortcuts` tab to see or change available keys.
- Right-click the tray icon to open the full screenshot history window.

The editor supports color choice, pen drawing, arrows, text, crop, capture, undo, redo, copy, and PNG save.
The Pro toolbar adds blur, redaction, and numbered step callouts.

The SDK build path includes a separate `Record` tab and `Ctrl+Shift+R` entry point for region recording. The Record tab can optionally include system audio, microphone audio, or both. Free recording is capped at 5 minutes; Pro unlocks unlimited recording length. Finished MP4s appear in the Record tab with Play, Open Folder, Copy Path, and Delete actions. Recording is intentionally separate from image capture so screenshot tools and their Pro gating stay unchanged.

## Free and Pro

The Free version keeps the last 5 snips in history and records clips up to 5 minutes. Pro keeps an expanded local history, enables the Pro editor tools, and unlocks unlimited recording length.

This local build includes a temporary test license key for development:

`SNIPCOPY-PRO-LOCAL`

Use `Settings / About` from the tray menu to activate or deactivate the local Pro state.

## Run

Double-click `Build.bat` once, then double-click `Start-SnipCopy.bat`.

The build uses the C# compiler that ships with Windows .NET Framework.

## New SDK Build Path

`SnipCopy.csproj` is the newer SDK-style Windows project that hosts the Windows capture recording implementation. It targets `.NET 8`, Windows Forms, and x64 because the native recorder library requires an explicit platform.

To build it locally, install the .NET 8 SDK, then run:

```bat
Build-Sdk.bat
```

To build and launch the SDK recording app:

```bat
Start-SnipCopy-Sdk.bat
```

The SDK output is written to:

```text
bin\x64\Release\net8.0-windows10.0.19041.0\SnipCopy.exe
```

The stable screenshot-only build still uses `Build.bat` and the root `SnipCopy.exe`.

## Draw Overlay C# Build

`draw-overlay-csharp/` is the C# version of Draw Overlay. It is a separate Windows app that uses the shared SavedCode license verifier in `shared-csharp/SavedCodeLicense.cs`.

To build it:

```bat
Build-DrawOverlay.bat
```

To build and launch it:

```bat
Start-DrawOverlay.bat
```

The output is written to:

```text
draw-overlay-csharp\bin\x64\Release\net8.0-windows\DrawOverlay.exe
```

## Exit

Right-click the tray icon and choose `Exit`.

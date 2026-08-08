# SavedCode C# App Plan

We will keep the products as separate Windows apps, but share the same C# licensing code.

## Product Apps

- `SnipCopy.exe` -> product slug `snipcopy`
- `DrawOverlay.exe` -> product slug `draw-overlay`
- `AudioCrop.exe` -> product slug `audio-crop`

Each app should have its own executable, icon, settings, and UI, while using the same SavedCode licensing protocol.

## Shared Code

Shared C# licensing lives in:

`shared-csharp/SavedCodeLicense.cs`

It provides:

- local encrypted license storage in `%AppData%\SavedCode\Licenses`
- activation via `https://www.savedcode.com/api/license/activate`
- sync via `https://www.savedcode.com/api/license/sync`
- offline token verification with the public key only
- product slug checks so a SnipCopy license cannot unlock Draw Overlay or Audio Crop
- one-machine activation support through the SavedCode backend

Server-only private signing keys stay in the website backend. Desktop apps only get the public verification key.

## Website / Stripe Status

The website already knows about all three product slugs in `website/src/lib/products.ts`.

Current local `.env` status:

- SnipCopy has the legacy fallback `STRIPE_PRO_PRICE_ID`.
- Draw Overlay still needs `STRIPE_DRAW_OVERLAY_PRO_PRICE_ID`.
- Audio Crop still needs `STRIPE_AUDIO_CROP_PRO_PRICE_ID`.

Before selling Draw Overlay or Audio Crop, create their yearly subscription prices in Stripe and add the exact `price_...` IDs to `website/.env` and Vercel environment variables.

## Draw Overlay C# Status

The first C# Draw Overlay app now lives in:

`draw-overlay-csharp/`

Build it from the repo root with:

`Build-DrawOverlay.bat`

Launch it from the repo root with:

`Start-DrawOverlay.bat`

The app includes:

- transparent always-on-top overlay window
- free tools: pen, highlighter, eraser, color, width, clear, undo
- Pro-gated tools: line, arrow, rectangle, ellipse, text
- tray icon and global `Ctrl+H` show/hide shortcut
- local settings under `%AppData%\SavedCode\DrawOverlay`
- SavedCode license dialog using `shared-csharp/SavedCodeLicense.cs`

Keep SnipCopy stable while the new Draw Overlay C# app is built beside it.

## Next Practical Step

Test `DrawOverlay.exe` interactively, then decide whether to replace or retire the Python `draw-overlay/` app. After that, use the same shared C# license module for `AudioCrop.exe`.

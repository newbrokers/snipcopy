# AudioCrop — Launch Plan

## Platforms to Get Publicity & Sell

### Product Launch (Get Visibility)
- **Product Hunt** — #1 place to launch indie tools, huge tech audience
- **Hacker News (Show HN)** — great for dev/tech tools
- **Reddit** — r/SideProject, r/InternetIsBeautiful, r/software
- **Twitter/X** — build in public, post demos with #buildinpublic
- **TikTok** — short demo videos, gen-z audience loves quick tool demos

### Sell the App
- **Gumroad** — simple storefront, handles payments, popular with indie creators
- **Lemonsqueezy** — like Gumroad but more modern, handles global taxes
- **Paddle** — good for software licensing
- **Itch.io** — not just games, also tools and creative software

### App Marketplaces
- **Microsoft Store** — very relevant since this is a Windows app
- **Flathub / Snapcraft** — if Linux support is added
- **Mac App Store** — if ported to macOS

### Directories & Listings
- **AlternativeTo** — list as alternative to Audacity, mp3cutter, etc.
- **SaaSHub** — software discovery
- **BetaList** — for early-stage products
- **AppSumo** — lifetime deals, big audience

## Tech Stack Decision: Switch to TypeScript Web App

### Why move away from Python/tkinter
- tkinter will always look dated, even with custom styling
- Packaging with PyInstaller creates 80-150MB executables
- Users need to trust downloading an .exe — big friction
- Hard to add payments/licensing
- No mobile support

### Why TypeScript web app
- **No install needed** — users just open a URL
- Modern UI with React + Tailwind (animations, glassmorphism, full gen-z aesthetic)
- **Web Audio API** handles playback natively in the browser
- **ffmpeg.wasm** handles cropping entirely client-side (no server needed)
- User's audio never leaves their machine — fully private
- Works on Windows, Mac, Linux, even phones
- Easy to add Stripe/Lemonsqueezy payments
- Deploy free on Vercel or Netlify
- Can later wrap as desktop app with Tauri (~10MB) if needed

### Web App Features (beyond current Python version)
- **Visual waveform** — click directly on the wave to set start/end
- **Drag handles** on the waveform to fine-tune positions
- **Instant preview** of just the selected segment before exporting
- Smooth animations, responsive design, works on mobile
- All processing happens in-browser with ffmpeg.wasm — no backend needed

### Recommended Stack
- **React + Vite** — fast dev, fast builds
- **Tailwind CSS** — modern styling
- **Web Audio API** — playback and waveform rendering
- **ffmpeg.wasm** — client-side audio cropping and export
- **Vercel / Netlify** — free hosting
- **Stripe or Lemonsqueezy** — payments (for premium features)

## Launch Strategy
1. Product Hunt launch for initial buzz
2. Gumroad or Lemonsqueezy as the storefront
3. Microsoft Store for discoverability
4. Short demo videos on Twitter/X and TikTok

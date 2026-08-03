# Draw Overlay — Technology Comparison for Distribution

| | Python/Qt (current) | Electron/TS | Tauri/TS+Rust |
|---|---|---|---|
| Performance | Great | OK | Great |
| Bundle size | ~30MB | ~150MB | ~5-10MB |
| Distribution | Painful | Easy | Easy |
| Licensing/protection | Hard | Medium | Medium |
| Dev speed | Fast | Fast | Medium |
| SmartScreen | Problem | Less issue | Least issue |

## Notes

- **Current build**: Python + PyQt6, fully functional prototype with all tools/UX figured out
- **If selling seriously**: Tauri is the recommended port target — small bundle, native performance, easy `.msi` installer, code signing built into build pipeline
- **Code signing**: Azure Trusted Signing (~$10/mo) recommended once revenue justifies it, avoids SmartScreen warnings
- **Selling platforms**: Gumroad or LemonSqueezy for license key generation + payments
- **Licensing approach**: Supabase for license validation, machine fingerprinting, 7-day offline grace period

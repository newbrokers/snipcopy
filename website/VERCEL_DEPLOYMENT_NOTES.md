# Vercel Deployment Notes

## SavedCode 404 Fix

The site built successfully on Vercel, but visiting `savedcode.com`, `www.savedcode.com`, and the Vercel project URL still showed:

```text
404: NOT_FOUND
Code: NOT_FOUND
```

The build logs proved the Next.js app was fine because Vercel generated the routes:

```text
/
/download
/pricing
/portal
```

The issue was the Vercel project configuration.

## Fix

In Vercel, open the `savedcode` project:

1. Go to `Settings` -> `Build and Deployment`.
2. Change `Framework Preset` from `Other` to `Next.js`.
3. Keep these overrides off:
   - `Build Command`
   - `Output Directory`
   - `Install Command`
   - `Development Command`
4. Confirm `Root Directory` is set to:

```text
website
```

5. Go to `Deployments`.
6. Click `Redeploy` on the latest production deployment.
7. Choose `Production`.
8. Leave `Use existing Build Cache` unchecked.
9. Click `Redeploy`.

After the redeploy completed, the production domains worked:

```text
https://savedcode.com
https://www.savedcode.com
```

## Correct Settings

```text
Framework Preset: Next.js
Root Directory: website
Build Command: default / override off
Output Directory: default / override off
Install Command: default / override off
Node.js Version: 24.x is OK
Production Branch: main
```

## Why This Happened

The app is a Next.js app inside the `website/` subfolder. With the framework set to `Other`, Vercel built the project but did not serve it as the Next.js application, which caused the Vercel edge route to return `404: NOT_FOUND`.

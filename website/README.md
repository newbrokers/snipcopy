# SavedCode Website and License Backend

SavedCode is a Next.js + TypeScript app for marketing, Stripe subscriptions, customer license access, and offline signed Pro license tokens across multiple software products.

Current products:

- SnipCopy
- Draw Overlay
- Audio Crop

## Stack

- Next.js App Router
- TypeScript
- Prisma
- Supabase Postgres for production data
- Stripe Checkout, Billing Portal, and webhooks
- Ed25519 signed offline license tokens
- Email-code customer portal sessions
- Supabase project wiring for Postgres and optional Auth

## Setup

1. Install dependencies:

```powershell
npm.cmd install
```

2. Copy environment values:

```bash
cp .env.example .env
```

3. Generate license signing keys:

```powershell
npm.cmd run db:generate
npx.cmd tsx scripts/generate-keys.ts
```

Put the printed values into `.env`. The private key must stay server-only. The public key is safe to embed in the Windows desktop app for offline verification.

4. Set the database connection:

For Supabase, set `DATABASE_URL` to the Postgres connection string from Dashboard > Connect. Add `?sslmode=require` for the direct Postgres URL.

5. Create or update the database:

```powershell
npm.cmd run db:migrate
```

Check the configured database connection:

```powershell
npm.cmd run db:check
```

6. Start the app:

```powershell
npm.cmd run dev
```

Open `http://localhost:3000`.

## Windows Preview Workflow

On this Windows machine, prefer the production preview flow. It avoids a few local friction points:

- PowerShell blocks `npm.ps1`, so use `npm.cmd`.
- `next dev` can hang at `Starting...` on this setup.
- Prisma can lock native DLL files while a Next server is running.
- Running `next dev` after `next build` can disturb `.next` production output.

Recommended preview steps:

```powershell
cd C:\Users\user\Desktop\SnipCopy\website
npm.cmd run build
npm.cmd start
```

Then open:

```text
http://localhost:3000
```

If a build fails with a Prisma DLL lock, stop the running Node/Next process for this `website` folder, then run `npm.cmd run build` again.

Avoid switching back to `npm.cmd run dev` unless hot reload is specifically needed. If port `3000` is busy, use another port:

```powershell
npm.cmd start -- --port 3001
```

Then open `http://localhost:3001`.

## Stripe

Create a yearly recurring Price in Stripe for each paid product. The checkout flow is product-aware and uses:

- `STRIPE_SECRET_KEY`
- `STRIPE_WEBHOOK_SECRET`
- `STRIPE_SNIPCOPY_PRO_PRICE_ID`
- `STRIPE_DRAW_OVERLAY_PRO_PRICE_ID`
- `STRIPE_AUDIO_CROP_PRO_PRICE_ID`
- `STRIPE_PORTAL_RETURN_URL`

`STRIPE_PRO_PRICE_ID` is still supported as a legacy fallback for SnipCopy. Prefer the product-specific variable names for new deployments.

Checkout sends product metadata to Stripe:

- `platform=savedcode`
- `product=snipcopy`, `draw-overlay`, or `audio-crop`
- `plan=pro-yearly`

Local webhook forwarding:

```powershell
stripe listen --forward-to localhost:3000/api/stripe/webhook
```

Handled events:

- `checkout.session.completed`
- `customer.subscription.created`
- `customer.subscription.updated`
- `customer.subscription.deleted`
- `invoice.paid`
- `invoice.payment_succeeded`
- `invoice.payment_failed`

## Customer Portal

The portal uses passwordless email-code login. Customers enter the purchase email, receive a 6-digit code, and then get a secure HttpOnly session cookie. License lookup and billing portal creation require that verified session; typing an email alone is not enough.

Production portal environment variables:

- `PORTAL_SESSION_SECRET`: random server-only string, at least 32 characters
- `RESEND_API_KEY`: server-only Resend API key for sign-in code emails
- `EMAIL_FROM`: verified sender, for example `SavedCode Support <support@savedcode.com>`

If email delivery is not configured in production, portal sign-in intentionally fails instead of exposing license data.

Resend setup:

1. Add and verify `savedcode.com` in Resend Domains.
2. Add the DNS records Resend provides at your DNS host.
3. Create a Sending access API key.
4. In Vercel Production, set `RESEND_API_KEY` to that key.
5. In Vercel Production, set `EMAIL_FROM=SavedCode Support <support@savedcode.com>`.
6. Redeploy production after saving environment variables.

## License API

- `POST /api/checkout/create`
- `POST /api/stripe/webhook`
- `POST /api/license/activate`
- `POST /api/license/sync`
- `GET /api/license/status` requires a verified portal session
- `GET /api/admin/licenses`
- `POST /api/billing/portal` requires a verified portal session

Activation and sync requests include a product slug plus the app's machine hash. The first successful activation locks the license to one active device. That same device can reactivate or sync after renewal, but a different device receives a reset-required error until support clears the activation.

## Offline Token Payload

The signed token contains:

- `license_key`
- `product_slug`
- `plan`
- `issued_at`
- `expires_at`
- `customer_email`
- `status`

Run an example:

```powershell
npm.cmd run license:example
```

## Admin

Set `ADMIN_TOKEN` in `.env`, then use `/admin`. The admin API expects:

```text
Authorization: Bearer YOUR_ADMIN_TOKEN
```

## Production Notes

Production domain:

- `https://www.savedcode.com`
- Set `APP_URL` to `https://www.savedcode.com` in production.
- Set `STRIPE_PORTAL_RETURN_URL` to `https://www.savedcode.com/portal` in production.
- Stripe webhook endpoint should be `https://www.savedcode.com/api/stripe/webhook`; avoid the apex URL because Vercel redirects it.

Current Supabase project:

- Name: `snipcopy`
- Ref: `eqzlvflcddarslxwblre`
- URL: `https://eqzlvflcddarslxwblre.supabase.co`

The Supabase project name is still `snipcopy`, but the public website brand is now SavedCode. The license tables have been created in Supabase Postgres with RLS enabled and explicit deny policies for `anon` and `authenticated`. The website backend should access these tables only from trusted server code via Prisma/Postgres, not from browser Supabase clients.

For production Prisma, copy the Postgres connection string from Supabase Dashboard > Connect and set it as `DATABASE_URL`. If Supabase provides a separate direct connection string, set it as `DIRECT_URL` for migrations. Keep Stripe keys, webhook secrets, admin tokens, Supabase secret keys, database URLs, and the license private key in server environment variables only.

Current local setup notes:

- Use the exact working Supabase pooler connection string for `DATABASE_URL`.
- URL-encode special characters in the database password, especially `!` as `%21`.
- Keep Stripe secrets and product price IDs in `.env` locally and Vercel environment variables in production.

## Product Direction

SavedCode houses SnipCopy, Draw Overlay, and Audio Crop under one billing and licensing backend. Each license record and signed token carries a product identifier, so a key issued for one product cannot activate a different product. Keep checkout license issuance trusted through Stripe webhook events, not client redirects.

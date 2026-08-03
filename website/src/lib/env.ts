import type { ProductSlug } from "./products";

function required(name: string) {
  const value = process.env[name];
  if (!value) {
    throw new Error(`Missing required environment variable: ${name}`);
  }
  return value;
}

export const env = {
  appUrl: process.env.APP_URL ?? "http://localhost:3000",
  databaseUrl: process.env.DATABASE_URL ?? "file:./dev.db",
  supabaseUrl: process.env.NEXT_PUBLIC_SUPABASE_URL,
  supabasePublishableKey: process.env.NEXT_PUBLIC_SUPABASE_PUBLISHABLE_KEY,
  stripeSecretKey: () => required("STRIPE_SECRET_KEY"),
  stripeWebhookSecret: () => required("STRIPE_WEBHOOK_SECRET"),
  stripeProPriceId: () => process.env.STRIPE_SNIPCOPY_PRO_PRICE_ID ?? required("STRIPE_PRO_PRICE_ID"),
  stripePriceIdForProduct: (productSlug: ProductSlug) => {
    if (productSlug === "snipcopy") return process.env.STRIPE_SNIPCOPY_PRO_PRICE_ID ?? required("STRIPE_PRO_PRICE_ID");
    if (productSlug === "draw-overlay") return required("STRIPE_DRAW_OVERLAY_PRO_PRICE_ID");
    return required("STRIPE_AUDIO_CROP_PRO_PRICE_ID");
  },
  stripePortalReturnUrl: process.env.STRIPE_PORTAL_RETURN_URL ?? "http://localhost:3000/portal",
  licensePrivateKeyPemBase64: () => required("LICENSE_PRIVATE_KEY_PEM_BASE64"),
  licensePublicKeyPemBase64: () => required("LICENSE_PUBLIC_KEY_PEM_BASE64"),
  adminToken: () => required("ADMIN_TOKEN")
};

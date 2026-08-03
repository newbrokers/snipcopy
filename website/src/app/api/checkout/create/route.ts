import { NextRequest } from "next/server";
import Stripe from "stripe";
import { z } from "zod";
import { env } from "@/lib/env";
import { getStripe } from "@/lib/stripe";
import { getClientIp, json, rateLimit } from "@/lib/http";
import { checkoutMetadata, DEFAULT_PRODUCT_SLUG, productBySlug, productSlugSchema } from "@/lib/products";

const checkoutSchema = z.object({
  email: z.string().email().optional(),
  productSlug: productSlugSchema.optional(),
  product_slug: productSlugSchema.optional()
}).transform((data) => ({
  ...data,
  productSlug: data.productSlug ?? data.product_slug ?? DEFAULT_PRODUCT_SLUG
}));

type ManagedPaymentsSessionCreateParams = Stripe.Checkout.SessionCreateParams & {
  managed_payments: {
    enabled: true;
  };
};

export async function POST(request: NextRequest) {
  const ip = getClientIp(request);
  const limited = rateLimit(`checkout:${ip}`, 8);
  if (!limited.ok) return json({ error: "Too many checkout attempts. Please try again shortly." }, { status: 429 });

  const body = checkoutSchema.safeParse(await request.json().catch(() => ({})));
  if (!body.success) return json({ error: "Enter a valid email address." }, { status: 400 });

  const stripe = getStripe();
  const product = productBySlug(body.data.productSlug);
  const metadata = checkoutMetadata(product.slug);
  const checkoutParams: ManagedPaymentsSessionCreateParams = {
    mode: "subscription",
    customer_email: body.data.email,
    line_items: [{ price: env.stripePriceIdForProduct(product.slug), quantity: 1 }],
    success_url: `${env.appUrl}/portal?checkout=success&session_id={CHECKOUT_SESSION_ID}`,
    cancel_url: `${env.appUrl}/pricing?checkout=cancelled&product=${product.slug}`,
    allow_promotion_codes: true,
    billing_address_collection: "auto",
    metadata,
    subscription_data: { metadata },
    managed_payments: { enabled: true }
  };
  const session = await stripe.checkout.sessions.create(checkoutParams);

  return json({ url: session.url });
}

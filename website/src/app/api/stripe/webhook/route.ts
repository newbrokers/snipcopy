import { NextRequest } from "next/server";
import { env } from "@/lib/env";
import { getStripe } from "@/lib/stripe";
import { handleStripeEvent } from "@/lib/stripe-events";
import { json } from "@/lib/http";

export async function POST(request: NextRequest) {
  const signature = request.headers.get("stripe-signature");
  if (!signature) return json({ error: "Missing Stripe signature." }, { status: 400 });

  const rawBody = await request.text();
  const stripe = getStripe();

  try {
    const event = stripe.webhooks.constructEvent(rawBody, signature, env.stripeWebhookSecret());
    const result = await handleStripeEvent(event);
    return json({ received: true, ...result });
  } catch (error) {
    const message = error instanceof Error ? error.message : "Invalid webhook.";
    return json({ error: message }, { status: 400 });
  }
}

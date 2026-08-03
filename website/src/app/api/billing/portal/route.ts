import { NextRequest } from "next/server";
import { env } from "@/lib/env";
import { getStripe } from "@/lib/stripe";
import { json } from "@/lib/http";
import { prisma } from "@/lib/prisma";
import { getPortalSession } from "@/lib/portal-auth";

export async function POST(request: NextRequest) {
  const portalSession = await getPortalSession(request);
  if (!portalSession) return json({ error: "Sign in to manage billing." }, { status: 401 });

  const customer = await prisma.customer.findUnique({ where: { email: portalSession.email } });
  if (!customer?.stripeCustomerId) {
    return json({ error: "No Stripe customer was found for this account." }, { status: 404 });
  }

  const stripe = getStripe();
  const billingSession = await stripe.billingPortal.sessions.create({
    customer: customer.stripeCustomerId,
    return_url: env.stripePortalReturnUrl
  });

  return json({ url: billingSession.url });
}

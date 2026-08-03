import { NextRequest } from "next/server";
import { z } from "zod";
import { env } from "@/lib/env";
import { getStripe } from "@/lib/stripe";
import { json } from "@/lib/http";
import { prisma } from "@/lib/prisma";

const portalSchema = z.object({
  email: z.string().email()
});

export async function POST(request: NextRequest) {
  const body = portalSchema.safeParse(await request.json().catch(() => ({})));
  if (!body.success) return json({ error: "Enter the email used for purchase." }, { status: 400 });

  const customer = await prisma.customer.findUnique({ where: { email: body.data.email.toLowerCase() } });
  if (!customer?.stripeCustomerId) {
    return json({ error: "No Stripe customer was found for that email." }, { status: 404 });
  }

  const stripe = getStripe();
  const session = await stripe.billingPortal.sessions.create({
    customer: customer.stripeCustomerId,
    return_url: env.stripePortalReturnUrl
  });

  return json({ url: session.url });
}

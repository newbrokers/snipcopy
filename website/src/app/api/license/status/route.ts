import { NextRequest } from "next/server";
import { Prisma } from "@prisma/client";
import { z } from "zod";
import { json } from "@/lib/http";
import { prisma } from "@/lib/prisma";
import { getStripe } from "@/lib/stripe";
import { productSlugFromMetadata, productSlugSchema } from "@/lib/products";

const statusSchema = z.object({
  email: z.string().email().optional(),
  licenseKey: z.string().optional(),
  productSlug: productSlugSchema.optional(),
  sessionId: z.string().min(8).optional()
});

export async function GET(request: NextRequest) {
  const params = statusSchema.safeParse({
    email: request.nextUrl.searchParams.get("email") ?? undefined,
    licenseKey: request.nextUrl.searchParams.get("licenseKey") ?? undefined,
    productSlug: request.nextUrl.searchParams.get("productSlug") ?? request.nextUrl.searchParams.get("product_slug") ?? undefined,
    sessionId: request.nextUrl.searchParams.get("session_id") ?? request.nextUrl.searchParams.get("sessionId") ?? undefined
  });
  if (!params.success) return json({ error: "Use a valid email or license key." }, { status: 400 });
  const matchers: Prisma.LicenseWhereInput[] = [];
  let sessionProductSlug = params.data.productSlug;

  if (params.data.sessionId) {
    try {
      const session = await getStripe().checkout.sessions.retrieve(params.data.sessionId);
      const subscriptionId = typeof session.subscription === "string" ? session.subscription : session.subscription?.id;
      const customerId = typeof session.customer === "string" ? session.customer : session.customer?.id;
      const sessionEmail = session.customer_details?.email ?? session.customer_email;

      sessionProductSlug = sessionProductSlug ?? productSlugFromMetadata(session.metadata);
      if (subscriptionId) matchers.push({ stripeSubscriptionId: subscriptionId });
      if (customerId) matchers.push({ stripeCustomerId: customerId });
      if (sessionEmail) matchers.push({ customerEmail: sessionEmail.toLowerCase() });
    } catch {
      return json({ error: "Checkout session was not found. Use your purchase email or license key instead." }, { status: 404 });
    }
  }

  if (params.data.email) matchers.push({ customerEmail: params.data.email.toLowerCase() });
  if (params.data.licenseKey) matchers.push({ licenseKey: params.data.licenseKey });
  if (!matchers.length) return json({ error: "Email, license key, or checkout session is required." }, { status: 400 });

  const licenses = await prisma.license.findMany({
    where: {
      AND: [{ OR: matchers }, sessionProductSlug ? { productSlug: sessionProductSlug } : {}]
    },
    orderBy: { createdAt: "desc" },
    select: {
      licenseKey: true,
      productSlug: true,
      customerEmail: true,
      plan: true,
      status: true,
      issuedAt: true,
      expiresAt: true,
      updatedAt: true
    }
  });

  return json({
    licenses,
    ...(params.data.sessionId && !licenses.length
      ? { pending: true, message: "Payment succeeded. Your license is still being issued by the Stripe webhook. Try again in a minute." }
      : {})
  });
}

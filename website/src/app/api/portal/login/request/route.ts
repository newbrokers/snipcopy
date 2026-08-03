import { NextRequest } from "next/server";
import { z } from "zod";
import { sendPortalLoginCode } from "@/lib/email";
import { getClientIp, json, rateLimit } from "@/lib/http";
import { createPortalLoginCode, normalizeEmail } from "@/lib/portal-auth";
import { getStripe } from "@/lib/stripe";

const requestSchema = z.object({
  email: z.string().email().optional(),
  sessionId: z.string().min(8).optional(),
  session_id: z.string().min(8).optional()
}).transform((data) => ({
  email: data.email,
  sessionId: data.sessionId ?? data.session_id
}));

async function emailFromCheckoutSession(sessionId?: string) {
  if (!sessionId) return undefined;

  const session = await getStripe().checkout.sessions.retrieve(sessionId);
  return session.customer_details?.email ?? session.customer_email ?? undefined;
}

export async function POST(request: NextRequest) {
  try {
    const body = requestSchema.safeParse(await request.json().catch(() => ({})));
    if (!body.success) return json({ error: "Enter the email used for purchase." }, { status: 400 });

    const ip = getClientIp(request);
    const rawEmail = body.data.email ?? (await emailFromCheckoutSession(body.data.sessionId).catch(() => undefined));
    if (!rawEmail) return json({ error: "Enter the email used for purchase." }, { status: 400 });

    const email = normalizeEmail(rawEmail);
    const limitedByIp = rateLimit(`portal-login-ip:${ip}`, 10, 15 * 60 * 1000);
    const limitedByEmail = rateLimit(`portal-login-email:${email}`, 5, 15 * 60 * 1000);
    if (!limitedByIp.ok || !limitedByEmail.ok) {
      return json({ error: "Too many sign-in attempts. Please try again later." }, { status: 429 });
    }

    const loginCode = await createPortalLoginCode(email, ip);
    let devCode: string | undefined;

    if (loginCode.created) {
      const delivery = await sendPortalLoginCode(email, loginCode.code);
      devCode = "devCode" in delivery ? delivery.devCode : undefined;
    }

    return json({
      ok: true,
      message: "If that email has a SavedCode license, a sign-in code has been sent.",
      ...(devCode ? { devCode } : {})
    });
  } catch (error) {
    const message = error instanceof Error ? error.message : "Could not start SavedCode portal sign-in.";
    return json({ error: message }, { status: 500 });
  }
}

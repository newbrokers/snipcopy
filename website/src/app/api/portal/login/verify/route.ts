import { NextRequest } from "next/server";
import { z } from "zod";
import { getClientIp, json, rateLimit } from "@/lib/http";
import { prisma } from "@/lib/prisma";
import { normalizeEmail, setPortalSessionCookie, verifyPortalLoginCode } from "@/lib/portal-auth";

const verifySchema = z.object({
  email: z.string().email(),
  code: z.string().regex(/^\d{6}$/)
});

export async function POST(request: NextRequest) {
  const body = verifySchema.safeParse(await request.json().catch(() => ({})));
  if (!body.success) return json({ error: "Enter the 6-digit sign-in code." }, { status: 400 });

  const email = normalizeEmail(body.data.email);
  const ip = getClientIp(request);
  const limited = rateLimit(`portal-verify:${ip}:${email}`, 10, 15 * 60 * 1000);
  if (!limited.ok) return json({ error: "Too many verification attempts. Please request a new code later." }, { status: 429 });

  const session = await verifyPortalLoginCode(email, body.data.code);
  if (!session) return json({ error: "That code is invalid or expired." }, { status: 401 });

  await prisma.auditEvent.create({
    data: {
      action: "portal.login",
      actorEmail: session.email,
      metadata: JSON.stringify({ sessionId: session.sessionId })
    }
  });

  const response = json({ authenticated: true, email: session.email });
  setPortalSessionCookie(response, session.token, session.expiresAt);
  return response;
}

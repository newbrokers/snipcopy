import { NextRequest } from "next/server";
import { z } from "zod";
import { getActiveLicenseByKeyAndEmail } from "@/lib/license-service";
import { createSignedLicense } from "@/lib/license";
import { getClientIp, hashValue, json, rateLimit } from "@/lib/http";
import { prisma } from "@/lib/prisma";
import { DEFAULT_PRODUCT_SLUG, productSlugSchema } from "@/lib/products";

const activationSchema = z.object({
  licenseKey: z.string().min(12),
  email: z.string().email(),
  machineHash: z.string().max(128).optional(),
  productSlug: productSlugSchema.optional(),
  product_slug: productSlugSchema.optional()
}).transform((data) => ({
  ...data,
  productSlug: data.productSlug ?? data.product_slug ?? DEFAULT_PRODUCT_SLUG
}));

export async function POST(request: NextRequest) {
  const ip = getClientIp(request);
  const limited = rateLimit(`activate:${ip}`, 10);
  if (!limited.ok) return json({ error: "Too many activation attempts. Please try again shortly." }, { status: 429 });

  const body = activationSchema.safeParse(await request.json().catch(() => ({})));
  if (!body.success) return json({ error: "Enter a valid license key and email." }, { status: 400 });

  const license = await getActiveLicenseByKeyAndEmail(body.data.licenseKey, body.data.email, body.data.productSlug);
  if (!license) return json({ error: "No active license found for those details." }, { status: 404 });

  const signed = createSignedLicense({
    licenseKey: license.licenseKey,
    customerEmail: license.customerEmail,
    productSlug: license.productSlug,
    expiresAt: license.expiresAt,
    status: license.status === "past_due" ? "past_due" : "active"
  });

  await prisma.activation.create({
    data: {
      licenseId: license.id,
      email: body.data.email.toLowerCase(),
      machineHash: body.data.machineHash,
      ipHash: hashValue(ip)
    }
  });

  return json({
    licenseKey: license.licenseKey,
    token: signed.token,
    payload: signed.payload
  });
}

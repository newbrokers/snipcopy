import { NextRequest } from "next/server";
import { z } from "zod";
import { createSignedLicense } from "@/lib/license";
import { getClientIp, json, rateLimit } from "@/lib/http";
import { prisma } from "@/lib/prisma";
import { DEFAULT_PRODUCT_SLUG, productSlugSchema } from "@/lib/products";

const syncSchema = z.object({
  licenseKey: z.string().min(12),
  productSlug: productSlugSchema.optional(),
  product_slug: productSlugSchema.optional()
}).transform((data) => ({
  ...data,
  productSlug: data.productSlug ?? data.product_slug ?? DEFAULT_PRODUCT_SLUG
}));

export async function POST(request: NextRequest) {
  const ip = getClientIp(request);
  const limited = rateLimit(`sync:${ip}`, 20);
  if (!limited.ok) return json({ error: "Too many sync attempts. Please try again shortly." }, { status: 429 });

  const body = syncSchema.safeParse(await request.json().catch(() => ({})));
  if (!body.success) return json({ error: "Enter a valid license key." }, { status: 400 });

  const license = await prisma.license.findUnique({ where: { licenseKey: body.data.licenseKey } });
  if (!license) return json({ error: "License not found." }, { status: 404 });
  if (license.productSlug !== body.data.productSlug) return json({ error: "License not found for this product." }, { status: 404 });
  if (license.status !== "active" && license.status !== "past_due") {
    return json({ error: "This license is not renewable online." }, { status: 403 });
  }
  if (license.expiresAt.getTime() < Date.now()) {
    await prisma.license.update({ where: { id: license.id }, data: { status: "expired" } });
    return json({ error: "License expired. Please update billing or contact support." }, { status: 403 });
  }

  const signed = createSignedLicense({
    licenseKey: license.licenseKey,
    customerEmail: license.customerEmail,
    productSlug: license.productSlug,
    expiresAt: license.expiresAt,
    status: license.status === "past_due" ? "past_due" : "active"
  });

  await prisma.licenseToken.create({
    data: {
      licenseId: license.id,
      productSlug: license.productSlug,
      token: signed.token,
      payloadJson: JSON.stringify(signed.payload),
      expiresAt: license.expiresAt
    }
  });

  return json({ licenseKey: license.licenseKey, token: signed.token, payload: signed.payload });
}

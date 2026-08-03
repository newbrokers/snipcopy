import { NextRequest } from "next/server";
import { Prisma } from "@prisma/client";
import { z } from "zod";
import { json } from "@/lib/http";
import { prisma } from "@/lib/prisma";
import { productSlugSchema } from "@/lib/products";

const statusSchema = z.object({
  email: z.string().email().optional(),
  licenseKey: z.string().optional(),
  productSlug: productSlugSchema.optional()
});

export async function GET(request: NextRequest) {
  const params = statusSchema.safeParse({
    email: request.nextUrl.searchParams.get("email") ?? undefined,
    licenseKey: request.nextUrl.searchParams.get("licenseKey") ?? undefined,
    productSlug: request.nextUrl.searchParams.get("productSlug") ?? request.nextUrl.searchParams.get("product_slug") ?? undefined
  });
  if (!params.success) return json({ error: "Use a valid email or license key." }, { status: 400 });
  if (!params.data.email && !params.data.licenseKey) return json({ error: "Email or license key is required." }, { status: 400 });
  const matchers: Prisma.LicenseWhereInput[] = [];
  if (params.data.email) matchers.push({ customerEmail: params.data.email.toLowerCase() });
  if (params.data.licenseKey) matchers.push({ licenseKey: params.data.licenseKey });

  const licenses = await prisma.license.findMany({
    where: {
      AND: [{ OR: matchers }, params.data.productSlug ? { productSlug: params.data.productSlug } : {}]
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

  return json({ licenses });
}

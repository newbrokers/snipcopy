import { NextRequest } from "next/server";
import { z } from "zod";
import { json, requireAdmin } from "@/lib/http";
import { issueOrRenewLicense } from "@/lib/license-service";
import { prisma } from "@/lib/prisma";
import { DEFAULT_PRODUCT_SLUG, productSlugSchema } from "@/lib/products";

const manualLicenseSchema = z.object({
  email: z.string().email(),
  productSlug: productSlugSchema.optional(),
  product_slug: productSlugSchema.optional()
}).transform((data) => ({
  email: data.email.toLowerCase(),
  productSlug: data.productSlug ?? data.product_slug ?? DEFAULT_PRODUCT_SLUG
}));

export async function GET(request: NextRequest) {
  if (!requireAdmin(request)) return json({ error: "Admin authorization required." }, { status: 401 });

  const q = request.nextUrl.searchParams.get("q")?.trim();
  const licenses = await prisma.license.findMany({
    where: q
      ? {
          OR: [
            { licenseKey: { contains: q } },
            { productSlug: { contains: q } },
            { customerEmail: { contains: q } },
            { stripeCustomerId: { contains: q } },
            { stripeSubscriptionId: { contains: q } }
          ]
        }
      : undefined,
    orderBy: { updatedAt: "desc" },
    take: 50,
    include: {
      tokens: {
        orderBy: { issuedAt: "desc" },
        take: 1,
        select: {
          id: true,
          productSlug: true,
          issuedAt: true,
          expiresAt: true,
          licenseId: true
        }
      },
      activations: { orderBy: { createdAt: "desc" }, take: 5 }
    }
  });

  return json({ licenses });
}

export async function POST(request: NextRequest) {
  if (!requireAdmin(request)) return json({ error: "Admin authorization required." }, { status: 401 });

  const body = manualLicenseSchema.safeParse(await request.json().catch(() => ({})));
  if (!body.success) return json({ error: "Enter a valid email and product." }, { status: 400 });

  const { license, payload } = await issueOrRenewLicense({
    email: body.data.email,
    productSlug: body.data.productSlug,
    status: "active"
  });

  return json({
    license: {
      licenseKey: license.licenseKey,
      customerEmail: license.customerEmail,
      productSlug: license.productSlug,
      plan: license.plan,
      status: license.status,
      issuedAt: license.issuedAt,
      expiresAt: license.expiresAt,
      activationEmail: payload.customer_email
    }
  });
}

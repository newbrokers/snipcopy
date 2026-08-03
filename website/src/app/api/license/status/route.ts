import { NextRequest } from "next/server";
import { Prisma } from "@prisma/client";
import { z } from "zod";
import { json } from "@/lib/http";
import { prisma } from "@/lib/prisma";
import { getPortalSession } from "@/lib/portal-auth";
import { productSlugSchema } from "@/lib/products";

const statusSchema = z.object({
  licenseKey: z.string().optional(),
  productSlug: productSlugSchema.optional()
});

export async function GET(request: NextRequest) {
  const session = await getPortalSession(request);
  if (!session) return json({ error: "Sign in to view your SavedCode licenses." }, { status: 401 });

  const params = statusSchema.safeParse({
    licenseKey: request.nextUrl.searchParams.get("licenseKey") ?? undefined,
    productSlug: request.nextUrl.searchParams.get("productSlug") ?? request.nextUrl.searchParams.get("product_slug") ?? undefined
  });
  if (!params.success) return json({ error: "Use a valid email or license key." }, { status: 400 });

  const matchers: Prisma.LicenseWhereInput[] = [{ customerEmail: session.email }];
  if (params.data.licenseKey) matchers.push({ licenseKey: params.data.licenseKey });

  const licenses = await prisma.license.findMany({
    where: {
      AND: [params.data.licenseKey ? { AND: matchers } : matchers[0], params.data.productSlug ? { productSlug: params.data.productSlug } : {}]
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

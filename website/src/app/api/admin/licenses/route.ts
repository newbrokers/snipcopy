import { NextRequest } from "next/server";
import { json, requireAdmin } from "@/lib/http";
import { prisma } from "@/lib/prisma";

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
      tokens: { orderBy: { issuedAt: "desc" }, take: 1 },
      activations: { orderBy: { createdAt: "desc" }, take: 5 }
    }
  });

  return json({ licenses });
}

import { prisma } from "./prisma";
import { addOneYear, createPayload, generateLicenseKey, signLicensePayload } from "./license";
import { normalizeProductSlug } from "./products";

export async function issueOrRenewLicense(input: {
  email: string;
  productSlug?: string | null;
  stripeCustomerId?: string | null;
  stripeSubscriptionId?: string | null;
  expiresAt?: Date;
  status?: string;
}) {
  const email = input.email.toLowerCase();
  const expiresAt = input.expiresAt ?? addOneYear();
  const existingBySubscription = input.stripeSubscriptionId
    ? await prisma.license.findFirst({ where: { stripeSubscriptionId: input.stripeSubscriptionId } })
    : null;
  const productSlug = normalizeProductSlug(input.productSlug ?? existingBySubscription?.productSlug);
  const customer = await prisma.customer.upsert({
    where: { email },
    create: { email, stripeCustomerId: input.stripeCustomerId ?? undefined },
    update: { stripeCustomerId: input.stripeCustomerId ?? undefined }
  });

  const existing = existingBySubscription ?? (await prisma.license.findFirst({ where: { customerEmail: email, plan: "pro", productSlug } }));

  const payloadLicenseKey = existing?.licenseKey ?? generateLicenseKey();
  const payload = createPayload({
    licenseKey: payloadLicenseKey,
    customerEmail: email,
    productSlug,
    expiresAt,
    status: input.status === "past_due" ? "past_due" : input.status === "canceled" ? "canceled" : "active"
  });
  const token = signLicensePayload(payload);

  const license = await prisma.license.upsert({
    where: { licenseKey: payloadLicenseKey },
    create: {
      licenseKey: payloadLicenseKey,
      customerEmail: email,
      stripeCustomerId: input.stripeCustomerId ?? undefined,
      stripeSubscriptionId: input.stripeSubscriptionId ?? undefined,
      productSlug,
      plan: "pro",
      status: payload.status,
      issuedAt: new Date(payload.issued_at),
      expiresAt,
      customerId: customer.id
    },
    update: {
      customerEmail: email,
      stripeCustomerId: input.stripeCustomerId ?? undefined,
      stripeSubscriptionId: input.stripeSubscriptionId ?? undefined,
      productSlug,
      status: payload.status,
      issuedAt: new Date(payload.issued_at),
      expiresAt,
      customerId: customer.id
    }
  });

  await prisma.licenseToken.create({
    data: {
      licenseId: license.id,
      productSlug,
      token,
      payloadJson: JSON.stringify(payload),
      expiresAt
    }
  });

  await prisma.auditEvent.create({
    data: {
      action: existing ? "license.renewed" : "license.issued",
      actorEmail: email,
      customerId: customer.id,
      metadata: JSON.stringify({ licenseKey: license.licenseKey, productSlug, stripeSubscriptionId: input.stripeSubscriptionId })
    }
  });

  return { license, token, payload };
}

export async function getActiveLicenseByKeyAndEmail(licenseKey: string, email?: string, productSlugInput?: string | null) {
  const productSlug = normalizeProductSlug(productSlugInput);
  const license = await prisma.license.findUnique({
    where: { licenseKey },
    include: { tokens: { orderBy: { issuedAt: "desc" }, take: 1 } }
  });

  if (!license) return null;
  if (license.productSlug !== productSlug) return null;
  if (email && license.customerEmail.toLowerCase() !== email.toLowerCase()) return null;
  if (license.status === "expired") return null;
  if (license.expiresAt.getTime() < Date.now()) {
    await prisma.license.update({ where: { id: license.id }, data: { status: "expired" } });
    return null;
  }

  return license;
}

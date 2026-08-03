import { randomBytes, sign, verify } from "crypto";
import { z } from "zod";
import { env } from "./env";
import { DEFAULT_PRODUCT_SLUG, normalizeProductSlug, productSlugSchema } from "./products";

export const licensePayloadSchema = z.object({
  license_key: z.string().min(12),
  product_slug: productSlugSchema.default(DEFAULT_PRODUCT_SLUG),
  plan: z.literal("pro"),
  issued_at: z.string().datetime(),
  expires_at: z.string().datetime(),
  customer_email: z.string().email(),
  status: z.enum(["active", "past_due", "canceled", "expired"])
});

export type LicensePayload = z.infer<typeof licensePayloadSchema>;

export function generateLicenseKey() {
  const alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  const bytes = randomBytes(20);
  const chars = Array.from(bytes, (byte) => alphabet[byte % alphabet.length]);
  return `SCP-${chars.slice(0, 5).join("")}-${chars.slice(5, 10).join("")}-${chars
    .slice(10, 15)
    .join("")}-${chars.slice(15, 20).join("")}`;
}

export function addOneYear(date = new Date()) {
  const next = new Date(date);
  next.setUTCFullYear(next.getUTCFullYear() + 1);
  return next;
}

export function createPayload(input: {
  licenseKey: string;
  customerEmail: string;
  expiresAt: Date;
  productSlug?: string | null;
  issuedAt?: Date;
  status?: LicensePayload["status"];
}): LicensePayload {
  return {
    license_key: input.licenseKey,
    product_slug: normalizeProductSlug(input.productSlug),
    plan: "pro",
    issued_at: (input.issuedAt ?? new Date()).toISOString(),
    expires_at: input.expiresAt.toISOString(),
    customer_email: input.customerEmail.toLowerCase(),
    status: input.status ?? "active"
  };
}

function base64url(input: Buffer | string) {
  return Buffer.from(input).toString("base64url");
}

function fromBase64url(input: string) {
  return Buffer.from(input, "base64url");
}

export function decodePem(base64Pem: string) {
  return Buffer.from(base64Pem, "base64").toString("utf8");
}

export function signLicensePayload(payload: LicensePayload, privateKeyPemBase64 = env.licensePrivateKeyPemBase64()) {
  const checked = licensePayloadSchema.parse(payload);
  const body = base64url(JSON.stringify(checked));
  const signature = sign(null, Buffer.from(body), decodePem(privateKeyPemBase64));
  return `${body}.${signature.toString("base64url")}`;
}

export function verifyLicenseToken(token: string, publicKeyPemBase64 = env.licensePublicKeyPemBase64()) {
  const [body, signature] = token.split(".");
  if (!body || !signature) {
    return { valid: false as const, reason: "Malformed token" };
  }

  const ok = verify(null, Buffer.from(body), decodePem(publicKeyPemBase64), fromBase64url(signature));
  if (!ok) {
    return { valid: false as const, reason: "Invalid signature" };
  }

  const parsed = licensePayloadSchema.safeParse(JSON.parse(fromBase64url(body).toString("utf8")));
  if (!parsed.success) {
    return { valid: false as const, reason: "Invalid payload" };
  }

  if (new Date(parsed.data.expires_at).getTime() < Date.now()) {
    return { valid: false as const, reason: "Token expired", payload: parsed.data };
  }

  return { valid: true as const, payload: parsed.data };
}

export function createSignedLicense(input: Parameters<typeof createPayload>[0]) {
  const payload = createPayload(input);
  return {
    payload,
    token: signLicensePayload(payload)
  };
}

import { generateKeyPairSync } from "crypto";
import { describe, expect, it } from "vitest";
import { createPayload, generateLicenseKey, signLicensePayload, verifyLicenseToken } from "../src/lib/license";

function keys() {
  const pair = generateKeyPairSync("ed25519", {
    privateKeyEncoding: { type: "pkcs8", format: "pem" },
    publicKeyEncoding: { type: "spki", format: "pem" }
  });
  return {
    privateKey: Buffer.from(pair.privateKey).toString("base64"),
    publicKey: Buffer.from(pair.publicKey).toString("base64")
  };
}

describe("license signing", () => {
  it("creates readable license keys", () => {
    expect(generateLicenseKey()).toMatch(/^SCP-[A-Z2-9]{5}-[A-Z2-9]{5}-[A-Z2-9]{5}-[A-Z2-9]{5}$/);
  });

  it("signs and verifies an offline token", () => {
    const key = keys();
    const expiresAt = new Date(Date.now() + 86_400_000);
    const payload = createPayload({
      licenseKey: "SCP-ABCDE-FGHJK-LMNPQ-RSTUV",
      customerEmail: "Buyer@Example.com",
      expiresAt
    });

    const token = signLicensePayload(payload, key.privateKey);
    const result = verifyLicenseToken(token, key.publicKey);

    expect(result.valid).toBe(true);
    if (result.valid) {
      expect(result.payload.customer_email).toBe("buyer@example.com");
      expect(result.payload.product_slug).toBe("snipcopy");
      expect(result.payload.plan).toBe("pro");
    }
  });

  it("includes the product slug in product-specific tokens", () => {
    const payload = createPayload({
      licenseKey: "SCP-ABCDE-FGHJK-LMNPQ-RSTUV",
      customerEmail: "buyer@example.com",
      productSlug: "draw-overlay",
      expiresAt: new Date(Date.now() + 86_400_000)
    });

    expect(payload.product_slug).toBe("draw-overlay");
  });

  it("rejects tampered tokens", () => {
    const key = keys();
    const payload = createPayload({
      licenseKey: "SCP-ABCDE-FGHJK-LMNPQ-RSTUV",
      customerEmail: "buyer@example.com",
      expiresAt: new Date(Date.now() + 86_400_000)
    });
    const token = signLicensePayload(payload, key.privateKey);
    const [body, signature] = token.split(".");
    const tamperedBody = `${body.slice(0, -1)}${body.endsWith("A") ? "B" : "A"}`;
    const tampered = `${tamperedBody}.${signature}`;

    expect(verifyLicenseToken(tampered, key.publicKey).valid).toBe(false);
  });
});

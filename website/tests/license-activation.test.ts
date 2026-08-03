import { beforeEach, describe, expect, it, vi } from "vitest";

const mocks = vi.hoisted(() => ({
  getActiveLicenseByKeyAndEmail: vi.fn(),
  createSignedLicense: vi.fn(),
  prisma: {
    activation: {
      findUnique: vi.fn(),
      create: vi.fn()
    },
    license: {
      findUnique: vi.fn(),
      update: vi.fn()
    },
    licenseToken: {
      create: vi.fn()
    }
  }
}));

vi.mock("@/lib/license-service", () => ({
  getActiveLicenseByKeyAndEmail: mocks.getActiveLicenseByKeyAndEmail
}));

vi.mock("@/lib/license", () => ({
  createSignedLicense: mocks.createSignedLicense
}));

vi.mock("@/lib/prisma", () => ({
  prisma: mocks.prisma
}));

vi.mock("@/lib/http", () => ({
  getClientIp: () => "127.0.0.1",
  hashValue: (value: string) => `hash:${value}`,
  json: (data: unknown, init?: ResponseInit) => Response.json(data, init),
  rateLimit: () => ({ ok: true, remaining: 9 })
}));

const activeLicense = {
  id: "license_123",
  licenseKey: "SCP-ABCDE-FGHJK-LMNPQ-RSTUV",
  customerEmail: "buyer@example.com",
  productSlug: "snipcopy",
  expiresAt: new Date(Date.now() + 86_400_000),
  status: "active"
};

function jsonRequest(path: string, body: unknown) {
  return new Request(`https://www.savedcode.com${path}`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(body)
  });
}

describe("license activation device lock", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.getActiveLicenseByKeyAndEmail.mockResolvedValue(activeLicense);
    mocks.createSignedLicense.mockReturnValue({ token: "signed-token", payload: { product_slug: "snipcopy" } });
    mocks.prisma.activation.create.mockResolvedValue({});
    mocks.prisma.licenseToken.create.mockResolvedValue({});
  });

  it("records the first activating machine", async () => {
    mocks.prisma.activation.findUnique.mockResolvedValue(null);

    const { POST } = await import("@/app/api/license/activate/route");
    const response = await POST(jsonRequest("/api/license/activate", {
      licenseKey: activeLicense.licenseKey,
      email: "buyer@example.com",
      product_slug: "snipcopy",
      machineHash: "machine-hash-123456"
    }) as never);

    expect(response.status).toBe(200);
    expect(mocks.prisma.activation.create).toHaveBeenCalledWith({
      data: {
        licenseId: activeLicense.id,
        email: "buyer@example.com",
        machineHash: "machine-hash-123456",
        ipHash: "hash:127.0.0.1"
      }
    });
  });

  it("blocks activation from a different machine", async () => {
    mocks.prisma.activation.findUnique.mockResolvedValue({ licenseId: activeLicense.id, machineHash: "original-machine-hash" });

    const { POST } = await import("@/app/api/license/activate/route");
    const response = await POST(jsonRequest("/api/license/activate", {
      licenseKey: activeLicense.licenseKey,
      email: "buyer@example.com",
      product_slug: "snipcopy",
      machineHash: "different-machine-hash"
    }) as never);
    const body = await response.json();

    expect(response.status).toBe(403);
    expect(body.error).toContain("already activated");
    expect(mocks.prisma.activation.create).not.toHaveBeenCalled();
    expect(mocks.createSignedLicense).not.toHaveBeenCalled();
  });

  it("allows sync only from the activated machine", async () => {
    mocks.prisma.license.findUnique.mockResolvedValue(activeLicense);
    mocks.prisma.activation.findUnique.mockResolvedValue({ licenseId: activeLicense.id, machineHash: "original-machine-hash" });

    const { POST } = await import("@/app/api/license/sync/route");
    const response = await POST(jsonRequest("/api/license/sync", {
      licenseKey: activeLicense.licenseKey,
      product_slug: "snipcopy",
      machineHash: "original-machine-hash"
    }) as never);

    expect(response.status).toBe(200);
    expect(mocks.prisma.licenseToken.create).toHaveBeenCalled();
  });

  it("blocks sync from a different machine", async () => {
    mocks.prisma.license.findUnique.mockResolvedValue(activeLicense);
    mocks.prisma.activation.findUnique.mockResolvedValue({ licenseId: activeLicense.id, machineHash: "original-machine-hash" });

    const { POST } = await import("@/app/api/license/sync/route");
    const response = await POST(jsonRequest("/api/license/sync", {
      licenseKey: activeLicense.licenseKey,
      product_slug: "snipcopy",
      machineHash: "different-machine-hash"
    }) as never);
    const body = await response.json();

    expect(response.status).toBe(403);
    expect(body.error).toContain("already activated");
    expect(mocks.prisma.licenseToken.create).not.toHaveBeenCalled();
  });
});

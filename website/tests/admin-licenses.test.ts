import { beforeEach, describe, expect, it, vi } from "vitest";

const mocks = vi.hoisted(() => ({
  issueOrRenewLicense: vi.fn()
}));

vi.mock("@/lib/license-service", () => ({
  issueOrRenewLicense: mocks.issueOrRenewLicense
}));

function jsonRequest(body: unknown, token = "admin-secret") {
  return new Request("https://www.savedcode.com/api/admin/licenses", {
    method: "POST",
    headers: {
      authorization: `Bearer ${token}`,
      "content-type": "application/json"
    },
    body: JSON.stringify(body)
  });
}

describe("admin license issuing", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    process.env.ADMIN_TOKEN = "admin-secret";
    mocks.issueOrRenewLicense.mockResolvedValue({
      license: {
        licenseKey: "SCP-FRIEND-ABCDE-FGHJK-LMNPQ",
        customerEmail: "friend@example.com",
        productSlug: "draw-overlay",
        plan: "pro",
        status: "active",
        issuedAt: new Date("2026-08-03T20:00:00.000Z"),
        expiresAt: new Date("2027-08-03T20:00:00.000Z")
      },
      payload: {
        customer_email: "friend@example.com"
      }
    });
  });

  it("requires the admin token", async () => {
    const { POST } = await import("@/app/api/admin/licenses/route");
    const response = await POST(jsonRequest({ email: "friend@example.com", productSlug: "snipcopy" }, "wrong-token") as never);

    expect(response.status).toBe(401);
    expect(mocks.issueOrRenewLicense).not.toHaveBeenCalled();
  });

  it("issues a one-year manual license for the selected product", async () => {
    const { POST } = await import("@/app/api/admin/licenses/route");
    const response = await POST(jsonRequest({ email: "Friend@Example.com", productSlug: "draw-overlay" }) as never);
    const body = await response.json();

    expect(response.status).toBe(200);
    expect(mocks.issueOrRenewLicense).toHaveBeenCalledWith({
      email: "friend@example.com",
      productSlug: "draw-overlay",
      status: "active"
    });
    expect(body.license).toMatchObject({
      licenseKey: "SCP-FRIEND-ABCDE-FGHJK-LMNPQ",
      customerEmail: "friend@example.com",
      productSlug: "draw-overlay",
      activationEmail: "friend@example.com",
      status: "active"
    });
  });
});

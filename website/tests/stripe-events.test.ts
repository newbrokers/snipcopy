import { describe, expect, it, vi } from "vitest";

vi.mock("../src/lib/license-service", () => ({
  issueOrRenewLicense: vi.fn(async () => ({ license: { licenseKey: "SCP-TEST" }, token: "token", payload: {} }))
}));

vi.mock("../src/lib/prisma", () => ({
  prisma: {
    stripeSubscription: { upsert: vi.fn(async () => ({})) },
    license: { updateMany: vi.fn(async () => ({ count: 1 })) }
  }
}));

describe("stripe event handler", () => {
  it("issues a license after checkout completion", async () => {
    const { handleStripeEvent } = await import("../src/lib/stripe-events");
    const { issueOrRenewLicense } = await import("../src/lib/license-service");

    const result = await handleStripeEvent({
      type: "checkout.session.completed",
      data: {
        object: {
          customer_details: { email: "buyer@example.com" },
          customer: "cus_123",
          subscription: "sub_123",
          metadata: { product: "audio-crop" }
        }
      }
    } as never);

    expect(result.action).toBe("checkout.license_issued");
    expect(issueOrRenewLicense).toHaveBeenCalledWith({
      email: "buyer@example.com",
      productSlug: "audio-crop",
      stripeCustomerId: "cus_123",
      stripeSubscriptionId: "sub_123"
    });
  });

  it("marks a license past due when payment fails", async () => {
    const { handleStripeEvent } = await import("../src/lib/stripe-events");
    const { prisma } = await import("../src/lib/prisma");

    const result = await handleStripeEvent({
      type: "invoice.payment_failed",
      data: { object: { subscription: "sub_123" } }
    } as never);

    expect(result.action).toBe("invoice.payment_failed");
    expect(prisma.license.updateMany).toHaveBeenCalledWith({
      where: { stripeSubscriptionId: "sub_123" },
      data: { status: "past_due" }
    });
  });
});

import Stripe from "stripe";
import { prisma } from "./prisma";
import { issueOrRenewLicense } from "./license-service";
import { DEFAULT_PRODUCT_SLUG, maybeProductSlugFromMetadata, productSlugFromMetadata } from "./products";

function toDateFromSeconds(value?: number | null) {
  return value ? new Date(value * 1000) : undefined;
}

function getCustomerEmail(session: Stripe.Checkout.Session) {
  return session.customer_details?.email || session.customer_email || undefined;
}

export async function handleStripeEvent(event: Stripe.Event) {
  switch (event.type) {
    case "checkout.session.completed": {
      const session = event.data.object as Stripe.Checkout.Session;
      const email = getCustomerEmail(session);
      if (!email || !session.subscription || !session.customer) {
        return { handled: true, action: "checkout.skipped" };
      }

      await issueOrRenewLicense({
        email,
        productSlug: productSlugFromMetadata(session.metadata),
        stripeCustomerId: String(session.customer),
        stripeSubscriptionId: String(session.subscription)
      });
      return { handled: true, action: "checkout.license_issued" };
    }
    case "invoice.payment_succeeded": {
      const invoice = event.data.object as Stripe.Invoice;
      const email = invoice.customer_email;
      const subscriptionId = typeof invoice.subscription === "string" ? invoice.subscription : invoice.subscription?.id;
      if (!email || !subscriptionId) return { handled: true, action: "invoice.skipped" };

      await issueOrRenewLicense({
        email,
        productSlug: maybeProductSlugFromMetadata(invoice.metadata, invoice.subscription_details?.metadata),
        stripeCustomerId: typeof invoice.customer === "string" ? invoice.customer : invoice.customer?.id,
        stripeSubscriptionId: subscriptionId,
        expiresAt: toDateFromSeconds(invoice.lines.data[0]?.period?.end)
      });
      return { handled: true, action: "invoice.license_renewed" };
    }
    case "customer.subscription.updated":
    case "customer.subscription.deleted": {
      const subscription = event.data.object as Stripe.Subscription;
      const status = subscription.status === "active" || subscription.status === "trialing" ? "active" : subscription.status === "past_due" ? "past_due" : "canceled";
      const productSlug = maybeProductSlugFromMetadata(subscription.metadata);
      await prisma.stripeSubscription.upsert({
        where: { stripeSubscriptionId: subscription.id },
        create: {
          stripeSubscriptionId: subscription.id,
          stripeCustomerId: String(subscription.customer),
          productSlug: productSlug ?? DEFAULT_PRODUCT_SLUG,
          status,
          currentPeriodEnd: toDateFromSeconds(subscription.current_period_end),
          cancelAtPeriodEnd: subscription.cancel_at_period_end
        },
        update: {
          ...(productSlug ? { productSlug } : {}),
          status,
          currentPeriodEnd: toDateFromSeconds(subscription.current_period_end),
          cancelAtPeriodEnd: subscription.cancel_at_period_end
        }
      });
      await prisma.license.updateMany({
        where: { stripeSubscriptionId: subscription.id },
        data: {
          ...(productSlug ? { productSlug } : {}),
          status,
          expiresAt: toDateFromSeconds(subscription.current_period_end)
        }
      });
      return { handled: true, action: "subscription.updated" };
    }
    case "invoice.payment_failed": {
      const invoice = event.data.object as Stripe.Invoice;
      const subscriptionId = typeof invoice.subscription === "string" ? invoice.subscription : invoice.subscription?.id;
      if (subscriptionId) {
        await prisma.license.updateMany({ where: { stripeSubscriptionId: subscriptionId }, data: { status: "past_due" } });
      }
      return { handled: true, action: "invoice.payment_failed" };
    }
    default:
      return { handled: false, action: "ignored" };
  }
}

import Stripe from "stripe";
import { prisma } from "./prisma";
import { issueOrRenewLicense } from "./license-service";
import { getStripe } from "./stripe";
import { DEFAULT_PRODUCT_SLUG, maybeProductSlugFromMetadata, productSlugFromMetadata } from "./products";

function toDateFromSeconds(value?: number | null) {
  return value ? new Date(value * 1000) : undefined;
}

function objectId(value: string | { id?: string } | null | undefined) {
  return typeof value === "string" ? value : value?.id;
}

function getCustomerEmail(session: Stripe.Checkout.Session) {
  return session.customer_details?.email || session.customer_email || undefined;
}

async function getStripeCustomerEmail(customer: string | Stripe.Customer | Stripe.DeletedCustomer | null | undefined) {
  if (!customer) return undefined;
  if (typeof customer !== "string") return "email" in customer ? customer.email ?? undefined : undefined;

  const stripeCustomer = await getStripe().customers.retrieve(customer);
  if (stripeCustomer.deleted) return undefined;
  return stripeCustomer.email ?? undefined;
}

function subscriptionLicenseStatus(subscription: Stripe.Subscription) {
  return subscription.status === "active" || subscription.status === "trialing"
    ? "active"
    : subscription.status === "past_due"
      ? "past_due"
      : "canceled";
}

function subscriptionPeriodEnd(subscription: Stripe.Subscription) {
  return toDateFromSeconds(subscription.current_period_end);
}

async function upsertStripeSubscription(subscription: Stripe.Subscription, productSlug: string, status: string) {
  const customerId = objectId(subscription.customer);
  if (!customerId) return;

  await prisma.stripeSubscription.upsert({
    where: { stripeSubscriptionId: subscription.id },
    create: {
      stripeSubscriptionId: subscription.id,
      stripeCustomerId: customerId,
      productSlug,
      status,
      currentPeriodEnd: subscriptionPeriodEnd(subscription),
      cancelAtPeriodEnd: subscription.cancel_at_period_end
    },
    update: {
      productSlug,
      status,
      currentPeriodEnd: subscriptionPeriodEnd(subscription),
      cancelAtPeriodEnd: subscription.cancel_at_period_end
    }
  });
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
    case "invoice.payment_succeeded":
    case "invoice.paid": {
      const invoice = event.data.object as Stripe.Invoice;
      const email = invoice.customer_email ?? (await getStripeCustomerEmail(invoice.customer));
      const subscriptionId = objectId(invoice.subscription);
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
    case "customer.subscription.created": {
      const subscription = event.data.object as Stripe.Subscription;
      const customerId = objectId(subscription.customer);
      const email = await getStripeCustomerEmail(subscription.customer);
      if (!email || !customerId) return { handled: true, action: "subscription.skipped" };

      const productSlug = productSlugFromMetadata(subscription.metadata);
      const status = subscriptionLicenseStatus(subscription);
      await issueOrRenewLicense({
        email,
        productSlug,
        stripeCustomerId: customerId,
        stripeSubscriptionId: subscription.id,
        expiresAt: subscriptionPeriodEnd(subscription),
        status
      });
      await upsertStripeSubscription(subscription, productSlug, status);
      return { handled: true, action: "subscription.license_issued" };
    }
    case "customer.subscription.updated":
    case "customer.subscription.deleted": {
      const subscription = event.data.object as Stripe.Subscription;
      const status = subscriptionLicenseStatus(subscription);
      const productSlug = maybeProductSlugFromMetadata(subscription.metadata);
      await upsertStripeSubscription(subscription, productSlug ?? DEFAULT_PRODUCT_SLUG, status);
      await prisma.license.updateMany({
        where: { stripeSubscriptionId: subscription.id },
        data: {
          ...(productSlug ? { productSlug } : {}),
          status,
          expiresAt: subscriptionPeriodEnd(subscription)
        }
      });
      return { handled: true, action: "subscription.updated" };
    }
    case "invoice.payment_failed": {
      const invoice = event.data.object as Stripe.Invoice;
      const subscriptionId = objectId(invoice.subscription);
      if (subscriptionId) {
        await prisma.license.updateMany({ where: { stripeSubscriptionId: subscriptionId }, data: { status: "past_due" } });
      }
      return { handled: true, action: "invoice.payment_failed" };
    }
    default:
      return { handled: false, action: "ignored" };
  }
}

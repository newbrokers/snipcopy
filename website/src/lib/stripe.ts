import Stripe from "stripe";
import { env } from "./env";

const STRIPE_API_VERSION = "2025-03-31.basil" as Stripe.LatestApiVersion;

export function getStripe() {
  return new Stripe(env.stripeSecretKey(), {
    apiVersion: STRIPE_API_VERSION,
    appInfo: {
      name: "SavedCode"
    }
  });
}

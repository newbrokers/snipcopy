import { z } from "zod";

export const SAVEDCODE_PLATFORM = "savedcode";
export const DEFAULT_PRODUCT_SLUG = "snipcopy";
export const PRODUCT_SLUGS = ["snipcopy", "draw-overlay", "audio-crop"] as const;
export const productSlugSchema = z.enum(PRODUCT_SLUGS);

export type ProductSlug = (typeof PRODUCT_SLUGS)[number];

export type Product = {
  slug: ProductSlug;
  name: string;
  headline: string;
  description: string;
  imageSrc: string;
  priceEnvKey: string;
  legacyPriceEnvKey?: string;
  downloadHref?: string;
  freeFeatures: string[];
  proFeatures: string[];
};

export const PRODUCTS: Record<ProductSlug, Product> = {
  snipcopy: {
    slug: "snipcopy",
    name: "SnipCopy",
    headline: "Fast screenshot capture and markup for Windows.",
    description: "Capture, annotate, copy, save, and keep a searchable local history of work screenshots.",
    imageSrc: "/images/products/snipcopy.png",
    priceEnvKey: "STRIPE_SNIPCOPY_PRO_PRICE_ID",
    legacyPriceEnvKey: "STRIPE_PRO_PRICE_ID",
    downloadHref: "/downloads/SnipCopy.zip",
    freeFeatures: ["Snip to clipboard", "Pen, arrow, and text tools", "Last 5 snips in history"],
    proFeatures: ["Expanded screenshot history", "Blur tool", "Redaction tool", "Numbered step callouts", "Priority updates"]
  },
  "draw-overlay": {
    slug: "draw-overlay",
    name: "Draw Overlay",
    headline: "Draw over any screen while presenting or recording.",
    description: "Keep a movable annotation toolbar above your desktop for calls, demos, lessons, and training videos.",
    imageSrc: "/images/products/draw-overlay.png",
    priceEnvKey: "STRIPE_DRAW_OVERLAY_PRO_PRICE_ID",
    downloadHref: "/downloads/DrawOverlay.zip",
    freeFeatures: ["Pen overlay", "Highlighter and eraser", "Movable toolbar", "Custom color and width"],
    proFeatures: ["Line tool", "Arrow tool", "Rectangle and ellipse tools", "Text overlay", "Priority updates"]
  },
  "audio-crop": {
    slug: "audio-crop",
    name: "Audio Crop",
    headline: "Trim audio clips quickly without opening a full editor.",
    description: "A planned SavedCode audio utility for trimming recordings, exporting clean clips, and speeding up repetitive audio cleanup.",
    imageSrc: "/images/products/audio-crop.png",
    priceEnvKey: "STRIPE_AUDIO_CROP_PRO_PRICE_ID",
    freeFeatures: ["Open common audio files", "Basic trim", "Export one clip"],
    proFeatures: ["Batch cropping", "Silence trimming", "Preset export settings", "Recent project history", "Priority updates"]
  }
};

export const PRODUCT_LIST = PRODUCT_SLUGS.map((slug) => PRODUCTS[slug]);

export function normalizeProductSlug(value?: string | null): ProductSlug {
  const candidate = (value || DEFAULT_PRODUCT_SLUG).trim().toLowerCase();
  const parsed = productSlugSchema.safeParse(candidate);
  return parsed.success ? parsed.data : DEFAULT_PRODUCT_SLUG;
}

export function maybeProductSlug(value?: string | null): ProductSlug | undefined {
  if (!value) return undefined;
  const parsed = productSlugSchema.safeParse(value.trim().toLowerCase());
  return parsed.success ? parsed.data : undefined;
}

export function productBySlug(productSlug: ProductSlug) {
  return PRODUCTS[productSlug];
}

export function checkoutMetadata(productSlug: ProductSlug = DEFAULT_PRODUCT_SLUG) {
  return {
    platform: SAVEDCODE_PLATFORM,
    product: productSlug,
    plan: "pro-yearly"
  };
}

export function maybeProductSlugFromMetadata(...sources: Array<Record<string, string> | null | undefined>) {
  for (const source of sources) {
    const productSlug = maybeProductSlug(source?.product_slug ?? source?.productSlug ?? source?.product);
    if (productSlug) return productSlug;
  }
  return undefined;
}

export function productSlugFromMetadata(...sources: Array<Record<string, string> | null | undefined>) {
  return maybeProductSlugFromMetadata(...sources) ?? DEFAULT_PRODUCT_SLUG;
}

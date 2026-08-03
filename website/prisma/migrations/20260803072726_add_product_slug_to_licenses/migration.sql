ALTER TABLE "License" ADD COLUMN "productSlug" TEXT NOT NULL DEFAULT 'snipcopy';
ALTER TABLE "LicenseToken" ADD COLUMN "productSlug" TEXT NOT NULL DEFAULT 'snipcopy';
ALTER TABLE "StripeSubscription" ADD COLUMN "productSlug" TEXT NOT NULL DEFAULT 'snipcopy';

CREATE INDEX "License_customerEmail_productSlug_idx" ON "License"("customerEmail", "productSlug");
CREATE INDEX "License_productSlug_idx" ON "License"("productSlug");
CREATE INDEX "LicenseToken_productSlug_idx" ON "LicenseToken"("productSlug");
CREATE INDEX "StripeSubscription_productSlug_idx" ON "StripeSubscription"("productSlug");

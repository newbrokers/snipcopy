alter table public."License"
  add column if not exists "productSlug" text not null default 'snipcopy';

alter table public."LicenseToken"
  add column if not exists "productSlug" text not null default 'snipcopy';

alter table public."StripeSubscription"
  add column if not exists "productSlug" text not null default 'snipcopy';

create index if not exists "License_customerEmail_productSlug_idx" on public."License"("customerEmail", "productSlug");
create index if not exists "License_productSlug_idx" on public."License"("productSlug");
create index if not exists "LicenseToken_productSlug_idx" on public."LicenseToken"("productSlug");
create index if not exists "StripeSubscription_productSlug_idx" on public."StripeSubscription"("productSlug");

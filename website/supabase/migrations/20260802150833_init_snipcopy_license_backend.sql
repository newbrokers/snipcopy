create table if not exists public."Customer" (
  "id" text primary key,
  "email" text not null unique,
  "name" text,
  "stripeCustomerId" text unique,
  "createdAt" timestamptz not null default now(),
  "updatedAt" timestamptz not null default now()
);

create table if not exists public."License" (
  "id" text primary key,
  "licenseKey" text not null unique,
  "customerEmail" text not null,
  "stripeCustomerId" text,
  "stripeSubscriptionId" text,
  "plan" text not null default 'pro',
  "status" text not null default 'active',
  "issuedAt" timestamptz not null default now(),
  "expiresAt" timestamptz not null,
  "createdAt" timestamptz not null default now(),
  "updatedAt" timestamptz not null default now(),
  "customerId" text references public."Customer"("id") on delete set null on update cascade
);

create table if not exists public."LicenseToken" (
  "id" text primary key,
  "token" text not null,
  "payloadJson" text not null,
  "issuedAt" timestamptz not null default now(),
  "expiresAt" timestamptz not null,
  "licenseId" text not null references public."License"("id") on delete cascade on update cascade
);

create table if not exists public."Activation" (
  "id" text primary key,
  "licenseId" text not null references public."License"("id") on delete cascade on update cascade,
  "email" text not null,
  "machineHash" text,
  "ipHash" text,
  "createdAt" timestamptz not null default now()
);

create table if not exists public."StripeSubscription" (
  "id" text primary key,
  "stripeSubscriptionId" text not null unique,
  "stripeCustomerId" text not null,
  "customerId" text references public."Customer"("id") on delete set null on update cascade,
  "status" text not null,
  "currentPeriodEnd" timestamptz,
  "cancelAtPeriodEnd" boolean not null default false,
  "createdAt" timestamptz not null default now(),
  "updatedAt" timestamptz not null default now()
);

create table if not exists public."AuditEvent" (
  "id" text primary key,
  "action" text not null,
  "actorEmail" text,
  "metadata" text,
  "createdAt" timestamptz not null default now(),
  "customerId" text references public."Customer"("id") on delete set null on update cascade
);

create index if not exists "License_customerEmail_idx" on public."License"("customerEmail");
create index if not exists "License_stripeSubscriptionId_idx" on public."License"("stripeSubscriptionId");
create index if not exists "License_status_idx" on public."License"("status");
create index if not exists "AuditEvent_action_idx" on public."AuditEvent"("action");

alter table public."Customer" enable row level security;
alter table public."License" enable row level security;
alter table public."LicenseToken" enable row level security;
alter table public."Activation" enable row level security;
alter table public."StripeSubscription" enable row level security;
alter table public."AuditEvent" enable row level security;

revoke all on table public."Customer" from anon, authenticated;
revoke all on table public."License" from anon, authenticated;
revoke all on table public."LicenseToken" from anon, authenticated;
revoke all on table public."Activation" from anon, authenticated;
revoke all on table public."StripeSubscription" from anon, authenticated;
revoke all on table public."AuditEvent" from anon, authenticated;

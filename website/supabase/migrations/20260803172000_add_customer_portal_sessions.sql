create table if not exists public."PortalLoginCode" (
  "id" text primary key,
  "email" text not null,
  "codeHash" text not null,
  "ipHash" text,
  "createdAt" timestamptz not null default now(),
  "expiresAt" timestamptz not null,
  "usedAt" timestamptz
);

create table if not exists public."CustomerPortalSession" (
  "id" text primary key,
  "email" text not null,
  "sessionTokenHash" text not null unique,
  "createdAt" timestamptz not null default now(),
  "expiresAt" timestamptz not null,
  "lastSeenAt" timestamptz,
  "revokedAt" timestamptz,
  "customerId" text references public."Customer"("id") on delete set null on update cascade
);

create index if not exists "PortalLoginCode_email_idx" on public."PortalLoginCode"("email");
create index if not exists "PortalLoginCode_expiresAt_idx" on public."PortalLoginCode"("expiresAt");
create index if not exists "CustomerPortalSession_email_idx" on public."CustomerPortalSession"("email");
create index if not exists "CustomerPortalSession_expiresAt_idx" on public."CustomerPortalSession"("expiresAt");
create index if not exists "CustomerPortalSession_customerId_idx" on public."CustomerPortalSession"("customerId");

alter table public."PortalLoginCode" enable row level security;
alter table public."CustomerPortalSession" enable row level security;

revoke all on table public."PortalLoginCode" from anon, authenticated;
revoke all on table public."CustomerPortalSession" from anon, authenticated;

create policy "deny public portal login code access" on public."PortalLoginCode" for all to anon, authenticated using (false) with check (false);
create policy "deny public customer portal session access" on public."CustomerPortalSession" for all to anon, authenticated using (false) with check (false);

create policy "deny public customer access" on public."Customer" for all to anon, authenticated using (false) with check (false);
create policy "deny public license access" on public."License" for all to anon, authenticated using (false) with check (false);
create policy "deny public license token access" on public."LicenseToken" for all to anon, authenticated using (false) with check (false);
create policy "deny public activation access" on public."Activation" for all to anon, authenticated using (false) with check (false);
create policy "deny public stripe subscription access" on public."StripeSubscription" for all to anon, authenticated using (false) with check (false);
create policy "deny public audit event access" on public."AuditEvent" for all to anon, authenticated using (false) with check (false);

create index if not exists "License_customerId_idx" on public."License"("customerId");
create index if not exists "LicenseToken_licenseId_idx" on public."LicenseToken"("licenseId");
create index if not exists "Activation_licenseId_idx" on public."Activation"("licenseId");
create index if not exists "StripeSubscription_customerId_idx" on public."StripeSubscription"("customerId");
create index if not exists "AuditEvent_customerId_idx" on public."AuditEvent"("customerId");

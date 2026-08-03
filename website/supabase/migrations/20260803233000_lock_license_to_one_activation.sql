delete from public."Activation" where "machineHash" is null;

alter table public."Activation"
  alter column "machineHash" set not null;

create unique index if not exists "Activation_licenseId_key" on public."Activation"("licenseId");

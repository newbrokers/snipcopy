DELETE FROM "Activation" WHERE "machineHash" IS NULL;

ALTER TABLE "Activation" ALTER COLUMN "machineHash" SET NOT NULL;

CREATE UNIQUE INDEX "Activation_licenseId_key" ON "Activation"("licenseId");

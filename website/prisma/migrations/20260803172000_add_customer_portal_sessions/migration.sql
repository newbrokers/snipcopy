-- CreateTable
CREATE TABLE "PortalLoginCode" (
    "id" TEXT NOT NULL,
    "email" TEXT NOT NULL,
    "codeHash" TEXT NOT NULL,
    "ipHash" TEXT,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "expiresAt" TIMESTAMP(3) NOT NULL,
    "usedAt" TIMESTAMP(3),

    CONSTRAINT "PortalLoginCode_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "CustomerPortalSession" (
    "id" TEXT NOT NULL,
    "email" TEXT NOT NULL,
    "sessionTokenHash" TEXT NOT NULL,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "expiresAt" TIMESTAMP(3) NOT NULL,
    "lastSeenAt" TIMESTAMP(3),
    "revokedAt" TIMESTAMP(3),
    "customerId" TEXT,

    CONSTRAINT "CustomerPortalSession_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE INDEX "PortalLoginCode_email_idx" ON "PortalLoginCode"("email");

-- CreateIndex
CREATE INDEX "PortalLoginCode_expiresAt_idx" ON "PortalLoginCode"("expiresAt");

-- CreateIndex
CREATE UNIQUE INDEX "CustomerPortalSession_sessionTokenHash_key" ON "CustomerPortalSession"("sessionTokenHash");

-- CreateIndex
CREATE INDEX "CustomerPortalSession_email_idx" ON "CustomerPortalSession"("email");

-- CreateIndex
CREATE INDEX "CustomerPortalSession_expiresAt_idx" ON "CustomerPortalSession"("expiresAt");

-- CreateIndex
CREATE INDEX "CustomerPortalSession_customerId_idx" ON "CustomerPortalSession"("customerId");

-- AddForeignKey
ALTER TABLE "CustomerPortalSession" ADD CONSTRAINT "CustomerPortalSession_customerId_fkey" FOREIGN KEY ("customerId") REFERENCES "Customer"("id") ON DELETE SET NULL ON UPDATE CASCADE;

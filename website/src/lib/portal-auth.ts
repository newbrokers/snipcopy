import { createHash, createHmac, randomBytes, randomInt, timingSafeEqual } from "crypto";
import { NextRequest, NextResponse } from "next/server";
import { prisma } from "./prisma";
import { hashValue } from "./http";

export const PORTAL_SESSION_COOKIE = "savedcode_portal_session";
const LOGIN_CODE_TTL_MS = 10 * 60 * 1000;
const PORTAL_SESSION_TTL_MS = 30 * 24 * 60 * 60 * 1000;

function portalSecret() {
  const value = process.env.PORTAL_SESSION_SECRET;
  if (!value || value.length < 32) {
    throw new Error("PORTAL_SESSION_SECRET must be set to a random value of at least 32 characters.");
  }
  return value;
}

export function normalizeEmail(email: string) {
  return email.trim().toLowerCase();
}

function codeHash(email: string, code: string) {
  return createHmac("sha256", portalSecret())
    .update(normalizeEmail(email))
    .update(":")
    .update(code.trim())
    .digest("hex");
}

function tokenHash(token: string) {
  return createHash("sha256").update(token).digest("hex");
}

function equalStrings(left: string, right: string) {
  const leftBuffer = Buffer.from(left, "utf8");
  const rightBuffer = Buffer.from(right, "utf8");
  return leftBuffer.length === rightBuffer.length && timingSafeEqual(leftBuffer, rightBuffer);
}

export async function createPortalLoginCode(emailInput: string, ip?: string) {
  const email = normalizeEmail(emailInput);
  const customer = await prisma.customer.findUnique({ where: { email } });
  const license = customer ? null : await prisma.license.findFirst({ where: { customerEmail: email }, select: { id: true } });

  if (!customer && !license) {
    return { email, created: false as const };
  }

  const code = randomInt(100000, 1000000).toString();
  await prisma.portalLoginCode.create({
    data: {
      email,
      codeHash: codeHash(email, code),
      ipHash: ip ? hashValue(ip) : undefined,
      expiresAt: new Date(Date.now() + LOGIN_CODE_TTL_MS)
    }
  });

  return { email, code, created: true as const };
}

export async function verifyPortalLoginCode(emailInput: string, codeInput: string) {
  const email = normalizeEmail(emailInput);
  const code = codeInput.trim();
  const expectedHash = codeHash(email, code);
  const loginCode = await prisma.portalLoginCode.findFirst({
    where: {
      email,
      usedAt: null,
      expiresAt: { gt: new Date() }
    },
    orderBy: { createdAt: "desc" }
  });

  if (!loginCode || !equalStrings(loginCode.codeHash, expectedHash)) return null;

  await prisma.portalLoginCode.update({
    where: { id: loginCode.id },
    data: { usedAt: new Date() }
  });

  const customer = await prisma.customer.findUnique({ where: { email } });
  const token = randomBytes(32).toString("base64url");
  const expiresAt = new Date(Date.now() + PORTAL_SESSION_TTL_MS);
  const session = await prisma.customerPortalSession.create({
    data: {
      email,
      sessionTokenHash: tokenHash(token),
      expiresAt,
      customerId: customer?.id
    }
  });

  return { email, token, expiresAt, sessionId: session.id };
}

export async function getPortalSession(request: NextRequest) {
  const token = request.cookies.get(PORTAL_SESSION_COOKIE)?.value;
  if (!token) return null;

  const session = await prisma.customerPortalSession.findUnique({
    where: { sessionTokenHash: tokenHash(token) },
    select: { id: true, email: true, expiresAt: true, revokedAt: true }
  });

  if (!session || session.revokedAt || session.expiresAt.getTime() <= Date.now()) return null;
  return session;
}

export async function revokePortalSession(request: NextRequest) {
  const token = request.cookies.get(PORTAL_SESSION_COOKIE)?.value;
  if (!token) return;

  await prisma.customerPortalSession.updateMany({
    where: { sessionTokenHash: tokenHash(token), revokedAt: null },
    data: { revokedAt: new Date() }
  });
}

export async function touchPortalSession(sessionId: string) {
  await prisma.customerPortalSession.update({
    where: { id: sessionId },
    data: { lastSeenAt: new Date() }
  });
}

export function setPortalSessionCookie(response: NextResponse, token: string, expiresAt: Date) {
  response.cookies.set(PORTAL_SESSION_COOKIE, token, {
    httpOnly: true,
    secure: process.env.VERCEL_ENV === "production",
    sameSite: "lax",
    path: "/",
    expires: expiresAt
  });
}

export function clearPortalSessionCookie(response: NextResponse) {
  response.cookies.set(PORTAL_SESSION_COOKIE, "", {
    httpOnly: true,
    secure: process.env.VERCEL_ENV === "production",
    sameSite: "lax",
    path: "/",
    maxAge: 0
  });
}

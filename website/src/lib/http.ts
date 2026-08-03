import { NextRequest, NextResponse } from "next/server";
import { createHash } from "crypto";

const buckets = new Map<string, { count: number; resetAt: number }>();

export function json(data: unknown, init?: ResponseInit) {
  return NextResponse.json(data, init);
}

export function getClientIp(request: NextRequest) {
  return (
    request.headers.get("x-forwarded-for")?.split(",")[0]?.trim() ||
    request.headers.get("x-real-ip") ||
    "local"
  );
}

export function hashValue(value: string) {
  return createHash("sha256").update(value).digest("hex");
}

export function rateLimit(key: string, limit = 12, windowMs = 60_000) {
  const now = Date.now();
  const current = buckets.get(key);

  if (!current || current.resetAt <= now) {
    buckets.set(key, { count: 1, resetAt: now + windowMs });
    return { ok: true, remaining: limit - 1 };
  }

  if (current.count >= limit) {
    return { ok: false, remaining: 0 };
  }

  current.count += 1;
  return { ok: true, remaining: limit - current.count };
}

export function requireAdmin(request: NextRequest) {
  const token = request.headers.get("authorization")?.replace(/^Bearer\s+/i, "");
  return Boolean(token && token === process.env.ADMIN_TOKEN);
}

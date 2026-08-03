import { NextRequest } from "next/server";
import { json } from "@/lib/http";
import { clearPortalSessionCookie, revokePortalSession } from "@/lib/portal-auth";

export async function POST(request: NextRequest) {
  await revokePortalSession(request);
  const response = json({ authenticated: false });
  clearPortalSessionCookie(response);
  return response;
}

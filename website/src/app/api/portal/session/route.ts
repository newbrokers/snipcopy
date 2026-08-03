import { NextRequest } from "next/server";
import { json } from "@/lib/http";
import { getPortalSession, touchPortalSession } from "@/lib/portal-auth";

export async function GET(request: NextRequest) {
  const session = await getPortalSession(request);
  if (!session) return json({ authenticated: false });

  await touchPortalSession(session.id);
  return json({ authenticated: true, email: session.email });
}

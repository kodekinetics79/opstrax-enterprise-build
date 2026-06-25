import type { Request, Response } from "express";
import { getSessionByToken } from "../modules/auth/auth.service";
import type { AuthenticatedUser } from "../modules/auth/auth.types";
import { fail } from "../lib/httpEnvelope";

export type RequestAuthContext = {
  token: string;
  user: AuthenticatedUser;
};

function readBearerToken(req: Request) {
  const header = req.get("authorization") || "";
  if (!header.toLowerCase().startsWith("bearer ")) return null;
  const token = header.slice(7).trim();
  return token || null;
}

export async function resolveSession(req: Request): Promise<RequestAuthContext | null> {
  const token = readBearerToken(req);
  if (!token) return null;
  const session = await getSessionByToken(token);
  if (!session) return null;
  return {
    token,
    user: session.user,
  };
}

export function resolveTenantId(req: Request, auth?: RequestAuthContext | null) {
  const fromAuth = auth?.user.companyId;
  if (fromAuth && Number.isFinite(fromAuth)) return fromAuth;

  const header = req.get("x-opstrax-tenant-id") || req.get("x-tenant-id") || "1";
  const parsed = Number.parseInt(header, 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : 1;
}

export async function requirePermission(
  req: Request,
  res: Response,
  permission: string
) {
  const auth = await resolveSession(req);
  if (!auth) {
    res.status(401).json(fail("Unauthorized", ["Missing or invalid bearer token"]));
    return null;
  }

  const permissions = auth.user.permissions;
  const allowed =
    permissions.includes("*") ||
    permissions.includes(permission) ||
    permissions.includes(permission.replace(":", "."));

  if (!allowed) {
    res.status(403).json(fail("Forbidden", [`Missing permission: ${permission}`]));
    return null;
  }

  return auth;
}


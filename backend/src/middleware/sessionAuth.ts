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

export function resolveTenantId(_req: Request, auth?: RequestAuthContext | null) {
  const tenantId = auth?.user.companyId;
  if (typeof tenantId !== "number" || !Number.isSafeInteger(tenantId) || tenantId <= 0) {
    throw new Error("Authenticated session is missing a valid tenant");
  }
  return tenantId;
}

export function tenantMatchesAuthenticatedUser(
  requestedTenantId: string | number | undefined,
  authenticatedTenantId: number
) {
  if (requestedTenantId === undefined) return true;
  return String(requestedTenantId).trim() === String(authenticatedTenantId);
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

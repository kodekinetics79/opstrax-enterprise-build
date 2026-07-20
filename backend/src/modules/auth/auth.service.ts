import { generateToken, hashPassword, verifyPassword } from "../../lib/password";
import { query, queryOne } from "../../lib/db";
import type { AuthenticatedUser, AuthSessionPayload } from "./auth.types";

type RawUserRow = {
  id: string | number;
  companyId: string | number;
  companyCode: string;
  companyName: string;
  email: string;
  fullName: string;
  roleId: string | number | null;
  roleName: string;
  passwordHash: string | null;
  userPermissionsJson: unknown;
  rolePermissionsJson: unknown;
};

function parsePermissions(value: unknown): string[] {
  if (Array.isArray(value)) return value.filter((item): item is string => typeof item === "string");
  if (typeof value === "string") {
    try {
      const parsed = JSON.parse(value);
      return Array.isArray(parsed) ? parsePermissions(parsed) : [];
    } catch {
      return value ? [value] : [];
    }
  }
  return [];
}

function buildPermissions(row: RawUserRow) {
  const permissions = new Set<string>();
  parsePermissions(row.userPermissionsJson).forEach((permission) => permissions.add(permission));
  parsePermissions(row.rolePermissionsJson).forEach((permission) => permissions.add(permission));
  if (String(row.roleName).toLowerCase().includes("super")) permissions.add("*");
  return Array.from(permissions);
}

async function resolveRawUser(email: string) {
  return queryOne<RawUserRow>(
    `
    SELECT
      u.id,
      u.company_id AS "companyId",
      c.company_code AS "companyCode",
      c.name AS "companyName",
      u.email,
      u.full_name AS "fullName",
      u.role_id AS "roleId",
      u.role_name AS "roleName",
      u.password_hash AS "passwordHash",
      u.permissions_json AS "userPermissionsJson",
      r.permissions_json AS "rolePermissionsJson"
    FROM users u
    JOIN companies c ON c.id = u.company_id
    LEFT JOIN roles r ON r.id = u.role_id
    WHERE lower(u.email) = lower($1)
    LIMIT 1
    `,
    [email]
  );
}

export async function authenticateWithPassword(email: string, password: string): Promise<AuthSessionPayload | null> {
  const row = await resolveRawUser(email);
  if (!row) return null;

  const passwordOk = verifyPassword(password, row.passwordHash);

  if (!passwordOk) return null;

  const token = generateToken(32);
  const csrfToken = generateToken(24);
  const permissions = buildPermissions(row);

  await query(
    `
    INSERT INTO user_sessions (user_id, company_id, session_token, refresh_token, csrf_token, expires_at)
    VALUES ($1, $2, $3, $4, $5, NOW() + INTERVAL '8 hours')
    `,
    [row.id, row.companyId, token, generateToken(32), csrfToken]
  );

  return {
    token,
    csrfToken,
    user: {
      id: Number(row.id),
      email: row.email,
      name: row.fullName,
      fullName: row.fullName,
      companyId: Number(row.companyId),
      companyCode: row.companyCode,
    },
    role: row.roleName,
    company: {
      id: Number(row.companyId),
      companyId: Number(row.companyId),
      code: row.companyCode,
      name: row.companyName,
    },
    permissions,
  };
}

export async function getSessionByToken(token: string) {
  const session = await queryOne<{
    id: string | number;
    userId: string | number;
    companyId: string | number;
    sessionToken: string;
    refreshToken: string | null;
    csrfToken: string | null;
    expiresAt: string;
    email: string;
    fullName: string;
    roleName: string;
    roleId: string | number | null;
    companyCode: string;
    companyName: string;
    userPermissionsJson: unknown;
    rolePermissionsJson: unknown;
  }>(
    `
    SELECT
      s.id,
      s.user_id AS "userId",
      s.company_id AS "companyId",
      s.session_token AS "sessionToken",
      s.refresh_token AS "refreshToken",
      s.csrf_token AS "csrfToken",
      s.expires_at AS "expiresAt",
      u.email,
      u.full_name AS "fullName",
      u.role_name AS "roleName",
      u.role_id AS "roleId",
      c.company_code AS "companyCode",
      c.name AS "companyName",
      u.permissions_json AS "userPermissionsJson",
      r.permissions_json AS "rolePermissionsJson"
    FROM user_sessions s
    JOIN users u ON u.id = s.user_id
    JOIN companies c ON c.id = s.company_id
    LEFT JOIN roles r ON r.id = u.role_id
    WHERE s.session_token = $1
      AND s.expires_at > NOW()
      AND u.status = 'Active'
    LIMIT 1
    `,
    [token]
  );

  if (!session) return null;

  const permissions = new Set<string>();
  parsePermissions(session.userPermissionsJson).forEach((permission) => permissions.add(permission));
  parsePermissions(session.rolePermissionsJson).forEach((permission) => permissions.add(permission));
  if (String(session.roleName).toLowerCase().includes("super")) permissions.add("*");

  return {
    session,
    user: {
      id: Number(session.userId),
      companyId: Number(session.companyId),
      companyCode: session.companyCode,
      companyName: session.companyName,
      email: session.email,
      fullName: session.fullName,
      roleId: session.roleId == null ? null : Number(session.roleId),
      roleName: session.roleName,
      permissions: Array.from(permissions),
    } satisfies AuthenticatedUser,
  };
}

export async function refreshSession(token: string) {
  const existing = await getSessionByToken(token);
  if (!existing) return null;

  const nextToken = generateToken(32);
  const nextRefreshToken = generateToken(32);
  const nextCsrfToken = generateToken(24);

  await query(
    `
    UPDATE user_sessions
    SET session_token=$1,
        refresh_token=$2,
        csrf_token=$3,
        expires_at=NOW() + INTERVAL '8 hours'
    WHERE session_token=$4
    `,
    [nextToken, nextRefreshToken, nextCsrfToken, token]
  );

  return {
    token: nextToken,
    csrfToken: nextCsrfToken,
    user: {
      id: existing.user.id,
      email: existing.user.email,
      name: existing.user.fullName,
      fullName: existing.user.fullName,
      companyId: existing.user.companyId,
      companyCode: existing.user.companyCode,
    },
    role: existing.user.roleName,
    company: {
      id: existing.user.companyId,
      companyId: existing.user.companyId,
      code: existing.user.companyCode,
      name: existing.user.companyName,
    },
    permissions: existing.user.permissions,
  };
}

export async function logoutSession(token: string) {
  await query("DELETE FROM user_sessions WHERE session_token = $1", [token]);
}

export async function changePassword(token: string, currentPassword: string, nextPassword: string) {
  const existing = await getSessionByToken(token);
  if (!existing) return null;

  const row = await queryOne<{ passwordHash: string | null }>(
    "SELECT password_hash AS \"passwordHash\" FROM users WHERE id=$1 LIMIT 1",
    [existing.user.id]
  );

  const passwordOk = verifyPassword(currentPassword, row?.passwordHash);

  if (!passwordOk) return false;

  await query("UPDATE users SET password_hash=$1 WHERE id=$2", [hashPassword(nextPassword), existing.user.id]);
  await query("DELETE FROM user_sessions WHERE user_id=$1", [existing.user.id]);
  return true;
}

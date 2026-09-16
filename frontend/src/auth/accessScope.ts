import type { AnyRecord, UserSession } from "@/types";

type SessionLike = Pick<UserSession, "role" | "permissions" | "portalContext"> | null | undefined;

export function scopeRowsForSession(kind: "vehicles" | "drivers" | "jobs" | "shipments" | "customers" | "assets", rows: AnyRecord[], session: SessionLike) {
  if (!session) return rows;
  const role = String(session.role ?? "").toLowerCase();
  if (isDriverPortalRole(role)) {
    const driverId = resolveAuthenticatedDriverId(session);
    if (!driverId) return [];
    return rows.filter((row) => matchesId(row, driverId, getDriverIdFields(kind)));
  }

  if (isCustomerPortalRole(role)) {
    const customerId = resolveAuthenticatedCustomerId(session);
    if (!customerId) return [];
    return rows.filter((row) => matchesId(row, customerId, getCustomerIdFields(kind)));
  }

  return rows;
}

export function resolveAuthenticatedDriverId(session: SessionLike) {
  return positiveId(session?.portalContext?.driverId);
}

export function resolveAuthenticatedCustomerId(session: SessionLike) {
  return positiveId(session?.portalContext?.customerId);
}

export function isDriverPortalRole(role: string) {
  const normalized = role.toLowerCase();
  return normalized.includes("driver");
}

export function isCustomerPortalRole(role: string) {
  const normalized = role.toLowerCase();
  return normalized.includes("customer") && !normalized.includes("service");
}

function getDriverIdFields(kind: string) {
  if (kind === "drivers") return ["id", "driverId"];
  return ["driverId", "assignedDriverId"];
}

function getCustomerIdFields(kind: string) {
  if (kind === "customers") return ["id", "customerId"];
  return ["customerId"];
}

function positiveId(value: unknown): string | null {
  const text = String(value ?? "").trim();
  return /^[1-9]\d*$/.test(text) ? text : null;
}

function matchesId(row: AnyRecord, expected: string, fields: string[]) {
  return fields.some((field) => positiveId(row[field]) === expected);
}

import type { AnyRecord, UserSession } from "@/types";

type SessionLike = Pick<UserSession, "role" | "user" | "company" | "permissions"> | null | undefined;

const DRIVER_IDENTITY_BY_EMAIL: Record<string, string> = {
  "driver@northshore-fleet.com": "Salman Qureshi",
  "driver@local-fleet.test": "Salman Qureshi",
};

const CUSTOMER_IDENTITY_BY_EMAIL: Record<string, string> = {
  "customer@client.com": "Gulf Express Logistics",
};

export function scopeRowsForSession(kind: "vehicles" | "drivers" | "jobs" | "shipments" | "customers" | "assets", rows: AnyRecord[], session: SessionLike) {
  if (!session) return rows;
  const role = String(session.role ?? "").toLowerCase();
  if (isDriverPortalRole(role)) {
    const driverIdentity = resolveDriverIdentity(session);
    if (!driverIdentity) return rows;
    return rows.filter((row) => matchesAny(row, driverIdentity, getDriverFields(kind)));
  }

  if (isCustomerPortalRole(role)) {
    const customerIdentity = resolveCustomerIdentity(session);
    if (!customerIdentity) return rows;
    return rows.filter((row) => matchesAny(row, customerIdentity, getCustomerFields(kind)));
  }

  return rows;
}

export function resolveDriverIdentity(session: SessionLike) {
  if (!session) return null;
  const email = String(session.user?.email ?? "").toLowerCase();
  const mapped = DRIVER_IDENTITY_BY_EMAIL[email];
  return (mapped ?? String(session.user?.name ?? "").trim()) || null;
}

export function resolveCustomerIdentity(session: SessionLike) {
  if (!session) return null;
  const email = String(session.user?.email ?? "").toLowerCase();
  const mapped = CUSTOMER_IDENTITY_BY_EMAIL[email];
  return (mapped ?? String(session.company?.name ?? "").trim()) || null;
}

export function isDriverPortalRole(role: string) {
  const normalized = role.toLowerCase();
  return normalized.includes("driver");
}

export function isCustomerPortalRole(role: string) {
  const normalized = role.toLowerCase();
  return normalized.includes("customer") && !normalized.includes("service");
}

function getDriverFields(kind: string) {
  if (kind === "vehicles") return ["assignedDriver", "assignedDriverName", "driverName", "driver", "fullName", "name"];
  if (kind === "drivers") return ["fullName", "name", "driverName", "assignedVehicle"];
  if (kind === "jobs" || kind === "shipments") return ["driverName", "assignedDriver", "assignedDriverId", "driver", "fullName"];
  if (kind === "assets") return ["assignedDriver", "assignedDriverName", "driverName"];
  return ["driverName", "assignedDriver", "fullName", "name"];
}

function getCustomerFields(kind: string) {
  if (kind === "customers") return ["name", "companyName", "customerName", "customer", "customerCode", "id"];
  if (kind === "jobs" || kind === "shipments") return ["customerName", "customer", "customerId", "companyName"];
  if (kind === "assets") return ["customerName", "customer", "companyName"];
  return ["customerName", "customer", "companyName"];
}

function matchesAny(row: AnyRecord, expected: string, fields: string[]) {
  const target = expected.toLowerCase();
  return fields.some((field) => String(row[field] ?? "").toLowerCase() === target || String(row[field] ?? "").toLowerCase().includes(target));
}

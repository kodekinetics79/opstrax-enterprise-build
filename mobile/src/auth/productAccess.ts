import type { WorkspaceRole } from "@/types";

export type ProductAppVariant = "driver" | "fleet" | "customer" | "unified";

type ProductAccessInput = {
  variant: ProductAppVariant;
  hasSession: boolean;
  normalizedRole: WorkspaceRole;
  permissions: string[];
};

export type ProductExperience = "driver" | "customer" | "operations" | "mismatch";

const FLEET_ROLES = new Set<WorkspaceRole>([
  "fieldWorker",
  "dispatcherSupervisor",
  "warehousePickup",
  "safetyMaintenance",
  "tenantAdmin",
]);

export function resolveProductAccess({
  variant,
  hasSession,
  normalizedRole,
  permissions,
}: ProductAccessInput): {
  allowed: boolean;
  experience: ProductExperience;
  isCustomer: boolean;
  isDriver: boolean;
  isFleetUser: boolean;
} {
  const directPermissions = new Set(permissions.map((permission) => permission.trim().toLowerCase()));
  const isDriver = hasSession
    && directPermissions.has("driver:self")
    && !directPermissions.has("*")
    && !directPermissions.has("dashboard:view")
    && !directPermissions.has("dashboard.view");
  const isCustomer = hasSession
    && normalizedRole === "customerClient"
    && directPermissions.has("customer_portal:view");
  const isFleetUser = hasSession
    && FLEET_ROLES.has(normalizedRole)
    && !isDriver
    && !isCustomer;

  const allowed = variant === "unified"
    || (variant === "driver" && isDriver)
    || (variant === "customer" && isCustomer)
    || (variant === "fleet" && isFleetUser);
  const experience: ProductExperience = !allowed
    ? "mismatch"
    : isCustomer
      ? "customer"
      : isDriver
        ? "driver"
        : "operations";

  return { allowed, experience, isCustomer, isDriver, isFleetUser };
}

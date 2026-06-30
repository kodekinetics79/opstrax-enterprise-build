import type { WorkspaceRole } from "@/types";

export type RoleModel = {
  role: WorkspaceRole;
  title: string;
  subtitle: string;
  routeFamilies: string[];
  permissions: string[];
  hiddenData: string[];
};

export const ROLE_MODELS: RoleModel[] = [
  {
    role: "driverOperator",
    title: "Driver / Operator",
    subtitle: "Field execution, proof capture, and weak-network safe submits.",
    routeFamilies: ["Dashboard", "Workflows", "Proof", "Telemetry"],
    permissions: ["operations.proof.read", "operations.proof.submit", "operations.execution_summary.read"],
    hiddenData: ["Internal cost", "admin controls", "cross-tenant records"],
  },
  {
    role: "fieldWorker",
    title: "Field Worker",
    subtitle: "Checklist, access, evidence, and exception handling.",
    routeFamilies: ["Dashboard", "Workflows", "Proof", "Settings"],
    permissions: ["operations.site_access.read", "operations.proof.create", "operations.proof_artifact.create"],
    hiddenData: ["Finance actions", "platform tools", "tenant admin settings"],
  },
  {
    role: "dispatcherSupervisor",
    title: "Dispatcher / Supervisor",
    subtitle: "Assignment control, access controls, and proof visibility.",
    routeFamilies: ["Dashboard", "Workflows", "Telemetry", "Settings"],
    permissions: ["dispatch.smart_assign.read", "dispatch.smart_assign.accept", "operations.execution_summary.read"],
    hiddenData: ["Customer-only views", "platform admin actions"],
  },
  {
    role: "warehousePickup",
    title: "Warehouse / Pickup",
    subtitle: "Handover, pickup authorization, and receipt visibility.",
    routeFamilies: ["Dashboard", "Workflows", "Proof", "Settings"],
    permissions: ["operations.pickup_authorization.read", "operations.warehouse_handover.read"],
    hiddenData: ["Driver telematics", "pricing", "platform controls"],
  },
  {
    role: "customerClient",
    title: "Customer / Client",
    subtitle: "Proof review and minimal execution visibility.",
    routeFamilies: ["Dashboard", "Proof", "Settings"],
    permissions: ["operations.proof.read", "customer_portal:view"],
    hiddenData: ["Driver private fields", "ops internal notes", "platform tools"],
  },
  {
    role: "safetyMaintenance",
    title: "Safety / Maintenance",
    subtitle: "Safety signals, maintenance readiness, and live state.",
    routeFamilies: ["Dashboard", "Telemetry", "Workflows", "Settings"],
    permissions: ["safety:view", "maintenance:view", "telemetry.live_state.read"],
    hiddenData: ["Customer portal data", "finance admin actions"],
  },
  {
    role: "tenantAdmin",
    title: "Tenant Admin",
    subtitle: "Operational administration inside a single tenant boundary.",
    routeFamilies: ["Dashboard", "Workflows", "Telemetry", "Settings"],
    permissions: ["users:view", "roles:update", "settings:update"],
    hiddenData: ["Platform-wide tenant controls", "cross-tenant reports"],
  },
  {
    role: "platformAdmin",
    title: "Platform Admin",
    subtitle: "Tenant and platform operations under strict audit.",
    routeFamilies: ["Dashboard", "Telemetry", "Settings"],
    permissions: ["ops:view", "platform:manage"],
    hiddenData: ["Tenant private operational details unless explicitly allowed"],
  },
  {
    role: "general",
    title: "General Workspace",
    subtitle: "Fallback view when the role is broader than the route model.",
    routeFamilies: ["Dashboard", "Workflows", "Proof", "Telemetry", "Settings"],
    permissions: [],
    hiddenData: ["Sensitive records outside granted permissions"],
  },
];

export function classifyRole(role: string | null | undefined): WorkspaceRole {
  const value = String(role ?? "").toLowerCase();
  if (!value) return "general";
  if (value.includes("platform")) return "platformAdmin";
  if (value.includes("super") || value.includes("tenant admin") || value.includes("company admin")) return "tenantAdmin";
  if (value.includes("dispatcher") || value.includes("supervisor") || value.includes("operations manager")) return "dispatcherSupervisor";
  if (value.includes("driver") || value.includes("operator")) return "driverOperator";
  if (value.includes("field") || value.includes("technician") || value.includes("cleaner") || value.includes("guard")) return "fieldWorker";
  if (value.includes("warehouse") || value.includes("pickup")) return "warehousePickup";
  if (value.includes("customer") || value.includes("client")) return "customerClient";
  if (value.includes("safety") || value.includes("maintenance")) return "safetyMaintenance";
  return "general";
}


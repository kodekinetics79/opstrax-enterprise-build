export const PERMISSIONS = {
  DASHBOARD_VIEW: "dashboard:view",

  VEHICLES_VIEW: "vehicles:view",
  VEHICLES_CREATE: "vehicles:create",
  VEHICLES_UPDATE: "vehicles:update",
  VEHICLES_DELETE: "vehicles:delete",
  VEHICLES_ASSIGN: "vehicles:assign",
  VEHICLES_EXPORT: "vehicles:export",

  DRIVERS_VIEW: "drivers:view",
  DRIVERS_CREATE: "drivers:create",
  DRIVERS_UPDATE: "drivers:update",
  DRIVERS_DELETE: "drivers:delete",
  DRIVERS_ASSIGN: "drivers:assign",
  DRIVERS_EXPORT: "drivers:export",

  SHIPMENTS_VIEW: "shipments:view",
  SHIPMENTS_CREATE: "shipments:create",
  SHIPMENTS_UPDATE: "shipments:update",
  SHIPMENTS_DELETE: "shipments:delete",
  SHIPMENTS_EXPORT: "shipments:export",

  DISPATCH_VIEW: "dispatch:view",
  DISPATCH_CREATE: "dispatch:create",
  DISPATCH_UPDATE: "dispatch:update",
  DISPATCH_ASSIGN: "dispatch:assign",
  DISPATCH_CANCEL: "dispatch:cancel",

  CUSTOMERS_VIEW: "customers:view",
  CUSTOMERS_CREATE: "customers:create",
  CUSTOMERS_UPDATE: "customers:update",
  CUSTOMERS_DELETE: "customers:delete",

  SAFETY_VIEW: "safety:view",
  SAFETY_CREATE: "safety:create",
  SAFETY_UPDATE: "safety:update",
  SAFETY_REVIEW: "safety:review",
  SAFETY_EVIDENCE_VIEW: "safety:evidence:view",
  SAFETY_EVIDENCE_EXPORT: "safety:evidence:export",

  MAINTENANCE_VIEW: "maintenance:view",
  MAINTENANCE_CREATE: "maintenance:create",
  MAINTENANCE_UPDATE: "maintenance:update",
  MAINTENANCE_CLOSE: "maintenance:close",

  COMPLIANCE_VIEW: "compliance:view",
  COMPLIANCE_UPDATE: "compliance:update",
  COMPLIANCE_EXPORT: "compliance:export",

  ALERTS_VIEW: "alerts:view",
  ALERTS_ACKNOWLEDGE: "alerts:acknowledge",
  ALERTS_CLOSE: "alerts:close",

  REPORTS_VIEW: "reports:view",
  REPORTS_EXPORT: "reports:export",

  USERS_VIEW: "users:view",
  USERS_CREATE: "users:create",
  USERS_UPDATE: "users:update",
  USERS_DELETE: "users:delete",

  ROLES_VIEW: "roles:view",
  ROLES_UPDATE: "roles:update",

  SETTINGS_VIEW: "settings:view",
  SETTINGS_UPDATE: "settings:update",

  AUDIT_VIEW: "audit:view",

  CUSTOMER_PORTAL_VIEW: "customer_portal:view",
} as const;

export type Permission = typeof PERMISSIONS[keyof typeof PERMISSIONS];

const P = PERMISSIONS;

const PERMISSION_GROUPS: Record<Permission, string[]> = {
  [P.DASHBOARD_VIEW]: ["dashboard.view"],

  [P.VEHICLES_VIEW]: ["fleet.view", "fleet:view"],
  [P.VEHICLES_CREATE]: ["fleet.manage", "fleet:manage"],
  [P.VEHICLES_UPDATE]: ["fleet.manage", "fleet:manage"],
  [P.VEHICLES_DELETE]: ["fleet.manage", "fleet:manage"],
  [P.VEHICLES_ASSIGN]: ["fleet.manage", "fleet:manage"],
  [P.VEHICLES_EXPORT]: ["fleet.view", "fleet:view", "fleet.manage", "fleet:manage"],

  [P.DRIVERS_VIEW]: ["drivers.view", "drivers:view", "fleet.view", "fleet:view"],
  [P.DRIVERS_CREATE]: ["drivers.manage", "drivers:manage"],
  [P.DRIVERS_UPDATE]: ["drivers.manage", "drivers:manage"],
  [P.DRIVERS_DELETE]: ["drivers.manage", "drivers:manage"],
  [P.DRIVERS_ASSIGN]: ["drivers.manage", "drivers:manage", "fleet.manage", "fleet:manage"],
  [P.DRIVERS_EXPORT]: ["drivers.view", "drivers:view", "drivers.manage", "drivers:manage"],

  [P.SHIPMENTS_VIEW]: ["shipments.view", "shipments:view", "orders.view", "orders:view"],
  [P.SHIPMENTS_CREATE]: ["shipments.manage", "shipments:manage", "orders.manage", "orders:manage", "dispatch.manage", "dispatch:manage"],
  [P.SHIPMENTS_UPDATE]: ["shipments.manage", "shipments:manage", "orders.manage", "orders:manage", "dispatch.manage", "dispatch:manage"],
  [P.SHIPMENTS_DELETE]: ["shipments.manage", "shipments:manage", "orders.manage", "orders:manage", "dispatch.manage", "dispatch:manage"],
  [P.SHIPMENTS_EXPORT]: ["shipments.view", "shipments:view", "shipments.manage", "shipments:manage", "orders.view", "orders:view"],

  [P.DISPATCH_VIEW]: ["dispatch.view", "dispatch:view"],
  [P.DISPATCH_CREATE]: ["dispatch.manage", "dispatch:manage", "orders.manage", "orders:manage"],
  [P.DISPATCH_UPDATE]: ["dispatch.manage", "dispatch:manage", "orders.manage", "orders:manage"],
  [P.DISPATCH_ASSIGN]: ["dispatch.manage", "dispatch:manage", "orders.manage", "orders:manage"],
  [P.DISPATCH_CANCEL]: ["dispatch.manage", "dispatch:manage", "orders.manage", "orders:manage"],

  [P.CUSTOMERS_VIEW]: ["customers.view", "customers:view", "crm.view", "crm:view"],
  [P.CUSTOMERS_CREATE]: ["customers.manage", "customers:manage", "crm.manage", "crm:manage"],
  [P.CUSTOMERS_UPDATE]: ["customers.manage", "customers:manage", "crm.manage", "crm:manage"],
  [P.CUSTOMERS_DELETE]: ["customers.manage", "customers:manage", "crm.manage", "crm:manage"],

  [P.SAFETY_VIEW]: ["safety.view", "safety:view"],
  [P.SAFETY_CREATE]: ["safety.manage", "safety:manage"],
  [P.SAFETY_UPDATE]: ["safety.manage", "safety:manage"],
  [P.SAFETY_REVIEW]: ["safety.manage", "safety:manage"],
  [P.SAFETY_EVIDENCE_VIEW]: ["safety.view", "safety:view", "dashcam.view", "dashcam:view"],
  [P.SAFETY_EVIDENCE_EXPORT]: ["safety.manage", "safety:manage", "dashcam.manage", "dashcam:manage"],

  [P.MAINTENANCE_VIEW]: ["maintenance.view", "maintenance:view"],
  [P.MAINTENANCE_CREATE]: ["maintenance.manage", "maintenance:manage"],
  [P.MAINTENANCE_UPDATE]: ["maintenance.manage", "maintenance:manage"],
  [P.MAINTENANCE_CLOSE]: ["maintenance.manage", "maintenance:manage"],

  [P.COMPLIANCE_VIEW]: ["compliance.view", "compliance:view"],
  [P.COMPLIANCE_UPDATE]: ["compliance.manage", "compliance:manage"],
  [P.COMPLIANCE_EXPORT]: ["compliance.manage", "compliance:manage"],

  [P.ALERTS_VIEW]: ["alerts.view", "alerts:view", "fleet.view", "fleet:view", "safety.view", "safety:view", "maintenance.view", "maintenance:view"],
  [P.ALERTS_ACKNOWLEDGE]: ["alerts.manage", "alerts:manage", "safety.manage", "safety:manage", "maintenance.manage", "maintenance:manage"],
  [P.ALERTS_CLOSE]: ["alerts.manage", "alerts:manage", "safety.manage", "safety:manage", "maintenance.manage", "maintenance:manage"],

  [P.REPORTS_VIEW]: ["reports.view", "reports:view"],
  [P.REPORTS_EXPORT]: ["reports.manage", "reports:manage", "reports.view", "reports:view"],

  [P.USERS_VIEW]: ["users.view", "users:view", "users.manage", "users:manage"],
  [P.USERS_CREATE]: ["users.manage", "users:manage"],
  [P.USERS_UPDATE]: ["users.manage", "users:manage"],
  [P.USERS_DELETE]: ["users.manage", "users:manage"],

  [P.ROLES_VIEW]: ["roles.view", "roles:view", "users.manage", "users:manage"],
  [P.ROLES_UPDATE]: ["roles.manage", "roles:manage", "users.manage", "users:manage"],

  [P.SETTINGS_VIEW]: ["settings.view", "settings:view", "settings.manage", "settings:manage"],
  [P.SETTINGS_UPDATE]: ["settings.manage", "settings:manage"],

  [P.AUDIT_VIEW]: ["audit.view", "audit:view", "reports.manage", "reports:manage"],

  [P.CUSTOMER_PORTAL_VIEW]: ["customer_portal.view", "customer-portal:view", "customer_portal:view"],
} satisfies Record<Permission, string[]>;

const permissionAliasLookup = new Map<string, string[]>();

function addPermissionGroup(canonical: Permission, aliases: string[]) {
  const variants = unique([canonical, ...aliases, ...aliases.flatMap(getPunctuationVariants)]);
  for (const token of variants) {
    const key = token.toLowerCase();
    const next = permissionAliasLookup.get(key) ?? [];
    permissionAliasLookup.set(key, unique([...next, ...variants]));
  }
}

for (const [canonical, aliases] of Object.entries(PERMISSION_GROUPS) as Array<[Permission, string[]]>) {
  addPermissionGroup(canonical, aliases);
}

const TENANT_ADMIN_PERMISSIONS = [
  P.DASHBOARD_VIEW,
  P.VEHICLES_VIEW, P.VEHICLES_CREATE, P.VEHICLES_UPDATE, P.VEHICLES_DELETE, P.VEHICLES_ASSIGN, P.VEHICLES_EXPORT,
  P.DRIVERS_VIEW, P.DRIVERS_CREATE, P.DRIVERS_UPDATE, P.DRIVERS_DELETE, P.DRIVERS_ASSIGN, P.DRIVERS_EXPORT,
  P.SHIPMENTS_VIEW, P.SHIPMENTS_CREATE, P.SHIPMENTS_UPDATE, P.SHIPMENTS_DELETE, P.SHIPMENTS_EXPORT,
  P.DISPATCH_VIEW, P.DISPATCH_CREATE, P.DISPATCH_UPDATE, P.DISPATCH_ASSIGN, P.DISPATCH_CANCEL,
  P.CUSTOMERS_VIEW, P.CUSTOMERS_CREATE, P.CUSTOMERS_UPDATE, P.CUSTOMERS_DELETE,
  P.SAFETY_VIEW, P.SAFETY_CREATE, P.SAFETY_UPDATE, P.SAFETY_REVIEW, P.SAFETY_EVIDENCE_VIEW, P.SAFETY_EVIDENCE_EXPORT,
  P.MAINTENANCE_VIEW, P.MAINTENANCE_CREATE, P.MAINTENANCE_UPDATE, P.MAINTENANCE_CLOSE,
  P.COMPLIANCE_VIEW, P.COMPLIANCE_UPDATE, P.COMPLIANCE_EXPORT,
  P.ALERTS_VIEW, P.ALERTS_ACKNOWLEDGE, P.ALERTS_CLOSE,
  P.REPORTS_VIEW, P.REPORTS_EXPORT,
  P.USERS_VIEW, P.USERS_CREATE, P.USERS_UPDATE, P.USERS_DELETE,
  P.ROLES_VIEW, P.ROLES_UPDATE,
  P.SETTINGS_VIEW, P.SETTINGS_UPDATE,
  P.AUDIT_VIEW,
];

const FLEET_MANAGER_PERMISSIONS = [
  P.DASHBOARD_VIEW,
  P.VEHICLES_VIEW, P.VEHICLES_CREATE, P.VEHICLES_UPDATE, P.VEHICLES_DELETE, P.VEHICLES_ASSIGN, P.VEHICLES_EXPORT,
  P.DRIVERS_VIEW, P.DRIVERS_CREATE, P.DRIVERS_UPDATE, P.DRIVERS_DELETE, P.DRIVERS_ASSIGN, P.DRIVERS_EXPORT,
  P.SHIPMENTS_VIEW, P.SHIPMENTS_CREATE, P.SHIPMENTS_UPDATE, P.SHIPMENTS_DELETE, P.SHIPMENTS_EXPORT,
  P.DISPATCH_VIEW, P.DISPATCH_CREATE, P.DISPATCH_UPDATE, P.DISPATCH_ASSIGN, P.DISPATCH_CANCEL,
  P.ALERTS_VIEW, P.ALERTS_ACKNOWLEDGE, P.ALERTS_CLOSE,
  P.MAINTENANCE_VIEW, P.MAINTENANCE_CREATE, P.MAINTENANCE_UPDATE, P.MAINTENANCE_CLOSE,
  P.COMPLIANCE_VIEW, P.COMPLIANCE_UPDATE, P.COMPLIANCE_EXPORT,
  P.REPORTS_VIEW, P.REPORTS_EXPORT,
];

const DISPATCHER_PERMISSIONS = [
  P.DASHBOARD_VIEW,
  P.VEHICLES_VIEW,
  P.DRIVERS_VIEW,
  P.SHIPMENTS_VIEW, P.SHIPMENTS_CREATE, P.SHIPMENTS_UPDATE, P.SHIPMENTS_EXPORT,
  P.DISPATCH_VIEW, P.DISPATCH_CREATE, P.DISPATCH_UPDATE, P.DISPATCH_ASSIGN, P.DISPATCH_CANCEL,
  P.ALERTS_VIEW, P.ALERTS_ACKNOWLEDGE,
  P.CUSTOMERS_VIEW,
  P.REPORTS_VIEW,
];

const SAFETY_MANAGER_PERMISSIONS = [
  P.DASHBOARD_VIEW,
  P.SAFETY_VIEW, P.SAFETY_CREATE, P.SAFETY_UPDATE, P.SAFETY_REVIEW, P.SAFETY_EVIDENCE_VIEW, P.SAFETY_EVIDENCE_EXPORT,
  P.ALERTS_VIEW, P.ALERTS_ACKNOWLEDGE, P.ALERTS_CLOSE,
  P.COMPLIANCE_VIEW, P.COMPLIANCE_UPDATE, P.COMPLIANCE_EXPORT,
  P.REPORTS_VIEW,
];

const MAINTENANCE_MANAGER_PERMISSIONS = [
  P.DASHBOARD_VIEW,
  P.VEHICLES_VIEW,
  P.MAINTENANCE_VIEW, P.MAINTENANCE_CREATE, P.MAINTENANCE_UPDATE, P.MAINTENANCE_CLOSE,
  P.ALERTS_VIEW, P.ALERTS_ACKNOWLEDGE, P.ALERTS_CLOSE,
  P.COMPLIANCE_VIEW,
  P.REPORTS_VIEW,
];

const DRIVER_PERMISSIONS = [
  P.SHIPMENTS_VIEW,
  P.VEHICLES_VIEW,
  P.DRIVERS_VIEW,
  P.SAFETY_VIEW,
  P.COMPLIANCE_VIEW,
  P.ALERTS_VIEW,
];

const CUSTOMER_PERMISSIONS = [
  P.SHIPMENTS_VIEW,
  P.CUSTOMER_PORTAL_VIEW,
  P.ALERTS_VIEW,
];

const READ_ONLY_AUDITOR_PERMISSIONS = [
  P.DASHBOARD_VIEW,
  P.VEHICLES_VIEW,
  P.DRIVERS_VIEW,
  P.SHIPMENTS_VIEW,
  P.DISPATCH_VIEW,
  P.CUSTOMERS_VIEW,
  P.SAFETY_VIEW,
  P.MAINTENANCE_VIEW,
  P.COMPLIANCE_VIEW,
  P.ALERTS_VIEW,
  P.REPORTS_VIEW,
  P.USERS_VIEW,
  P.ROLES_VIEW,
  P.SETTINGS_VIEW,
  P.AUDIT_VIEW,
];

export const ROLE_PERMISSIONS = {
  super_admin: ["*"],
  tenant_admin: TENANT_ADMIN_PERMISSIONS,
  fleet_manager: FLEET_MANAGER_PERMISSIONS,
  dispatcher: DISPATCHER_PERMISSIONS,
  safety_manager: SAFETY_MANAGER_PERMISSIONS,
  maintenance_manager: MAINTENANCE_MANAGER_PERMISSIONS,
  driver: DRIVER_PERMISSIONS,
  customer: CUSTOMER_PERMISSIONS,
  read_only_auditor: READ_ONLY_AUDITOR_PERMISSIONS,

  // Legacy aliases kept for compatibility with older seeded/authenticated sessions.
  platform_super_admin: ["*"],
  company_admin: TENANT_ADMIN_PERMISSIONS,
  operations_manager: FLEET_MANAGER_PERMISSIONS,
  safety_compliance_manager: SAFETY_MANAGER_PERMISSIONS,
  dispatcher_legacy: DISPATCHER_PERMISSIONS,
  driver_legacy: DRIVER_PERMISSIONS,
  customer_portal_user: CUSTOMER_PERMISSIONS,
  vendor_service_provider: [
    P.MAINTENANCE_VIEW,
    P.MAINTENANCE_CREATE,
    P.MAINTENANCE_UPDATE,
    P.CUSTOMER_PORTAL_VIEW,
    P.SHIPMENTS_VIEW,
  ],
  finance_billing_manager: [
    P.DASHBOARD_VIEW,
    P.REPORTS_VIEW,
    P.REPORTS_EXPORT,
    P.SETTINGS_VIEW,
  ],
  crm_sales_manager: [
    P.CUSTOMERS_VIEW,
    P.CUSTOMERS_CREATE,
    P.CUSTOMERS_UPDATE,
    P.REPORTS_VIEW,
  ],
} satisfies Record<string, string[]>;

export type RoleKey = keyof typeof ROLE_PERMISSIONS;

export function getPermissionsForRole(roleKey: RoleKey): string[] {
  return ROLE_PERMISSIONS[roleKey] ?? [];
}

export function getPermissionVariants(permission: string): string[] {
  const normalized = permission.trim().toLowerCase();
  const variants = permissionAliasLookup.get(normalized);
  if (variants && variants.length > 0) return variants;
  return getPunctuationVariants(normalized);
}

export function hasPermission(ownedPermissions: string[], requiredPermission: string): boolean {
  if (ownedPermissions.some((permission) => permission.trim() === "*")) return true;
  const owned = new Set(ownedPermissions.flatMap(getPermissionVariants).map((permission) => permission.toLowerCase()));
  return getPermissionVariants(requiredPermission).some((permission) => owned.has(permission.toLowerCase()));
}

export function isPermissionGranted(ownedPermissions: string[], requiredPermission: string) {
  return hasPermission(ownedPermissions, requiredPermission);
}

function getPunctuationVariants(permission: string) {
  const normalized = permission.trim().toLowerCase();
  return unique([
    normalized,
    normalized.replace(/\./g, ":"),
    normalized.replace(/:/g, "."),
    normalized.replace(/-/g, ":"),
    normalized.replace(/_/g, "-"),
    normalized.replace(/-/g, "_"),
  ]);
}

function unique(values: string[]) {
  return Array.from(new Set(values.filter(Boolean)));
}

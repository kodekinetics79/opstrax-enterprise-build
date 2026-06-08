import { ROLE_PERMISSIONS, type RoleKey } from "@/auth/rbacConfig";

export type DemoUser = {
  email: string;
  password: string;
  name: string;
  roleKey: RoleKey;
  roleLabel: string;
  company: {
    id: string;
    name: string;
    plan: string;
  };
};

export const demoUsers: DemoUser[] = [
  { email: "superadmin@opstrax.com", password: "demo123", name: "Platform Super Admin", roleKey: "super_admin", roleLabel: "Super Admin", company: { id: "opstrax-platform", name: "OpsTrax Platform", plan: "Internal" } },
  { email: "admin@northshore-fleet.com", password: "demo123", name: "Tenant Admin", roleKey: "tenant_admin", roleLabel: "Tenant Admin", company: { id: "northshore-fleet", name: "Northshore Fleet Logistics", plan: "Enterprise" } },
  { email: "operations@northshore-fleet.com", password: "demo123", name: "Fleet Manager", roleKey: "fleet_manager", roleLabel: "Fleet Manager", company: { id: "northshore-fleet", name: "Northshore Fleet Logistics", plan: "Enterprise" } },
  { email: "dispatcher@northshore-fleet.com", password: "demo123", name: "Dispatcher", roleKey: "dispatcher", roleLabel: "Dispatcher", company: { id: "northshore-fleet", name: "Northshore Fleet Logistics", plan: "Enterprise" } },
  { email: "fleet@northshore-fleet.com", password: "demo123", name: "Read-Only Auditor", roleKey: "read_only_auditor", roleLabel: "Read-Only Auditor", company: { id: "northshore-fleet", name: "Northshore Fleet Logistics", plan: "Enterprise" } },
  { email: "driver@northshore-fleet.com", password: "demo123", name: "Salman Qureshi", roleKey: "driver", roleLabel: "Driver", company: { id: "northshore-fleet", name: "Northshore Fleet Logistics", plan: "Enterprise" } },
  { email: "safety@northshore-fleet.com", password: "demo123", name: "Safety Manager", roleKey: "safety_manager", roleLabel: "Safety Manager", company: { id: "northshore-fleet", name: "Northshore Fleet Logistics", plan: "Enterprise" } },
  { email: "maintenance@northshore-fleet.com", password: "demo123", name: "Maintenance Manager", roleKey: "maintenance_manager", roleLabel: "Maintenance Manager", company: { id: "northshore-fleet", name: "Northshore Fleet Logistics", plan: "Enterprise" } },
  { email: "finance@northshore-fleet.com", password: "demo123", name: "Finance & Billing Manager", roleKey: "finance_billing_manager", roleLabel: "Finance & Billing Manager", company: { id: "northshore-fleet", name: "Northshore Fleet Logistics", plan: "Enterprise" } },
  { email: "crm@northshore-fleet.com", password: "demo123", name: "CRM & Sales Manager", roleKey: "crm_sales_manager", roleLabel: "CRM & Sales Manager", company: { id: "northshore-fleet", name: "Northshore Fleet Logistics", plan: "Enterprise" } },
  { email: "customer@client.com", password: "demo123", name: "Erin Matthews", roleKey: "customer", roleLabel: "Customer", company: { id: "CUS-US-006", name: "Gulf Express Logistics", plan: "Portal" } },
  { email: "vendor@service.com", password: "demo123", name: "Vendor Service Provider", roleKey: "vendor_service_provider", roleLabel: "Vendor Service Provider", company: { id: "vendor-tenant", name: "Vendor Services", plan: "Partner" } },
];

export const demoUsersByEmail = Object.fromEntries(
  demoUsers.map((user) => [user.email.toLowerCase(), user]),
) as Record<string, DemoUser>;

export function getPermissionsForRole(roleKey: RoleKey): string[] {
  return ROLE_PERMISSIONS[roleKey];
}

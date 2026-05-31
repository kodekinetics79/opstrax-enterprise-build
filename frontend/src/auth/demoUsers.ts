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
  { email: "superadmin@opstrax.com", password: "demo123", name: "Platform Super Admin", roleKey: "platform_super_admin", roleLabel: "Platform Super Admin", company: { id: "opstrax-platform", name: "OpsTrax Platform", plan: "Internal" } },
  { email: "admin@demo-fleet.com", password: "demo123", name: "Company Admin", roleKey: "company_admin", roleLabel: "Company Admin", company: { id: "demo-fleet", name: "Demo Fleet Logistics", plan: "Enterprise Demo" } },
  { email: "operations@demo-fleet.com", password: "demo123", name: "Operations Manager", roleKey: "operations_manager", roleLabel: "Operations Manager", company: { id: "demo-fleet", name: "Demo Fleet Logistics", plan: "Enterprise Demo" } },
  { email: "dispatcher@demo-fleet.com", password: "demo123", name: "Dispatcher", roleKey: "dispatcher", roleLabel: "Dispatcher", company: { id: "demo-fleet", name: "Demo Fleet Logistics", plan: "Enterprise Demo" } },
  { email: "fleet@demo-fleet.com", password: "demo123", name: "Fleet Manager", roleKey: "fleet_manager", roleLabel: "Fleet Manager", company: { id: "demo-fleet", name: "Demo Fleet Logistics", plan: "Enterprise Demo" } },
  { email: "driver@demo-fleet.com", password: "demo123", name: "Driver", roleKey: "driver", roleLabel: "Driver", company: { id: "demo-fleet", name: "Demo Fleet Logistics", plan: "Enterprise Demo" } },
  { email: "safety@demo-fleet.com", password: "demo123", name: "Safety & Compliance Manager", roleKey: "safety_compliance_manager", roleLabel: "Safety & Compliance Manager", company: { id: "demo-fleet", name: "Demo Fleet Logistics", plan: "Enterprise Demo" } },
  { email: "maintenance@demo-fleet.com", password: "demo123", name: "Maintenance Manager", roleKey: "maintenance_manager", roleLabel: "Maintenance Manager", company: { id: "demo-fleet", name: "Demo Fleet Logistics", plan: "Enterprise Demo" } },
  { email: "finance@demo-fleet.com", password: "demo123", name: "Finance & Billing Manager", roleKey: "finance_billing_manager", roleLabel: "Finance & Billing Manager", company: { id: "demo-fleet", name: "Demo Fleet Logistics", plan: "Enterprise Demo" } },
  { email: "crm@demo-fleet.com", password: "demo123", name: "CRM & Sales Manager", roleKey: "crm_sales_manager", roleLabel: "CRM & Sales Manager", company: { id: "demo-fleet", name: "Demo Fleet Logistics", plan: "Enterprise Demo" } },
  { email: "customer@client.com", password: "demo123", name: "Customer Portal User", roleKey: "customer_portal_user", roleLabel: "Customer Portal User", company: { id: "client-tenant", name: "Client Tenant", plan: "Portal Demo" } },
  { email: "vendor@service.com", password: "demo123", name: "Vendor Service Provider", roleKey: "vendor_service_provider", roleLabel: "Vendor Service Provider", company: { id: "vendor-tenant", name: "Vendor Services", plan: "Partner Demo" } },
];

export const demoUsersByEmail = Object.fromEntries(
  demoUsers.map((user) => [user.email.toLowerCase(), user]),
) as Record<string, DemoUser>;

export function getPermissionsForRole(roleKey: RoleKey): string[] {
  return ROLE_PERMISSIONS[roleKey];
}

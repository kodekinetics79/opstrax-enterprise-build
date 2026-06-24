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
  { email: "superadmin@opstrax.com",     password: "demo123", name: "Mason Lee",          roleKey: "super_admin",           roleLabel: "Super Admin",                company: { id: "1", name: "OpsTrax Demo Logistics", plan: "Internal" } },
  { email: "admin@opstrax.com",          password: "demo123", name: "Avery Stone",        roleKey: "tenant_admin",          roleLabel: "Company Admin",              company: { id: "1", name: "OpsTrax Demo Logistics", plan: "Enterprise" } },
  { email: "operations@demo-fleet.com",  password: "demo123", name: "Erin Parker",        roleKey: "fleet_manager",         roleLabel: "Operations Manager",         company: { id: "1", name: "OpsTrax Demo Logistics", plan: "Enterprise" } },
  { email: "dispatcher@demo-fleet.com",  password: "demo123", name: "Maya Patel",         roleKey: "dispatcher",            roleLabel: "Dispatcher",                 company: { id: "1", name: "OpsTrax Demo Logistics", plan: "Enterprise" } },
  { email: "fleet@demo-fleet.com",       password: "demo123", name: "Nolan Brooks",       roleKey: "fleet_manager",         roleLabel: "Fleet Manager",              company: { id: "1", name: "OpsTrax Demo Logistics", plan: "Enterprise" } },
  { email: "driver@demo-fleet.com",      password: "demo123", name: "Omar Ali",           roleKey: "driver",                roleLabel: "Driver",                     company: { id: "1", name: "OpsTrax Demo Logistics", plan: "Enterprise" } },
  { email: "safety@demo-fleet.com",      password: "demo123", name: "Sofia Ramirez",      roleKey: "safety_manager",        roleLabel: "Safety & Compliance Manager",company: { id: "1", name: "OpsTrax Demo Logistics", plan: "Enterprise" } },
  { email: "maintenance@demo-fleet.com", password: "demo123", name: "Jordan Reyes",       roleKey: "maintenance_manager",   roleLabel: "Maintenance Manager",        company: { id: "1", name: "OpsTrax Demo Logistics", plan: "Enterprise" } },
  { email: "finance@demo-fleet.com",     password: "demo123", name: "Priya Shah",         roleKey: "finance_billing_manager",roleLabel: "Finance & Billing Manager", company: { id: "1", name: "OpsTrax Demo Logistics", plan: "Enterprise" } },
  { email: "crm@demo-fleet.com",         password: "demo123", name: "Jordan Kim",         roleKey: "crm_sales_manager",     roleLabel: "CRM & Sales Manager",        company: { id: "1", name: "OpsTrax Demo Logistics", plan: "Enterprise" } },
  { email: "customer@client.com",        password: "demo123", name: "Priya Shah",         roleKey: "customer",              roleLabel: "Customer Portal User",       company: { id: "3", name: "Client Tenant", plan: "Portal" } },
  { email: "vendor@service.com",         password: "demo123", name: "Victor Chen",        roleKey: "vendor_service_provider",roleLabel: "Vendor Service Provider",   company: { id: "4", name: "Vendor Services", plan: "Partner" } },
];

export const demoUsersByEmail = Object.fromEntries(
  demoUsers.map((user) => [user.email.toLowerCase(), user]),
) as Record<string, DemoUser>;

export function getPermissionsForRole(roleKey: RoleKey): string[] {
  return ROLE_PERMISSIONS[roleKey];
}

import { apiClient, unwrap } from "./apiClient";
import type { UserSession } from "@/types";

const usernameToEmail: Record<string, string> = {
  admin:      "admin@opstrax.com",
  dispatcher: "dispatcher@opstrax.com",
  driver:     "driver@opstrax.com",
  mechanic:   "mechanic@opstrax.com",
  customer:   "customer@opstrax.com",
  demo:       "demo@opstrax.com",
};

const demoUsers: Record<string, { name: string; role: string; permissions: string[]; password: string }> = {
  "admin@opstrax.com": {
    name: "OpsTrax Admin",
    role: "Company Admin",
    permissions: ["*"],
    password: "Admin@12345",
  },
  "dispatcher@opstrax.com": {
    name: "Dana Dispatcher",
    role: "Dispatcher",
    permissions: ["dispatch:view", "dispatch:update", "jobs:view", "jobs:update", "map:view"],
    password: "Admin@12345",
  },
  "driver@opstrax.com": {
    name: "Dylan Driver",
    role: "Driver",
    permissions: ["driver:portal", "jobs:view", "dvir:update"],
    password: "Admin@12345",
  },
  "mechanic@opstrax.com": {
    name: "Maya Mechanic",
    role: "Mechanic",
    permissions: ["maintenance:view", "maintenance:update", "workorders:update", "dvir:review"],
    password: "Admin@12345",
  },
  "customer@opstrax.com": {
    name: "Casey Customer",
    role: "Customer Portal User",
    permissions: ["customer-eta:view", "shipments:view"],
    password: "Admin@12345",
  },
  "demo@opstrax.com": {
    name: "Demo User",
    role: "Company Admin",
    permissions: ["*"],
    password: "Demo@2025",
  },
};

function resolveEmail(usernameOrEmail: string): string {
  const lower = usernameOrEmail.toLowerCase().trim();
  return usernameToEmail[lower] ?? lower;
}

function createDemoSession(usernameOrEmail: string, password: string): UserSession {
  const email = resolveEmail(usernameOrEmail);
  const demoUser = demoUsers[email];

  if (!demoUser || password !== demoUser.password) {
    throw new Error("Invalid demo credentials");
  }

  return {
    token: `demo-token-${email.replace(/[^a-z0-9]/g, "-")}`,
    user: {
      id: email,
      email,
      name: demoUser.name,
    },
    role: demoUser.role,
    company: {
      id: "demo-company",
      name: "OpsTrax Demo Logistics",
      plan: "Enterprise Demo",
    },
    permissions: demoUser.permissions,
  };
}

export const authApi = {
  login: async (usernameOrEmail: string, password: string) => {
    const email = resolveEmail(usernameOrEmail);
    try {
      return await unwrap<UserSession>(apiClient.post("/api/auth/login", { email, password }));
    } catch (error) {
      const isDemoLogin = email in demoUsers;
      const demoFallbackEnabled = import.meta.env.VITE_ENABLE_DEMO_AUTH !== "false";

      if (isDemoLogin && demoFallbackEnabled) {
        return createDemoSession(usernameOrEmail, password);
      }

      throw error;
    }
  },
};

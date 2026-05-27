import { apiClient, unwrap } from "./apiClient";
import type { UserSession } from "@/types";

const DEMO_PASSWORD = "Admin@12345";

const demoUsers: Record<string, { name: string; role: string; permissions: string[] }> = {
  "admin@opstrax.com": {
    name: "OpsTrax Admin",
    role: "Company Admin",
    permissions: ["*"],
  },
  "dispatcher@opstrax.com": {
    name: "Dana Dispatcher",
    role: "Dispatcher",
    permissions: ["dispatch:view", "dispatch:update", "jobs:view", "jobs:update", "map:view"],
  },
  "driver@opstrax.com": {
    name: "Dylan Driver",
    role: "Driver",
    permissions: ["driver:portal", "jobs:view", "dvir:update"],
  },
  "mechanic@opstrax.com": {
    name: "Maya Mechanic",
    role: "Mechanic",
    permissions: ["maintenance:view", "maintenance:update", "workorders:update", "dvir:review"],
  },
  "customer@opstrax.com": {
    name: "Casey Customer",
    role: "Customer Portal User",
    permissions: ["customer-eta:view", "shipments:view"],
  },
};

function createDemoSession(email: string, password: string): UserSession {
  const normalizedEmail = email.toLowerCase();
  const demoUser = demoUsers[normalizedEmail];

  if (!demoUser || password !== DEMO_PASSWORD) {
    throw new Error("Invalid demo credentials");
  }

  return {
    token: `demo-token-${normalizedEmail.replace(/[^a-z0-9]/g, "-")}`,
    user: {
      id: normalizedEmail,
      email: normalizedEmail,
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
  login: async (email: string, password: string) => {
    try {
      return await unwrap<UserSession>(apiClient.post("/api/auth/login", { email, password }));
    } catch (error) {
      const isDemoLogin = email.toLowerCase() in demoUsers;
      const demoFallbackEnabled = import.meta.env.VITE_ENABLE_DEMO_AUTH !== "false";

      if (isDemoLogin && demoFallbackEnabled) {
        return createDemoSession(email, password);
      }

      throw error;
    }
  },
};

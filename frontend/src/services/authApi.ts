import { apiClient, unwrap } from "./apiClient";
import { demoUsersByEmail, getPermissionsForRole } from "@/auth/demoUsers";
import { setGlobalCsrfToken } from "@/hooks/useCsrf";
import type { UserSession } from "@/types";

function resolveEmail(usernameOrEmail: string): string {
  return usernameOrEmail.toLowerCase().trim();
}

function createDemoSession(usernameOrEmail: string, password: string): UserSession {
  const email = resolveEmail(usernameOrEmail);
  const demoUser = demoUsersByEmail[email];

  if (!demoUser || password !== demoUser.password) {
    throw new Error("Invalid credentials");
  }

  const csrfToken = `demo-csrf-${Math.random().toString(36).slice(2)}`;
  setGlobalCsrfToken(csrfToken);

  return {
    token: `demo-token-${email.replace(/[^a-z0-9]/g, "-")}`,
    csrfToken,
    user: {
      id: email,
      email,
      name: demoUser.name,
    },
    role: demoUser.roleLabel,
    company: demoUser.company,
    permissions: getPermissionsForRole(demoUser.roleKey),
  };
}

export const authApi = {
  login: async (usernameOrEmail: string, password: string) => {
    const email = resolveEmail(usernameOrEmail);
    try {
      const response = await unwrap<UserSession>(
        apiClient.post("/api/auth/login", { email, password })
      );
      if (response.csrfToken) {
        setGlobalCsrfToken(response.csrfToken);
      }
      return response;
    } catch (error) {
      const isDemoLogin = email in demoUsersByEmail;
      const demoFallbackEnabled = import.meta.env.VITE_ENABLE_DEMO_AUTH !== "false";

      if (isDemoLogin && demoFallbackEnabled) {
        return createDemoSession(usernameOrEmail, password);
      }

      throw error;
    }
  },
};

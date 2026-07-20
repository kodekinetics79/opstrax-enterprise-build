import { apiClient, unwrap } from "./apiClient";
import { setGlobalCsrfToken } from "@/hooks/useCsrf";
import type { UserSession } from "@/types";

function resolveEmail(usernameOrEmail: string): string {
  return usernameOrEmail.toLowerCase().trim();
}

export type SsoConnection = {
  /** Connection id; the client initiates the flow via /api/auth/sso/start/{id}. */
  id: number;
  displayName: string;
  protocol: "saml" | "oidc";
};

export type SsoDiscovery = {
  ssoConfigured: boolean;
  usePassword: boolean;
  connection: SsoConnection | null;
};

export const authApi = {
  bootstrap: async () => {
    try { await apiClient.get("/api/health"); } catch { /* warm-up only — never block login */ }
  },
  login: async (usernameOrEmail: string, password: string) => {
    const email = resolveEmail(usernameOrEmail);
    const response = await unwrap<UserSession>(
      apiClient.post("/api/auth/login", { email, password })
    );
    if (response.csrfToken) {
      setGlobalCsrfToken(response.csrfToken);
    }
    return response;
  },
  /**
   * Identifier-first routing hint. Given an email, asks the backend whether the
   * tenant that owns the email's domain has an enabled SSO connection. Returns
   * `usePassword: true` for every domain with no SSO (the honest default while the
   * admin-provisioned `sso_connections` table is empty), so the UI simply reveals
   * the password field. Never reveals whether a *user* exists (enumeration-safe).
   */
  ssoDiscover: async (email: string) =>
    unwrap<SsoDiscovery>(apiClient.post("/api/auth/sso/discover", { email: resolveEmail(email) })),
  me: async () => unwrap<UserSession>(apiClient.get("/api/auth/me")),
  refresh: async () => unwrap<UserSession>(apiClient.post("/api/auth/refresh")),
  logout: async () => unwrap<{ loggedOut: boolean }>(apiClient.post("/api/auth/logout")),
  changePassword: async (currentPassword: string, newPassword: string) =>
    unwrap<{ changed: boolean }>(
      apiClient.post("/api/auth/change-password", { currentPassword, newPassword })
    ),
  forgotPassword: async (email: string) =>
    unwrap<{ accepted: boolean }>(apiClient.post("/api/auth/forgot-password", { email: resolveEmail(email) })),
  resetPassword: async (email: string, token: string, newPassword: string) =>
    unwrap<{ changed: boolean }>(apiClient.post("/api/auth/reset-password", { email: resolveEmail(email), token, newPassword })),
};

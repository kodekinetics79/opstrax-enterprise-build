import { apiClient, unwrap } from "./apiClient";
import { setGlobalCsrfToken } from "@/auth/csrfTokenStore";
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

/** Returned by /api/auth/login when the tenant's security policy requires a second
 *  factor: no session is issued yet, only a short-lived signed challenge that must
 *  be proven via authApi.mfaLoginVerify. */
export type MfaChallenge = {
  mfaRequired: true;
  challengeToken: string;
  email: string;
};

export type LoginResult = UserSession | MfaChallenge;

export function isMfaChallenge(result: LoginResult): result is MfaChallenge {
  return (result as MfaChallenge).mfaRequired === true;
}

export const authApi = {
  bootstrap: async () => {
    // Use the real liveness route. The former /api/health path is not mapped and
    // generated a guaranteed 404 on every password login before the real request.
    try { await apiClient.get("/health/live"); } catch { /* warm-up only — never block login */ }
  },
  login: async (usernameOrEmail: string, password: string, companyCode: string): Promise<LoginResult> => {
    const email = resolveEmail(usernameOrEmail);
    const response = await unwrap<LoginResult>(
      apiClient.post("/api/auth/login", { email, password, companyCode: companyCode.trim() })
    );
    if (!isMfaChallenge(response) && response.csrfToken) {
      setGlobalCsrfToken(response.csrfToken);
    }
    return response;
  },
  /** Completes a two-step login: proves the TOTP code against the signed challenge
   *  from `login`'s mfaRequired response and returns the real session. */
  mfaLoginVerify: async (challengeToken: string, code: string) => {
    const response = await unwrap<UserSession>(
      apiClient.post("/api/auth/mfa/login-verify", { challengeToken, code })
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
  ssoDiscover: async (email: string, companyCode: string) =>
    unwrap<SsoDiscovery>(apiClient.post("/api/auth/sso/discover", {
      email: resolveEmail(email),
      companyCode: companyCode.trim(),
    })),
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

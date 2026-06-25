import { apiClient, unwrap } from "./apiClient";
import { setGlobalCsrfToken } from "@/hooks/useCsrf";
import type { UserSession } from "@/types";

function resolveEmail(usernameOrEmail: string): string {
  return usernameOrEmail.toLowerCase().trim();
}

export const authApi = {
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
  me: async () => unwrap<UserSession>(apiClient.get("/api/auth/me")),
  refresh: async () => unwrap<UserSession>(apiClient.post("/api/auth/refresh")),
  logout: async () => unwrap<{ loggedOut: boolean }>(apiClient.post("/api/auth/logout")),
  changePassword: async (currentPassword: string, newPassword: string) =>
    unwrap<{ changed: boolean }>(
      apiClient.post("/api/auth/change-password", { currentPassword, newPassword })
    ),
};

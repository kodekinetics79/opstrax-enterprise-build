import { apiClient, unwrap } from "./apiClient";
import type { UserSession } from "@/types";

export const authApi = {
  login: (email: string, password: string) =>
    unwrap<UserSession>(apiClient.post("/api/auth/login", { email, password })),
};

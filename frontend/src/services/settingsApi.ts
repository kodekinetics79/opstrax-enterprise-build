import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export const settingsApi = {
  apiKeysList: () => unwrap<AnyRecord[]>(apiClient.get("/api/settings/api-keys")),
  apiKeyCreate: (label?: string) => unwrap<AnyRecord>(apiClient.post("/api/settings/api-keys", { label })),
  apiKeyRevoke: (id: number) => unwrap<AnyRecord>(apiClient.post(`/api/settings/api-keys/${id}/revoke`, {})),

  webhookGet: () => unwrap<AnyRecord>(apiClient.get("/api/settings/webhook")),
  webhookPut: (body: { endpointUrl: string | null; events: string[]; enabled?: boolean }) =>
    unwrap<AnyRecord>(apiClient.put("/api/settings/webhook", body)),
  webhookRotateSecret: () => unwrap<AnyRecord>(apiClient.post("/api/settings/webhook/rotate-secret", {})),

  companyProfileGet: () => unwrap<AnyRecord>(apiClient.get("/api/settings/company-profile")),
  companyProfilePut: (body: Record<string, unknown>) => unwrap<AnyRecord>(apiClient.put("/api/settings/company-profile", body)),

  notificationPrefsGet: () => unwrap<AnyRecord>(apiClient.get("/api/settings/notification-prefs")),
  notificationPrefsPut: (body: Record<string, unknown>) => unwrap<AnyRecord>(apiClient.put("/api/settings/notification-prefs", body)),

  securitySettingsGet: () => unwrap<AnyRecord>(apiClient.get("/api/security/settings")),
  securitySettingsPut: (body: Record<string, unknown>) => unwrap<AnyRecord>(apiClient.put("/api/security/settings", body)),
};

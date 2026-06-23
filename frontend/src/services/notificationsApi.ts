import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export const notificationsApi = {
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/notifications")),

  unreadCount: () => unwrap<{ count: number }>(apiClient.get("/api/notifications/unread-count")),

  markRead: (id: number | string) =>
    unwrap<AnyRecord>(apiClient.post(`/api/notifications/${id}/read`, {})),

  acknowledge: (id: number | string, note?: string) =>
    unwrap<AnyRecord>(apiClient.post(`/api/notifications/${id}/acknowledge`, { note })),

  acknowledgeAll: () =>
    unwrap<AnyRecord>(apiClient.post("/api/notifications/acknowledge-all", {})),
};

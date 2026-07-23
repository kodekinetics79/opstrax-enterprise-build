import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export const messagesApi = {
  listConversations: () =>
    unwrap<AnyRecord[]>(apiClient.get("/api/messages/conversations")),

  getConversation: (id: number | string) =>
    unwrap<AnyRecord>(apiClient.get(`/api/messages/conversations/${id}`)),

  createConversation: (payload: {
    subject?: string;
    driverId?: number;
    dispatchAssignmentId?: number;
    tripId?: number;
  }) => unwrap<AnyRecord>(apiClient.post("/api/messages/conversations", payload)),

  sendMessage: (conversationId: number | string, body: string, attachmentRef?: string) =>
    unwrap<AnyRecord>(
      apiClient.post(`/api/messages/conversations/${conversationId}/messages`, {
        body,
        attachmentRef,
      })
    ),

  markRead: (conversationId: number | string) =>
    unwrap<AnyRecord>(apiClient.post(`/api/messages/conversations/${conversationId}/read`, {})),

  // Count of DISTINCT threads with unread inbound messages — drives the driver "Messages" nav badge.
  unreadCount: () =>
    unwrap<{ count: number }>(apiClient.get("/api/messages/unread-count")),
};

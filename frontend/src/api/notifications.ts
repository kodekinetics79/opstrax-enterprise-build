import client from './client';

export interface NotificationItem {
  id: string;
  title: string;
  message: string;
  entityName: string;
  entityId: string | null;
  status: string;
  channel: string;
  createdAtUtc: string;
  readAtUtc: string | null;
}

export const notificationsApi = {
  list: () => client.get<NotificationItem[]>('/api/notifications').then((r) => r.data),
  markRead: (id: string) => client.post(`/api/notifications/${id}/read`),
};

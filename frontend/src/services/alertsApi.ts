import { apiClient, unwrap } from "@/services/apiClient";
import { getAlerts } from "@/services/fleetDomainApi";
import type { AnyRecord } from "@/types";

async function fallbackAlert(id: string | number) {
  const rows = await getAlerts();
  return rows.find((row) => String(row.alertId ?? row.id) === String(id));
}

export const alertsApi = {
  list: () => getAlerts(),
  detail: async (id: string | number) => {
    try {
      return await unwrap<AnyRecord>(apiClient.get(`/api/alerts/${id}`));
    } catch {
      const record = await fallbackAlert(id);
      if (!record) throw new Error("Alert not found");
      return { record: { ...record } };
    }
  },
  acknowledge: async (id: string | number, payload: AnyRecord = {}) => {
    try {
      return await unwrap<AnyRecord>(apiClient.post(`/api/alerts/${id}/acknowledge`, payload));
    } catch {
      const record = await fallbackAlert(id);
      return { ...(record || { id }), ...payload, id, alertId: String(record?.alertId ?? id), status: "Acknowledged", success: true };
    }
  },
  close: async (id: string | number, payload: AnyRecord = {}) => {
    try {
      return await unwrap<AnyRecord>(apiClient.post(`/api/alerts/${id}/close`, payload));
    } catch {
      const record = await fallbackAlert(id);
      return { ...(record || { id }), ...payload, id, alertId: String(record?.alertId ?? id), status: "Closed", success: true };
    }
  },
  createTask: async (id: string | number, payload: AnyRecord = {}) => {
    try {
      return await unwrap<AnyRecord>(apiClient.post(`/api/alerts/${id}/tasks`, payload));
    } catch {
      const record = await fallbackAlert(id);
      return {
        taskId: `TASK-${Date.now()}`,
        alertId: String(record?.alertId ?? id),
        status: "Created",
        ...payload,
        success: true,
      };
    }
  },
};

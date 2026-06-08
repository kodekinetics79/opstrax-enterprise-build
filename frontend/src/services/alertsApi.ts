import { apiClient, unwrap } from "@/services/apiClient";
import { getAlerts, withFallback } from "@/services/fleetDomainApi";
import type { AnyRecord } from "@/types";

function fallbackAlert(id: string | number) {
  return getAlerts().then((rows) => rows.find((row) => String(row.alertId ?? row.id) === String(id)));
}

export const alertsApi = {
  list: () => getAlerts(),
  detail: (id: string | number) =>
    withFallback(unwrap<AnyRecord>(apiClient.get(`/api/alerts/${id}`)), async () => {
      const record = await fallbackAlert(id);
      if (!record) throw new Error("Alert not found");
      return { record: { ...record } };
    }),
  acknowledge: (id: string | number, payload: AnyRecord = {}) =>
    withFallback(unwrap<AnyRecord>(apiClient.post(`/api/alerts/${id}/acknowledge`, payload)), async () => {
      const record = await fallbackAlert(id);
      return { ...(record || { id }), ...payload, id, alertId: String(record?.alertId ?? id), status: "Acknowledged", success: true };
    }),
  close: (id: string | number, payload: AnyRecord = {}) =>
    withFallback(unwrap<AnyRecord>(apiClient.post(`/api/alerts/${id}/close`, payload)), async () => {
      const record = await fallbackAlert(id);
      return { ...(record || { id }), ...payload, id, alertId: String(record?.alertId ?? id), status: "Closed", success: true };
    }),
  createTask: (id: string | number, payload: AnyRecord = {}) =>
    withFallback(unwrap<AnyRecord>(apiClient.post(`/api/alerts/${id}/tasks`, payload)), async () => {
      const record = await fallbackAlert(id);
      return {
        taskId: `TASK-${Date.now()}`,
        alertId: String(record?.alertId ?? id),
        status: "Created",
        ...payload,
        success: true,
      };
    }),
};

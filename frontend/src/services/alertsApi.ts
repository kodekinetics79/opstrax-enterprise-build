import { apiClient, unwrap } from "@/services/apiClient";
import { getAlerts } from "@/services/fleetDomainApi";
import type { AnyRecord } from "@/types";

export const alertsApi = {
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/alerts/summary")),
  list: () => getAlerts(),
  detail: async (id: string | number) => {
    return unwrap<AnyRecord>(apiClient.get(`/api/alerts/${id}`));
  },
  acknowledge: async (id: string | number, payload: AnyRecord = {}) => {
    return unwrap<AnyRecord>(apiClient.post(`/api/alerts/${id}/acknowledge`, payload));
  },
  close: async (id: string | number, payload: AnyRecord = {}) => {
    return unwrap<AnyRecord>(apiClient.post(`/api/alerts/${id}/close`, payload));
  },
  createTask: async (id: string | number, payload: AnyRecord = {}) => {
    return unwrap<AnyRecord>(apiClient.post(`/api/alerts/${id}/tasks`, payload));
  },
};

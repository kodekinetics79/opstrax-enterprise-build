import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export const maintenanceApi = {
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/maintenance")),
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/maintenance/summary")),
  due: () => unwrap<AnyRecord[]>(apiClient.get("/api/maintenance/due")),
  overdue: () => unwrap<AnyRecord[]>(apiClient.get("/api/maintenance/overdue")),
  recommendations: () => unwrap<AnyRecord[]>(apiClient.get("/api/maintenance/recommendations")),
  detail: (id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/maintenance/${id}`)),
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/maintenance", payload)),
  update: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/maintenance/${id}`, payload)),
  remove: (id: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/maintenance/${id}`)),
  schedule: (id: string | number, payload: AnyRecord = {}) => unwrap<AnyRecord>(apiClient.post(`/api/maintenance/${id}/schedule`, payload)),
  defer: (id: string | number, payload: AnyRecord = {}) => unwrap<AnyRecord>(apiClient.post(`/api/maintenance/${id}/defer`, payload)),
  createWorkOrder: (id: string | number, payload: AnyRecord = {}) => unwrap<AnyRecord>(apiClient.post(`/api/maintenance/${id}/create-workorder`, payload)),
};

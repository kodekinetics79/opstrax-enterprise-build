import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export const workOrdersApi = {
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/workorders")),
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/workorders/summary")),
  detail: (id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/workorders/${id}`)),
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/workorders", payload)),
  update: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/workorders/${id}`, payload)),
  remove: (id: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/workorders/${id}`)),
  timeline: (id: string | number) => unwrap<AnyRecord[]>(apiClient.get(`/api/workorders/${id}/timeline`)),
  recommendations: (id: string | number) => unwrap<AnyRecord[]>(apiClient.get(`/api/workorders/${id}/recommendations`)),
  assign: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post(`/api/workorders/${id}/assign`, payload)),
  status: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post(`/api/workorders/${id}/status`, payload)),
  addLabor: (id: string | number, payload: AnyRecord = {}) => unwrap<AnyRecord>(apiClient.post(`/api/workorders/${id}/add-labor`, payload)),
  addPart: (id: string | number, payload: AnyRecord = {}) => unwrap<AnyRecord>(apiClient.post(`/api/workorders/${id}/add-part`, payload)),
  complete: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/workorders/${id}/complete`, {})),
  approveCost: (id: string | number, payload: AnyRecord = {}) => unwrap<AnyRecord>(apiClient.post(`/api/workorders/${id}/approve-cost`, payload)),
};

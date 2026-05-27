import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export const dvirApi = {
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/dvir/reports")),
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/dvir/summary")),
  detail: (id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/dvir/reports/${id}`)),
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/dvir/reports", payload)),
  update: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/dvir/reports/${id}`, payload)),
  remove: (id: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/dvir/reports/${id}`)),
  templates: () => unwrap<AnyRecord>(apiClient.get("/api/dvir/templates")),
  createTemplate: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/dvir/templates", payload)),
  updateTemplate: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/dvir/templates/${id}`, payload)),
  mechanicReview: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/dvir/reports/${id}/mechanic-review`, {})),
  certifyRepair: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/dvir/reports/${id}/certify-repair`, {})),
  driverSign: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/dvir/reports/${id}/driver-sign`, {})),
  timeline: (id: string | number) => unwrap<AnyRecord[]>(apiClient.get(`/api/dvir/reports/${id}/timeline`)),
  recommendations: () => unwrap<AnyRecord[]>(apiClient.get("/api/dvir/recommendations")),
};

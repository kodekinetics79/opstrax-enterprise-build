import { apiClient, unwrap } from "./apiClient";
import type { AnyRecord } from "@/types";

export const jobsApi = {
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/jobs")),
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/jobs/summary")),
  detail: (id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/jobs/${id}`)),
  timeline: (id: string | number) => unwrap<AnyRecord[]>(apiClient.get(`/api/jobs/${id}/timeline`)),
  recommendations: (id: string | number) => unwrap<AnyRecord[]>(apiClient.get(`/api/jobs/${id}/recommendations`)),
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/jobs", payload)),
  update: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/jobs/${id}`, payload)),
  remove: (id: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/jobs/${id}`)),
  importPreview: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/jobs/import-preview", payload)),
  assign: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post(`/api/jobs/${id}/assign`, payload)),
  changeStatus: (id: string | number, status: string, notes?: string) => unwrap<AnyRecord>(apiClient.post(`/api/jobs/${id}/status`, { status, notes })),
  sendEta: (id: string | number, payload: AnyRecord = {}) => unwrap<AnyRecord>(apiClient.post(`/api/jobs/${id}/send-eta`, payload)),
  proofPlaceholder: (id: string | number, payload: AnyRecord = {}) => unwrap<AnyRecord>(apiClient.post(`/api/jobs/${id}/proof-placeholder`, payload)),
};

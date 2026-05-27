import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export const documentsApi = {
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/documents")),
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/documents/summary")),
  detail: (id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/documents/${id}`)),
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/documents", payload)),
  update: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/documents/${id}`, payload)),
  remove: (id: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/documents/${id}`)),
  expiring: () => unwrap<AnyRecord[]>(apiClient.get("/api/documents/expiring")),
  uploadPlaceholder: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/documents/upload-placeholder", payload)),
  renewPlaceholder: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/documents/${id}/renew-placeholder`, {})),
  timeline: (id: string | number) => unwrap<AnyRecord[]>(apiClient.get(`/api/documents/${id}/timeline`)),
  recommendations: () => unwrap<AnyRecord[]>(apiClient.get("/api/documents/recommendations")),
};

import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export const carriersApi = {
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/carriers/summary")),
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/carriers")),
  detail: (id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/carriers/${id}`)),
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/carriers", payload)),
  update: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/carriers/${id}`, payload)),
  remove: (id: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/carriers/${id}`)),
  performance: (id: string | number) => unwrap<AnyRecord[]>(apiClient.get(`/api/carriers/${id}/performance`)),
  documents: (id: string | number) => unwrap<AnyRecord[]>(apiClient.get(`/api/carriers/${id}/documents`)),
  setStatus: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post(`/api/carriers/${id}/status`, payload)),
  recommendations: () => unwrap<AnyRecord[]>(apiClient.get("/api/carriers/recommendations")),
};

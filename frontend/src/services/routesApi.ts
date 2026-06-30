import { apiClient, unwrap } from "./apiClient";
import type { AnyRecord } from "@/types";

export const routesApi = {
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/routes")),
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/routes/summary")),
  detail: (id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/routes/${id}`)),
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/routes", payload)),
  update: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/routes/${id}`, payload)),
  remove: (id: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/routes/${id}`)),
  stops: (id: string | number) => unwrap<AnyRecord[]>(apiClient.get(`/api/routes/${id}/stops`)),
  createStop: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post(`/api/routes/${id}/stops`, payload)),
  updateStop: (id: string | number, stopId: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/routes/${id}/stops/${stopId}`, payload)),
  deleteStop: (id: string | number, stopId: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/routes/${id}/stops/${stopId}`)),
  optimizePreview: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/routes/${id}/optimize-preview`, {})),
  assign: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post(`/api/routes/${id}/assign`, payload)),
  timeline: (id: string | number) => unwrap<AnyRecord[]>(apiClient.get(`/api/routes/${id}/timeline`)),
  recommendations: (id: string | number) => unwrap<AnyRecord[]>(apiClient.get(`/api/routes/${id}/recommendations`)),
};

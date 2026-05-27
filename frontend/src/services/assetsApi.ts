import { apiClient, unwrap } from "./apiClient";
import type { AnyRecord } from "@/types";

export const assetsApi = {
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/assets")),
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/assets/summary")),
  detail: (id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/assets/${id}`)),
  timeline: (id: string | number) => unwrap<AnyRecord[]>(apiClient.get(`/api/assets/${id}/timeline`)),
  recommendations: (id: string | number) => unwrap<AnyRecord[]>(apiClient.get(`/api/assets/${id}/recommendations`)),
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/assets", payload)),
  update: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/assets/${id}`, payload)),
  remove: (id: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/assets/${id}`)),
  assign: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post(`/api/assets/${id}/assign`, payload)),
};

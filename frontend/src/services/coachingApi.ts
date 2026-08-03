import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export const coachingApi = {
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/coaching/summary")),
  tasks: () => unwrap<AnyRecord[]>(apiClient.get("/api/coaching/tasks")),
  detail: (id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/coaching/tasks/${id}`)),
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/coaching/tasks", payload)),
  update: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/coaching/tasks/${id}`, payload)),
  remove: (id: string | number, rowVersion: string | number) => unwrap<AnyRecord>(
    apiClient.delete(`/api/coaching/tasks/${id}`, { headers: { "If-Match": String(rowVersion) } }),
  ),
  recommendations: () => unwrap<AnyRecord[]>(apiClient.get("/api/coaching/recommendations")),
  assign: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post(`/api/coaching/tasks/${id}/assign`, payload)),
  acknowledge: (id: string | number, rowVersion: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/coaching/tasks/${id}/acknowledge`, { rowVersion })),
  complete: (id: string | number, payload: { rowVersion: string | number; completionNote: string; afterSafetyScore: number }) => unwrap<AnyRecord>(apiClient.post(`/api/coaching/tasks/${id}/complete`, payload)),
  addNote: (id: string | number, payload: AnyRecord = {}) => unwrap<AnyRecord>(apiClient.post(`/api/coaching/tasks/${id}/add-note`, payload)),
};

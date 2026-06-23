import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";
import { getCoachingTasks, getCoachingSummary } from "@/data/developmentFleetSeedData";

async function withFallback<T>(req: Promise<T>, fb: () => T | Promise<T>): Promise<T> {
  try { return await req; } catch { return fb(); }
}

export const coachingApi = {
  summary: () => withFallback(unwrap<AnyRecord>(apiClient.get("/api/coaching/summary")), () => getCoachingSummary() as AnyRecord),
  tasks: () => withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/coaching/tasks")), () => getCoachingTasks() as AnyRecord[]),
  detail: (id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/coaching/tasks/${id}`)),
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/coaching/tasks", payload)),
  update: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/coaching/tasks/${id}`, payload)),
  remove: (id: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/coaching/tasks/${id}`)),
  recommendations: () => unwrap<AnyRecord[]>(apiClient.get("/api/coaching/recommendations")),
  assign: (id: string | number, payload: AnyRecord = {}) => unwrap<AnyRecord>(apiClient.post(`/api/coaching/tasks/${id}/assign`, payload)),
  acknowledge: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/coaching/tasks/${id}/acknowledge`, {})),
  complete: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/coaching/tasks/${id}/complete`, {})),
  addNote: (id: string | number, payload: AnyRecord = {}) => unwrap<AnyRecord>(apiClient.post(`/api/coaching/tasks/${id}/add-note`, payload)),
};

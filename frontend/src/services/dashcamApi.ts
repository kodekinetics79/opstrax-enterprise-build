import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";
import { getDashcamEvents, getDashcamSummary } from "@/data/developmentFleetSeedData";

async function withFallback<T>(req: Promise<T>, fb: () => T | Promise<T>): Promise<T> {
  void fb;
  return await req;
}

export const dashcamApi = {
  summary: () => withFallback(unwrap<AnyRecord>(apiClient.get("/api/dashcam/summary")), () => getDashcamSummary() as AnyRecord),
  events: () => withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/dashcam/events")), () => getDashcamEvents() as AnyRecord[]),
  detail: (id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/dashcam/events/${id}`)),
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/dashcam/events", payload)),
  update: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/dashcam/events/${id}`, payload)),
  remove: (id: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/dashcam/events/${id}`)),
  recommendations: () => unwrap<AnyRecord[]>(apiClient.get("/api/dashcam/recommendations")),
  review: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/dashcam/events/${id}/review`, {})),
  falsePositive: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/dashcam/events/${id}/mark-false-positive`, {})),
  createCoaching: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/dashcam/events/${id}/create-coaching-task`, {})),
  createEvidencePackage: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/dashcam/events/${id}/create-evidence-package`, {})),
  createIncidentReport: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/dashcam/events/${id}/create-incident-report`, {})),
};

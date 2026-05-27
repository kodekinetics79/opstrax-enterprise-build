import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export const dashcamApi = {
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/dashcam/summary")),
  events: () => unwrap<AnyRecord[]>(apiClient.get("/api/dashcam/events")),
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

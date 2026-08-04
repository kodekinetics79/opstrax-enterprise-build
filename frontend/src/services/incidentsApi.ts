import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export type IncidentStatusPayload = { status: string; rowVersion: number };
export type IncidentEvidencePayload = {
  evidenceType: string;
  evidenceTitle: string;
  evidenceUrl: string;
  contentHash: string;
  rowVersion: number;
};

export const incidentsApi = {
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/incidents/summary")),
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/incidents")),
  detail: (id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/incidents/${id}`)),
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/incidents", payload)),
  update: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/incidents/${id}`, payload)),
  remove: (id: string | number, rowVersion: number) => unwrap<AnyRecord>(apiClient.delete(`/api/incidents/${id}`, { headers: { "If-Match": String(rowVersion) } })),
  timeline: (id: string | number) => unwrap<AnyRecord[]>(apiClient.get(`/api/incidents/${id}/timeline`)),
  recommendations: (id: string | number) => unwrap<AnyRecord[]>(apiClient.get(`/api/incidents/${id}/recommendations`)),
  status: (id: string | number, payload: IncidentStatusPayload) => unwrap<AnyRecord>(apiClient.post(`/api/incidents/${id}/status`, payload)),
  attachEvidence: (id: string | number, payload: IncidentEvidencePayload) => unwrap<AnyRecord>(apiClient.post(`/api/incidents/${id}/attach-evidence`, payload)),
  createInsuranceReport: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post(`/api/incidents/${id}/create-insurance-report`, payload)),
};

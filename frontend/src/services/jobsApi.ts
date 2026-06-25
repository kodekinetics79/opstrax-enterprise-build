import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

function asRows(value: unknown): AnyRecord[] {
  return Array.isArray(value) ? (value as AnyRecord[]) : [];
}

export const jobsApi = {
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/jobs")),
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/jobs/summary")),
  detail: (id: string | number) =>
    unwrap<AnyRecord>(apiClient.get(`/api/jobs/${id}`)).then((detail) => ({
      ...detail,
      record: (detail.record as AnyRecord) ?? detail,
      timeline: asRows(detail.timeline),
      recommendations: asRows(detail.recommendations),
      stops: asRows(detail.stops),
      proof: asRows(detail.proof),
      communications: asRows(detail.communications),
      etaUpdates: asRows(detail.etaUpdates),
      auditTrail: asRows(detail.auditTrail),
    })),
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

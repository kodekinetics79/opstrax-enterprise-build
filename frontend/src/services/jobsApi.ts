import { apiClient, unwrap } from "@/services/apiClient";
import { apiPaged } from "@/services/fleetDomainApi";
import type { AnyRecord } from "@/types";

function asRows(value: unknown): AnyRecord[] {
  return Array.isArray(value) ? (value as AnyRecord[]) : [];
}

export type JobListOptions = { limit?: number; offset?: number; search?: string; status?: string; priority?: string; jobId?: string | number };

function jobFilters(opts?: JobListOptions) {
  const filters = new URLSearchParams();
  if (opts?.search?.trim()) filters.set("search", opts.search.trim());
  if (opts?.status && opts.status !== "All") filters.set("status", opts.status);
  if (opts?.priority && opts.priority !== "All") filters.set("priority", opts.priority);
  if (opts?.jobId != null && String(opts.jobId).trim()) filters.set("jobId", String(opts.jobId).trim());
  return filters;
}

export const jobsApi = {
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/jobs")),
  listPaged: (opts?: JobListOptions) => {
    const filters = jobFilters(opts);
    filters.delete("search"); // apiPaged adds the search parameter together with paging.
    const query = filters.toString();
    return apiPaged(`/api/jobs${query ? `?${query}` : ""}`, opts);
  },
  exportPath: (opts?: JobListOptions) => {
    const query = jobFilters(opts).toString();
    return `/api/jobs/export${query ? `?${query}` : ""}`;
  },
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/jobs/summary")),
  customerOptions: (search = "") => unwrap<AnyRecord[]>(apiClient.get("/api/jobs/customer-options", {
    params: { search: search.trim(), limit: 200 },
  })),
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
  importPreview: (rows: AnyRecord[]) => unwrap<AnyRecord>(apiClient.post("/api/jobs/import-preview", { rows })),
  importCommit: (rows: AnyRecord[], idempotencyKey: string) => unwrap<AnyRecord>(apiClient.post("/api/jobs/import", { rows }, {
    headers: { "Idempotency-Key": idempotencyKey },
  })),
  assign: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post(`/api/jobs/${id}/assign`, payload)),
  changeStatus: (id: string | number, status: string, notes?: string) => unwrap<AnyRecord>(apiClient.post(`/api/jobs/${id}/status`, { status, notes })),
  sendEta: (id: string | number, payload: AnyRecord = {}, idempotencyKey = globalThis.crypto.randomUUID()) =>
    unwrap<AnyRecord>(apiClient.post(`/api/jobs/${id}/send-eta`, payload, { headers: { "Idempotency-Key": idempotencyKey } })),
  proofPlaceholder: (id: string | number, payload: AnyRecord = {}) => unwrap<AnyRecord>(apiClient.post(`/api/jobs/${id}/proof-placeholder`, payload)),
  captureProof: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post(`/api/jobs/${id}/proof`, payload)),
};

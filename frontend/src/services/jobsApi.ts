import { apiClient, unwrap } from "@/services/apiClient";
import { developmentFleetSeedData } from "@/data/developmentFleetSeedData";
import { withFallback } from "@/services/fleetDomainApi";
import type { AnyRecord } from "@/types";

export const jobsApi = {
  list: () => withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/jobs")), () => developmentFleetSeedData.jobs),
  summary: () => withFallback(unwrap<AnyRecord>(apiClient.get("/api/jobs/summary")), () => ({
    totalJobsToday: developmentFleetSeedData.jobs.length,
    unassignedJobs: developmentFleetSeedData.jobs.filter((job) => String(job.status) === "Unassigned").length,
    assignedJobs: developmentFleetSeedData.jobs.filter((job) => String(job.status) === "Assigned").length,
    enRoute: 0,
    atStop: 0,
    completed: developmentFleetSeedData.jobs.filter((job) => /Delivered|Completed/i.test(String(job.status))).length,
    delayed: developmentFleetSeedData.jobs.filter((job) => /Delayed/i.test(String(job.status))).length,
    slaAtRisk: developmentFleetSeedData.jobs.filter((job) => /risk/i.test(String(job.priority))).length,
    proofPending: developmentFleetSeedData.jobs.length,
    customerUpdatesSent: 0,
    averageEtaAccuracy: 94,
    revenueMarginPlaceholder: "24%",
  })),
  detail: (id: string | number) => withFallback(unwrap<AnyRecord>(apiClient.get(`/api/jobs/${id}`)), () => ({ record: developmentFleetSeedData.jobs.find((job) => String(job.id) === String(id) || String(job.jobNumber) === String(id)) || developmentFleetSeedData.jobs[0], stops: [], proof: [], communications: [], auditTrail: [], recommendations: [] })),
  timeline: async () => [],
  recommendations: async () => [],
  create: (payload: AnyRecord) => withFallback(unwrap<AnyRecord>(apiClient.post("/api/jobs", payload)), () => ({ ...payload, id: payload.id ?? `job-${Date.now()}`, success: true })),
  update: (id: string | number, payload: AnyRecord) => withFallback(unwrap<AnyRecord>(apiClient.put(`/api/jobs/${id}`, payload)), () => ({ ...payload, id, success: true })),
  remove: (id: string | number) => withFallback(unwrap<AnyRecord>(apiClient.delete(`/api/jobs/${id}`)), () => ({ id, success: true })),
  importPreview: (payload: AnyRecord) => withFallback(unwrap<AnyRecord>(apiClient.post("/api/jobs/import-preview", payload)), () => ({ ...payload, preview: [] })),
  assign: (id: string | number, payload: AnyRecord) => withFallback(unwrap<AnyRecord>(apiClient.post(`/api/jobs/${id}/assign`, payload)), () => ({ id, ...payload, success: true })),
  changeStatus: (id: string | number, status: string, notes?: string) => withFallback(unwrap<AnyRecord>(apiClient.post(`/api/jobs/${id}/status`, { status, notes })), () => ({ id, status, notes, success: true })),
  sendEta: (id: string | number, payload: AnyRecord = {}) => withFallback(unwrap<AnyRecord>(apiClient.post(`/api/jobs/${id}/send-eta`, payload)), () => ({ id, ...payload, success: true })),
  proofPlaceholder: (id: string | number, payload: AnyRecord = {}) => withFallback(unwrap<AnyRecord>(apiClient.post(`/api/jobs/${id}/proof-placeholder`, payload)), () => ({ id, ...payload, success: true })),
};

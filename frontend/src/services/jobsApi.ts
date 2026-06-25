import { apiClient, unwrap } from "@/services/apiClient";
import { developmentFleetSeedData } from "@/data/developmentFleetSeedData";
import { withFallback } from "@/services/fleetDomainApi";
import type { AnyRecord } from "@/types";

export const jobsApi = {
  list: () => withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/jobs")), () => developmentFleetSeedData.jobs),
  summary: () => withFallback(unwrap<AnyRecord>(apiClient.get("/api/jobs/summary")), () => {
    // Offline dev safety net only — every figure is derived from the seed rows,
    // never fabricated, so it stays consistent with the table the user sees.
    const jobs = developmentFleetSeedData.jobs as unknown as AnyRecord[];
    const count = (re: RegExp, field = "status") =>
      jobs.filter((job) => re.test(String(job[field] ?? ""))).length;
    const revenueMargin = jobs.reduce((sum, job) => sum + Number(job.marginEstimate ?? 0), 0);
    const withEta = jobs.filter((job) => job.eta && job.slaDueAt);
    const onTime = withEta.filter((job) => new Date(String(job.eta)) <= new Date(String(job.slaDueAt))).length;
    return {
      totalJobsToday: jobs.length,
      unassignedJobs: count(/^Unassigned$/),
      assignedJobs: count(/^Assigned$/),
      enRoute: count(/En Route|In Progress/),
      atStop: count(/At Stop/),
      completed: count(/Delivered|Completed/),
      delayed: count(/Delayed|At Risk/),
      slaAtRisk: count(/At Risk/, "slaStatus"),
      proofPending: count(/Pending/, "proofStatus"),
      customerUpdatesSent: count(/Sent/, "customerUpdateStatus"),
      averageEtaAccuracy: withEta.length ? `${Math.round((onTime / withEta.length) * 100)}%` : "N/A",
      revenueMargin: revenueMargin ? `$${revenueMargin.toLocaleString()}` : "N/A",
    };
  }),
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

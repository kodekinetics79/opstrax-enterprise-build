/**
 * batch7Api — all calls use the shared apiClient (Axios) which attaches
 * the session Bearer token and CSRF header via its request interceptors.
 * withFallback ensures graceful degradation in demo / no-backend mode;
 * seed data lives in the UI layer (SlaKpiPage, ExecutivePage, etc.).
 */
import { apiClient, unwrap } from "@/services/apiClient";
import { withFallback } from "@/services/fleetDomainApi";
import type { AnyRecord } from "@/types";
import { getAuditLogs } from "@/data/developmentFleetSeedData";

// ── Typed helpers ─────────────────────────────────────────────────────────────

function getList(path: string, qs?: Record<string, string>): Promise<AnyRecord[]> {
  const search = qs ? "?" + new URLSearchParams(qs).toString() : "";
  return withFallback(
    unwrap<AnyRecord[]>(apiClient.get(`${path}${search}`)),
    () => [],
  );
}

function getObj(path: string): Promise<AnyRecord> {
  return withFallback(
    unwrap<AnyRecord>(apiClient.get(path)),
    () => ({}),
  );
}

async function post(path: string, body?: unknown): Promise<AnyRecord> {
  const res = await apiClient.post<{ success: boolean; data: AnyRecord }>(path, body ?? {});
  return res.data.data ?? {};
}

// ── Reports API ───────────────────────────────────────────────────────────────

export const reportsApi = {
  catalog:           () => getList("/api/reports/catalog"),
  summary:           () => getObj("/api/reports/summary"),
  runs:              () => getList("/api/reports/runs"),
  runReport:         (key: string, filters?: Record<string, unknown>) => post(`/api/reports/${key}/run`, filters ?? {}),
  scheduled:         () => getList("/api/reports/scheduled"),
  createScheduled:   (body: Record<string, unknown>) => post("/api/reports/scheduled", body),
  pauseScheduled:    (id: number) => post(`/api/reports/scheduled/${id}/pause`),
  resumeScheduled:   (id: number) => post(`/api/reports/scheduled/${id}/resume`),
  exports:           () => getList("/api/reports/exports"),
  createExport:      (body: Record<string, unknown>) => post("/api/reports/exports", body),
  aiRecommendations: () => getList("/api/reports/ai/recommendations"),
};

// ── KPI API ───────────────────────────────────────────────────────────────────

export const kpiApi = {
  metrics:           () => getList("/api/kpi/metrics"),
  summary:           () => getObj("/api/kpi/summary"),
  targets:           () => getList("/api/kpi/targets"),
  aiRecommendations: () => getList("/api/kpi/ai/recommendations"),
};

// ── SLA API ───────────────────────────────────────────────────────────────────

export const slaApi = {
  records:            () => getList("/api/sla/records"),
  summary:            () => getObj("/api/sla/summary"),
  breaches:           () => getList("/api/sla/breaches"),
  acknowledgeBreache: (id: number) => post(`/api/sla/breaches/${id}/acknowledge`),
  resolveBreache:     (id: number) => post(`/api/sla/breaches/${id}/resolve`),
};

// ── Audit API ─────────────────────────────────────────────────────────────────

export const auditApi = {
  logs:              (params?: Record<string, string>) => withFallback(
    unwrap<AnyRecord[]>(apiClient.get("/api/audit/logs" + (params ? "?" + new URLSearchParams(params).toString() : ""))),
    () => getAuditLogs() as AnyRecord[],
  ),
  log:               (id: number) => getObj(`/api/audit/logs/${id}`),
  exportRequests:    () => getList("/api/audit/export-requests"),
  createExport:      (body: Record<string, unknown>) => post("/api/audit/export-requests", body),
  aiRecommendations: () => getList("/api/audit/ai/recommendations"),
};

// ── Executive API ─────────────────────────────────────────────────────────────

export const executiveApi = {
  snapshots:         () => getList("/api/executive/snapshots"),
  summary:           () => getObj("/api/executive/summary"),
  aiRecommendations: () => getList("/api/executive/ai/recommendations"),
};

const BASE = import.meta.env.VITE_API_URL ?? "http://localhost:8088";

async function get<T>(path: string): Promise<T> {
  const r = await fetch(`${BASE}${path}`);
  if (!r.ok) throw new Error(`${r.status} ${path}`);
  const json = await r.json();
  return json.data ?? json;
}

async function post<T>(path: string, body?: unknown): Promise<T> {
  const r = await fetch(`${BASE}${path}`, { method: "POST", headers: { "Content-Type": "application/json" }, body: body ? JSON.stringify(body) : undefined });
  if (!r.ok) throw new Error(`${r.status} ${path}`);
  const json = await r.json();
  return json.data ?? json;
}

export const reportsApi = {
  catalog:           () => get("/api/reports/catalog"),
  summary:           () => get("/api/reports/summary"),
  runs:              () => get("/api/reports/runs"),
  runReport:         (key: string, filters?: Record<string, unknown>) => post(`/api/reports/${key}/run`, filters ?? {}),
  scheduled:         () => get("/api/reports/scheduled"),
  createScheduled:   (body: Record<string, unknown>) => post("/api/reports/scheduled", body),
  pauseScheduled:    (id: number) => post(`/api/reports/scheduled/${id}/pause`),
  resumeScheduled:   (id: number) => post(`/api/reports/scheduled/${id}/resume`),
  exports:           () => get("/api/reports/exports"),
  createExport:      (body: Record<string, unknown>) => post("/api/reports/exports", body),
  aiRecommendations: () => get("/api/reports/ai/recommendations"),
};

export const kpiApi = {
  metrics:           () => get("/api/kpi/metrics"),
  summary:           () => get("/api/kpi/summary"),
  targets:           () => get("/api/kpi/targets"),
  aiRecommendations: () => get("/api/kpi/ai/recommendations"),
};

export const slaApi = {
  records:            () => get("/api/sla/records"),
  summary:            () => get("/api/sla/summary"),
  breaches:           () => get("/api/sla/breaches"),
  acknowledgeBreache: (id: number) => post(`/api/sla/breaches/${id}/acknowledge`),
  resolveBreache:     (id: number) => post(`/api/sla/breaches/${id}/resolve`),
};

export const auditApi = {
  logs:              (params?: Record<string, string>) => {
    const qs = params ? "?" + new URLSearchParams(params).toString() : "";
    return get(`/api/audit/logs${qs}`);
  },
  log:               (id: number) => get(`/api/audit/logs/${id}`),
  exportRequests:    () => get("/api/audit/export-requests"),
  createExport:      (body: Record<string, unknown>) => post("/api/audit/export-requests", body),
  aiRecommendations: () => get("/api/audit/ai/recommendations"),
};

export const executiveApi = {
  snapshots:         () => get("/api/executive/snapshots"),
  summary:           () => get("/api/executive/summary"),
  aiRecommendations: () => get("/api/executive/ai/recommendations"),
};

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

async function put<T>(path: string, body: unknown): Promise<T> {
  const r = await fetch(`${BASE}${path}`, { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
  if (!r.ok) throw new Error(`${r.status} ${path}`);
  const json = await r.json();
  return json.data ?? json;
}

export const complianceApi = {
  summary:         () => get("/api/compliance/summary"),
  profiles:        () => get("/api/compliance/profiles"),
  rules:           () => get("/api/compliance/rules"),
  violations:      () => get("/api/compliance/violations"),
  violation:       (id: number) => get(`/api/compliance/violations/${id}`),
  acknowledgeViolation: (id: number) => post(`/api/compliance/violations/${id}/acknowledge`),
  resolveViolation: (id: number) => post(`/api/compliance/violations/${id}/resolve`),
  documents:       () => get("/api/compliance/documents"),
  auditPackages:   () => get("/api/compliance/audit-packages"),
  auditPackage:    (id: number) => get(`/api/compliance/audit-packages/${id}`),
  createAuditPackage: (body: Record<string, unknown>) => post("/api/compliance/audit-packages", body),
  finalizeAuditPackage: (id: number) => post(`/api/compliance/audit-packages/${id}/finalize`),
  crossBorderWatch: () => get("/api/compliance/cross-border-watch"),
  driverStatus:    () => get("/api/compliance/driver-status"),
  vehicleStatus:   () => get("/api/compliance/vehicle-status"),
  aiRecommendations: () => get("/api/compliance/ai/recommendations"),
};

export const hosApi = {
  summary:      () => get("/api/hos/summary"),
  drivers:      () => get("/api/hos/drivers"),
  clocks:       () => get("/api/hos/clocks"),
  logs:         () => get("/api/hos/logs"),
  driverLogs:   (driverId: number) => get(`/api/hos/logs/${driverId}`),
  certifyLog:   (id: number) => post(`/api/hos/logs/${id}/certify`),
  aiRecommendations: () => get("/api/hos/ai/recommendations"),
};

export const eldApi = {
  devices:          () => get("/api/eld/devices"),
  device:           (id: number) => get(`/api/eld/devices/${id}`),
  markMalfunction:  (id: number, body: Record<string, unknown>) => post(`/api/eld/devices/${id}/mark-malfunction`, body),
  resolveMalfunction: (id: number) => post(`/api/eld/devices/${id}/resolve-malfunction`),
};

export const localizationApi = {
  countries:       () => get("/api/localization/countries"),
  languages:       () => get("/api/localization/languages"),
  settings:        () => get("/api/localization/settings"),
  updateSettings:  (body: Record<string, unknown>) => put("/api/localization/settings", body),
  userPreferences: () => get("/api/localization/user-preferences"),
  updateUserPreferences: (body: Record<string, unknown>) => put("/api/localization/user-preferences", body),
};

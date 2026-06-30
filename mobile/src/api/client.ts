import { API_BASE_URL } from "@/config";
import type { ApiEnvelope, JsonRecord, MobileSession } from "@/types";

type RequestOptions = {
  method?: "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
  body?: JsonRecord | unknown;
  retryOn401?: boolean;
};

type SessionAccess = {
  getSession: () => MobileSession | null;
  setSession: (session: MobileSession | null) => void;
};

function isEnvelope<T>(value: unknown): value is ApiEnvelope<T> {
  return Boolean(value) && typeof value === "object" && "success" in (value as Record<string, unknown>) && "data" in (value as Record<string, unknown>);
}

async function parseJson(response: Response) {
  const text = await response.text();
  if (!text) return null;
  try {
    return JSON.parse(text) as unknown;
  } catch {
    return text;
  }
}

function withSessionHeaders(headers: HeadersInit, session: MobileSession | null, method: string, csrfToken?: string) {
  const next = new Headers(headers);
  next.set("Accept", "application/json");
  if (session?.token) {
    next.set("Authorization", `Bearer ${session.token}`);
  }
  if (csrfToken && ["POST", "PUT", "PATCH", "DELETE"].includes(method)) {
    next.set("X-CSRF-Token", csrfToken);
  }
  return next;
}

function buildUrl(path: string) {
  return `${API_BASE_URL}${path.startsWith("/") ? path : `/${path}`}`;
}

export function createMobileApiClient(access: SessionAccess) {
  const rawRequest = async <T>(path: string, options: RequestOptions = {}, attempt = 0): Promise<T> => {
    const session = access.getSession();
    const method = options.method ?? "GET";
    const headers = withSessionHeaders({}, session, method, session?.csrfToken);
    const body = options.body === undefined ? undefined : JSON.stringify(options.body);
    if (body !== undefined) headers.set("Content-Type", "application/json");

    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 30_000);
    const response = await fetch(buildUrl(path), { method, headers, body, signal: controller.signal });
    clearTimeout(timeout);

    if (response.status === 401 && options.retryOn401 !== false && attempt === 0 && session?.token && !path.startsWith("/api/auth/")) {
      const refreshed = await refreshSession(access);
      if (refreshed) {
        return rawRequest<T>(path, options, attempt + 1);
      }
    }

    const payload = await parseJson(response);
    if (!response.ok) {
      const message = typeof payload === "object" && payload && "message" in (payload as Record<string, unknown>)
        ? String((payload as Record<string, unknown>).message ?? `Request failed with ${response.status}`)
        : `Request failed with ${response.status}`;
      throw new Error(message);
    }

    if (payload == null) return undefined as T;
    if (isEnvelope<T>(payload)) {
      if (!payload.success) {
        throw new Error(payload.message || "Request failed.");
      }
      return payload.data;
    }
    return payload as T;
  };

  const request = {
    get: <T>(path: string) => rawRequest<T>(path, { method: "GET" }),
    post: <T>(path: string, body?: JsonRecord | unknown) => rawRequest<T>(path, { method: "POST", body }),
    put: <T>(path: string, body?: JsonRecord | unknown) => rawRequest<T>(path, { method: "PUT", body }),
    patch: <T>(path: string, body?: JsonRecord | unknown) => rawRequest<T>(path, { method: "PATCH", body }),
    delete: <T>(path: string, body?: JsonRecord | unknown) => rawRequest<T>(path, { method: "DELETE", body }),
  };

  return {
    request,
    login: (email: string, password: string) =>
      rawRequest<MobileSession>("/api/auth/login", {
        method: "POST",
        body: { email, password },
        retryOn401: false,
      }),
    me: () => rawRequest<MobileSession>("/api/auth/me", { method: "GET" }),
    refresh: () => rawRequest<MobileSession>("/api/auth/refresh", { method: "POST" }),
    logout: () => rawRequest<{ loggedOut: boolean }>("/api/auth/logout", { method: "POST", retryOn401: false }),
    jobs: () => request.get<JsonRecord[]>("/api/jobs"),
    executionSummary: (jobId: number | string) => request.get<JsonRecord>(`/api/operations/jobs/${jobId}/execution-summary`),
    smartAssignmentRecommendations: (jobId: number | string) => request.get<JsonRecord[]>(`/api/jobs/${jobId}/smart-assign/recommendations`),
    recommendSmartAssignment: (jobId: number | string, body: JsonRecord) => request.post<JsonRecord>(`/api/jobs/${jobId}/smart-assign/recommend`, body),
    acceptSmartAssignment: (recommendationId: number | string, body: JsonRecord = {}) => request.post<JsonRecord>(`/api/smart-assign/recommendations/${recommendationId}/accept`, body),
    rejectSmartAssignment: (recommendationId: number | string, body: JsonRecord = {}) => request.post<JsonRecord>(`/api/smart-assign/recommendations/${recommendationId}/reject`, body),
    siteAccess: (jobId: number | string) => request.get<JsonRecord[]>(`/api/jobs/${jobId}/site-access`),
    createSiteAccess: (jobId: number | string, body: JsonRecord) => request.post<JsonRecord>(`/api/jobs/${jobId}/site-access`, body),
    updateSiteAccess: (id: number | string, body: JsonRecord) => request.patch<JsonRecord>(`/api/site-access/${id}`, body),
    pickupAuthorizations: (jobId: number | string) => request.get<JsonRecord[]>(`/api/jobs/${jobId}/pickup-authorizations`),
    createPickupAuthorization: (jobId: number | string, body: JsonRecord) => request.post<JsonRecord>(`/api/jobs/${jobId}/pickup-authorizations`, body),
    updatePickupAuthorization: (id: number | string, body: JsonRecord) => request.patch<JsonRecord>(`/api/pickup-authorizations/${id}`, body),
    warehouseHandovers: (jobId: number | string) => request.get<JsonRecord[]>(`/api/jobs/${jobId}/warehouse-handovers`),
    createWarehouseHandover: (jobId: number | string, body: JsonRecord) => request.post<JsonRecord>(`/api/jobs/${jobId}/warehouse-handovers`, body),
    updateWarehouseHandover: (id: number | string, body: JsonRecord) => request.patch<JsonRecord>(`/api/warehouse-handovers/${id}`, body),
    proofPackages: (jobId: number | string) => request.get<JsonRecord[]>(`/api/jobs/${jobId}/proof-packages`),
    createProofPackage: (jobId: number | string, body: JsonRecord) => request.post<JsonRecord>(`/api/jobs/${jobId}/proof-packages`, body),
    proofPackage: (id: number | string) => request.get<JsonRecord>(`/api/proof-packages/${id}`),
    updateProofPackage: (id: number | string, body: JsonRecord) => request.patch<JsonRecord>(`/api/proof-packages/${id}`, body),
    submitProofPackage: (id: number | string, body: JsonRecord = {}) => request.post<JsonRecord>(`/api/proof-packages/${id}/submit`, body),
    validateProofPackage: (id: number | string, body: JsonRecord = {}) => request.post<JsonRecord>(`/api/proof-packages/${id}/validate`, body),
    proofArtifacts: (proofPackageId: number | string) => request.get<JsonRecord[]>(`/api/proof-packages/${proofPackageId}/artifacts`),
    createProofArtifact: (proofPackageId: number | string, body: JsonRecord) => request.post<JsonRecord>(`/api/proof-packages/${proofPackageId}/artifacts`, body),
    billingConfidence: (proofPackageId: number | string) => request.get<JsonRecord>(`/api/proof-packages/${proofPackageId}/billing-confidence`),
    telemetrySummary: () => request.get<JsonRecord>("/api/telemetry/live-map-summary"),
    telemetryAssets: () => request.get<JsonRecord[]>("/api/telemetry/assets/live-state"),
    telemetryAsset: (vehicleId: number | string) => request.get<JsonRecord>(`/api/telemetry/assets/${vehicleId}/live-state`),
    safetyDashboard: () => request.get<JsonRecord>("/api/safety/dashboard"),
    maintenanceDashboard: () => request.get<JsonRecord>("/api/maintenance/dashboard"),
  };
}

async function refreshSession(access: SessionAccess) {
  const session = access.getSession();
  if (!session?.token) return false;

  const response = await fetch(`${API_BASE_URL}/api/auth/refresh`, {
    method: "POST",
    headers: {
      Accept: "application/json",
      Authorization: `Bearer ${session.token}`,
      ...(session.csrfToken ? { "X-CSRF-Token": session.csrfToken } : {}),
    },
  });

  if (!response.ok) return false;
  const payload = await parseJson(response);
  if (isEnvelope<MobileSession>(payload) && payload.success) {
    access.setSession(payload.data);
    return true;
  }
  return false;
}


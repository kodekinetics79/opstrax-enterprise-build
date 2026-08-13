import { API_BASE_URL } from "@/config";
import type {
  ApiEnvelope,
  DriverAssignment,
  DriverCurrentAssignment,
  DriverProfile,
  DriverProofArtifact,
  JsonRecord,
  LoginResult,
  MobileSession,
} from "@/types";

type RequestOptions = {
  method?: "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
  body?: JsonRecord | unknown;
  retryOn401?: boolean;
  headers?: HeadersInit;
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
  let refreshInFlight: Promise<boolean> | null = null;
  const rawRequest = async <T>(path: string, options: RequestOptions = {}, attempt = 0): Promise<T> => {
    const session = access.getSession();
    const method = options.method ?? "GET";
    const headers = withSessionHeaders(options.headers ?? {}, session, method, session?.csrfToken);
    const isMultipart = typeof FormData !== "undefined" && options.body instanceof FormData;
    const body = options.body === undefined ? undefined : isMultipart ? options.body : JSON.stringify(options.body);
    if (body !== undefined && !isMultipart) headers.set("Content-Type", "application/json");

    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 30_000);
    let response: Response;
    try {
      response = await fetch(buildUrl(path), { method, headers, body: body as BodyInit | undefined, signal: controller.signal });
    } catch (error) {
      if (error instanceof Error && error.name === "AbortError") {
        throw new Error("The request timed out. Check your connection and try again.");
      }
      throw new Error("OpsTrax could not reach the server. Check your connection and try again.");
    } finally {
      clearTimeout(timeout);
    }

    if (response.status === 401 && options.retryOn401 !== false && attempt === 0 && session?.token && !path.startsWith("/api/auth/")) {
      refreshInFlight ??= refreshSession(access).finally(() => {
        refreshInFlight = null;
      });
      const refreshed = await refreshInFlight;
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
    post: <T>(path: string, body?: JsonRecord | unknown, headers?: HeadersInit) => rawRequest<T>(path, { method: "POST", body, headers }),
    put: <T>(path: string, body?: JsonRecord | unknown, headers?: HeadersInit) => rawRequest<T>(path, { method: "PUT", body, headers }),
    patch: <T>(path: string, body?: JsonRecord | unknown, headers?: HeadersInit) => rawRequest<T>(path, { method: "PATCH", body, headers }),
    delete: <T>(path: string, body?: JsonRecord | unknown, headers?: HeadersInit) => rawRequest<T>(path, { method: "DELETE", body, headers }),
  };

  return {
    request,
    login: (email: string, password: string, companyCode: string) =>
      rawRequest<LoginResult>("/api/auth/login", {
        method: "POST",
        body: { email, password, companyCode: companyCode.trim() },
        retryOn401: false,
      }),
    verifyMfaLogin: (challengeToken: string, code: string) =>
      rawRequest<MobileSession>("/api/auth/mfa/login-verify", {
        method: "POST",
        body: { challengeToken, code },
        retryOn401: false,
      }),
    me: () => rawRequest<MobileSession>("/api/auth/me", { method: "GET" }),
    refresh: () => rawRequest<MobileSession>("/api/auth/refresh", { method: "POST" }),
    logout: () => rawRequest<{ loggedOut: boolean }>("/api/auth/logout", { method: "POST", retryOn401: false }),
    jobs: () => request.get<JsonRecord[]>("/api/jobs"),
    executionSummary: (jobId: number | string) => request.get<JsonRecord>(`/api/operations/jobs/${jobId}/execution-summary`),
    smartAssignmentRecommendations: (jobId: number | string) => request.get<{ items: JsonRecord[] }>(`/api/jobs/${jobId}/smart-assign/recommendations`),
    recommendSmartAssignment: (jobId: number | string, body: JsonRecord) => request.post<JsonRecord>(`/api/jobs/${jobId}/smart-assign/recommend`, body),
    acceptSmartAssignment: (recommendationId: number | string, body: JsonRecord = {}) => request.post<JsonRecord>(`/api/smart-assign/recommendations/${recommendationId}/accept`, body),
    rejectSmartAssignment: (recommendationId: number | string, body: JsonRecord = {}) => request.post<JsonRecord>(`/api/smart-assign/recommendations/${recommendationId}/reject`, body),
    siteAccess: (jobId: number | string) => request.get<{ items: JsonRecord[] }>(`/api/jobs/${jobId}/site-access`),
    createSiteAccess: (jobId: number | string, body: JsonRecord) => request.post<JsonRecord>(`/api/jobs/${jobId}/site-access`, body),
    updateSiteAccess: (id: number | string, body: JsonRecord) => request.patch<JsonRecord>(`/api/site-access/${id}`, body),
    pickupAuthorizations: (jobId: number | string) => request.get<{ items: JsonRecord[] }>(`/api/jobs/${jobId}/pickup-authorizations`),
    createPickupAuthorization: (jobId: number | string, body: JsonRecord) => request.post<JsonRecord>(`/api/jobs/${jobId}/pickup-authorizations`, body),
    updatePickupAuthorization: (id: number | string, body: JsonRecord) => request.patch<JsonRecord>(`/api/pickup-authorizations/${id}`, body),
    warehouseHandovers: (jobId: number | string) => request.get<{ items: JsonRecord[] }>(`/api/jobs/${jobId}/warehouse-handovers`),
    createWarehouseHandover: (jobId: number | string, body: JsonRecord) => request.post<JsonRecord>(`/api/jobs/${jobId}/warehouse-handovers`, body),
    updateWarehouseHandover: (id: number | string, body: JsonRecord) => request.patch<JsonRecord>(`/api/warehouse-handovers/${id}`, body),
    proofPackages: (jobId: number | string) => request.get<{ items: JsonRecord[] }>(`/api/jobs/${jobId}/proof-packages`),
    createProofPackage: (jobId: number | string, body: JsonRecord) => request.post<JsonRecord>(`/api/jobs/${jobId}/proof-packages`, body),
    proofPackage: (id: number | string) => request.get<JsonRecord>(`/api/proof-packages/${id}`),
    updateProofPackage: (id: number | string, body: JsonRecord) => request.patch<JsonRecord>(`/api/proof-packages/${id}`, body),
    submitProofPackage: (id: number | string, body: JsonRecord = {}) => request.post<JsonRecord>(`/api/proof-packages/${id}/submit`, body),
    validateProofPackage: (id: number | string, body: JsonRecord = {}) => request.post<JsonRecord>(`/api/proof-packages/${id}/validate`, body),
    proofArtifacts: (proofPackageId: number | string) => request.get<{ items: JsonRecord[] }>(`/api/proof-packages/${proofPackageId}/artifacts`),
    createProofArtifact: (proofPackageId: number | string, body: JsonRecord) => request.post<JsonRecord>(`/api/proof-packages/${proofPackageId}/artifacts`, body),
    billingConfidence: (proofPackageId: number | string) => request.get<JsonRecord>(`/api/proof-packages/${proofPackageId}/billing-confidence`),
    telemetrySummary: () => request.get<JsonRecord>("/api/telemetry/live-map-summary"),
    telemetryAssets: () => request.get<JsonRecord[]>("/api/telemetry/assets/live-state"),
    telemetryAsset: (vehicleId: number | string) => request.get<JsonRecord>(`/api/telemetry/assets/${vehicleId}/live-state`),
    safetyDashboard: () => request.get<JsonRecord>("/api/safety/dashboard"),
    maintenanceDashboard: () => request.get<JsonRecord>("/api/maintenance/dashboard"),
    driverMe: () => request.get<DriverProfile>("/api/driver/me"),
    driverAssignments: () => request.get<DriverAssignment[]>("/api/driver/assignments"),
    driverCurrentAssignment: () => request.get<DriverCurrentAssignment>("/api/driver/assignments/current"),
    acceptDriverAssignment: (assignmentId: number | string) =>
      request.post<JsonRecord>(`/api/driver/assignments/${assignmentId}/accept`, {}),
    updateDriverAssignmentStatus: (assignmentId: number | string, status: string, notes?: string) =>
      request.post<JsonRecord>(`/api/driver/assignments/${assignmentId}/status`, { status, notes }),
    reportDriverException: (
      assignmentId: number | string,
      body: { exceptionType: string; severity: string; title?: string; notes: string },
    ) => request.post<JsonRecord>(`/api/driver/assignments/${assignmentId}/exception`, body),
    uploadDriverProofArtifact: async (
      assignmentId: number | string,
      asset: { uri: string; fileName?: string | null; mimeType?: string | null; fileSize?: number; file?: Blob | null },
      kind: DriverProofArtifact["kind"],
    ) => {
      const form = new FormData();
      if (asset.file) {
        form.append("file", asset.file, asset.fileName ?? `${kind}.jpg`);
      } else {
        form.append("file", {
          uri: asset.uri,
          name: asset.fileName ?? `${kind}-${Date.now()}.jpg`,
          type: asset.mimeType ?? "image/jpeg",
        } as unknown as Blob);
      }
      form.append("kind", kind);
      const uploaded = await rawRequest<JsonRecord>(`/api/driver/assignments/${assignmentId}/proof/upload`, {
        method: "POST",
        body: form,
      });
      return {
        kind,
        reference: String(uploaded.reference ?? ""),
        contentType: String(uploaded.contentType ?? asset.mimeType ?? "image/jpeg"),
        size: Number(uploaded.size ?? asset.fileSize ?? 0) || undefined,
        previewUrl: uploaded.url ? String(uploaded.url) : undefined,
      } satisfies DriverProofArtifact;
    },
    submitDriverProof: (
      assignmentId: number | string,
      body: {
        proofType: "pickup" | "delivery";
        notes?: string;
        evidenceHash?: string;
        lat?: number;
        lng?: number;
        artifacts?: Omit<DriverProofArtifact, "previewUrl">[];
      },
    ) => request.post<JsonRecord>(`/api/driver/assignments/${assignmentId}/proof`, body),
    driverDvirTemplates: () => request.get<JsonRecord[]>("/api/driver/dvir/templates"),
    driverDvirReports: () => request.get<JsonRecord[]>("/api/driver/dvir/reports"),
    submitDriverDvir: (body: JsonRecord, idempotencyKey: string) =>
      request.post<JsonRecord>("/api/driver/dvir", body, { "Idempotency-Key": idempotencyKey }),
    driverHos: () => request.get<JsonRecord>("/api/driver/hos"),
    driverHosLogs: () => request.get<JsonRecord>("/api/driver/hos/logs"),
    driverCoaching: () => request.get<{ tasks?: JsonRecord[]; pendingCount?: number; insights?: JsonRecord[] }>("/api/driver/coaching"),
    acknowledgeDriverCoaching: (id: number | string, note?: string) =>
      request.post<JsonRecord>(`/api/driver/coaching/${id}/acknowledge`, { note }),
  };
}

async function refreshSession(access: SessionAccess) {
  const session = access.getSession();
  if (!session?.token) return false;

  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 15_000);
  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}/api/auth/refresh`, {
      method: "POST",
      headers: {
        Accept: "application/json",
        Authorization: `Bearer ${session.token}`,
        ...(session.csrfToken ? { "X-CSRF-Token": session.csrfToken } : {}),
      },
      signal: controller.signal,
    });
  } catch {
    // A timeout or network outage is not proof that the bearer session is invalid.
    return false;
  } finally {
    clearTimeout(timeout);
  }

  if (!response.ok) {
    if ([400, 401, 403].includes(response.status)) access.setSession(null);
    return false;
  }
  const payload = await parseJson(response);
  if (isEnvelope<MobileSession>(payload) && payload.success) {
    access.setSession(payload.data);
    return true;
  }
  // Preserve the local session on malformed/transient refresh responses; the
  // original request still fails and the user can retry without forced logout.
  return false;
}

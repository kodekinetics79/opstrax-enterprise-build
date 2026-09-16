import axios from "axios";
import type { AxiosError, InternalAxiosRequestConfig } from "axios";
import type { ApiEnvelope } from "@/types";
import { getGlobalCsrfToken, hydrateGlobalCsrfToken, setGlobalCsrfToken } from "@/auth/csrfTokenStore";
import { readRawSession, clearAllSessionKeys } from "@/auth/sessionStorage";
import { enforceRequestSessionGuard } from "@/auth/requestSessionGuard";

const isLocalhost =
  typeof window !== "undefined" &&
  /^(localhost|127\.0\.0\.1|\[::1\])$/.test(window.location.hostname);

// Local Vite development defaults to the published API port. A deployed build with
// no explicit API URL uses the current origin instead of sending customer traffic to
// localhost. The production nginx image deliberately supplies "/" and proxies /api
// over the Compose network; Vercel deployments should set VITE_API_BASE_URL to the
// externally reachable API origin when they are not using a same-origin proxy.
export const API_BASE_URL =
  import.meta.env.VITE_API_BASE_URL ||
  import.meta.env.VITE_DOTNET_API_URL ||
  import.meta.env.VITE_PLATFORM_API_BASE_URL ||
  (isLocalhost ? "http://localhost:8088" : "");

// ── Distributed tracing (W3C trace context) ─────────────────────────────────────
// The frontend ORIGINATES the trace so a failed call can be followed all the way
// frontend → backend → DB with one trace_id. We mint a 16-byte trace id + 8-byte
// span id per request and send them as `traceparent` (+ a human-facing
// X-Correlation-Id). The backend continues this trace and echoes X-Trace-Id back.
function hex(bytes: number): string {
  const a = new Uint8Array(bytes);
  (globalThis.crypto ?? (window as unknown as { crypto: Crypto }).crypto).getRandomValues(a);
  return Array.from(a, (b) => b.toString(16).padStart(2, "0")).join("");
}

/** The most recent server-assigned trace id, so error UIs can show a reference. */
export let lastTraceId = "";

export function newTraceParent(): { traceparent: string; traceId: string; correlationId: string } {
  const traceId = hex(16);
  const spanId = hex(8);
  return { traceparent: `00-${traceId}-${spanId}-01`, traceId, correlationId: hex(16) };
}

type ApiClientPipelineOptions = {
  prepareRequest?: (config: InternalAxiosRequestConfig) => void;
  onUnauthorized?: (error: AxiosError) => void;
};

// Every browser API surface uses this pipeline for credentials, CSRF, tracing,
// timeout, and trace capture. Callers may add only the identity headers that
// belong to their own trust boundary (tenant bearer vs platform HttpOnly cookie).
export function createApiClient(options: ApiClientPipelineOptions = {}) {
  const client = axios.create({
    baseURL: API_BASE_URL,
    headers: { Accept: "application/json" },
    timeout: 30000,
    withCredentials: true,
  });

  client.interceptors.request.use((config) => {
    options.prepareRequest?.(config);

    const csrfToken = getGlobalCsrfToken();
    if (csrfToken && ["POST", "PUT", "DELETE", "PATCH"].includes(config.method?.toUpperCase() || "")) {
      config.headers["X-CSRF-Token"] = csrfToken;
    }

    const tp = newTraceParent();
    config.headers["traceparent"] = tp.traceparent;
    config.headers["X-Correlation-Id"] = tp.correlationId;
    return config;
  });

  client.interceptors.response.use(
    (response) => {
      const csrfToken = response.headers["x-csrf-token"];
      if (csrfToken) setGlobalCsrfToken(csrfToken);
      const tid = response.headers["x-trace-id"];
      if (tid) lastTraceId = tid;
      return response;
    },
    (error: AxiosError) => {
      const tid = error.response?.headers?.["x-trace-id"];
      if (typeof tid === "string") lastTraceId = tid;
      if (error.response?.status === 401) options.onUnauthorized?.(error);
      return Promise.reject(error);
    },
  );

  return client;
}

export const apiClient = createApiClient({
  prepareRequest: (config) => {
  // Read the session (for the bearer token) from the SHARED key list — never hardcode it here, or a
  // key bump silently drops the Authorization header and every authenticated call 401s.
  const session = readRawSession();
  enforceRequestSessionGuard(config, session);
  if (session) {
    try {
      const parsed = JSON.parse(session);
      // session.v2 stores { session: { token, csrfToken, ... }, expiresAt }
      const inner = parsed.session ?? parsed;
      if (inner.token) {
        config.headers.Authorization = `Bearer ${inner.token}`;
      }
      const tenantId = inner.company?.id ?? inner.company?.companyId ?? inner.user?.companyId ?? inner.user?.company_id;
      if (tenantId) {
        config.headers["X-Opstrax-Tenant-Id"] = String(tenantId);
      }
      if (inner.csrfToken) {
        hydrateGlobalCsrfToken(inner.csrfToken);
      }
    } catch {
      clearAllSessionKeys();
    }
  }

  },
  onUnauthorized: (error) => {
      const url = (error.config?.url ?? "") as string;
      const hadAuthenticatedSession = Boolean(readRawSession()) || Boolean(error.config?.headers?.Authorization);
      const isPreSessionAuthRequest =
        url.includes("/api/auth/login") ||
        url.includes("/api/auth/forgot-password") ||
        url.includes("/api/auth/reset-password") ||
        url.includes("/api/auth/activate");
      // A protected endpoint uses 403 for insufficient permission. Therefore a
      // 401 on any request that carried a session means the session is no longer
      // authoritative and must use one application-wide expired-session flow.
      const shouldClearSession = hadAuthenticatedSession && !isPreSessionAuthRequest;

      if (shouldClearSession) {
        clearAllSessionKeys();
        if (window.location.pathname !== "/login") {
          window.location.href = "/login";
        }
      }
  }
});

export async function unwrap<T>(request: Promise<{ data: ApiEnvelope<T> }>): Promise<T> {
  const response = await request;
  if (!response.data.success) throw new Error(response.data.message || "API request failed");
  return response.data.data;
}

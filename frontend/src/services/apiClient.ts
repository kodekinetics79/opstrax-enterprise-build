import axios from "axios";
import type { ApiEnvelope } from "@/types";
import { getGlobalCsrfToken, hydrateGlobalCsrfToken, setGlobalCsrfToken } from "@/auth/csrfTokenStore";
import { readRawSession, clearAllSessionKeys } from "@/auth/sessionStorage";

const isLocalhost =
  typeof window !== "undefined" &&
  /^(localhost|127\.0\.0\.1|\[::1\])$/.test(window.location.hostname);

const isVercelDeployment =
  typeof window !== "undefined" &&
  /\.vercel\.app$/i.test(window.location.hostname);

// Keep localhost behavior for dev, but in Vercel production builds we harden
// toward the managed backend so auth/login does not silently hit the same-origin
// SPA host (which will 405 the API path).
const vercelApiFallback = isVercelDeployment ? "https://osptrax-fleet-management.onrender.com" : "";

// Local Vite development defaults to the published API port. A deployed build with
// no explicit API URL uses the current origin instead of sending customer traffic to
// localhost. The production nginx image deliberately supplies "/" and proxies /api
// over the Compose network; Vercel deployments should set VITE_API_BASE_URL to the
// externally reachable API origin when they are not using a same-origin proxy.
export const API_BASE_URL =
  import.meta.env.VITE_API_BASE_URL ||
  import.meta.env.VITE_DOTNET_API_URL ||
  (isLocalhost ? "http://localhost:8088" : vercelApiFallback);

export const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: { Accept: "application/json" },
  timeout: 30000,
  withCredentials: true,
});

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

// Request interceptor: Add auth token and CSRF token
apiClient.interceptors.request.use((config) => {
  // Read the session (for the bearer token) from the SHARED key list — never hardcode it here, or a
  // key bump silently drops the Authorization header and every authenticated call 401s.
  const session = readRawSession();
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

  // Add CSRF token for state-changing requests
  const csrfToken = getGlobalCsrfToken();
  if (csrfToken && ["POST", "PUT", "DELETE", "PATCH"].includes(config.method?.toUpperCase() || "")) {
    config.headers["X-CSRF-Token"] = csrfToken;
  }

  // Originate a W3C trace so this call is followable end-to-end.
  const tp = newTraceParent();
  config.headers["traceparent"] = tp.traceparent;
  config.headers["X-Correlation-Id"] = tp.correlationId;

  return config;
});

// Response interceptor: Capture and store CSRF token; redirect to /login on 401
apiClient.interceptors.response.use(
  (response) => {
    const csrfToken = response.headers["x-csrf-token"];
    if (csrfToken) {
      setGlobalCsrfToken(csrfToken);
    }
    // Remember the server-side trace id so error surfaces can show a reference.
    const tid = response.headers["x-trace-id"];
    if (tid) lastTraceId = tid;
    return response;
  },
  (error) => {
    const tid = error?.response?.headers?.["x-trace-id"];
    if (tid) lastTraceId = tid;
    if (error?.response?.status === 401) {
      const url = (error.config?.url ?? "") as string;
      // Only auth bootstrap / refresh failures should invalidate the local session.
      // Page/data 401s are handled by the caller so we do not accidentally log the
      // user out because of a transient or endpoint-specific failure.
      const shouldClearSession =
        url.includes("/api/auth/me") ||
        url.includes("/api/auth/refresh");

      if (shouldClearSession) {
        clearAllSessionKeys();
        if (window.location.pathname !== "/login") {
          window.location.href = "/login";
        }
      }
    }
    return Promise.reject(error);
  }
);

export async function unwrap<T>(request: Promise<{ data: ApiEnvelope<T> }>): Promise<T> {
  const response = await request;
  if (!response.data.success) throw new Error(response.data.message || "API request failed");
  return response.data.data;
}

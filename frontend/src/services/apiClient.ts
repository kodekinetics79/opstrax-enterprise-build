import axios from "axios";
import type { ApiEnvelope } from "@/types";
import { getGlobalCsrfToken, setGlobalCsrfToken } from "@/hooks/useCsrf";
import { createMockAdapter } from "@/services/devMockAdapter";

// ── DEV MOCK: set to false when the backend is running ──
export const DEV_MOCK_ENABLED = true;

export const API_BASE_URL =
  import.meta.env.VITE_API_BASE_URL ||
  import.meta.env.VITE_DOTNET_API_URL ||
  "http://localhost:8088";

// Optional realtime/integrations side-service. Only default to the local dev port
// when actually running on localhost — in production an unset VITE_NODE_EVENTS_URL
// resolves to "" (feature disabled) instead of hammering http://localhost:8090,
// which would otherwise spam failed requests and infinite EventSource retries.
const isLocalhost =
  typeof window !== "undefined" &&
  /^(localhost|127\.0\.0\.1|\[::1\])$/.test(window.location.hostname);

export const NODE_EVENTS_URL =
  import.meta.env.VITE_NODE_EVENTS_URL ||
  (isLocalhost ? "http://localhost:8090" : "");

export const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: { Accept: "application/json" },
  timeout: 30000,
  withCredentials: true,
});

// Install mock adapter so all requests return seed data (no backend needed)
if (DEV_MOCK_ENABLED) {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  apiClient.defaults.adapter = createMockAdapter((axios.defaults.adapter ?? undefined) as any);
}

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
  const session = localStorage.getItem("opstrax.session.v2") || localStorage.getItem("opstrax.session");
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
        setGlobalCsrfToken(inner.csrfToken);
      }
    } catch {
      localStorage.removeItem("opstrax.session");
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
        localStorage.removeItem("opstrax.session.v2");
        localStorage.removeItem("opstrax.session");
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

// ── Node.js microservice client ───────────────────────────────────────────────
// Used for modules served by the Node.js event/integration backend (NODE_EVENTS_URL).
// Auth is tenant-scoped via X-Opstrax-Tenant-Id header; no JWT check on this service.
export const nodeApiClient = axios.create({
  baseURL: NODE_EVENTS_URL,
  headers: { Accept: "application/json" },
  timeout: 15000,
});

if (DEV_MOCK_ENABLED) {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  nodeApiClient.defaults.adapter = createMockAdapter((axios.defaults.adapter ?? undefined) as any);
}

nodeApiClient.interceptors.request.use((config) => {
  const session = localStorage.getItem("opstrax.session.v2") || localStorage.getItem("opstrax.session");
  if (session) {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const p = JSON.parse(session) as any;
      const inner = p.session ?? p;
      if (inner.token) {
        config.headers.Authorization = `Bearer ${inner.token}`;
      }
      const tid = inner?.company?.id ?? inner?.company?.companyId ?? inner?.user?.companyId ?? inner?.user?.company_id;
      if (tid) config.headers["X-Opstrax-Tenant-Id"] = String(tid);
    } catch { /* ignore */ }
  }
  return config;
});

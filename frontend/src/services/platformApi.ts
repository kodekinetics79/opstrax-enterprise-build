import axios from "axios";
import type { ApiEnvelope, AnyRecord } from "@/types";
import { API_BASE_URL } from "@/services/apiClient";

// Platform Admin uses a SEPARATE session store and axios instance from the tenant
// app, so platform staff identity never mixes with tenant user sessions.
const PLATFORM_STORAGE_KEY = "opstrax.platform.session.v1";

export type PlatformSession = {
  token: string;
  admin: { id: number; email: string; name: string };
  role: { key: string; name: string };
  permissions: string[];
};

export function loadPlatformSession(): PlatformSession | null {
  try {
    const raw = localStorage.getItem(PLATFORM_STORAGE_KEY);
    if (!raw) return null;
    return JSON.parse(raw) as PlatformSession;
  } catch {
    localStorage.removeItem(PLATFORM_STORAGE_KEY);
    return null;
  }
}

export function storePlatformSession(session: PlatformSession | null) {
  if (session) localStorage.setItem(PLATFORM_STORAGE_KEY, JSON.stringify(session));
  else localStorage.removeItem(PLATFORM_STORAGE_KEY);
}

export const platformClient = axios.create({
  baseURL: API_BASE_URL,
  headers: { Accept: "application/json" },
  timeout: 30000,
  withCredentials: true,
});

// The API protects state-changing requests with a double-submit CSRF token: a
// __CSRF_Token__ cookie (sent automatically via withCredentials) that must match
// an X-CSRF-Token header. The server echoes the current token on every response,
// so we capture it and replay it on the next mutation.
let platformCsrfToken = "";

platformClient.interceptors.request.use((config) => {
  const session = loadPlatformSession();
  if (session?.token) config.headers.Authorization = `Bearer ${session.token}`;
  if (platformCsrfToken && ["post", "put", "delete", "patch"].includes((config.method ?? "").toLowerCase())) {
    config.headers["X-CSRF-Token"] = platformCsrfToken;
  }
  return config;
});

platformClient.interceptors.response.use(
  (response) => {
    const token = response.headers["x-csrf-token"];
    if (token) platformCsrfToken = token;
    return response;
  },
  (error) => {
    const token = error?.response?.headers?.["x-csrf-token"];
    if (token) platformCsrfToken = token;
    if (error?.response?.status === 401) {
      storePlatformSession(null);
      if (!window.location.pathname.startsWith("/platform/login")) {
        window.location.href = "/platform/login";
      }
    }
    return Promise.reject(error);
  },
);

async function unwrap<T>(request: Promise<{ data: ApiEnvelope<T> }>): Promise<T> {
  const response = await request;
  if (!response.data.success) throw new Error(response.data.message || "Request failed");
  return response.data.data;
}

export function hasPlatformPermission(perms: string[], required: string): boolean {
  return perms.some((p) => {
    if (p === "platform:*") return true;
    if (p === required) return true;
    if (p.endsWith(":*")) return required.startsWith(p.slice(0, -1));
    return false;
  });
}

export function formatMoney(cents: number | undefined | null, currency = "USD"): string {
  const value = (Number(cents) || 0) / 100;
  return new Intl.NumberFormat("en-US", { style: "currency", currency, maximumFractionDigits: 0 }).format(value);
}

export const platformApi = {
  // Auth
  login: (email: string, password: string) =>
    unwrap<PlatformSession>(platformClient.post("/api/platform/auth/login", { email, password })),
  me: () => unwrap<PlatformSession>(platformClient.get("/api/platform/auth/me")),
  logout: () => platformClient.post("/api/platform/auth/logout").catch(() => undefined),

  // Command Center
  commandCenter: () => unwrap<AnyRecord>(platformClient.get("/api/platform/command-center/summary")),

  // Tenants
  tenants: () => unwrap<AnyRecord[]>(platformClient.get("/api/platform/tenants")),
  tenant: (id: number) => unwrap<AnyRecord>(platformClient.get(`/api/platform/tenants/${id}`)),
  createTenant: (body: AnyRecord) => unwrap<AnyRecord>(platformClient.post("/api/platform/tenants", body)),
  updateTenant: (id: number, body: AnyRecord) => unwrap<AnyRecord>(platformClient.put(`/api/platform/tenants/${id}`, body)),
  tenantStatus: (id: number, body: AnyRecord) => unwrap<AnyRecord>(platformClient.post(`/api/platform/tenants/${id}/status`, body)),
  assignPackage: (id: number, body: AnyRecord) => unwrap<AnyRecord>(platformClient.post(`/api/platform/tenants/${id}/assign-package`, body)),
  resetInvite: (id: number, body: AnyRecord) => unwrap<AnyRecord>(platformClient.post(`/api/platform/tenants/${id}/reset-admin-invite`, body)),

  // Entitlements
  entitlements: (id: number) => unwrap<AnyRecord[]>(platformClient.get(`/api/platform/tenants/${id}/entitlements`)),
  setEntitlement: (id: number, body: AnyRecord) => unwrap<AnyRecord>(platformClient.put(`/api/platform/tenants/${id}/entitlements`, body)),

  // Packages
  packages: () => unwrap<AnyRecord[]>(platformClient.get("/api/platform/packages")),
  createPackage: (body: AnyRecord) => unwrap<AnyRecord>(platformClient.post("/api/platform/packages", body)),
  updatePackage: (id: number, body: AnyRecord) => unwrap<AnyRecord>(platformClient.put(`/api/platform/packages/${id}`, body)),

  // Billing
  invoices: () => unwrap<AnyRecord[]>(platformClient.get("/api/platform/invoices")),
  createInvoice: (body: AnyRecord) => unwrap<AnyRecord>(platformClient.post("/api/platform/invoices", body)),
  markPaid: (id: number) => unwrap<AnyRecord>(platformClient.post(`/api/platform/invoices/${id}/mark-paid`)),

  // Customer success + audit + roles
  health: () => unwrap<AnyRecord[]>(platformClient.get("/api/platform/health")),
  audit: () => unwrap<AnyRecord[]>(platformClient.get("/api/platform/audit")),
  roles: () => unwrap<AnyRecord[]>(platformClient.get("/api/platform/roles")),

  // Opstrax revenue foundation
  modulePackages: () => unwrap<AnyRecord>(platformClient.get("/api/platform/opstrax/module-packages")),
  meters: () => unwrap<AnyRecord>(platformClient.get("/api/platform/opstrax/meters")),
  tenantUsage: (id: number) => unwrap<AnyRecord>(platformClient.get(`/api/platform/opstrax/tenants/${id}/usage`)),
  invoicePreview: (id: number) => unwrap<AnyRecord>(platformClient.get(`/api/platform/opstrax/tenants/${id}/invoice-preview`)),
  setOverride: (id: number, body: AnyRecord) => unwrap<AnyRecord>(platformClient.put(`/api/platform/opstrax/tenants/${id}/overrides`, body)),
};

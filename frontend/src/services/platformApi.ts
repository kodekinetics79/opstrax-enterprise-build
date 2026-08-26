import axios from "axios";
import type { ApiEnvelope, AnyRecord } from "@/types";
import { API_BASE_URL } from "@/services/apiClient";

// Platform Admin uses a SEPARATE session store and axios instance from the tenant
// app, so platform staff identity never mixes with tenant user sessions.
export const PLATFORM_STORAGE_KEY = "opstrax.platform.session.v1";

export type PlatformSession = {
  token: string;
  admin: { id: number; email: string; name: string };
  role: { key: string; name: string };
  permissions: string[];
  productPilotAvailable?: boolean;
};

export type PlatformSessionProfile = Omit<PlatformSession, "token">;

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
      // accept-invite is pre-session: a wrong/expired token 401s and the page
      // itself must show the error rather than bouncing to login.
      const isPreSessionPage =
        window.location.pathname.startsWith("/platform/login") ||
        window.location.pathname.startsWith("/platform/accept-invite");
      storePlatformSession(null);
      if (!isPreSessionPage) {
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

// Currencies whose ISO 4217 exponent is not 2. Amounts are stored in the minor
// unit, so JPY 5000 is ¥5,000 (not ¥50.00) and KWD 5000 is 5.000 KWD.
const MINOR_UNIT_OVERRIDES: Record<string, number> = {
  JPY: 0, KRW: 0, CLP: 0, ISK: 0, VND: 0, XOF: 0, XAF: 0,
  KWD: 3, BHD: 3, OMR: 3, JOD: 3, TND: 3, IQD: 3, LYD: 3,
};

export function minorUnits(currency?: string | null): number {
  return MINOR_UNIT_OVERRIDES[String(currency ?? "USD").toUpperCase()] ?? 2;
}

// Exact money for invoice surfaces — an invoice that rounds its own lines to
// whole units cannot be reconciled against what the customer was actually charged.
export function formatAmount(cents: number | undefined | null, currency = "USD"): string {
  const digits = minorUnits(currency);
  const value = (Number(cents) || 0) / 10 ** digits;
  try {
    return new Intl.NumberFormat("en-US", {
      style: "currency", currency,
      minimumFractionDigits: digits, maximumFractionDigits: digits,
    }).format(value);
  } catch {
    // Unknown ISO code — still show the number rather than throwing away the line.
    return `${value.toFixed(digits)} ${currency}`;
  }
}

export function formatRate(rate: number | undefined | null): string {
  const n = Number(rate) || 0;
  return n === 0 ? "0%" : `${(n * 100).toFixed(n * 100 % 1 === 0 ? 0 : 2)}%`;
}

export const platformApi = {
  // Auth
  login: (email: string, password: string, mfaCode?: string) =>
    unwrap<PlatformSession>(platformClient.post("/api/platform/auth/login", { email, password, mfaCode })),
  // Revalidation intentionally does not echo the bearer credential in a response
  // body. The auth provider preserves the locally held token after this succeeds.
  me: () => unwrap<PlatformSessionProfile>(platformClient.get("/api/platform/auth/me")),
  logout: () => platformClient.post("/api/platform/auth/logout").catch(() => undefined),

  // Self-service account management (any platform admin, own record only)
  changeOwnPassword: (currentPassword: string, newPassword: string) =>
    unwrap<AnyRecord>(platformClient.post("/api/platform/auth/change-password", { currentPassword, newPassword })),
  updateOwnProfile: (body: { fullName?: string; email?: string }) =>
    unwrap<AnyRecord>(platformClient.patch("/api/platform/auth/profile", body)),

  // Platform settings — outbound email (SMTP) and public app URLs. The SMTP password is
  // write-only: the server never returns it, so `passwordSet` is what the form renders.
  emailSettings: () => unwrap<AnyRecord>(platformClient.get("/api/platform/settings/email")),
  saveEmailSettings: (body: {
    host?: string; port?: number; username?: string; password?: string;
    fromAddress?: string; fromName?: string; enableSsl?: boolean;
  }) => unwrap<AnyRecord>(platformClient.put("/api/platform/settings/email", body)),
  // Passing the form's current values tests them BEFORE saving; with only `to`,
  // the stored/live configuration is tested instead.
  sendTestEmail: (body: {
    to: string; host?: string; port?: number; username?: string; password?: string;
    fromAddress?: string; fromName?: string; enableSsl?: boolean;
  }) => unwrap<AnyRecord>(platformClient.post("/api/platform/settings/email/test", body)),
  appUrlSettings: () => unwrap<AnyRecord>(platformClient.get("/api/platform/settings/urls")),
  saveAppUrlSettings: (body: { tenantAppUrl?: string; platformAppUrl?: string }) =>
    unwrap<AnyRecord>(platformClient.put("/api/platform/settings/urls", body)),

  // Command Center
  commandCenter: () => unwrap<AnyRecord>(platformClient.get("/api/platform/command-center/summary")),
  commercialOps: () => unwrap<AnyRecord>(platformClient.get("/api/platform/commercial-ops/summary")),

  // Staging-only, fixed-tenant pilot readiness. The server does not map these
  // routes unless the dedicated staging configuration is enabled.
  productPilot: () => unwrap<AnyRecord>(platformClient.get("/api/platform/product-pilot")),
  enableProductPilotCrm: (body: { tenantCode: string; requestId: string; acknowledgeStagingOnly: boolean }) =>
    unwrap<AnyRecord>(platformClient.post("/api/platform/product-pilot/enable-crm", body)),

  // Tenants
  tenants: () => unwrap<AnyRecord[]>(platformClient.get("/api/platform/tenants")),
  tenant: (id: number) => unwrap<AnyRecord>(platformClient.get(`/api/platform/tenants/${id}`)),
  createTenant: (body: AnyRecord) => unwrap<AnyRecord>(platformClient.post("/api/platform/tenants", body)),
  updateTenant: (id: number, body: AnyRecord) => unwrap<AnyRecord>(platformClient.put(`/api/platform/tenants/${id}`, body)),
  tenantStatus: (id: number, body: AnyRecord) => unwrap<AnyRecord>(platformClient.post(`/api/platform/tenants/${id}/status`, body)),
  assignPackage: (id: number, body: AnyRecord) => unwrap<AnyRecord>(platformClient.post(`/api/platform/tenants/${id}/assign-package`, body)),
  resetInvite: (id: number, body: AnyRecord) => unwrap<AnyRecord>(platformClient.post(`/api/platform/tenants/${id}/reset-admin-invite`, body)),
  revokeSessions: (id: number) => unwrap<AnyRecord>(platformClient.post(`/api/platform/tenants/${id}/revoke-sessions`)),

  // Break-glass support access — bounded, reason-captured, dual-audited.
  supportAccess: () => unwrap<AnyRecord>(platformClient.get("/api/platform/support-access")),
  startSupportAccess: (id: number, body: { targetUserId: number; reason: string; minutes: number }) =>
    unwrap<AnyRecord>(platformClient.post(`/api/platform/tenants/${id}/impersonate`, body)),
  endSupportAccess: (grantId: number) =>
    unwrap<AnyRecord>(platformClient.post(`/api/platform/impersonation/${grantId}/end`)),
  deleteTenant: (id: number, confirm: string) => unwrap<AnyRecord>(platformClient.delete(`/api/platform/tenants/${id}`, { data: { confirm } })),
  bulkTenants: (body: AnyRecord) => unwrap<AnyRecord>(platformClient.post("/api/platform/tenants/bulk", body)),
  // Tenant user directory + platform-initiated password reset (returns a one-time password)
  tenantUsers: (id: number) => unwrap<AnyRecord>(platformClient.get(`/api/platform/tenants/${id}/users`)),
  createTenantUser: (id: number, body: { email: string; fullName: string; roleName: string }) =>
    unwrap<AnyRecord>(platformClient.post(`/api/platform/tenants/${id}/users`, body)),
  updateTenantUser: (id: number, userId: number, body: AnyRecord) =>
    unwrap<AnyRecord>(platformClient.put(`/api/platform/tenants/${id}/users/${userId}`, body)),
  resendTenantUserInvite: (id: number, userId: number) =>
    unwrap<AnyRecord>(platformClient.post(`/api/platform/tenants/${id}/users/${userId}/resend-invite`)),
  resetTenantUserPassword: (id: number, userId: number) =>
    unwrap<AnyRecord>(platformClient.post(`/api/platform/tenants/${id}/users/${userId}/reset-password`)),
  captureTenantControlSnapshot: (id: number) =>
    unwrap<AnyRecord>(platformClient.post(`/api/platform/tenants/${id}/control-snapshot`)),

  // Entitlements
  entitlements: (id: number) => unwrap<AnyRecord[]>(platformClient.get(`/api/platform/tenants/${id}/entitlements`)),
  setEntitlement: (id: number, body: AnyRecord) => unwrap<AnyRecord>(platformClient.put(`/api/platform/tenants/${id}/entitlements`, body)),
  setEntitlementPolicy: (id: number, policyMode: "legacy_allow" | "package_allowlist") =>
    unwrap<AnyRecord>(platformClient.put(`/api/platform/tenants/${id}/entitlement-policy`, { policyMode })),

  // Country profiles (market/localization defaults driving tenant-creation cascade)
  countryProfiles: () => unwrap<AnyRecord[]>(platformClient.get("/api/platform/country-profiles")),
  countryProfile: (code: string) => unwrap<AnyRecord>(platformClient.get(`/api/platform/country-profiles/${code}`)),
  upsertCountryProfile: (body: AnyRecord) => unwrap<AnyRecord>(platformClient.post("/api/platform/country-profiles", body)),

  // Packages
  packages: () => unwrap<AnyRecord[]>(platformClient.get("/api/platform/packages")),
  createPackage: (body: AnyRecord) => unwrap<AnyRecord>(platformClient.post("/api/platform/packages", body)),
  updatePackage: (id: number, body: AnyRecord) => unwrap<AnyRecord>(platformClient.put(`/api/platform/packages/${id}`, body)),
  deletePackage: (id: number) => unwrap<AnyRecord>(platformClient.delete(`/api/platform/packages/${id}`)),

  // Billing
  invoices: () => unwrap<AnyRecord[]>(platformClient.get("/api/platform/invoices")),
  createInvoice: (body: AnyRecord) => unwrap<AnyRecord>(platformClient.post("/api/platform/invoices", body)),
  markPaid: (id: number) => unwrap<AnyRecord>(platformClient.post(`/api/platform/invoices/${id}/mark-paid`)),
  bulkInvoices: (body: AnyRecord) => unwrap<AnyRecord>(platformClient.post("/api/platform/invoices/bulk", body)),

  // Customer success + audit + roles
  health: () => unwrap<AnyRecord[]>(platformClient.get("/api/platform/health")),
  audit: (params?: AnyRecord) => unwrap<AnyRecord>(platformClient.get("/api/platform/audit", { params })),
  // Returns the raw CSV body so the caller can hand the browser a download.
  auditExportCsv: (params?: AnyRecord) =>
    platformClient.get("/api/platform/audit/export.csv", { params, responseType: "blob" }).then((r) => r.data as Blob),
  roles: () => unwrap<AnyRecord[]>(platformClient.get("/api/platform/roles")),

  // Platform operators (admin self-management — see PlatformAdminEndpoints.cs)
  platformAdmins: () => unwrap<AnyRecord[]>(platformClient.get("/api/platform/admins")),
  createPlatformAdmin: (body: { email: string; fullName: string; roleKey: string }) =>
    unwrap<AnyRecord>(platformClient.post("/api/platform/admins/invite", body)),
  setPlatformAdminRole: (id: number, roleKey: string) =>
    unwrap<AnyRecord>(platformClient.patch(`/api/platform/admins/${id}`, { roleKey })),
  setPlatformAdminStatus: (id: number, status: "Active" | "Disabled") =>
    unwrap<AnyRecord>(platformClient.post(`/api/platform/admins/${id}/${status === "Disabled" ? "disable" : "enable"}`)),
  revokePlatformAdminSessions: (id: number) =>
    unwrap<AnyRecord>(platformClient.post(`/api/platform/admins/${id}/revoke-sessions`)),
  bulkAdmins: (body: AnyRecord) => unwrap<AnyRecord>(platformClient.post("/api/platform/admins/bulk", body)),
  resetPlatformAdminInvite: (id: number) =>
    unwrap<AnyRecord>(platformClient.post(`/api/platform/admins/${id}/reset-invite`)),
  acceptPlatformInvite: (body: { email: string; token: string; password: string }) =>
    unwrap<AnyRecord>(platformClient.post("/api/platform/auth/accept-invite", body)),

  // MFA (TOTP)
  mfaEnroll: () => unwrap<{ secret: string; otpauthUri: string }>(platformClient.post("/api/platform/auth/mfa/enroll")),
  mfaVerify: (code: string) => unwrap<AnyRecord>(platformClient.post("/api/platform/auth/mfa/verify", { code })),
  resetPlatformAdminMfa: (id: number) =>
    unwrap<AnyRecord>(platformClient.post(`/api/platform/admins/${id}/mfa/reset`)),

  // Reliability Center — real system health, SLOs, error budget, incidents.
  reliability: () => unwrap<AnyRecord>(platformClient.get("/api/platform/reliability")),
  reliabilitySlo: () => unwrap<AnyRecord>(platformClient.get("/api/platform/reliability/slo")),
  ackIncident: (id: number) =>
    unwrap<AnyRecord>(platformClient.post(`/api/platform/reliability/incidents/${id}/ack`)),
  resolveIncident: (id: number, body: { rootCause?: string; actionsTaken?: string }) =>
    unwrap<AnyRecord>(platformClient.post(`/api/platform/reliability/incidents/${id}/resolve`, body)),

  // Opstrax revenue foundation
  modulePackages: () => unwrap<AnyRecord>(platformClient.get("/api/platform/opstrax/module-packages")),
  meters: () => unwrap<AnyRecord>(platformClient.get("/api/platform/opstrax/meters")),
  tenantUsage: (id: number) => unwrap<AnyRecord>(platformClient.get(`/api/platform/opstrax/tenants/${id}/usage`)),
  invoicePreview: (id: number) => unwrap<AnyRecord>(platformClient.get(`/api/platform/opstrax/tenants/${id}/invoice-preview`)),
  setOverride: (id: number, body: AnyRecord) => unwrap<AnyRecord>(platformClient.put(`/api/platform/opstrax/tenants/${id}/overrides`, body)),

  // Market packs
  marketPacks: () => unwrap<AnyRecord>(platformClient.get("/api/platform/opstrax/market-packs")),
  tenantMarketPacks: (id: number) => unwrap<AnyRecord>(platformClient.get(`/api/platform/opstrax/tenants/${id}/market-packs`)),
  setTenantMarketPack: (id: number, body: AnyRecord) => unwrap<AnyRecord>(platformClient.put(`/api/platform/opstrax/tenants/${id}/market-packs`, body)),
  complianceUsage: (id: number) => unwrap<AnyRecord>(platformClient.get(`/api/platform/opstrax/tenants/${id}/compliance-usage`)),
  deleteOverride: (id: number, meterKey: string) =>
    unwrap<AnyRecord>(platformClient.delete(`/api/platform/opstrax/tenants/${id}/overrides/${encodeURIComponent(meterKey)}`)),

  // Itemized invoicing — document lifecycle
  invoice: (id: number) => unwrap<AnyRecord>(platformClient.get(`/api/platform/invoices/${id}`)),
  previewInvoice: (body: AnyRecord) => unwrap<AnyRecord>(platformClient.post("/api/platform/invoices/preview", body)),
  generateInvoice: (body: AnyRecord) => unwrap<AnyRecord>(platformClient.post("/api/platform/invoices/generate", body)),
  replaceInvoiceLines: (id: number, lines: AnyRecord[]) =>
    unwrap<AnyRecord>(platformClient.put(`/api/platform/invoices/${id}/lines`, { lines })),
  issueInvoice: (id: number) => unwrap<AnyRecord>(platformClient.post(`/api/platform/invoices/${id}/issue`)),
  voidInvoice: (id: number, reason: string) =>
    unwrap<AnyRecord>(platformClient.post(`/api/platform/invoices/${id}/void`, { reason })),
  creditNote: (id: number, reason: string) =>
    unwrap<AnyRecord>(platformClient.post(`/api/platform/invoices/${id}/credit-note`, { reason })),

  // Per-tenant commercial terms
  billingPlan: (id: number) => unwrap<AnyRecord>(platformClient.get(`/api/platform/tenants/${id}/billing-plan`)),
  setBillingPlanItem: (id: number, body: AnyRecord) =>
    unwrap<AnyRecord>(platformClient.put(`/api/platform/tenants/${id}/billing-plan`, body)),
  deleteBillingPlanItem: (id: number, featureKey: string) =>
    unwrap<AnyRecord>(platformClient.delete(`/api/platform/tenants/${id}/billing-plan/${encodeURIComponent(featureKey)}`)),
  tenantTaxContext: (id: number) => unwrap<AnyRecord>(platformClient.get(`/api/platform/tenants/${id}/tax-context`)),

  // Tax configuration
  taxRegistrations: () => unwrap<AnyRecord[]>(platformClient.get("/api/platform/tax/registrations")),
  setTaxRegistration: (country: string, body: AnyRecord) =>
    unwrap<AnyRecord>(platformClient.put(`/api/platform/tax/registrations/${country}`, body)),
  taxRules: () => unwrap<AnyRecord[]>(platformClient.get("/api/platform/tax/rules")),
  setTaxRule: (body: AnyRecord) => unwrap<AnyRecord>(platformClient.put("/api/platform/tax/rules", body)),
  deleteTaxRule: (ruleKey: string) =>
    unwrap<AnyRecord>(platformClient.delete(`/api/platform/tax/rules/${encodeURIComponent(ruleKey)}`)),

  // Billing readiness + batch run
  billingReadiness: () => unwrap<AnyRecord>(platformClient.get("/api/platform/billing/readiness")),
  generateInvoiceBatch: (body: AnyRecord) => unwrap<AnyRecord>(platformClient.post("/api/platform/invoices/generate-batch", body)),

  // Revenue cockpit
  revenueSummary: () => unwrap<AnyRecord>(platformClient.get("/api/platform/revenue/summary")),
};

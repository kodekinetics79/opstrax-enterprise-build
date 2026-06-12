import axios from 'axios';

const platform = axios.create({ baseURL: '' });

platform.interceptors.request.use(cfg => {
  const token = typeof window !== 'undefined' ? localStorage.getItem('platform_access_token') : null;
  if (token) cfg.headers.Authorization = `Bearer ${token}`;
  return cfg;
});

export default platform;

// ── Types ─────────────────────────────────────────────────────────────────────

export interface PlatformStats {
  totalTenants: number;
  activeTenants: number;
  totalUsers: number;
  totalEmployees: number;
  tenantsByPlan: {
    trial: number;
    starter: number;
    growth: number;
    enterprise: number;
  };
}

export interface CreateTenantBody {
  name: string;
  slug: string;
  adminEmail: string;
  adminFullName?: string;
  adminPassword: string;
  plan?: string;
  maxUsers?: number | null;
  maxEmployees?: number | null;
  billingEmail?: string;
  billingCycle?: string;
  monthlyAmount?: number;
  currencyCode?: string;
  expiresAtUtc?: string | null;
}

export interface CreateTenantResult {
  tenantId: string;
  name: string;
  slug: string;
  adminUserId: string;
  adminEmail: string;
  plan: string;
  loginHint: string;
}

export interface TenantAdminUser {
  id: string;
  email: string;
  fullName: string;
  isActive: boolean;
  status: string;
  createdAtUtc: string;
}

export interface TenantUser {
  id: string;
  email: string;
  fullName: string;
  isActive: boolean;
  status: string;
  createdAtUtc: string;
  roles: string[];
}

export interface PlatformTenantSummary {
  id: string;
  name: string;
  slug: string;
  isActive: boolean;
  subscription: {
    plan: string;
    status: string;
    maxEmployees: number;
    maxUsers: number;
    expiresAtUtc: string | null;
  };
  activeUserCount: number;
  activeEmployeeCount: number;
}

export interface PlatformTenantDetail {
  id: string;
  name: string;
  slug: string;
  isActive: boolean;
  subscription: {
    plan: string;
    status: string;
    maxEmployees: number;
    maxUsers: number;
    billingEmail: string;
    billingCycle: string;
    monthlyAmount: number;
    currencyCode: string;
    startedAtUtc: string;
    expiresAtUtc: string | null;
  };
  featureFlags: Array<{ featureKey: string; isEnabled: boolean }>;
  localization: Record<string, unknown> | null;
  branding: Record<string, unknown> | null;
  userCount: number;
  employeeCount: number;
}

export interface PlatformAuditLog {
  id: string;
  tenantId: string;
  entityType: string;
  entityId: string;
  action: string;
  oldValuesJson: string;
  newValuesJson: string;
  performedByName: string;
  ipAddress: string;
  createdAtUtc: string;
}

export interface PasswordResetResult {
  userId: string;
  userEmail: string;
  message: string;
  emailDeliveryAvailable: boolean;
  emailSent: boolean;
  resetTokenExpiresAt?: string;
}

export interface PlatformPlan {
  name: string;
  maxUsers: number;
  maxEmployees: number;
  monthlyPrice: number;
  description: string;
}

export interface TenantInvoice {
  id: string;
  tenantId: string;
  invoiceNumber: string;
  amount: number;
  currencyCode: string;
  status: 'Draft' | 'Sent' | 'Paid' | 'Overdue' | 'Cancelled';
  paymentMethod: string | null;
  paymentReference: string | null;
  periodDescription: string | null;
  invoiceDate: string;
  dueDate: string;
  paidDate: string | null;
  notes: string | null;
  createdAtUtc: string;
}

export interface TenantAiUsage {
  tenantId: string;
  plan: string;
  yearMonth: number;
  tokensUsed: number;
  requestCount: number;
  blockedCount: number;
  monthlyTokenLimit: number;
  isUnlimited: boolean;
  usagePct: number;
}

export interface SupportSession {
  id: string;
  tenantId: string;
  targetUserId: string;
  targetUserEmail: string;
  reason: string;
  startedByEmail: string;
  startedByIp: string;
  startedAtUtc: string;
  expiresAtUtc: string;
  endedAtUtc: string | null;
  isActive: boolean;
}

export interface StartSupportAccessResult {
  sessionId: string;
  token: string;
  expiresAt: string;
  targetUserEmail: string;
  tenantSlug: string;
  reason: string;
}

// ── Platform API ──────────────────────────────────────────────────────────────

export const platformApi = {
  login: (email: string, password: string) =>
    platform.post<{ token: string }>('/api/platform/auth/login', { email, password }).then(r => r.data),

  getStats: () =>
    platform.get<PlatformStats>('/api/platform/stats').then(r => r.data),

  listTenants: () =>
    platform.get<PlatformTenantSummary[]>('/api/platform/tenants').then(r => r.data),

  getTenant: (tenantId: string) =>
    platform.get<PlatformTenantDetail>(`/api/platform/tenants/${tenantId}`).then(r => r.data),

  updateTenant: (tenantId: string, name: string) =>
    platform.patch<{ id: string; name: string; slug: string }>(`/api/platform/tenants/${tenantId}`, { name }).then(r => r.data),

  updateSubscription: (tenantId: string, body: {
    plan: string;
    status: string;
    maxUsers: number;
    maxEmployees: number;
    billingEmail: string;
    billingCycle: string;
    monthlyAmount: number;
    currencyCode: string;
    startedAtUtc: string;
    expiresAtUtc: string | null;
  }) =>
    platform.put(`/api/platform/tenants/${tenantId}/subscription`, body).then(r => r.data),

  suspendTenant: (tenantId: string, reason: string) =>
    platform.post(`/api/platform/tenants/${tenantId}/suspend`, { reason }).then(r => r.data),

  reactivateTenant: (tenantId: string, reason: string) =>
    platform.post(`/api/platform/tenants/${tenantId}/reactivate`, { reason }).then(r => r.data),

  setFeature: (tenantId: string, featureKey: string, isEnabled: boolean) =>
    platform.put(`/api/platform/tenants/${tenantId}/features/${featureKey}`, { isEnabled }).then(r => r.data),

  impersonate: (tenantId: string, userId: string) =>
    platform.post<{ token: string }>(`/api/platform/tenants/${tenantId}/impersonate`, { userId }).then(r => r.data),

  createTenant: (body: CreateTenantBody) =>
    platform.post<CreateTenantResult>('/api/platform/tenants', body).then(r => r.data),

  listAdmins: (tenantId: string) =>
    platform.get<TenantAdminUser[]>(`/api/platform/tenants/${tenantId}/admins`).then(r => r.data),

  addAdmin: (tenantId: string, body: { email: string; fullName?: string; password: string }) =>
    platform.post<TenantAdminUser>(`/api/platform/tenants/${tenantId}/admins`, body).then(r => r.data),

  listTenantUsers: (tenantId: string, search?: string) =>
    platform.get<TenantUser[]>(`/api/platform/tenants/${tenantId}/users`, { params: search ? { search } : {} }).then(r => r.data),

  sendPasswordReset: (userId: string) =>
    platform.post<PasswordResetResult>(`/api/platform/users/${userId}/send-password-reset`).then(r => r.data),

  forcePasswordReset: (userId: string, tempPassword: string) =>
    platform.post(`/api/platform/users/${userId}/force-password-reset`, { tempPassword }).then(r => r.data),

  getAuditLogs: (tenantId?: string, page = 1, pageSize = 50) =>
    platform.get<{ total: number; page: number; pageSize: number; logs: PlatformAuditLog[] }>(
      '/api/platform/audit-logs',
      { params: { ...(tenantId ? { tenantId } : {}), page, pageSize } }
    ).then(r => r.data),

  getPlans: () =>
    platform.get<PlatformPlan[]>('/api/platform/plans').then(r => r.data),

  startSupportAccess: (tenantId: string, userId: string, reason: string) =>
    platform.post<StartSupportAccessResult>('/api/platform/support-access/start', { tenantId, userId, reason }).then(r => r.data),

  endSupportAccess: (sessionId: string) =>
    platform.post('/api/platform/support-access/end', { sessionId }).then(r => r.data),

  listSupportSessions: (tenantId?: string, activeOnly = false, page = 1, pageSize = 50) =>
    platform.get<{ total: number; page: number; pageSize: number; sessions: SupportSession[] }>(
      '/api/platform/support-access',
      { params: { ...(tenantId ? { tenantId } : {}), activeOnly, page, pageSize } }
    ).then(r => r.data),

  listInvoices: (tenantId: string) =>
    platform.get<TenantInvoice[]>(`/api/platform/tenants/${tenantId}/invoices`).then(r => r.data),

  createInvoice: (tenantId: string, body: {
    invoiceNumber: string;
    amount: number;
    currencyCode?: string;
    status?: string;
    paymentMethod?: string;
    paymentReference?: string;
    periodDescription?: string;
    invoiceDate: string;
    dueDate: string;
    paidDate?: string | null;
    notes?: string;
  }) =>
    platform.post<TenantInvoice>(`/api/platform/tenants/${tenantId}/invoices`, body).then(r => r.data),

  updateInvoice: (tenantId: string, invoiceId: string, body: {
    status?: string;
    paymentMethod?: string;
    paymentReference?: string;
    paidDate?: string | null;
    notes?: string;
  }) =>
    platform.put<TenantInvoice>(`/api/platform/tenants/${tenantId}/invoices/${invoiceId}`, body).then(r => r.data),

  deleteInvoice: (tenantId: string, invoiceId: string) =>
    platform.delete(`/api/platform/tenants/${tenantId}/invoices/${invoiceId}`).then(r => r.data),

  getTenantAiUsage: (tenantId: string, yearMonth?: number) =>
    platform.get<TenantAiUsage>(`/api/platform/tenants/${tenantId}/ai-usage`, { params: yearMonth ? { yearMonth } : {} }).then(r => r.data),
};

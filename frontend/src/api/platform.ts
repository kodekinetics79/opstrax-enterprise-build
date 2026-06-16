import axios from 'axios';

const platform = axios.create({ baseURL: '' });

platform.interceptors.request.use(cfg => {
  const token = typeof window !== 'undefined' ? localStorage.getItem('platform_access_token') : null;
  if (token) cfg.headers.Authorization = `Bearer ${token}`;
  return cfg;
});

// 401 → token expired → clear + redirect to login
platform.interceptors.response.use(
  res => res,
  err => {
    if (err.response?.status === 401 && typeof window !== 'undefined') {
      localStorage.removeItem('platform_access_token');
      if (!window.location.pathname.startsWith('/platform/login')) {
        window.location.replace('/platform/login');
      }
    }
    return Promise.reject(err);
  }
);

export default platform;

// ── Types ─────────────────────────────────────────────────────────────────────

export interface PlatformStats {
  totalTenants: number;
  activeTenants: number;
  totalUsers: number;
  totalEmployees: number;
  estimatedMrr: number;
  expiringCount: number;
  suspendedCount: number;
  overdueCount: number;
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
  createdAtUtc: string;
  subscription: {
    plan: string;
    status: string;
    maxEmployees: number;
    maxUsers: number;
    expiresAtUtc: string | null;
    monthlyAmount: number;
    currencyCode: string;
    billingEmail: string;
    billingCycle: string;
  } | null;
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
  tenantName?: string;
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
  recipientEmail: string | null;
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

export interface PlatformAnnouncement {
  id: string;
  title: string;
  body: string;
  targetPlan: string;
  status: 'Draft' | 'Published' | 'Archived';
  publishedAtUtc: string | null;
  expiresAtUtc: string | null;
  createdByEmail: string;
  createdAtUtc: string;
}

export interface PlatformLead {
  id: string;
  companyName: string;
  contactName: string;
  contactEmail: string;
  phone: string | null;
  message: string | null;
  status: 'New' | 'Contacted' | 'DemoScheduled' | 'Converted' | 'Lost';
  notes: string | null;
  assignedTo: string | null;
  source: string;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  convertedToTenantId: string | null;
}

export interface PlatformSettings {
  smtp: {
    host: string;
    port: number;
    username: string;
    fromEmail: string;
    fromName: string;
    useSsl: boolean;
    isConfigured: boolean;
  };
  trial: { durationDays: number };
  branding: { platformName: string; supportEmail: string };
}

export interface BillingSummary {
  totalMrr: number;
  totalArr: number;
  overdueTotalAmount: number;
  overdueCount: number;
  totalInvoices: number;
  paidThisMonth: number;
  sentThisMonth: number;
}

export interface TenantSecurityPosture {
  tenantId: string;
  tenantName: string;
  tenantSlug: string;
  hasMfaEnabled: boolean;
  hasSecurityPolicy: boolean;
  riskLevel: 'Low' | 'Medium' | 'High';
  lastSecurityEvent: string | null;
}

export interface PlatformTeamMember {
  id: string;
  email: string;
  fullName: string;
  role: string;
  isActive: boolean;
  lastLoginAtUtc: string | null;
  lastLoginIp: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
}

export interface PlatformHealthStatus {
  status: 'healthy' | 'degraded' | 'error';
  components: {
    database: { status: 'ok' | 'error' | 'unknown' };
    smtp:     { status: 'configured' | 'not_configured' | 'unknown' };
    redis:    { status: 'ok' | 'error' | 'unknown' | 'not_configured' | 'disconnected' };
    jobs:     { status: 'ok' | 'error' | 'unknown' };
  };
  version: string;
  environment: string;
  checkedAtUtc: string;
}

export interface TenantSecurityPolicy {
  tenantId: string;
  tenantName: string;
  passwordMinLength: number;
  passwordRequireUppercase: boolean;
  passwordRequireLowercase: boolean;
  passwordRequireDigit: boolean;
  passwordRequireSpecial: boolean;
  passwordExpiryDays: number;
  passwordHistoryCount: number;
  maxFailedLoginAttempts: number;
  lockoutDurationMinutes: number;
  sessionTimeoutMinutes: number;
  refreshTokenExpiryDays: number;
  allowMultipleSessions: boolean;
  isCustomPolicy: boolean;
  updatedAtUtc?: string;
}

export const PLATFORM_ROLES = ['Owner', 'Admin', 'Finance', 'Support', 'Marketing', 'Auditor'] as const;
export type PlatformRole = typeof PLATFORM_ROLES[number];

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

  updatePlanPrice: (planName: string, monthlyPrice: number) =>
    platform.put<{ planName: string; monthlyPrice: number }>(`/api/platform/plans/${planName}/price`, { monthlyPrice }).then(r => r.data),

  downloadInvoicePdf: (tenantId: string, invoiceId: string, invoiceNumber: string) => {
    return platform.get(`/api/platform/tenants/${tenantId}/invoices/${invoiceId}/pdf`, { responseType: 'blob' })
      .then(r => {
        const url = window.URL.createObjectURL(new Blob([r.data], { type: 'application/pdf' }));
        const a = document.createElement('a');
        a.href = url;
        a.download = `Invoice_${invoiceNumber}.pdf`;
        a.click();
        window.URL.revokeObjectURL(url);
      });
  },

  sendInvoiceEmail: (tenantId: string, invoiceId: string) =>
    platform.post<{ sent: boolean; billingEmail: string; invoiceNumber: string; pdfAttached?: boolean; smtpRequired?: boolean; message?: string }>(`/api/platform/tenants/${tenantId}/invoices/${invoiceId}/send`).then(r => r.data),

  startSupportAccess: (tenantId: string, userId: string, reason: string) =>
    platform.post<StartSupportAccessResult>('/api/platform/support-access/start', { tenantId, userId, reason }).then(r => r.data),

  endSupportAccess: (sessionId: string) =>
    platform.post('/api/platform/support-access/end', { sessionId }).then(r => r.data),

  listSupportSessions: (tenantId?: string, activeOnly = false, page = 1, pageSize = 50) =>
    platform.get<{ total: number; page: number; pageSize: number; sessions: SupportSession[] }>(
      '/api/platform/support-access',
      { params: { ...(tenantId ? { tenantId } : {}), activeOnly, page, pageSize } }
    ).then(r => r.data),

  // ── Marketing ────────────────────────────────────────────────────────────────

  listAnnouncements: (status?: string) =>
    platform.get<PlatformAnnouncement[]>('/api/platform/marketing/announcements', { params: status ? { status } : {} }).then(r => r.data),

  createAnnouncement: (body: { title: string; body: string; targetPlan?: string; expiresAtUtc?: string | null }) =>
    platform.post<PlatformAnnouncement>('/api/platform/marketing/announcements', body).then(r => r.data),

  updateAnnouncement: (id: string, body: { title?: string; body?: string; status?: string; expiresAtUtc?: string | null }) =>
    platform.patch<PlatformAnnouncement>(`/api/platform/marketing/announcements/${id}`, body).then(r => r.data),

  deleteAnnouncement: (id: string) =>
    platform.delete(`/api/platform/marketing/announcements/${id}`).then(r => r.data),

  // ── Leads ─────────────────────────────────────────────────────────────────

  listLeads: (status?: string) =>
    platform.get<PlatformLead[]>('/api/platform/leads', { params: status ? { status } : {} }).then(r => r.data),

  createLead: (body: { companyName: string; contactName: string; contactEmail: string; phone?: string; message?: string; source?: string }) =>
    platform.post<PlatformLead>('/api/platform/leads', body).then(r => r.data),

  updateLead: (id: string, body: { status?: string; notes?: string; assignedTo?: string }) =>
    platform.patch<PlatformLead>(`/api/platform/leads/${id}`, body).then(r => r.data),

  convertLead: (id: string, tenantBody: { adminEmail: string; adminPassword: string; plan?: string }) =>
    platform.post<{ tenantId: string; tenantName: string }>(`/api/platform/leads/${id}/convert`, tenantBody).then(r => r.data),

  // ── Settings ──────────────────────────────────────────────────────────────

  getSettings: () =>
    platform.get<PlatformSettings>('/api/platform/settings').then(r => r.data),

  updateSmtpSettings: (body: { host: string; port: number; username: string; password?: string; fromEmail: string; fromName?: string; useSsl: boolean }) =>
    platform.put('/api/platform/settings/smtp', body).then(r => r.data),

  testSmtp: () =>
    platform.post<{ sent: boolean; message: string }>('/api/platform/settings/smtp/test').then(r => r.data),

  getVersion: () =>
    platform.get<{ version: string; environment: string; deployedAt?: string; migrations?: number }>('/api/platform/settings/version').then(r => r.data),

  // ── Billing Summary ───────────────────────────────────────────────────────

  getBillingSummary: () =>
    platform.get<BillingSummary>('/api/platform/billing/summary').then(r => r.data),

  listAllInvoices: (params?: { status?: string; page?: number; pageSize?: number }) =>
    platform.get<{ total: number; page: number; invoices: (TenantInvoice & { tenantName: string; tenantSlug: string })[] }>(
      '/api/platform/billing/invoices', { params }
    ).then(r => r.data),

  // ── Security Center ───────────────────────────────────────────────────────

  getSecuritySummary: () =>
    platform.get<TenantSecurityPosture[]>('/api/platform/security/summary').then(r => r.data),

  getTenantSecurityPolicy: (tenantId: string) =>
    platform.get<TenantSecurityPolicy>(`/api/platform/tenants/${tenantId}/security-policy`).then(r => r.data),

  updateTenantSecurityPolicy: (tenantId: string, body: Partial<TenantSecurityPolicy>) =>
    platform.put(`/api/platform/tenants/${tenantId}/security-policy`, body),

  getHealth: () =>
    platform.get<PlatformHealthStatus>('/api/platform/health').then(r => r.data),

  listTeam: () =>
    platform.get<PlatformTeamMember[]>('/api/platform/team').then(r => r.data),

  createTeamMember: (body: { email: string; fullName?: string; password: string; role?: string }) =>
    platform.post<PlatformTeamMember>('/api/platform/team', body).then(r => r.data),

  updateTeamMember: (id: string, body: { role?: string; isActive?: boolean; fullName?: string }) =>
    platform.patch<PlatformTeamMember>(`/api/platform/team/${id}`, body).then(r => r.data),

  deactivateTeamMember: (id: string) =>
    platform.patch(`/api/platform/team/${id}`, { isActive: false }).then(r => r.data),

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
    recipientEmail?: string;
  }) =>
    platform.post<TenantInvoice>(`/api/platform/tenants/${tenantId}/invoices`, body).then(r => r.data),

  updateInvoice: (tenantId: string, invoiceId: string, body: {
    status?: string;
    paymentMethod?: string;
    paymentReference?: string;
    paidDate?: string | null;
    notes?: string;
    recipientEmail?: string;
  }) =>
    platform.put<TenantInvoice>(`/api/platform/tenants/${tenantId}/invoices/${invoiceId}`, body).then(r => r.data),

  deleteInvoice: (tenantId: string, invoiceId: string) =>
    platform.delete(`/api/platform/tenants/${tenantId}/invoices/${invoiceId}`).then(r => r.data),

  getTenantAiUsage: (tenantId: string, yearMonth?: number) =>
    platform.get<TenantAiUsage>(`/api/platform/tenants/${tenantId}/ai-usage`, { params: yearMonth ? { yearMonth } : {} }).then(r => r.data),
};

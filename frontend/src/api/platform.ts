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
    starter: number;
    growth: number;
    enterprise: number;
  };
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
    expiresAtUtc: string | null;
  };
  activeUserCount: number;
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

  updateSubscription: (tenantId: string, body: {
    plan: string;
    status: string;
    maxEmployees: number;
    billingEmail: string;
    billingCycle: string;
    monthlyAmount: number;
    currencyCode: string;
    startedAtUtc: string;
    expiresAtUtc: string | null;
  }) =>
    platform.put(`/api/platform/tenants/${tenantId}/subscription`, body).then(r => r.data),

  setFeature: (tenantId: string, featureKey: string, isEnabled: boolean) =>
    platform.put(`/api/platform/tenants/${tenantId}/features/${featureKey}`, { isEnabled }).then(r => r.data),

  impersonate: (tenantId: string, userId: string) =>
    platform.post<{ token: string }>(`/api/platform/tenants/${tenantId}/impersonate`, { userId }).then(r => r.data),
};

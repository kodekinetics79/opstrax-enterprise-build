import client from './client';

export interface AuthUser {
  id: string;
  tenantId: string;
  tenantSlug: string;
  email: string;
  fullName: string;
  roles: string[];
  permissions: string[];
  employeeId?: number;
  accessMode?: string;
  requiresPasswordSetup?: boolean;
}

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAtUtc: string;
  user: AuthUser;
}

export const authApi = {
  login: (email: string, password: string, tenantSlug = 'zayra') =>
    client.post<AuthResponse>('/api/auth/login', { email, password, tenantSlug }).then((r) => r.data),

  logout: (refreshToken: string) =>
    client.post('/api/auth/logout', { refreshToken }),

  me: () => client.get<AuthUser>('/api/auth/me').then((r) => r.data),
};

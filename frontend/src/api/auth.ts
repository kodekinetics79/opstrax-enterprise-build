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

export interface ForgotPasswordResponse {
  message: string;
  resetToken?: string;
  resetTokenExpiresAtUtc?: string;
}

export const authApi = {
  login: (email: string, password: string, tenantSlug = '') =>
    client.post<AuthResponse>('/api/auth/login', { email, password, tenantSlug }).then((r) => r.data),

  logout: (refreshToken: string) =>
    client.post('/api/auth/logout', { refreshToken }),

  me: () => client.get<AuthUser>('/api/auth/me').then((r) => r.data),

  forgotPassword: (email: string, tenantSlug?: string) =>
    client.post<ForgotPasswordResponse>('/api/auth/forgot-password', { email, tenantSlug }).then((r) => r.data),

  resetPassword: (email: string, resetToken: string, newPassword: string, tenantSlug?: string) =>
    client.post('/api/auth/reset-password', { email, resetToken, newPassword, tenantSlug }),
};

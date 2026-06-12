'use client';

import { createContext, useCallback, useContext, useEffect, useState } from 'react';
import { authApi } from '../api/auth';
import type { AuthUser } from '../api/auth';

interface AuthContextValue {
  user: AuthUser | null;
  isLoading: boolean;
  login: (email: string, password: string, tenantSlug?: string) => Promise<void>;
  logout: () => Promise<void>;
  hasPermission: (permission: string) => boolean;
  hasRole: (role: string) => boolean;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    const token = localStorage.getItem('zayra_access_token');
    if (!token) {
      setIsLoading(false);
      return;
    }
    authApi
      .me()
      .then(setUser)
      .catch(() => {
        localStorage.removeItem('zayra_access_token');
        localStorage.removeItem('zayra_refresh_token');
      })
      .finally(() => setIsLoading(false));
  }, []);

  const login = useCallback(async (email: string, password: string, tenantSlug = '') => {
    const res = await authApi.login(email, password, tenantSlug);
    localStorage.setItem('zayra_access_token', res.accessToken);
    localStorage.setItem('zayra_refresh_token', res.refreshToken);
    setUser(res.user);
  }, []);

  const logout = useCallback(async () => {
    const refreshToken = localStorage.getItem('zayra_refresh_token') ?? '';
    try {
      await authApi.logout(refreshToken);
    } catch {
      // ignore
    }
    localStorage.removeItem('zayra_access_token');
    localStorage.removeItem('zayra_refresh_token');
    setUser(null);
  }, []);

  const hasPermission = useCallback(
    (permission: string) => user?.permissions.includes(permission) ?? false,
    [user],
  );

  const hasRole = useCallback(
    (role: string) => user?.roles.includes(role) ?? false,
    [user],
  );

  return (
    <AuthContext.Provider value={{ user, isLoading, login, logout, hasPermission, hasRole }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}

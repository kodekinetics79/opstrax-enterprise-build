import { apiClient, unwrap } from "./apiClient";
import type { AnyRecord } from "@/types";

type UserFilters = {
  search?: string;
  role?: string;
  status?: string;
  companyId?: string | number;
};

export type AdminUser = AnyRecord & {
  id: number;
  fullName: string;
  email: string;
  companyId: number;
  companyName: string;
  roleId?: number | null;
  roleName: string;
  status: string;
  permissionsJson?: string | null;
  lastLoginAt?: string;
  mfaStatus?: string;
};

export type AdminRole = AnyRecord & {
  id: number;
  name: string;
  permissions: string[];
  userCount: number;
  scope?: string;
};

export type AdminOverview = {
  totalUsers: number;
  activeUsers: number;
  tenantAdmins: number;
  roles: number;
  recentAuditEvents: number;
  permissionCoverage: number;
};

async function get<T>(path: string) {
  const response = await apiClient.get(path);
  return unwrap<T>(Promise.resolve(response));
}

async function post<T>(path: string, body?: Record<string, unknown>) {
  const response = await apiClient.post(path, body ?? {});
  return unwrap<T>(Promise.resolve(response));
}

async function put<T>(path: string, body?: Record<string, unknown>) {
  const response = await apiClient.put(path, body ?? {});
  return unwrap<T>(Promise.resolve(response));
}

async function del<T>(path: string) {
  const response = await apiClient.delete(path);
  return unwrap<T>(Promise.resolve(response));
}

export const adminApi = {
  overview: async (): Promise<AdminOverview> => get<AdminOverview>("/api/admin/overview"),
  users: async (filters?: UserFilters): Promise<AdminUser[]> => get<AdminUser[]>(
    `/api/admin/users${filters ? `?${new URLSearchParams(Object.entries(filters).reduce<Record<string, string>>((acc, [key, value]) => {
      if (value != null && String(value).trim() !== "") acc[key] = String(value);
      return acc;
    }, {})).toString()}` : ""}`
  ),
  user: async (id: number): Promise<AdminUser> => get<AdminUser>(`/api/admin/users/${id}`),
  createUser: async (body: Record<string, unknown>) => post<{ id: number }>("/api/admin/users", body),
  updateUser: async (id: number, body: Record<string, unknown>) => put<{ id: number }>(`/api/admin/users/${id}`, body),
  deleteUser: async (id: number) => del<{ id: number }>(`/api/admin/users/${id}`),
  roles: async (): Promise<AdminRole[]> => get<AdminRole[]>("/api/admin/roles"),
  updateRole: async (id: number, body: Record<string, unknown>) => put<{ id: number }>(`/api/admin/roles/${id}`, body),
  permissions: async (): Promise<string[]> => get<string[]>("/api/admin/permissions"),
  auditLog: async (body: Record<string, unknown>) => post<{ id?: number }>("/api/admin/audit-events", body),
};

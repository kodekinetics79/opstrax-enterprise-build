import axios from "axios";
import { apiClient, unwrap } from "./apiClient";
import type { AnyRecord } from "@/types";
import { developmentFleetSeedData } from "@/data/developmentFleetSeedData";

type UserFilters = {
  search?: string;
  role?: string;
  status?: string;
  companyId?: string | number;
};

async function withSafeFallback<T>(request: Promise<T>, fallback: () => T): Promise<T> {
  try {
    return await request;
  } catch (error) {
    if (axios.isAxiosError(error) && [401, 403].includes(error.response?.status ?? 0)) {
      throw error;
    }
    return fallback();
  }
}

function applyFilters<T extends Record<string, unknown>>(rows: T[], filters?: UserFilters) {
  return rows.filter((row) => {
    const search = filters?.search?.trim().toLowerCase();
    const role = filters?.role?.trim().toLowerCase();
    const status = filters?.status?.trim().toLowerCase();
    const companyId = filters?.companyId ? String(filters.companyId) : "";

    const searchable = [
      row.fullName,
      row.email,
      row.roleName,
      row.companyName,
      row.status,
    ].map((value) => String(value ?? "").toLowerCase()).join(" | ");

    if (search && !searchable.includes(search)) return false;
    if (role && String(row.roleName ?? "").toLowerCase() !== role) return false;
    if (status && String(row.status ?? "").toLowerCase() !== status) return false;
    if (companyId && String(row.companyId ?? "") !== companyId) return false;
    return true;
  });
}

const fallbackPermissions = [...developmentFleetSeedData.permissions];

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

const fallbackUsers: AdminUser[] = developmentFleetSeedData.adminUsers.map((user) => ({ ...user }));
const fallbackRoles: AdminRole[] = developmentFleetSeedData.adminRoles.map((role) => ({ ...role, permissions: [...role.permissions] }));

export const adminApi = {
  overview: async (): Promise<AdminOverview> => withSafeFallback(get<AdminOverview>("/api/admin/overview"), () => ({
      totalUsers: fallbackUsers.length,
      activeUsers: fallbackUsers.filter((user) => user.status === "Active").length,
      tenantAdmins: fallbackUsers.filter((user) => /admin/i.test(String(user.roleName))).length,
      roles: fallbackRoles.length,
      recentAuditEvents: developmentFleetSeedData.adminAuditLogs.length,
      permissionCoverage: fallbackPermissions.length,
    })),
  users: async (filters?: UserFilters): Promise<AdminUser[]> => withSafeFallback(get<AdminUser[]>(`/api/admin/users${filters ? `?${new URLSearchParams(Object.entries(filters).reduce<Record<string, string>>((acc, [key, value]) => {
      if (value != null && String(value).trim() !== "") acc[key] = String(value);
      return acc;
    }, {})).toString()}` : ""}`), () => applyFilters(fallbackUsers, filters) as AdminUser[]),
  user: async (id: number): Promise<AdminUser> => withSafeFallback(get<AdminUser>(`/api/admin/users/${id}`), () => {
      const user = fallbackUsers.find((row) => row.id === id);
      if (!user) throw new Error("User not found");
      return user as AdminUser;
    }),
  createUser: async (body: Record<string, unknown>) => withSafeFallback(post<{ id: number }>("/api/admin/users", body), () => {
      const nextId = Math.max(0, ...fallbackUsers.map((row) => row.id)) + 1;
      const roleId = Number(body.roleId ?? 0) || null;
      const roleName = String(body.roleName ?? body.role ?? "Tenant Admin");
      const companyId = Number(body.companyId ?? 1) || 1;
      const companyName = String(body.companyName ?? developmentFleetSeedData.tenants.find((tenant) => tenant.id === `tenant-${companyId}`)?.name ?? "Tenant");
      const record: AdminUser = {
        id: nextId,
        fullName: String(body.fullName ?? "New User"),
        email: String(body.email ?? `user${nextId}@example.com`),
        companyId,
        companyName,
        roleId,
        roleName,
        status: String(body.status ?? "Active"),
        permissionsJson: body.permissionsJson ? String(body.permissionsJson) : null,
        lastLoginAt: new Date().toISOString(),
        mfaStatus: String(body.mfaStatus ?? "Disabled"),
      };
      fallbackUsers.unshift(record);
      return { id: nextId };
    }),
  updateUser: async (id: number, body: Record<string, unknown>) => withSafeFallback(put<{ id: number }>(`/api/admin/users/${id}`, body), () => {
      const index = fallbackUsers.findIndex((row) => row.id === id);
      if (index >= 0) {
        fallbackUsers[index] = {
          ...fallbackUsers[index],
          fullName: String(body.fullName ?? fallbackUsers[index].fullName),
          email: String(body.email ?? fallbackUsers[index].email),
          roleId: body.roleId != null ? Number(body.roleId) : fallbackUsers[index].roleId,
          roleName: String(body.roleName ?? fallbackUsers[index].roleName),
          status: String(body.status ?? fallbackUsers[index].status),
          companyId: body.companyId != null ? Number(body.companyId) : fallbackUsers[index].companyId,
          companyName: String(body.companyName ?? fallbackUsers[index].companyName),
          permissionsJson: body.permissionsJson ? String(body.permissionsJson) : fallbackUsers[index].permissionsJson ?? null,
          mfaStatus: String(body.mfaStatus ?? fallbackUsers[index].mfaStatus ?? "Disabled"),
        };
      }
      return { id };
    }),
  deleteUser: async (id: number) => withSafeFallback(del<{ id: number }>(`/api/admin/users/${id}`), () => {
      const index = fallbackUsers.findIndex((row) => row.id === id);
      if (index >= 0) fallbackUsers[index] = { ...fallbackUsers[index], status: "Inactive" };
      return { id };
    }),
  roles: async (): Promise<AdminRole[]> => withSafeFallback(get<AdminRole[]>("/api/admin/roles"), () => fallbackRoles),
  updateRole: async (id: number, body: Record<string, unknown>) => withSafeFallback(put<{ id: number }>(`/api/admin/roles/${id}`, body), () => {
      const index = fallbackRoles.findIndex((row) => row.id === id);
      if (index >= 0) {
        fallbackRoles[index] = {
          ...fallbackRoles[index],
          name: String(body.name ?? fallbackRoles[index].name),
          permissions: Array.isArray(body.permissions) ? body.permissions.map(String) : fallbackRoles[index].permissions,
          scope: String(body.scope ?? fallbackRoles[index].scope ?? "Tenant"),
        };
      }
      return { id };
    }),
  permissions: async (): Promise<string[]> => withSafeFallback(get<string[]>("/api/admin/permissions"), () => fallbackPermissions),
  auditLog: async (body: Record<string, unknown>) => withSafeFallback(post<{ id?: number }>("/api/admin/audit-events", body), () => ({ id: Date.now() })),
};

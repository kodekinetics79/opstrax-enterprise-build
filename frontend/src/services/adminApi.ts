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

export type AccessReview = AnyRecord & {
  id: number;
  title: string;
  status: string;
  totalItems: number;
  itemsApproved: number;
  itemsRevoked: number;
  itemsPending: number;
  dueDate?: string | null;
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
  // password is optional on create — when omitted the user is created as
  // "Pending" and the response carries a one-time activation link to share.
  createUser: async (body: Record<string, unknown>) =>
    post<{ id: number; activationLink?: string; activationExpiresAt?: string }>("/api/admin/users", body),
  updateUser: async (id: number, body: Record<string, unknown>) => put<{ id: number }>(`/api/admin/users/${id}`, body),
  deleteUser: async (id: number) => del<{ id: number }>(`/api/admin/users/${id}`),
  activationLink: async (id: number) => post<{ link: string; expiresAt: string }>(`/api/admin/users/${id}/activation-link`),
  userSessions: async (id: number): Promise<AnyRecord[]> => get<AnyRecord[]>(`/api/admin/users/${id}/sessions`),
  revokeUserSessions: async (id: number) => del<{ id: number; sessionsRevoked: number }>(`/api/admin/users/${id}/sessions`),
  roles: async (): Promise<AdminRole[]> => get<AdminRole[]>("/api/admin/roles"),
  createRole: async (body: Record<string, unknown>) => post<{ id: number }>("/api/admin/roles", body),
  updateRole: async (id: number, body: Record<string, unknown>) => put<{ id: number }>(`/api/admin/roles/${id}`, body),
  permissions: async (): Promise<string[]> => get<string[]>("/api/admin/permissions"),
  accessReviews: async (): Promise<AccessReview[]> => get<AccessReview[]>("/api/security/access-reviews"),
  accessReview: async (id: number): Promise<AccessReview> => get<AccessReview>(`/api/security/access-reviews/${id}`),
  createAccessReview: async (body: Record<string, unknown>) => post<{ id: number }>("/api/security/access-reviews", body),
  decideAccessReviewItem: async (reviewId: number, itemId: number, decision: "approve" | "revoke", notes?: string) =>
    post<{ itemId: number; status: string }>(`/api/security/access-reviews/${reviewId}/items/${itemId}/${decision}`, { notes: notes ?? "" }),
  completeAccessReview: async (id: number) => post<{ id: number; status: string }>(`/api/security/access-reviews/${id}/complete`),
};

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { adminApi } from "@/services/adminApi";

export function useAdminOverview() {
  return useQuery({ queryKey: ["admin-overview"], queryFn: adminApi.overview, staleTime: 20_000 });
}

export function useAdminUsers(filters?: Record<string, string>) {
  return useQuery({
    queryKey: ["admin-users", filters],
    queryFn: () => adminApi.users(filters),
    staleTime: 10_000,
  });
}

export function useAdminUser(id: number | null) {
  return useQuery({
    queryKey: ["admin-user", id],
    queryFn: () => adminApi.user(Number(id)),
    enabled: Number.isFinite(Number(id)),
  });
}

export function useCreateAdminUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: Record<string, unknown>) => adminApi.createUser(body),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ["admin-users"] });
      await qc.invalidateQueries({ queryKey: ["admin-overview"] });
    },
  });
}

export function useUpdateAdminUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: number; body: Record<string, unknown> }) => adminApi.updateUser(id, body),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ["admin-users"] });
      await qc.invalidateQueries({ queryKey: ["admin-user"] });
      await qc.invalidateQueries({ queryKey: ["admin-overview"] });
    },
  });
}

export function useDeleteAdminUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => adminApi.deleteUser(id),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ["admin-users"] });
      await qc.invalidateQueries({ queryKey: ["admin-overview"] });
    },
  });
}

export function useAdminRoles() {
  return useQuery({ queryKey: ["admin-roles"], queryFn: adminApi.roles, staleTime: 20_000 });
}

export function useUpdateAdminRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: number; body: Record<string, unknown> }) => adminApi.updateRole(id, body),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ["admin-roles"] });
      await qc.invalidateQueries({ queryKey: ["admin-overview"] });
    },
  });
}

export function useAdminPermissions() {
  return useQuery({ queryKey: ["admin-permissions"], queryFn: adminApi.permissions, staleTime: 60_000 });
}

import { apiClient, unwrap } from "./apiClient";
import type { AnyRecord } from "@/types";

export const branchesApi = {
  list: async (): Promise<AnyRecord[]> => unwrap<AnyRecord[]>(apiClient.get("/api/branches")),
  create: async (body: Record<string, unknown>): Promise<{ id: number }> =>
    unwrap<{ id: number }>(apiClient.post("/api/branches", body)),
  update: async (id: number, body: Record<string, unknown>): Promise<{ id: number }> =>
    unwrap<{ id: number }>(apiClient.put(`/api/branches/${id}`, body)),
  archive: async (id: number): Promise<{ id: number }> =>
    unwrap<{ id: number }>(apiClient.delete(`/api/branches/${id}`)),
};

import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export const evidenceApi = {
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/evidence-packages/summary")),
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/evidence-packages")),
  detail: (id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/evidence-packages/${id}`)),
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/evidence-packages", payload)),
  update: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/evidence-packages/${id}`, payload)),
  remove: (id: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/evidence-packages/${id}`)),
  exportPlaceholder: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/evidence-packages/${id}/generate-export-placeholder`, {})),
  lock: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/evidence-packages/${id}/lock-package`, {})),
};

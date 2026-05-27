import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export const contractsApi = {
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/contracts/summary")),
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/contracts")),
  detail: (id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/contracts/${id}`)),
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/contracts", payload)),
  update: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/contracts/${id}`, payload)),
  remove: (id: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/contracts/${id}`)),
  rates: (contractId: string | number) => unwrap<AnyRecord[]>(apiClient.get(`/api/contracts/${contractId}/rates`)),
  createRate: (contractId: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post(`/api/contracts/${contractId}/rates`, payload)),
  updateRate: (contractId: string | number, rateId: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/contracts/${contractId}/rates/${rateId}`, payload)),
  activate: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/contracts/${id}/activate`, {})),
  expire: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/contracts/${id}/expire`, {})),
  recommendations: () => unwrap<AnyRecord[]>(apiClient.get("/api/contracts/recommendations")),
};

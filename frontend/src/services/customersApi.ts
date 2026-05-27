import { apiClient, unwrap } from "./apiClient";
import type { AnyRecord } from "@/types";

export const customersApi = {
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/customers")),
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/customers/summary")),
  detail: (id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/customers/${id}`)),
  timeline: (id: string | number) => unwrap<AnyRecord[]>(apiClient.get(`/api/customers/${id}/timeline`)),
  recommendations: (id: string | number) => unwrap<AnyRecord[]>(apiClient.get(`/api/customers/${id}/recommendations`)),
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/customers", payload)),
  update: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/customers/${id}`, payload)),
  remove: (id: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/customers/${id}`)),
};

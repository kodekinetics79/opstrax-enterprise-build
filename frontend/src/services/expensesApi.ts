import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export const expensesApi = {
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/expenses/summary")),
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/expenses")),
  detail: (id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/expenses/${id}`)),
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/expenses", payload)),
  update: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/expenses/${id}`, payload)),
  remove: (id: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/expenses/${id}`)),
  approve: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/expenses/${id}/approve`, {})),
  reject: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/expenses/${id}/reject`, {})),
  categories: () => unwrap<AnyRecord[]>(apiClient.get("/api/expenses/categories")),
  recommendations: () => unwrap<AnyRecord[]>(apiClient.get("/api/expenses/recommendations")),
};

import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";
import { getExpensesSummary, getExpensesList } from "@/data/developmentFleetSeedData";

async function withFallback<T>(req: Promise<T>, fb: () => T | Promise<T>): Promise<T> {
  try { return await req; } catch { return fb(); }
}

export const expensesApi = {
  summary: () => withFallback(unwrap<AnyRecord>(apiClient.get("/api/expenses/summary")), () => getExpensesSummary() as AnyRecord),
  list: () => withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/expenses")), () => getExpensesList() as AnyRecord[]),
  detail: (id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/expenses/${id}`)),
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/expenses", payload)),
  update: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/expenses/${id}`, payload)),
  remove: (id: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/expenses/${id}`)),
  approve: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/expenses/${id}/approve`, {})),
  reject: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/expenses/${id}/reject`, {})),
  categories: () => unwrap<AnyRecord[]>(apiClient.get("/api/expenses/categories")),
  recommendations: () => unwrap<AnyRecord[]>(apiClient.get("/api/expenses/recommendations")),
};

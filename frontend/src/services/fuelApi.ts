import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";
import { getFuelSummary, getFuelTransactions } from "@/data/developmentFleetSeedData";

async function withFallback<T>(req: Promise<T>, fb: () => T | Promise<T>): Promise<T> {
  try { return await req; } catch { return fb(); }
}

export const fuelApi = {
  summary: () => withFallback(unwrap<AnyRecord>(apiClient.get("/api/fuel/summary")), () => getFuelSummary() as AnyRecord),
  transactions: () => withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/fuel/transactions")), () => getFuelTransactions() as AnyRecord[]),
  transaction: (id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/fuel/transactions/${id}`)),
  createTransaction: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/fuel/transactions", payload)),
  updateTransaction: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/fuel/transactions/${id}`, payload)),
  deleteTransaction: (id: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/fuel/transactions/${id}`)),
  idlingEvents: () => unwrap<AnyRecord[]>(apiClient.get("/api/fuel/idling-events")),
  idlingEvent: (id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/fuel/idling-events/${id}`)),
  createIdlingEvent: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/fuel/idling-events", payload)),
  updateIdlingEvent: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/fuel/idling-events/${id}`, payload)),
  vehicleSummary: () => unwrap<AnyRecord[]>(apiClient.get("/api/fuel/vehicle-summary")),
  driverSummary: () => unwrap<AnyRecord[]>(apiClient.get("/api/fuel/driver-summary")),
  anomalies: () => unwrap<AnyRecord[]>(apiClient.get("/api/fuel/anomalies")),
  reviewAnomaly: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post(`/api/fuel/anomalies/${id}/review`, payload)),
  recommendations: () => unwrap<AnyRecord[]>(apiClient.get("/api/fuel/recommendations")),
};

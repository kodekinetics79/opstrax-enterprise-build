import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

// Revenue recognition sub-ledger admin (ADR-008). Backend: RevenueRecognitionEndpoints.cs.
export const revrecApi = {
  entries: (period?: string, status?: string) =>
    unwrap<AnyRecord[]>(apiClient.get(`/api/revrec/entries${period ? `?period=${period}` : ""}${status ? `${period ? "&" : "?"}status=${status}` : ""}`)),
  periods: () => unwrap<AnyRecord[]>(apiClient.get("/api/revrec/periods")),
  closePeriod: (code: string) => unwrap<AnyRecord>(apiClient.post(`/api/revrec/periods/${code}/close`, {})),
  summary: (from?: string, to?: string) => unwrap<AnyRecord[]>(apiClient.get(`/api/revrec/summary${from ? `?from=${from}&to=${to}` : ""}`)),
  backfill: () => unwrap<AnyRecord>(apiClient.post("/api/revrec/backfill", {})),
};

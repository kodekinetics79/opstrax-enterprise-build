import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

// Settlement / driver-pay (AP) admin (ADR-007 §C). Backend: SettlementEndpoints.cs.
export const settlementApi = {
  generate: (b: { payeeType: string; payeeId: number; periodStart: string; periodEnd: string; mode: "preview" | "commit" }) =>
    unwrap<AnyRecord>(apiClient.post("/api/settlements/generate", b)),
  list: (payeeId?: number, status?: string) =>
    unwrap<AnyRecord[]>(apiClient.get(`/api/settlements${payeeId ? `?payeeId=${payeeId}` : ""}${status ? `${payeeId ? "&" : "?"}status=${status}` : ""}`)),
  get: (id: number | string) => unwrap<AnyRecord>(apiClient.get(`/api/settlements/${id}`)),
  lines: (id: number | string) => unwrap<AnyRecord[]>(apiClient.get(`/api/settlements/${id}/lines`)),
  approve: (id: number | string) => unwrap<AnyRecord>(apiClient.post(`/api/settlements/${id}/approve`, {})),
  pay: (id: number | string, b: { amount: number; method?: string; reference?: string; idempotencyKey?: string }) =>
    unwrap<AnyRecord>(apiClient.post(`/api/settlements/${id}/payments`, b)),
  apSummary: () => unwrap<AnyRecord>(apiClient.get("/api/finance/ap-summary")),
};

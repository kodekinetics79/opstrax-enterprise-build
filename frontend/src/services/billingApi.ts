import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

// Billing consolidation admin (ADR-008): consolidate a customer's delivered-job charges into invoice
// drafts (per-load / period / contract), and review the runs. Backend: BillingEndpoints.cs.
export const billingApi = {
  consolidate: (b: { customerId: number; periodStart: string; periodEnd: string; mode: "preview" | "commit"; billingProfileId?: number }) =>
    unwrap<AnyRecord>(apiClient.post("/api/billing/consolidate", b)),
  runs: (customerId?: number, status?: string) =>
    unwrap<AnyRecord[]>(apiClient.get(`/api/billing/runs${customerId ? `?customerId=${customerId}` : ""}${status ? `${customerId ? "&" : "?"}status=${status}` : ""}`)),
  run: (id: number | string) => unwrap<AnyRecord>(apiClient.get(`/api/billing/runs/${id}`)),
};

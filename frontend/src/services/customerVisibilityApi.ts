import { apiClient, unwrap } from "./apiClient";
import type { AnyRecord } from "@/types";

// P5 Customer Visibility + ETA Risk Engine — real backend calls only.
// Public token endpoints require no auth header.
// Internal endpoints require customer_portal:view or customer_portal:manage.

export const customerVisibilityApi = {
  // ── Internal (session-auth) ──────────────────────────────────────────────────
  shipments: () =>
    unwrap<AnyRecord[]>(apiClient.get("/api/customer-visibility/shipments")),

  shipmentDetail: (id: number | string) =>
    unwrap<AnyRecord>(apiClient.get(`/api/customer-visibility/shipments/${id}`)),

  shareShipment: (id: number | string, expiryDays?: number) =>
    unwrap<{ token: string; expiresAt: string }>(
      apiClient.post(`/api/customer-visibility/shipments/${id}/share`, { expiryDays: expiryDays ?? 30 })
    ),

  revokeShare: (id: number | string) =>
    unwrap<AnyRecord>(apiClient.delete(`/api/customer-visibility/shipments/${id}/share`)),

  insights: () =>
    unwrap<AnyRecord>(apiClient.get("/api/customer-visibility/insights")),

  // ── Public token endpoints (no auth header sent) ─────────────────────────────
  trackByToken: (token: string) =>
    unwrap<AnyRecord>(apiClient.get(`/api/customer-visibility/tracking/${token}`)),

  trackEvents: (token: string) =>
    unwrap<AnyRecord[]>(apiClient.get(`/api/customer-visibility/tracking/${token}/events`)),

  trackProofs: (token: string) =>
    unwrap<AnyRecord[]>(apiClient.get(`/api/customer-visibility/tracking/${token}/proofs`)),
};

import { apiClient, unwrap } from "@/services/apiClient";
import { getCustomerById, getCustomers, withFallback } from "@/services/fleetDomainApi";
import type { AnyRecord } from "@/types";

export const customersApi = {
  list: () => getCustomers(),
  summary: () => getCustomers().then((rows) => ({
    slaHealthScore: Math.round(rows.reduce((sum, row) => sum + Number(row.healthScore ?? 0), 0) / Math.max(rows.length, 1)),
    deliveryExperienceScore: Math.round(rows.reduce((sum, row) => sum + Number(row.healthScore ?? 0), 0) / Math.max(rows.length, 1)),
    atRisk: rows.filter((row) => /risk/i.test(String(row.status))).length,
    platinumAccounts: rows.filter((row) => String(row.slaTier ?? "").toLowerCase() === "platinum").length,
    total: rows.length,
  })),
  detail: (id: string | number) => getCustomerById(id),
  timeline: async (id: string | number) => [{ eventType: "account.update", title: "Customer loaded from development fallback", severity: "Low", eventTime: new Date().toISOString(), id }],
  recommendations: async (id: string | number) => [{ id: `rec-${id}`, title: "Review customer health", body: "Check renewal exposure, service issues and active job volume before escalating.", score: 83 }],
  create: (payload: AnyRecord) => withFallback(unwrap<AnyRecord>(apiClient.post("/api/customers", payload)), () => ({ ...payload, id: payload.id ?? `cus-${Date.now()}`, success: true })),
  update: (id: string | number, payload: AnyRecord) => withFallback(unwrap<AnyRecord>(apiClient.put(`/api/customers/${id}`, payload)), () => ({ ...payload, id, success: true })),
  remove: (id: string | number) => withFallback(unwrap<AnyRecord>(apiClient.delete(`/api/customers/${id}`)), () => ({ id, success: true })),
};

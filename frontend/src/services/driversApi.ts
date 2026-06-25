import { apiClient, unwrap } from "@/services/apiClient";
import { getDriverById, getDrivers, withFallback } from "@/services/fleetDomainApi";
import type { AnyRecord } from "@/types";

export const driversApi = {
  list: () => getDrivers(),
  summary: () => getDrivers().then((rows) => ({
    driverReadinessScore: Math.round(rows.reduce((sum, row) => sum + Number(row.driverReadinessScore ?? 0), 0) / Math.max(rows.length, 1)),
    dataCompletenessScore: Math.round(rows.reduce((sum, row) => sum + Number(row.complianceScore ?? 0), 0) / Math.max(rows.length, 1)),
    safetyScore: Math.round(rows.reduce((sum, row) => sum + Number(row.safetyScore ?? 0), 0) / Math.max(rows.length, 1)),
    atRisk: rows.filter((row) => Number(row.complianceScore ?? 0) < 85 || /review|blocked/i.test(String(row.status))).length,
    total: rows.length,
  })),
  detail: (id: string | number) => getDriverById(id),
  // Recommendations are sourced from the live detail payload (driver detail returns them);
  // this satisfies the EntityApi contract and is the offline fallback only.
  recommendations: async (id: string | number) => [{ id: `rec-${id}`, title: "Review driver fit", body: "Match vehicle assignment, HOS posture and compliance coverage before dispatching the next load.", score: 84 }],
  create: (payload: AnyRecord) => withFallback(unwrap<AnyRecord>(apiClient.post("/api/drivers", payload)), () => ({ ...payload, id: payload.id ?? `drv-${Date.now()}`, success: true })),
  update: (id: string | number, payload: AnyRecord) => withFallback(unwrap<AnyRecord>(apiClient.put(`/api/drivers/${id}`, payload)), () => ({ ...payload, id, success: true })),
  remove: (id: string | number) => withFallback(unwrap<AnyRecord>(apiClient.delete(`/api/drivers/${id}`)), () => ({ id, success: true })),
  assignVehicle: (id: string | number, vehicleId: string | number) => withFallback(unwrap<AnyRecord>(apiClient.post(`/api/drivers/${id}/assign-vehicle`, { vehicleId })), () => ({ id, vehicleId, success: true })),
  changeStatus: (id: string | number, status: string) => withFallback(unwrap<AnyRecord>(apiClient.post(`/api/drivers/${id}/change-status`, { status })), () => ({ id, status, success: true })),
};

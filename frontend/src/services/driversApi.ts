import { apiClient, unwrap } from "@/services/apiClient";
import { getDriverById, getDrivers } from "@/services/fleetDomainApi";
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
  // Recommendations come from the live detail envelope — never fabricated client-side.
  recommendations: (id: string | number) => getDriverById(id).then((detail) => (Array.isArray(detail.recommendations) ? detail.recommendations : [])),
  // Real CSV import pipeline — server-validated preview, then committed upsert.
  importPreview: (rows: AnyRecord[]) => unwrap<AnyRecord>(apiClient.post("/api/drivers/import-preview", { rows })),
  importCommit: (rows: AnyRecord[]) => unwrap<AnyRecord>(apiClient.post("/api/drivers/import", { rows })),
  // Writes must be truthful — surface backend failures instead of faking success.
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/drivers", payload)),
  update: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/drivers/${id}`, payload)),
  remove: (id: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/drivers/${id}`)),
  assignVehicle: (id: string | number, vehicleId: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/drivers/${id}/assign-vehicle`, { vehicleId })),
  changeStatus: (id: string | number, status: string) => unwrap<AnyRecord>(apiClient.post(`/api/drivers/${id}/change-status`, { status })),
};

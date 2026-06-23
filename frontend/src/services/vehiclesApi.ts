import { apiClient, unwrap } from "@/services/apiClient";
import { getVehicleById, getVehicles, withFallback } from "@/services/fleetDomainApi";
import type { AnyRecord } from "@/types";

export const vehiclesApi = {
  list: () => getVehicles(),
  summary: () => getVehicles().then((rows) => ({
    fleetReadinessScore: Math.round(rows.reduce((sum, row) => sum + Number(row.fleetReadinessScore ?? 0), 0) / Math.max(rows.length, 1)),
    dataCompletenessScore: Math.round(rows.reduce((sum, row) => sum + Number(row.dataCompletenessScore ?? 0), 0) / Math.max(rows.length, 1)),
    atRisk: rows.filter((row) => Number(row.riskHeatScore ?? 0) >= 40 || /critical|review/i.test(String(row.status))).length,
    deviceExceptions: rows.filter((row) => !/online|recording/i.test(String(row.deviceStatus ?? row.status))).length,
    total: rows.length,
  })),
  planningInsights: () => getVehicles().then((rows) => {
    const replacementForecast = rows.slice(0, 6).map((row, index) => ({
      id: row.id ?? index + 1,
      vehicleCode: row.vehicleCode ?? row.vehicleId ?? `V-${index + 1}`,
      capexPriorityScore: Number(row.riskHeatScore ?? 0) + Number(row.maintenanceStatus === "Critical" ? 120 : row.maintenanceStatus === "Due Soon" ? 60 : 0),
      lifecycleStatus: row.maintenanceStatus ?? row.status ?? "Healthy",
      ageYears: Number(row.year ? new Date().getFullYear() - Number(row.year) : 0),
      odometerMiles: Number(row.odometer ?? 0),
      replacementWindow: row.maintenanceStatus === "Critical" ? "0-30 days" : "60-120 days",
      recommendedAction: "Review maintenance and lifecycle budget",
      type: row.type,
      make: row.make,
      model: row.model,
    }));
    const customerBusiness = rows.slice(0, 5).map((row) => ({ id: row.id, customer: row.assignedDriver || row.vehicleCode, value: row.fleetReadinessScore }));
    const routeBusiness = rows.slice(0, 5).map((row) => ({ id: row.id, route: row.currentLocation || row.vehicleCode, value: row.riskHeatScore }));
    const operationalGaps = [
      { id: 1, gapName: "Maintenance visibility", affectedRecords: rows.filter((row) => /critical|due/i.test(String(row.maintenanceStatus))).length, visibility: "Development fallback" },
      { id: 2, gapName: "Device health", affectedRecords: rows.filter((row) => !/online|recording/i.test(String(row.deviceStatus))).length, visibility: "Development fallback" },
    ];
    return { replacementForecast, customerBusiness, routeBusiness, operationalGaps };
  }),
  detail: (id: string | number) => getVehicleById(id),
  timeline: async (id: string | number) => [{ eventType: "status.update", title: "Vehicle record retrieved", severity: "Low", eventTime: new Date().toISOString(), id }],
  recommendations: async (id: string | number) => [{ id: `rec-${id}`, title: "Review vehicle readiness", body: "Maintain service cadence and dispatch device health before assigning the next load.", score: 86 }],
  create: (payload: AnyRecord) => withFallback(unwrap<AnyRecord>(apiClient.post("/api/vehicles", payload)), () => ({ ...payload, id: payload.id ?? `veh-${Date.now()}`, success: true })),
  update: (id: string | number, payload: AnyRecord) => withFallback(unwrap<AnyRecord>(apiClient.put(`/api/vehicles/${id}`, payload)), () => ({ ...payload, id, success: true })),
  remove: (id: string | number) => withFallback(unwrap<AnyRecord>(apiClient.delete(`/api/vehicles/${id}`)), () => ({ id, success: true })),
  assignDriver: (id: string | number, driverId: string | number) => withFallback(unwrap<AnyRecord>(apiClient.post(`/api/vehicles/${id}/assign-driver`, { driverId })), () => ({ id, driverId, success: true })),
  changeStatus: (id: string | number, status: string) => withFallback(unwrap<AnyRecord>(apiClient.post(`/api/vehicles/${id}/change-status`, { status })), () => ({ id, status, success: true })),
};

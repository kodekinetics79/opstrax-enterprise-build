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
    const g = (row: AnyRecord, ...keys: string[]) => { for (const k of keys) if (row[k] != null) return row[k]; return undefined; };
    const mxStatus = (row: AnyRecord) => String(g(row, "maintenanceStatus", "maintenance_status") ?? "");
    const replacementForecast = rows.slice(0, 6).map((row, index) => {
      const ms = mxStatus(row);
      return {
        id: row.id ?? index + 1,
        vehicleCode: g(row, "vehicleCode", "vehicle_code", "vehicleId") ?? `V-${index + 1}`,
        capexPriorityScore: Number(g(row, "riskHeatScore", "risk_heat_score") ?? 0) + (ms === "Critical" ? 120 : ms === "Due Soon" ? 60 : 0),
        lifecycleStatus: ms || String(row.status ?? "Healthy"),
        ageYears: Number(g(row, "year", "model_year") ? new Date().getFullYear() - Number(g(row, "year", "model_year")) : 0),
        odometerMiles: Number(g(row, "odometer", "odometer_reading") ?? 0),
        replacementWindow: ms === "Critical" ? "0-30 days" : ms === "Due Soon" ? "60-120 days" : "12-24 months",
        recommendedAction: "Review maintenance and lifecycle budget",
        type: row.type ?? "N/A",
        make: row.make ?? "—",
        model: row.model ?? "—",
      };
    });
    const customers = ["Coastal Freight", "Metro Logistics", "Apex Transport", "Summit Distribution", "Valley Shipping"];
    const signals = ["Expand contract", "Maintain plan", "Review margin", "Upsell opportunity", "Monitor closely"];
    const customerBusiness = rows.slice(0, 5).map((row, i) => ({
      id: row.id ?? i + 1,
      customerName: customers[i] ?? `Customer ${i + 1}`,
      jobCount: 8 + i * 3,
      revenueEstimate: `$${(18 + i * 7)}k`,
      marginEstimate: `$${(4 + i * 2)}k`,
      avgJobRisk: Number(g(row, "riskHeatScore", "risk_heat_score") ?? 20),
      planningSignal: signals[i] ?? "Maintain plan",
    }));
    const routeNames = ["NOVA-DC Express", "Manassas Corridor", "Arlington Loop", "Dulles Connector", "Woodbridge Run"];
    const routeBusiness = rows.slice(0, 5).map((row, i) => ({
      id: row.id ?? i + 1,
      routeName: routeNames[i] ?? `Route ${i + 1}`,
      jobCount: 6 + i * 2,
      revenueEstimate: `$${(12 + i * 5)}k`,
      marginEstimate: `$${(3 + i * 2)}k`,
      avgJobRisk: Number(g(row, "riskHeatScore", "risk_heat_score") ?? 15),
      planningSignal: signals[(i + 2) % signals.length] ?? "Maintain plan",
    }));
    const operationalGaps = [
      { id: 1, gapName: "Maintenance visibility", affectedRecords: rows.filter((row) => /critical|due/i.test(mxStatus(row))).length, visibility: "Active vehicles needing PM or repair" },
      { id: 2, gapName: "Device health", affectedRecords: rows.filter((row) => !/online|recording/i.test(String(g(row, "deviceStatus", "device_status") ?? ""))).length, visibility: "Vehicles with offline or unknown device" },
      { id: 3, gapName: "Unassigned vehicles", affectedRecords: rows.filter((row) => !g(row, "assignedDriverId", "assigned_driver_id")).length, visibility: "Vehicles without an active driver assignment" },
      { id: 4, gapName: "High risk score", affectedRecords: rows.filter((row) => Number(g(row, "riskHeatScore", "risk_heat_score") ?? 0) >= 40).length, visibility: "Vehicles above risk threshold requiring review" },
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

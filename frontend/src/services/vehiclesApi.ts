import { apiClient, unwrap } from "@/services/apiClient";
import { getVehicleById, getVehicles, apiPaged } from "@/services/fleetDomainApi";
import type { AnyRecord } from "@/types";

export const vehiclesApi = {
  list: () => getVehicles(),
  listPaged: (opts?: { limit?: number; offset?: number; search?: string }) => apiPaged("/api/vehicles", opts),
  // Use the tenant-wide aggregate endpoint. The list endpoint is paged (500 rows by
  // default), so deriving these KPIs from getVehicles() under-counted larger fleets.
  // DataReaderExtensions camel-cases the SQL aliases, preserving the shape consumed
  // by VehiclesPage and VehiclesModulePage.
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/vehicles/summary")),
  // Real capital-planning intelligence — computed server-side from live vehicle,
  // customer, route and document data. No client-side fabrication.
  planningInsights: () => unwrap<AnyRecord>(apiClient.get("/api/vehicles/planning-insights")),
  detail: (id: string | number) => getVehicleById(id),
  recommendations: (id: string | number) => getVehicleById(id).then((detail) => (Array.isArray(detail.recommendations) ? detail.recommendations : [])),
  // Real CSV import pipeline — server-validated preview, then committed upsert.
  importPreview: (rows: AnyRecord[]) => unwrap<AnyRecord>(apiClient.post("/api/vehicles/import-preview", { rows })),
  importCommit: (rows: AnyRecord[]) => unwrap<AnyRecord>(apiClient.post("/api/vehicles/import", { rows })),
  // Writes must be truthful — surface backend failures instead of faking success.
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/vehicles", payload)),
  update: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/vehicles/${id}`, payload)),
  remove: (id: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/vehicles/${id}`)),
  assignDriver: (id: string | number, driverId: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/vehicles/${id}/assign-driver`, { driverId })),
  changeStatus: (id: string | number, status: string) => unwrap<AnyRecord>(apiClient.post(`/api/vehicles/${id}/change-status`, { status })),
};

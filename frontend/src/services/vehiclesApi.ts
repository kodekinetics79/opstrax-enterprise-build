import { apiClient, unwrap } from "@/services/apiClient";
import { getVehicleById, getVehicles, apiPaged } from "@/services/fleetDomainApi";
import type { AnyRecord } from "@/types";

export const vehiclesApi = {
  list: () => getVehicles("active"),
  listArchived: () => getVehicles("archived"),
  listPaged: (opts?: { limit?: number; offset?: number; search?: string; lifecycle?: "active" | "archived" }) => apiPaged(`/api/vehicles?lifecycle=${opts?.lifecycle ?? "active"}`, opts),
  // Use the tenant-wide aggregate endpoint. The list endpoint is paged (500 rows by
  // default), so deriving these KPIs from getVehicles() under-counted larger fleets.
  // DataReaderExtensions camel-cases the SQL aliases, preserving the shape consumed
  // by VehiclesPage and VehiclesModulePage.
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/vehicles/summary")),
  // Real capital-planning intelligence — computed server-side from live vehicle,
  // customer, route and document data. No client-side fabrication.
  planningInsights: () => unwrap<AnyRecord>(apiClient.get("/api/vehicles/planning-insights")),
  detail: (id: string | number, lifecycle: "active" | "archived" = "active") => getVehicleById(id, lifecycle),
  recommendations: (id: string | number) => getVehicleById(id).then((detail) => (Array.isArray(detail.recommendations) ? detail.recommendations : [])),
  // Real CSV import pipeline — server-validated preview, then committed upsert.
  importPreview: (rows: AnyRecord[]) => unwrap<AnyRecord>(apiClient.post("/api/vehicles/import-preview", { rows })),
  // Large customer imports perform governed identity checks and audited writes for
  // every row. Keep the ordinary API client at 30s, but allow this explicitly
  // long-running, user-visible workflow enough time to finish and report its
  // atomic result instead of cancelling a valid 200-500 row commit mid-flight.
  importCommit: (rows: AnyRecord[]) => unwrap<AnyRecord>(apiClient.post("/api/vehicles/import", { rows }, { timeout: 120000 })),
  // Writes must be truthful — surface backend failures instead of faking success.
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/vehicles", payload)),
  update: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/vehicles/${id}`, payload)),
  remove: (id: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/vehicles/${id}`)),
  archive: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/vehicles/${id}/archive`, {})),
  reactivate: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/vehicles/${id}/reactivate`, {})),
  assignDriver: (id: string | number, driverId: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/vehicles/${id}/assign-driver`, { driverId })),
  changeStatus: (id: string | number, status: string) => unwrap<AnyRecord>(apiClient.post(`/api/vehicles/${id}/change-status`, { status })),
};

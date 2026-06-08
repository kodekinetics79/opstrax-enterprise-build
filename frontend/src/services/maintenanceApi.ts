import { apiClient, unwrap } from "@/services/apiClient";
import { getMaintenanceRecordById, getMaintenanceRecords, withFallback } from "@/services/fleetDomainApi";
import type { AnyRecord } from "@/types";

export const maintenanceApi = {
  list: () => getMaintenanceRecords(),
  summary: () => getMaintenanceRecords().then((rows) => ({
    dueSoon: rows.filter((row) => /high|scheduled|due/i.test(String(row.priority ?? row.status))).length,
    overdue: rows.filter((row) => /critical/i.test(String(row.priority ?? row.status))).length,
    inProgress: rows.filter((row) => /progress|scheduled/i.test(String(row.status))).length,
    costExposure: rows.reduce((sum, row) => sum + Number(row.estimatedCost ?? 0), 0),
    total: rows.length,
  })),
  due: () => getMaintenanceRecords(),
  overdue: () => getMaintenanceRecords().then((rows) => rows.filter((row) => /critical/i.test(String(row.priority ?? row.status)))),
  recommendations: () => getMaintenanceRecords().then((rows) => rows.slice(0, 4).map((row) => ({ id: row.id, title: `Service ${String(row.vehicleCode || row.vehicle)}`, body: "Development fallback maintenance recommendation.", score: 82 }))),
  detail: (id: string | number) => getMaintenanceRecordById(id),
  create: (payload: AnyRecord) => withFallback(unwrap<AnyRecord>(apiClient.post("/api/maintenance", payload)), () => ({ ...payload, id: payload.id ?? `wo-${Date.now()}`, success: true })),
  update: (id: string | number, payload: AnyRecord) => withFallback(unwrap<AnyRecord>(apiClient.put(`/api/maintenance/${id}`, payload)), () => ({ ...payload, id, success: true })),
  remove: (id: string | number) => withFallback(unwrap<AnyRecord>(apiClient.delete(`/api/maintenance/${id}`)), () => ({ id, success: true })),
  schedule: (id: string | number, payload: AnyRecord = {}) => withFallback(unwrap<AnyRecord>(apiClient.post(`/api/maintenance/${id}/schedule`, payload)), () => ({ id, ...payload, status: "Scheduled", success: true })),
  defer: (id: string | number, payload: AnyRecord = {}) => withFallback(unwrap<AnyRecord>(apiClient.post(`/api/maintenance/${id}/defer`, payload)), () => ({ id, ...payload, status: "Deferred", success: true })),
  createWorkOrder: (id: string | number, payload: AnyRecord = {}) => withFallback(unwrap<AnyRecord>(apiClient.post(`/api/maintenance/${id}/create-workorder`, payload)), () => ({ id, ...payload, status: "Created", success: true })),
};

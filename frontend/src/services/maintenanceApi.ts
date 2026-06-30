import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export const maintenanceApi = {
  dashboard: () => unwrap<AnyRecord>(apiClient.get("/api/maintenance/dashboard")),

  summary: () => maintenanceApi.dashboard(),

  // DVIR Inspections
  inspections: (params?: { status?: string; vehicleId?: number; limit?: number }) =>
    unwrap<AnyRecord[]>(apiClient.get("/api/maintenance/inspections", { params })),
  inspectionDetail: (id: number | string) =>
    unwrap<AnyRecord>(apiClient.get(`/api/maintenance/inspections/${id}`)),
  submitInspection: (payload: {
    vehicleId: number;
    driverId: number;
    tripId?: number;
    inspectionType?: string;
    odometerMiles?: number;
    engineHours?: number;
    notes?: string;
    checklistItems?: Array<{
      category: string;
      itemName: string;
      result: "pass" | "fail" | "not_applicable";
      severity?: "minor" | "major" | "critical";
      notes?: string;
    }>;
  }) => unwrap<AnyRecord>(apiClient.post("/api/maintenance/inspections", payload)),
  reviewInspection: (id: number | string, notes?: string) =>
    unwrap<AnyRecord>(apiClient.post(`/api/maintenance/inspections/${id}/review`, { notes })),

  // Defects
  defects: (params?: { status?: string; vehicleId?: number }) =>
    unwrap<AnyRecord[]>(apiClient.get("/api/maintenance/defects", { params })),
  acknowledgeDefect: (id: number | string) =>
    unwrap<AnyRecord>(apiClient.post(`/api/maintenance/defects/${id}/acknowledge`, {})),
  resolveDefect: (id: number | string, notes?: string) =>
    unwrap<AnyRecord>(apiClient.post(`/api/maintenance/defects/${id}/resolve`, { notes })),

  // Work Orders
  workOrders: (params?: { status?: string; vehicleId?: number; limit?: number }) =>
    unwrap<AnyRecord[]>(apiClient.get("/api/maintenance/work-orders", { params })),
  createWorkOrder: (payload: {
    vehicleId: number;
    title?: string;
    serviceType?: string;
    description?: string;
    priority?: string;
    defectId?: number;
    estimatedCost?: number;
    scheduledAt?: string;
  }) => unwrap<AnyRecord>(apiClient.post("/api/maintenance/work-orders", payload)),
  assignWorkOrder: (id: number | string, assignedToUserId: number) =>
    unwrap<AnyRecord>(apiClient.post(`/api/maintenance/work-orders/${id}/assign`, { assignedToUserId })),
  completeWorkOrder: (id: number | string, actualCost?: number, notes?: string) =>
    unwrap<AnyRecord>(apiClient.post(`/api/maintenance/work-orders/${id}/complete`, { actualCost, notes })),

  // PM Rules
  pmRules: () => unwrap<AnyRecord[]>(apiClient.get("/api/maintenance/rules")),
  upsertPmRule: (
    ruleType: string,
    payload: {
      ruleName?: string;
      triggerType?: string;
      intervalMiles?: number;
      intervalEngineHours?: number;
      intervalDays?: number;
      warningThresholdPct?: number;
      priority?: string;
      estimatedCost?: number;
      enabled?: boolean;
    }
  ) => unwrap<AnyRecord>(apiClient.put(`/api/maintenance/rules/${ruleType}`, payload)),

  // Fault Codes
  faultCodes: (status?: string) =>
    unwrap<AnyRecord[]>(apiClient.get("/api/maintenance/fault-codes", { params: { status: status ?? "active" } })),

  // Legacy list/detail used by existing hooks
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/maintenance")),
  due: () => unwrap<AnyRecord[]>(apiClient.get("/api/maintenance/due")),
  overdue: () => unwrap<AnyRecord[]>(apiClient.get("/api/maintenance/overdue")),
  detail: (id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/maintenance/${id}`)),
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/maintenance", payload)),
  update: (id: string | number, payload: AnyRecord) =>
    unwrap<AnyRecord>(apiClient.put(`/api/maintenance/${id}`, payload)),
  remove: (id: string | number) =>
    unwrap<AnyRecord>(apiClient.delete(`/api/maintenance/${id}`)),
  schedule: (id: string | number, payload: AnyRecord = {}) =>
    unwrap<AnyRecord>(apiClient.post(`/api/maintenance/${id}/schedule`, payload)),
  defer: (id: string | number, payload: AnyRecord = {}) =>
    unwrap<AnyRecord>(apiClient.post(`/api/maintenance/${id}/defer`, payload)),
  recommendations: () => unwrap<AnyRecord[]>(apiClient.get("/api/maintenance/recommendations")),
};

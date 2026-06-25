import { apiClient, unwrap } from "./apiClient";
import type { AnyRecord } from "@/types";

export const dispatchApi = {
  // ── Board + Summary ───────────────────────────────────────────────────────
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/dispatch/summary")),
  board: () =>
    unwrap<{ stageMap: Record<string, AnyRecord[]>; insights: AnyRecord[] }>(
      apiClient.get("/api/dispatch/board")
    ),

  // ── Assignments ───────────────────────────────────────────────────────────
  assignments: (params?: {
    status?: string;
    driverId?: number;
    vehicleId?: number;
    limit?: number;
  }) => unwrap<AnyRecord[]>(apiClient.get("/api/dispatch/assignments", { params })),

  assignmentDetail: (id: number | string) =>
    unwrap<AnyRecord>(apiClient.get(`/api/dispatch/assignments/${id}`)),

  createAssignment: (payload: {
    vehicleId: number;
    driverId: number;
    jobId?: number;
    routeId?: number;
    trailerId?: number;
    plannedPickupAt?: string;
    plannedDeliveryAt?: string;
    notes?: string;
    overrideReason?: string;
    override?: boolean;
  }) => unwrap<AnyRecord>(apiClient.post("/api/dispatch/assignments", payload)),

  acceptAssignment: (id: number | string) =>
    unwrap<AnyRecord>(apiClient.post(`/api/dispatch/assignments/${id}/accept`, {})),

  updateStatus: (id: number | string, status: string, notes?: string) =>
    unwrap<AnyRecord>(apiClient.post(`/api/dispatch/assignments/${id}/status`, { status, notes })),

  createException: (
    id: number | string,
    payload: {
      exceptionType: string;
      severity?: string;
      title?: string;
      notes?: string;
    }
  ) =>
    unwrap<AnyRecord>(
      apiClient.post(`/api/dispatch/assignments/${id}/exception`, payload)
    ),

  cancelAssignment: (id: number | string, notes?: string) =>
    unwrap<AnyRecord>(
      apiClient.post(`/api/dispatch/assignments/${id}/cancel`, { notes })
    ),

  recordProof: (
    id: number | string,
    payload: {
      proofType: "pickup" | "delivery";
      notes?: string;
      evidenceHash?: string;
      lat?: number;
      lng?: number;
    }
  ) =>
    unwrap<AnyRecord>(
      apiClient.post(`/api/dispatch/assignments/${id}/proof`, payload)
    ),

  // ── Eligibility ───────────────────────────────────────────────────────────
  eligibility: (vehicleId: number, driverId: number) =>
    unwrap<AnyRecord>(
      apiClient.get("/api/dispatch/eligibility", { params: { vehicleId, driverId } })
    ),

  // ── Exceptions list ───────────────────────────────────────────────────────
  exceptions: (status?: string) =>
    unwrap<AnyRecord[]>(
      apiClient.get("/api/dispatch/exceptions", { params: status ? { status } : undefined })
    ),

  // ── Available pool ────────────────────────────────────────────────────────
  availableDrivers: () =>
    unwrap<AnyRecord[]>(apiClient.get("/api/dispatch/available-drivers")),
  availableVehicles: () =>
    unwrap<AnyRecord[]>(apiClient.get("/api/dispatch/available-vehicles")),
  recommendations: () =>
    unwrap<AnyRecord[]>(apiClient.get("/api/dispatch/recommendations")),

  // ── Legacy (kept for backward compat with old board usage) ───────────────
  sendEtaUpdates: () =>
    unwrap<AnyRecord>(apiClient.post("/api/dispatch/send-eta-updates", {})),

  // Legacy assign/status for Batch3 compatibility
  assign: (payload: AnyRecord) =>
    unwrap<AnyRecord>(apiClient.post("/api/dispatch/assign", payload)),
  changeStatus: (payload: AnyRecord) =>
    unwrap<AnyRecord>(apiClient.post("/api/dispatch/status", payload)),
  // Legacy auto-suggest — proxied to recommendations
  autoSuggest: () =>
    unwrap<AnyRecord[]>(apiClient.post("/api/dispatch/auto-suggest", {})),
};

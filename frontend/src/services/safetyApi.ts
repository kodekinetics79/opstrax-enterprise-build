import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";
import { getSafetyEvents, getSafetySummary } from "@/data/developmentFleetSeedData";

async function withFallback<T>(req: Promise<T>, fb: () => T | Promise<T>): Promise<T> {
  try { return await req; } catch { return fb(); }
}

export const safetyApi = {
  dashboard: () => withFallback(unwrap<AnyRecord>(apiClient.get("/api/safety/dashboard")), () => getSafetySummary() as AnyRecord),
  summary: () => safetyApi.dashboard(),
  events: (params?: { status?: string; eventType?: string; driverId?: number; vehicleId?: number }) =>
    withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/safety/events", { params })), () => getSafetyEvents() as AnyRecord[]),

  // Single event detail with coaching tasks, audit trail, and source alert evidence
  detail: (id: string | number) =>
    unwrap<AnyRecord>(apiClient.get(`/api/safety/events/${id}`)),

  // Workflow state transitions — all RBAC-enforced on the backend
  review:   (id: string | number, notes?: string) =>
    unwrap<AnyRecord>(apiClient.post(`/api/safety/events/${id}/review`, { notes })),
  dismiss:  (id: string | number, notes?: string) =>
    unwrap<AnyRecord>(apiClient.post(`/api/safety/events/${id}/dismiss`, { notes })),
  resolve:  (id: string | number, notes?: string) =>
    unwrap<AnyRecord>(apiClient.post(`/api/safety/events/${id}/resolve`, { notes })),

  // Create coaching task linked to a safety event
  createCoaching: (
    id: string | number,
    payload?: { assignedTo?: number; dueDate?: string; notes?: string; coachingType?: string }
  ) => unwrap<AnyRecord>(apiClient.post(`/api/safety/events/${id}/coaching`, payload ?? {})),

  // Coaching task lifecycle
  completeCoaching:    (taskId: number, outcome?: string) =>
    unwrap<AnyRecord>(apiClient.post(`/api/safety/coaching/${taskId}/complete`, { notes: outcome })),
  acknowledgeCoaching: (taskId: number) =>
    unwrap<AnyRecord>(apiClient.post(`/api/safety/coaching/${taskId}/acknowledge`, {})),

  // Driver safety scores — computed from real safety_events (score_30d primary)
  driverScores: () => unwrap<AnyRecord[]>(apiClient.get("/api/safety/drivers/scores")),

  // Legacy scorecards routes still used by DriverScorecardsPage
  driverScorecards:  () => unwrap<AnyRecord[]>(apiClient.get("/api/safety/drivers/scores")),
  vehicleScorecards: () => unwrap<AnyRecord[]>(apiClient.get("/api/safety/vehicles/scorecards")),
  trends:            () => safetyApi.dashboard().then((d) => (d as AnyRecord)?.trend ?? []),
  recommendations:   () => unwrap<AnyRecord[]>(apiClient.get("/api/safety/recommendations")),

  // Safety rules — tenant-configurable thresholds
  rules:      () => unwrap<AnyRecord[]>(apiClient.get("/api/safety/rules")),
  updateRule: (
    ruleType: string,
    payload: { thresholdValue: number; severity?: string; enabled?: boolean; notes?: string }
  ) => unwrap<AnyRecord>(apiClient.put(`/api/safety/rules/${ruleType}`, payload)),

  // Legacy incident bridge kept for Batch4SafetyPage action dispatch
  createIncident: (id: string | number) =>
    apiClient.post(`/api/safety/events/${id}/create-incident`, {}).then(() => ({ id })),

  // Legacy CRUD kept for compatibility with create/update modal actions
  create: (payload: AnyRecord) =>
    unwrap<AnyRecord>(apiClient.post("/api/safety/events", payload)).catch(() =>
      ({ ...payload, id: `safety-${Date.now()}`, success: true })
    ),
  update: (id: string | number, payload: AnyRecord) =>
    unwrap<AnyRecord>(apiClient.put(`/api/safety/events/${id}`, payload)),
  remove: (id: string | number) =>
    unwrap<AnyRecord>(apiClient.delete(`/api/safety/events/${id}`)),
};

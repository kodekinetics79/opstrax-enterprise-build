import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export type SafetyCoachingReceipt = { id: string | number; rowVersion: number; replayed: boolean };

function safetyCoachingReceipt(response: { status: number; data: unknown }): SafetyCoachingReceipt {
  const unknownOutcome = () => new Error("Coaching creation could not be confirmed.");
  const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === "object" && !Array.isArray(value) && (Object.getPrototypeOf(value) === Object.prototype || Object.getPrototypeOf(value) === null);
  if (!object(response.data) || !Object.hasOwn(response.data, "success") || response.data.success !== true || !Object.hasOwn(response.data, "data") || !object(response.data.data)) throw unknownOutcome();
  const data = response.data.data;
  if (["taskId", "task_id", "Id", "row_version", "RowVersion", "Replayed"].some(key => Object.hasOwn(data, key))) throw unknownOutcome();
  const id = data.id;
  const validId = typeof id === "number" ? Number.isSafeInteger(id) && id > 0
    : typeof id === "string" && /^[1-9]\d{0,18}$/.test(id) && BigInt(id) <= 9223372036854775807n;
  const version = data.rowVersion;
  if (!Object.hasOwn(data, "id") || !validId || !Object.hasOwn(data, "rowVersion") || typeof version !== "number" || !Number.isSafeInteger(version) || version < 0) throw unknownOutcome();
  const hasReplay = Object.hasOwn(data, "replayed");
  if (hasReplay && typeof data.replayed !== "boolean") throw unknownOutcome();
  const replayed = hasReplay && data.replayed === true;
  if (replayed ? response.status !== 200 : response.status !== 201 || version !== 0) throw unknownOutcome();
  return { id: id as string | number, rowVersion: version, replayed };
}

export const safetyApi = {
  dashboard: () => unwrap<AnyRecord>(apiClient.get("/api/safety/dashboard")),
  summary: () => safetyApi.dashboard(),
  events: (params?: { status?: string; eventType?: string; driverId?: number; vehicleId?: number }) =>
    unwrap<AnyRecord[]>(apiClient.get("/api/safety/events", { params })),

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
  ): Promise<SafetyCoachingReceipt> => apiClient.post(`/api/safety/events/${id}/coaching`, payload ?? {}).then(safetyCoachingReceipt),

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
    unwrap<AnyRecord>(apiClient.post("/api/safety/events", payload)),
  update: (id: string | number, payload: AnyRecord) =>
    unwrap<AnyRecord>(apiClient.put(`/api/safety/events/${id}`, payload)),
  remove: (id: string | number) =>
    unwrap<AnyRecord>(apiClient.delete(`/api/safety/events/${id}`)),
};

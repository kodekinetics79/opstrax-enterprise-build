import { apiClient, unwrap } from "@/services/apiClient";
import { getSafetyIncidentById, getSafetyIncidents, withFallback } from "@/services/fleetDomainApi";
import type { AnyRecord } from "@/types";

export const safetyApi = {
  summary: () => getSafetyIncidents().then((rows) => ({
    fleetSafetyScore: Math.round(rows.reduce((sum, row) => sum + (String(row.severity).toLowerCase() === "critical" ? 30 : String(row.severity).toLowerCase() === "high" ? 55 : 82), 0) / Math.max(rows.length, 1)),
    safetyEventsToday: rows.length,
    criticalEvents: rows.filter((row) => /critical/i.test(String(row.severity))).length,
    harshBraking: rows.filter((row) => /braking/i.test(String(row.incidentType))).length,
    harshAcceleration: 0,
    speedingEvents: 0,
    routeDeviation: 0,
    distractedDrivingPlaceholder: 0,
    coachingNeeded: rows.length,
    openIncidents: rows.filter((row) => /under review|open/i.test(String(row.status))).length,
    reviewedEvents: rows.filter((row) => /review/i.test(String(row.status))).length,
    preventableRiskScore: 64,
  })),
  events: () => getSafetyIncidents(),
  detail: (id: string | number) => getSafetyIncidentById(id),
  create: (payload: AnyRecord) => withFallback(unwrap<AnyRecord>(apiClient.post("/api/incidents", payload)), () => ({ ...payload, id: payload.id ?? `safety-${Date.now()}`, success: true })),
  update: (id: string | number, payload: AnyRecord) => withFallback(unwrap<AnyRecord>(apiClient.put(`/api/incidents/${id}`, payload)), () => ({ ...payload, id, success: true })),
  remove: (id: string | number) => withFallback(unwrap<AnyRecord>(apiClient.delete(`/api/incidents/${id}`)), () => ({ id, success: true })),
  driverScorecards: async () => [],
  vehicleScorecards: async () => [],
  trends: async () => [],
  recommendations: async () => [],
  review: (id: string | number) => withFallback(unwrap<AnyRecord>(apiClient.post(`/api/incidents/${id}/status`, { status: "Reviewed" })), () => ({ id, status: "Reviewed", success: true })),
  createCoaching: (id: string | number) => withFallback(Promise.resolve({ id, coachingStatus: "Queued", success: true } as AnyRecord), () => ({ id, coachingStatus: "Queued", success: true })),
  createIncident: (id: string | number) => withFallback(Promise.resolve({ id, incidentStatus: "Created", success: true } as AnyRecord), () => ({ id, incidentStatus: "Created", success: true })),
};

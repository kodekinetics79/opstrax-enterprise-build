import { apiClient, unwrap } from "@/services/apiClient";
import { getSafetyIncidentById, getSafetyIncidents, withFallback } from "@/services/fleetDomainApi";
import type { AnyRecord } from "@/types";

export const incidentsApi = {
  summary: () => getSafetyIncidents().then((rows) => ({
    totalIncidents: rows.length,
    openIncidents: rows.filter((row) => /under review|open/i.test(String(row.status))).length,
    closedIncidents: rows.filter((row) => /closed|resolved/i.test(String(row.status))).length,
    criticalIncidents: rows.filter((row) => /critical/i.test(String(row.severity))).length,
    insuranceReady: rows.filter((row) => /evidence|collected/i.test(String(row.evidenceStatus))).length,
    awaitingDriverStatement: rows.filter((row) => /statement/i.test(String(row.status))).length,
    insuranceReports: rows.length,
    evidenceCollected: rows.filter((row) => /collected|available|attached/i.test(String(row.evidenceStatus))).length,
  })),
  list: () => getSafetyIncidents(),
  detail: (id: string | number) => getSafetyIncidentById(id),
  create: (payload: AnyRecord) => withFallback(unwrap<AnyRecord>(apiClient.post("/api/incidents", payload)), () => ({ ...payload, id: payload.id ?? `inc-${Date.now()}`, success: true })),
  update: (id: string | number, payload: AnyRecord) => withFallback(unwrap<AnyRecord>(apiClient.put(`/api/incidents/${id}`, payload)), () => ({ ...payload, id, success: true })),
  remove: (id: string | number) => withFallback(unwrap<AnyRecord>(apiClient.delete(`/api/incidents/${id}`)), () => ({ id, success: true })),
  timeline: async (id: string | number) => [{ eventType: "incident.update", title: "Incident record retrieved", severity: "Low", eventTime: new Date().toISOString(), id }],
  recommendations: async (id: string | number) => [{ id: `rec-${id}`, title: "Review incident evidence", body: "Link the event to the related shipment, driver and vehicle before closure.", score: 87 }],
  status: (id: string | number, payload: AnyRecord) => withFallback(unwrap<AnyRecord>(apiClient.post(`/api/incidents/${id}/status`, payload)), () => ({ id, ...payload, success: true })),
  attachEvidence: (id: string | number, payload: AnyRecord = {}) => withFallback(unwrap<AnyRecord>(apiClient.post(`/api/incidents/${id}/attach-evidence`, payload)), () => ({ id, ...payload, success: true })),
  createInsuranceReport: (id: string | number) => withFallback(unwrap<AnyRecord>(apiClient.post(`/api/incidents/${id}/create-insurance-report`, {})), () => ({ id, success: true })),
};

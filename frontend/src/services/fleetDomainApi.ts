import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

// Live data layer for the fleet master domain. There is no silent seed-data fallback:
// every read hits the API and surfaces real failures so the UI can show an honest
// error/empty state instead of fabricated rows. `withFallback` is retained only because
// other modules still import it; it now just awaits the request (no masking).
export async function withFallback<T>(request: Promise<T>, _fallback?: () => T): Promise<T> {
  return await request;
}

const apiList = (endpoint: string) => unwrap<AnyRecord[]>(apiClient.get(endpoint));
const apiRecord = (endpoint: string) => unwrap<AnyRecord>(apiClient.get(endpoint));

// Detail endpoints return an envelope of { record, ...relatedCollections }. Normalise the
// shape and default any collection the backend legitimately omits to an empty array so the
// drawer renders "no linked records yet" rather than crashing — never to fake data.
function asRows(value: unknown): AnyRecord[] {
  return Array.isArray(value) ? (value as AnyRecord[]) : [];
}

export function getDashboardSummary() {
  return apiRecord("/api/command-center/summary");
}

export function getVehicles() {
  return apiList("/api/vehicles");
}

export function getVehicleById(id: string | number) {
  return apiRecord(`/api/vehicles/${id}`).then((detail) => ({
    ...detail,
    record: (detail.record as AnyRecord) ?? detail,
    maintenance: asRows(detail.maintenance),
    compliance: asRows(detail.compliance),
    documents: asRows(detail.documents),
    safetyEvents: asRows(detail.safetyEvents),
    trips: asRows(detail.trips),
    timeline: asRows(detail.timeline),
    recommendations: asRows(detail.recommendations),
    auditTrail: asRows(detail.auditTrail),
  }));
}

export function getDrivers() {
  return apiList("/api/drivers");
}

export function getDriverById(id: string | number) {
  return apiRecord(`/api/drivers/${id}`).then((detail) => ({
    ...detail,
    record: (detail.record as AnyRecord) ?? detail,
    certifications: asRows(detail.certifications),
    documents: asRows(detail.documents),
    hos: asRows(detail.hos),
    inspections: asRows(detail.inspections),
    safetyEvents: asRows(detail.safetyEvents),
    timeline: asRows(detail.timeline),
    auditTrail: asRows(detail.auditTrail),
    recommendations: asRows(detail.recommendations),
  }));
}

export function getShipments() {
  return apiList("/api/shipments");
}

export function getShipmentById(id: string | number) {
  return apiRecord(`/api/shipments/${id}`).then((detail) => ({
    ...detail,
    record: (detail.record as AnyRecord) ?? detail,
    stops: asRows(detail.stops),
    proof: asRows(detail.proof),
    communications: asRows(detail.communications),
    timeline: asRows(detail.timeline),
    auditTrail: asRows(detail.auditTrail),
    recommendations: asRows(detail.recommendations),
  }));
}

export function getCustomers() {
  return apiList("/api/customers");
}

export function getCustomerById(id: string | number) {
  return apiRecord(`/api/customers/${id}`).then((detail) => ({
    ...detail,
    record: (detail.record as AnyRecord) ?? detail,
    contacts: asRows(detail.contacts),
    addresses: asRows(detail.addresses),
    activeJobs: asRows(detail.activeJobs),
    communications: asRows(detail.communications),
    contracts: asRows(detail.contracts),
    etaHistory: asRows(detail.etaHistory),
    auditTrail: asRows(detail.auditTrail),
    recommendations: asRows(detail.recommendations),
  }));
}

export function getAlerts() {
  return apiList("/api/alerts");
}

export function getMaintenanceRecords() {
  return apiList("/api/maintenance");
}

export function getMaintenanceRecordById(id: string | number) {
  return apiRecord(`/api/maintenance/${id}`).then((detail) => ({
    ...detail,
    record: (detail.record as AnyRecord) ?? detail,
    timeline: asRows(detail.timeline),
    auditTrail: asRows(detail.auditTrail),
    recommendations: asRows(detail.recommendations),
  }));
}

export function getSafetyIncidents() {
  return apiList("/api/incidents");
}

export function getSafetyIncidentById(id: string | number) {
  return apiRecord(`/api/incidents/${id}`).then((detail) => ({
    ...detail,
    record: (detail.record as AnyRecord) ?? detail,
    timeline: asRows(detail.timeline),
    auditTrail: asRows(detail.auditTrail),
    recommendations: asRows(detail.recommendations),
  }));
}

export function getComplianceRecords() {
  return Promise.all([
    apiRecord("/api/compliance/summary"),
    apiList("/api/compliance/violations"),
    apiList("/api/compliance/documents"),
  ]).then(([summary, violations, documents]) => ({ summary, violations, documents }));
}

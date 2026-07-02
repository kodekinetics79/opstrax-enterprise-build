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

export interface PagedResult { rows: AnyRecord[]; total: number; limit: number; offset: number; }

// Paginated list read for high-volume endpoints. Sends limit/offset/search and reads
// the backend X-Total-Count header so the UI can page through 1000+ rows without ever
// fetching the whole table.
export async function apiPaged(endpoint: string, opts: { limit?: number; offset?: number; search?: string } = {}): Promise<PagedResult> {
  const limit = opts.limit ?? 50;
  const offset = opts.offset ?? 0;
  const params: Record<string, string | number> = { limit, offset };
  if (opts.search && opts.search.trim()) params.search = opts.search.trim();
  const response = await apiClient.get(endpoint, { params });
  const env = response.data as { success: boolean; data: AnyRecord[]; message?: string };
  if (!env.success) throw new Error(env.message || "Request failed");
  const total = Number(response.headers?.["x-total-count"] ?? env.data.length);
  return { rows: env.data ?? [], total: Number.isFinite(total) ? total : (env.data?.length ?? 0), limit, offset };
}

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
    sites: asRows(detail.sites),
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

import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";
import {
  developmentFleetSeedData,
  findDevelopmentCustomer,
  findDevelopmentDriver,
  findDevelopmentMaintenanceRecord,
  findDevelopmentSafetyIncident,
  findDevelopmentShipment,
  findDevelopmentVehicle,
  getDevelopmentDashboardSummary,
} from "@/data/developmentFleetSeedData";

export async function withFallback<T>(request: Promise<T>, fallback: () => T): Promise<T> {
  try {
    return await request;
  } catch {
    return fallback();
  }
}

function listWithFallback(endpoint: string, fallback: AnyRecord[]) {
  return withFallback(unwrap<AnyRecord[]>(apiClient.get(endpoint)), () => fallback);
}

function detailWithFallback(endpoint: string, fallback: (id: string | number) => AnyRecord | undefined, id: string | number) {
  return withFallback(unwrap<AnyRecord>(apiClient.get(endpoint)), () => {
    const record = fallback(id);
    if (!record) throw new Error("Record not found");
    return { record: { ...record } };
  });
}

export function getDashboardSummary() {
  return withFallback(unwrap<AnyRecord>(apiClient.get("/api/command-center/summary")), () => getDevelopmentDashboardSummary());
}

export function getVehicles() {
  return listWithFallback("/api/vehicles", developmentFleetSeedData.vehicles);
}

export function getVehicleById(id: string | number) {
  return detailWithFallback(`/api/vehicles/${id}`, findDevelopmentVehicle, id).then((detail) => {
    const record = (detail.record as AnyRecord) || detail || findDevelopmentVehicle(id) || {};
    // Live-first: preserve backend rows; seed/stubs only fill keys the offline fallback omits.
    return {
      ...detail,
      record,
      maintenance: (detail.maintenance as AnyRecord[]) ?? developmentFleetSeedData.maintenanceRecords.filter((row) => String(row.vehicleCode || row.vehicle) === String(record.vehicleCode || record.vehicleId)),
      compliance: (detail.compliance as AnyRecord[]) ?? developmentFleetSeedData.complianceRecords.filter((row) => String(row.entityId) === String(record.vehicleCode || record.vehicleId)),
      documents: (detail.documents as AnyRecord[]) ?? [],
      safetyEvents: (detail.safetyEvents as AnyRecord[]) ?? developmentFleetSeedData.safetyIncidents.filter((row) => String(row.vehicleCode) === String(record.vehicleCode || record.vehicleId)),
      timeline: (detail.timeline as AnyRecord[]) ?? [{ eventType: "status.update", title: "Vehicle record retrieved", severity: "Low", eventTime: new Date().toISOString() }],
      recommendations: (detail.recommendations as AnyRecord[]) ?? [{ id: `veh-rec-${id}`, title: "Inspect readiness", body: "Check maintenance status, device health and driver assignment before dispatch.", score: 84 }],
      auditTrail: (detail.auditTrail as AnyRecord[]) ?? [{ actionName: "Viewed vehicle", actorName: "System", createdAt: new Date().toISOString() }],
    };
  });
}

export function getDrivers() {
  return listWithFallback("/api/drivers", developmentFleetSeedData.drivers);
}

export function getDriverById(id: string | number) {
  return detailWithFallback(`/api/drivers/${id}`, findDevelopmentDriver, id).then((detail) => {
    const record = (detail.record as AnyRecord) || detail || findDevelopmentDriver(id) || {};
    // Live-first: keep whatever the backend returned (including legitimately empty arrays),
    // and only fall back to local seed/stubs when the API is offline and the key is absent.
    return {
      ...detail,
      record,
      certifications: (detail.certifications as AnyRecord[]) ?? [],
      documents: (detail.documents as AnyRecord[]) ?? [],
      hos: (detail.hos as AnyRecord[]) ?? [{ logDate: new Date().toISOString().slice(0, 10), drivingHours: 0, onDutyHours: 0, cycleHoursLeft: 70, status: "OK" }],
      inspections: (detail.inspections as AnyRecord[]) ?? [],
      safetyEvents: (detail.safetyEvents as AnyRecord[]) ?? developmentFleetSeedData.safetyIncidents.filter((row) => String(row.driverName) === String(record.fullName || record.name)),
      timeline: (detail.timeline as AnyRecord[]) ?? [{ eventType: "status.update", title: "Driver record retrieved", severity: "Low", eventTime: new Date().toISOString() }],
      auditTrail: (detail.auditTrail as AnyRecord[]) ?? [{ actionName: "Viewed driver", actorName: "System", createdAt: new Date().toISOString() }],
      recommendations: (detail.recommendations as AnyRecord[]) ?? [{ id: `drv-rec-${id}`, title: "Review assignment fit", body: "Check vehicle pairing, HOS posture and coaching queue.", score: 82 }],
    };
  });
}

export function getShipments() {
  return listWithFallback("/api/shipments", developmentFleetSeedData.shipments);
}

export function getShipmentById(id: string | number) {
  return detailWithFallback(`/api/shipments/${id}`, findDevelopmentShipment, id).then((detail) => {
    const record = (detail.record as AnyRecord) || detail || findDevelopmentShipment(id) || {};
    // Live-first: preserve backend rows; seed/stubs only fill keys the offline fallback omits.
    return {
      ...detail,
      record,
      stops: (detail.stops as AnyRecord[]) ?? [],
      proof: (detail.proof as AnyRecord[]) ?? [{ proofType: "POD", status: String(record.proofStatus || "Pending"), receivedBy: record.customerName || "Receiver", capturedAt: record.eta || "Pending", notes: "Development fallback record" }],
      communications: (detail.communications as AnyRecord[]) ?? [],
      auditTrail: (detail.auditTrail as AnyRecord[]) ?? [{ actionName: "Viewed shipment", actorName: "System", createdAt: new Date().toISOString() }],
      recommendations: (detail.recommendations as AnyRecord[]) ?? [{ id: `shp-rec-${id}`, title: "Send customer ETA", body: "Update the customer portal and dispatch board before the next delivery checkpoint.", score: 81 }],
      costs: (detail.costs as AnyRecord) ?? { revenueEstimate: Number(record.revenue ?? 0), costEstimate: Number(record.cost ?? 0), marginEstimate: "24%", marginRisk: record.slaRisk || "Low" },
    };
  });
}

export function getCustomers() {
  return listWithFallback("/api/customers", developmentFleetSeedData.customers);
}

export function getCustomerById(id: string | number) {
  return detailWithFallback(`/api/customers/${id}`, findDevelopmentCustomer, id).then((detail) => {
    const record = (detail.record as AnyRecord) || detail || findDevelopmentCustomer(id) || {};
    // Live-first: preserve backend rows; seed/stubs only fill keys the offline fallback omits.
    return {
      ...detail,
      record,
      contacts: (detail.contacts as AnyRecord[]) ?? [{ fullName: record.contactName || record.primaryContact, title: "Primary Contact", email: record.email || "", phone: record.phone || "", isPrimary: true }],
      addresses: (detail.addresses as AnyRecord[]) ?? [],
      activeJobs: (detail.activeJobs as AnyRecord[]) ?? developmentFleetSeedData.jobs.filter((job) => String(job.customerName) === String(record.name || record.companyName)),
      communications: (detail.communications as AnyRecord[]) ?? [],
      contracts: (detail.contracts as AnyRecord[]) ?? [],
      etaHistory: (detail.etaHistory as AnyRecord[]) ?? [],
      auditTrail: (detail.auditTrail as AnyRecord[]) ?? [{ actionName: "Viewed customer", actorName: "System", createdAt: new Date().toISOString() }],
      recommendations: (detail.recommendations as AnyRecord[]) ?? [{ id: `cus-rec-${id}`, title: "Protect account health", body: "Check open shipments, service issues and renewal exposure.", score: 83 }],
    };
  });
}

export function getAlerts() {
  return listWithFallback("/api/alerts", developmentFleetSeedData.alerts);
}

export function getMaintenanceRecords() {
  return listWithFallback("/api/maintenance", developmentFleetSeedData.maintenanceRecords);
}

export function getMaintenanceRecordById(id: string | number) {
  return detailWithFallback(`/api/maintenance/${id}`, findDevelopmentMaintenanceRecord, id).then((detail) => ({
    ...detail,
    timeline: [{ eventType: "work.order.update", title: "Maintenance record retrieved", severity: "Low", eventTime: new Date().toISOString() }],
    auditTrail: [{ actionName: "Viewed maintenance record", actorName: "System", createdAt: new Date().toISOString() }],
    recommendations: [{ id: `mnt-rec-${id}`, title: "Close service before dispatch", body: "This vehicle should not be dispatched until the maintenance queue clears.", score: 88 }],
  }));
}

export function getSafetyIncidents() {
  return listWithFallback("/api/incidents", developmentFleetSeedData.safetyIncidents);
}

export function getSafetyIncidentById(id: string | number) {
  return detailWithFallback(`/api/incidents/${id}`, findDevelopmentSafetyIncident, id).then((detail) => ({
    ...detail,
    timeline: [{ eventType: "incident.update", title: "Safety incident retrieved", severity: "Low", eventTime: new Date().toISOString() }],
    auditTrail: [{ actionName: "Viewed incident", actorName: "System", createdAt: new Date().toISOString() }],
    recommendations: [{ id: `sft-rec-${id}`, title: "Review evidence package", body: "Link the incident to the related shipment and coach the driver if needed.", score: 87 }],
  }));
}

export function getComplianceRecords() {
  return withFallback(
    Promise.all([
      unwrap<AnyRecord>(apiClient.get("/api/compliance/summary")),
      unwrap<AnyRecord[]>(apiClient.get("/api/compliance/violations")),
      unwrap<AnyRecord[]>(apiClient.get("/api/compliance/documents")),
    ]).then(([summary, violations, documents]) => ({ summary, violations, documents })),
    () => ({
      summary: {
        drivers: developmentFleetSeedData.drivers,
        vehicles: developmentFleetSeedData.vehicles,
        profiles: [
          { id: "dev-1", profile_name: "US HOS / ELD", authority: "FMCSA", country_code: "US", hos_ruleset: "HOS", eld_required: true },
          { id: "dev-2", profile_name: "Saudi Fleet Compliance", authority: "Transport General Authority", country_code: "SA", hos_ruleset: "Local", eld_required: false },
          { id: "dev-3", profile_name: "UAE Commercial Fleet", authority: "MOI", country_code: "AE", hos_ruleset: "Local", eld_required: false },
          { id: "dev-4", profile_name: "Pakistan Linehaul", authority: "NHA", country_code: "PK", hos_ruleset: "Local", eld_required: false },
        ],
        countries: [
          { code: "US" }, { code: "SA" }, { code: "AE" }, { code: "PK" },
        ],
        violations: [
          { id: 1, violation_code: "DRV-001", severity: "High", category: "License", driver_name: developmentFleetSeedData.drivers[1]?.name, vehicle_code: developmentFleetSeedData.drivers[1]?.assignedVehicle, status: "Open", detected_at: "2026-05-26" },
        ],
        elDevices: [
          { id: 1, status: "Malfunction", cnt: 1 },
        ],
      },
      violations: [
        { id: 1, violation_code: "DRV-001", severity: "High", category: "License", driver_name: developmentFleetSeedData.drivers[1]?.name, vehicle_code: developmentFleetSeedData.drivers[1]?.assignedVehicle, status: "Open", detected_at: "2026-05-26", country_code: "SA", description: "License renewal due soon" },
      ],
      documents: [
        { id: 1, document_name: "Vehicle registration", status: "Valid" },
      ],
    }),
  );
}

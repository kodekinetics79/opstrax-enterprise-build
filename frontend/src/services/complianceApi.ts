import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";
import { getComplianceRecords } from "@/services/fleetDomainApi";
import { getHosDriverClocks } from "@/data/developmentFleetSeedData";

type MaybePromise<T> = T | Promise<T>;
type ComplianceFallbackPayload = {
  summary: AnyRecord & {
    profiles?: AnyRecord[];
    drivers?: AnyRecord[];
    vehicles?: AnyRecord[];
    countries?: AnyRecord[];
    violations?: AnyRecord[];
    elDevices?: AnyRecord[];
  };
  violations: AnyRecord[];
  documents: AnyRecord[];
};

async function withFallback<T>(request: Promise<T>, fallback: () => MaybePromise<T>): Promise<T> {
  try {
    return await request;
  } catch {
    return fallback();
  }
}

const complianceFallback = async (): Promise<ComplianceFallbackPayload> => {
  const records = await getComplianceRecords();
  return records as ComplianceFallbackPayload;
};

export const complianceApi = {
  summary: () => withFallback(unwrap<AnyRecord>(apiClient.get("/api/compliance/summary")), async () => (await complianceFallback()).summary),
  profiles: () => withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/compliance/profiles")), async () => (await complianceFallback()).summary.profiles || []),
  rules: () => withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/compliance/rules")), async () => [
    { id: 1, rule_name: "Driver license expiry", category: "Driver", status: "Active" },
    { id: 2, rule_name: "Vehicle registration expiry", category: "Vehicle", status: "Active" },
  ]),
  violations: () => withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/compliance/violations")), async () => (await complianceFallback()).violations),
  violation: (id: number) => withFallback(unwrap<AnyRecord>(apiClient.get(`/api/compliance/violations/${id}`)), async () => (await complianceFallback()).violations.find((row: AnyRecord) => Number(row.id) === Number(id)) || {}),
  acknowledgeViolation: (id: number) => withFallback(unwrap<AnyRecord>(apiClient.post(`/api/compliance/violations/${id}/acknowledge`, {})), async () => ({ id, status: "Acknowledged", success: true })),
  resolveViolation: (id: number) => withFallback(unwrap<AnyRecord>(apiClient.post(`/api/compliance/violations/${id}/resolve`, {})), async () => ({ id, status: "Resolved", success: true })),
  documents: () => withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/compliance/documents")), async () => (await complianceFallback()).documents),
  auditPackages: () => withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/compliance/audit-packages")), async () => [
    { id: 1, package_name: "Standard audit package", status: "Draft", included_drivers: 2, included_vehicles: 2 },
  ]),
  auditPackage: (id: number) => withFallback(unwrap<AnyRecord>(apiClient.get(`/api/compliance/audit-packages/${id}`)), async () => ({ id, package_name: "Development fallback package", status: "Draft" })),
  createAuditPackage: (body: Record<string, unknown>) => withFallback(unwrap<AnyRecord>(apiClient.post("/api/compliance/audit-packages", body)), async () => ({ id: Date.now(), ...body, success: true })),
  finalizeAuditPackage: (id: number) => withFallback(unwrap<AnyRecord>(apiClient.post(`/api/compliance/audit-packages/${id}/finalize`, {})), async () => ({ id, status: "Finalized", success: true })),
  crossBorderWatch: () => withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/compliance/cross-border-watch")), async () => [
    { id: 1, country_code: "SA", issue: "ELD rule change watch", status: "Watch" },
  ]),
  driverStatus: () => withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/compliance/driver-status")), async () => (await complianceFallback()).summary.drivers),
  vehicleStatus: () => withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/compliance/vehicle-status")), async () => (await complianceFallback()).summary.vehicles),
  aiRecommendations: () => withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/compliance/ai/recommendations")), async () => [
    { id: 1, title: "Review expiring driver licenses", body: "Development fallback recommendation." },
  ]),
};

export const hosApi = {
  summary: () => withFallback(unwrap<AnyRecord>(apiClient.get("/api/hos/summary")), () => {
    const clocks = getHosDriverClocks();
    return {
      totalDrivers: clocks.length,
      hosOk: clocks.filter(d => d.status === "OK").length,
      hosWarning: clocks.filter(d => d.status === "Warning").length,
      hosViolation: clocks.filter(d => d.status === "Vehicle Blocked").length,
      eldMalfunction: 1,
    } as AnyRecord;
  }),
  drivers: () => withFallback(
    unwrap<AnyRecord[]>(apiClient.get("/api/hos/drivers")),
    () => getHosDriverClocks() as AnyRecord[],
  ),
  clocks: () => withFallback(
    unwrap<AnyRecord[]>(apiClient.get("/api/hos/clocks")),
    () => getHosDriverClocks() as AnyRecord[],
  ),
  logs: () => withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/hos/logs")), async () => []),
  driverLogs: (driverId: number) => withFallback(unwrap<AnyRecord[]>(apiClient.get(`/api/hos/logs/${driverId}`)), async () => []),
  certifyLog: (id: number) => withFallback(unwrap<AnyRecord>(apiClient.post(`/api/hos/logs/${id}/certify`, {})), async () => ({ id, status: "Certified", success: true })),
  aiRecommendations: () => withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/hos/ai/recommendations")), async () => []),
};

export const eldApi = {
  devices: () =>
    withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/eld/devices")), async () =>
      (await complianceFallback()).summary.vehicles?.map((vehicle: AnyRecord, index: number) => ({
        id: index + 1,
        status: vehicle.deviceStatus || "Online",
        vehicleCode: vehicle.vehicleId,
      })) ?? [],
    ),
  device: (id: number) => withFallback(unwrap<AnyRecord>(apiClient.get(`/api/eld/devices/${id}`)), async () => ({ id, status: "Online" })),
  markMalfunction: (id: number, body: Record<string, unknown>) => withFallback(unwrap<AnyRecord>(apiClient.post(`/api/eld/devices/${id}/mark-malfunction`, body)), async () => ({ id, status: "Malfunction", success: true })),
  resolveMalfunction: (id: number) => withFallback(unwrap<AnyRecord>(apiClient.post(`/api/eld/devices/${id}/resolve-malfunction`, {})), async () => ({ id, status: "Resolved", success: true })),
};

export const localizationApi = {
  countries: () => withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/localization/countries")), async () => [{ code: "US" }, { code: "SA" }, { code: "AE" }, { code: "PK" }]),
  languages: () => withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/localization/languages")), async () => [{ code: "en" }, { code: "ar" }]),
  settings: () => withFallback(unwrap<AnyRecord>(apiClient.get("/api/localization/settings")), async () => ({ timezone: "UTC", locale: "en-US" })),
  updateSettings: (body: Record<string, unknown>) => withFallback(unwrap<AnyRecord>(apiClient.put("/api/localization/settings", body)), async () => ({ success: true, ...body })),
  userPreferences: () => withFallback(unwrap<AnyRecord>(apiClient.get("/api/localization/user-preferences")), async () => ({ locale: "en-US" })),
  updateUserPreferences: (body: Record<string, unknown>) => withFallback(unwrap<AnyRecord>(apiClient.put("/api/localization/user-preferences", body)), async () => ({ success: true, ...body })),
};

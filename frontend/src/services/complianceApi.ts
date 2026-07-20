import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export const complianceApi = {
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/compliance/summary")),
  profiles: () => unwrap<AnyRecord[]>(apiClient.get("/api/compliance/profiles")),
  rules: () => unwrap<AnyRecord[]>(apiClient.get("/api/compliance/rules")),
  violations: () => unwrap<AnyRecord[]>(apiClient.get("/api/compliance/violations")),
  violation: (id: number) => unwrap<AnyRecord>(apiClient.get(`/api/compliance/violations/${id}`)),
  acknowledgeViolation: (id: number) => unwrap<AnyRecord>(apiClient.post(`/api/compliance/violations/${id}/acknowledge`, {})),
  resolveViolation: (id: number) => unwrap<AnyRecord>(apiClient.post(`/api/compliance/violations/${id}/resolve`, {})),
  documents: () => unwrap<AnyRecord[]>(apiClient.get("/api/compliance/documents")),
  auditPackages: () => unwrap<AnyRecord[]>(apiClient.get("/api/compliance/audit-packages")),
  auditPackage: (id: number) => unwrap<AnyRecord>(apiClient.get(`/api/compliance/audit-packages/${id}`)),
  createAuditPackage: (body: Record<string, unknown>) => unwrap<AnyRecord>(apiClient.post("/api/compliance/audit-packages", body)),
  finalizeAuditPackage: (id: number) => unwrap<AnyRecord>(apiClient.post(`/api/compliance/audit-packages/${id}/finalize`, {})),
  crossBorderWatch: () => unwrap<AnyRecord[]>(apiClient.get("/api/compliance/cross-border-watch")),
  driverStatus: () => unwrap<AnyRecord[]>(apiClient.get("/api/compliance/driver-status")),
  vehicleStatus: () => unwrap<AnyRecord[]>(apiClient.get("/api/compliance/vehicle-status")),
  aiRecommendations: () => unwrap<AnyRecord[]>(apiClient.get("/api/compliance/ai/recommendations")),
};

export const hosApi = {
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/hos/summary")),
  drivers: () => unwrap<AnyRecord[]>(apiClient.get("/api/hos/drivers")),
  clocks: () => unwrap<AnyRecord[]>(apiClient.get("/api/hos/clocks")),
  logs: () => unwrap<AnyRecord[]>(apiClient.get("/api/hos/logs")),
  driverLogs: (driverId: number) => unwrap<AnyRecord[]>(apiClient.get(`/api/hos/logs/${driverId}`)),
  certifyLog: (id: number) => unwrap<AnyRecord>(apiClient.post(`/api/hos/logs/${id}/certify`, {})),
  aiRecommendations: () => unwrap<AnyRecord[]>(apiClient.get("/api/hos/ai/recommendations")),
};

export const eldApi = {
  devices: () => unwrap<AnyRecord[]>(apiClient.get("/api/eld/devices")),
  device: (id: number) => unwrap<AnyRecord>(apiClient.get(`/api/eld/devices/${id}`)),
  markMalfunction: (id: number, body: Record<string, unknown>) => unwrap<AnyRecord>(apiClient.post(`/api/eld/devices/${id}/mark-malfunction`, body)),
  resolveMalfunction: (id: number) => unwrap<AnyRecord>(apiClient.post(`/api/eld/devices/${id}/resolve-malfunction`, {})),
};

export const localizationApi = {
  countries: () => unwrap<AnyRecord[]>(apiClient.get("/api/localization/countries")),
  languages: () => unwrap<AnyRecord[]>(apiClient.get("/api/localization/languages")),
  settings: () => unwrap<AnyRecord>(apiClient.get("/api/localization/settings")),
  updateSettings: (body: Record<string, unknown>) => unwrap<AnyRecord>(apiClient.put("/api/localization/settings", body)),
  userPreferences: () => unwrap<AnyRecord>(apiClient.get("/api/localization/user-preferences")),
  updateUserPreferences: (body: Record<string, unknown>) => unwrap<AnyRecord>(apiClient.put("/api/localization/user-preferences", body)),
};

import { apiClient, unwrap } from "./apiClient";
import type { AnyRecord } from "@/types";

export const telemetryApi = {
  liveMapSummary: () => unwrap<AnyRecord>(apiClient.get("/api/telemetry/live-map-summary")),
  liveStates: () => unwrap<AnyRecord[]>(apiClient.get("/api/telemetry/assets/live-state")),
  liveState: (vehicleId: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/telemetry/assets/${vehicleId}/live-state`)),
  devices: () => unwrap<AnyRecord[]>(apiClient.get("/api/telemetry/devices")),
  device: (id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/telemetry/devices/${id}`)),
  alerts: (status = "Open") => unwrap<AnyRecord[]>(apiClient.get(`/api/telemetry/alerts?status=${encodeURIComponent(status)}`)),
  acknowledgeAlert: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/telemetry/alerts/${id}/acknowledge`)),
  resolveAlert: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/telemetry/alerts/${id}/resolve`)),
  rules: () => unwrap<AnyRecord[]>(apiClient.get("/api/telemetry/rules")),
};

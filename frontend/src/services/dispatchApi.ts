import { apiClient, unwrap } from "./apiClient";
import type { AnyRecord } from "@/types";

export const dispatchApi = {
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/dispatch/summary")),
  board: () => unwrap<Record<string, AnyRecord[]>>(apiClient.get("/api/dispatch/board")),
  recommendations: () => unwrap<AnyRecord[]>(apiClient.get("/api/dispatch/recommendations")),
  availableDrivers: () => unwrap<AnyRecord[]>(apiClient.get("/api/dispatch/available-drivers")),
  availableVehicles: () => unwrap<AnyRecord[]>(apiClient.get("/api/dispatch/available-vehicles")),
  autoSuggest: () => unwrap<AnyRecord[]>(apiClient.post("/api/dispatch/auto-suggest", {})),
  assign: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/dispatch/assign", payload)),
  changeStatus: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/dispatch/status", payload)),
  sendEtaUpdates: () => unwrap<AnyRecord>(apiClient.post("/api/dispatch/send-eta-updates", {})),
};

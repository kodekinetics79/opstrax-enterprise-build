import { apiClient, unwrap } from "./apiClient";
import type { AnyRecord } from "@/types";

export const driversApi = {
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/drivers")),
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/drivers/summary")),
  detail: (id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/drivers/${id}`)),
  timeline: (id: string | number) => unwrap<AnyRecord[]>(apiClient.get(`/api/drivers/${id}/timeline`)),
  recommendations: (id: string | number) => unwrap<AnyRecord[]>(apiClient.get(`/api/drivers/${id}/recommendations`)),
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/drivers", payload)),
  update: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/drivers/${id}`, payload)),
  remove: (id: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/drivers/${id}`)),
  assignVehicle: (id: string | number, vehicleId: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/drivers/${id}/assign-vehicle`, { vehicleId })),
  changeStatus: (id: string | number, status: string) => unwrap<AnyRecord>(apiClient.post(`/api/drivers/${id}/change-status`, { status })),
};

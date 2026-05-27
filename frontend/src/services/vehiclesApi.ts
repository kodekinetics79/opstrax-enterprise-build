import { apiClient, unwrap } from "./apiClient";
import type { AnyRecord } from "@/types";

export const vehiclesApi = {
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/vehicles")),
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/vehicles/summary")),
  detail: (id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/vehicles/${id}`)),
  timeline: (id: string | number) => unwrap<AnyRecord[]>(apiClient.get(`/api/vehicles/${id}/timeline`)),
  recommendations: (id: string | number) => unwrap<AnyRecord[]>(apiClient.get(`/api/vehicles/${id}/recommendations`)),
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/vehicles", payload)),
  update: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/vehicles/${id}`, payload)),
  remove: (id: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/vehicles/${id}`)),
  assignDriver: (id: string | number, driverId: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/vehicles/${id}/assign-driver`, { driverId })),
  changeStatus: (id: string | number, status: string) => unwrap<AnyRecord>(apiClient.post(`/api/vehicles/${id}/change-status`, { status })),
};

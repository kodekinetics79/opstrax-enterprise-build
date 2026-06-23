import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export const tripApi = {
  list: (params?: {
    status?: string;
    vehicleId?: number;
    driverId?: number;
    routeId?: number;
    limit?: number;
    offset?: number;
  }) => unwrap<AnyRecord[]>(apiClient.get("/api/trips", { params })),

  detail: (id: number | string) =>
    unwrap<AnyRecord>(apiClient.get(`/api/trips/${id}`)),

  breadcrumbs: (id: number | string) =>
    unwrap<AnyRecord[]>(apiClient.get(`/api/trips/${id}/breadcrumbs`)),

  compliance: (id: number | string) =>
    unwrap<AnyRecord>(apiClient.get(`/api/trips/${id}/compliance`)),

  start: (id: number | string) =>
    unwrap<AnyRecord>(apiClient.post(`/api/trips/${id}/start`, {})),

  complete: (id: number | string) =>
    unwrap<AnyRecord>(apiClient.post(`/api/trips/${id}/complete`, {})),

  exception: (id: number | string, notes?: string) =>
    unwrap<AnyRecord>(apiClient.post(`/api/trips/${id}/exception`, { notes })),
};

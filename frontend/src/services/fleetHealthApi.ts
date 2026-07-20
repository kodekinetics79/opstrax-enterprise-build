import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export const fleetHealthApi = {
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/fleet-health/summary")),

  risks: (params?: { severity?: string; category?: string; limit?: number }) =>
    unwrap<AnyRecord[]>(apiClient.get("/api/fleet-health/risks", { params })),

  vehicleDetail: (id: number | string) =>
    unwrap<AnyRecord>(apiClient.get(`/api/fleet-health/vehicles/${id}`)),

  driverDetail: (id: number | string) =>
    unwrap<AnyRecord>(apiClient.get(`/api/fleet-health/drivers/${id}`)),
};

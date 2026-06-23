import { apiClient, unwrap } from "@/services/apiClient";
import { withFallback } from "@/services/fleetDomainApi";
import { getFleetHealthSummary, getFleetHealthRisks } from "@/data/developmentFleetSeedData";
import type { AnyRecord } from "@/types";

export const fleetHealthApi = {
  summary: () =>
    withFallback(
      unwrap<AnyRecord>(apiClient.get("/api/fleet-health/summary")),
      () => getFleetHealthSummary() as AnyRecord,
    ),

  risks: (params?: { severity?: string; category?: string; limit?: number }) =>
    withFallback(
      unwrap<AnyRecord[]>(apiClient.get("/api/fleet-health/risks", { params })),
      () => getFleetHealthRisks() as AnyRecord[],
    ),

  vehicleDetail: (id: number | string) =>
    unwrap<AnyRecord>(apiClient.get(`/api/fleet-health/vehicles/${id}`)),

  driverDetail: (id: number | string) =>
    unwrap<AnyRecord>(apiClient.get(`/api/fleet-health/drivers/${id}`)),
};

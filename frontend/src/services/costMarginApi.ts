import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export const costMarginApi = {
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/cost-margin/summary")),
  jobs: () => unwrap<AnyRecord[]>(apiClient.get("/api/cost-margin/jobs")),
  routes: () => unwrap<AnyRecord[]>(apiClient.get("/api/cost-margin/routes")),
  vehicles: () => unwrap<AnyRecord[]>(apiClient.get("/api/cost-margin/vehicles")),
  customers: () => unwrap<AnyRecord[]>(apiClient.get("/api/cost-margin/customers")),
  predictions: () => unwrap<AnyRecord[]>(apiClient.get("/api/cost-margin/predictions")),
  recommendations: () => unwrap<AnyRecord[]>(apiClient.get("/api/cost-margin/recommendations")),
  recalculate: () => unwrap<AnyRecord>(apiClient.post("/api/cost-margin/recalculate", {})),
};

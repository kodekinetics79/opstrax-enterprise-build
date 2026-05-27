import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export const costLeakageApi = {
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/cost-leakage/summary")),
  items: () => unwrap<AnyRecord[]>(apiClient.get("/api/cost-leakage/items")),
  recommendations: () => unwrap<AnyRecord[]>(apiClient.get("/api/cost-leakage/recommendations")),
  acknowledge: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/cost-leakage/items/${id}/acknowledge`, {})),
  createAction: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post(`/api/cost-leakage/items/${id}/create-action`, payload)),
};

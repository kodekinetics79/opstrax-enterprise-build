import { apiClient, unwrap } from "./apiClient";
import type { AnyRecord } from "@/types";

export const customerEtaApi = {
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/customer-eta/summary")),
  track: (trackingCode: string) => unwrap<AnyRecord>(apiClient.get(`/api/customer-eta/track/${trackingCode}`)),
  job: (jobId: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/customer-eta/job/${jobId}`)),
  sendUpdate: (jobId: string | number, payload: AnyRecord = {}) => unwrap<AnyRecord>(apiClient.post(`/api/customer-eta/job/${jobId}/send-update`, payload)),
  feedback: (jobId: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post(`/api/customer-eta/job/${jobId}/feedback`, payload)),
  communications: () => unwrap<AnyRecord[]>(apiClient.get("/api/customer-eta/communications")),
  recommendations: () => unwrap<AnyRecord[]>(apiClient.get("/api/customer-eta/recommendations")),
};

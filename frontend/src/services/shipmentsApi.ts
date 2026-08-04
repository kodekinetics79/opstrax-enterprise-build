import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";
import { downloadServerExport, getShipmentById, getShipments } from "@/services/fleetDomainApi";

export { getShipmentById, getShipments };

export interface ProofOfDeliveryPage {
  items: AnyRecord[];
  total: number;
  limit: number;
  offset: number;
}

export interface ProofOfDeliveryFilters {
  status?: string;
  search?: string;
  limit?: number;
  offset?: number;
  jobId?: string | number;
}

function podParams(input: ProofOfDeliveryFilters) {
  return Object.fromEntries(Object.entries(input).filter(([, value]) => value !== undefined && value !== "" && value !== "All"));
}

export const shipmentsApi = {
  list: () => getShipments(),
  detail: (id: string | number) => getShipmentById(id),
  proofOfDelivery: async (input: ProofOfDeliveryFilters = {}): Promise<ProofOfDeliveryPage> => {
    const response = await apiClient.get("/api/proof-of-delivery", { params: podParams(input) });
    const envelope = response.data as { success: boolean; data: AnyRecord[]; message?: string };
    if (!envelope.success) throw new Error(envelope.message || "Unable to load proof of delivery records");
    const items = envelope.data ?? [];
    const total = Number(response.headers?.["x-total-count"] ?? items.length);
    return { items, total: Number.isFinite(total) ? total : items.length, limit: input.limit ?? 25, offset: input.offset ?? 0 };
  },
  proofOfDeliverySummary: () => unwrap<AnyRecord>(apiClient.get("/api/proof-of-delivery/summary")),
  proofOfDeliveryDetail: (proofId: string | number) =>
    unwrap<AnyRecord>(apiClient.get(`/api/proof-of-delivery/${proofId}`)),
  uploadProofEvidence: (jobId: string | number, file: Blob, kind: "photo" | "signature" | "document", filename: string) => {
    const form = new FormData();
    form.append("file", file, filename);
    form.append("kind", kind);
    return unwrap<AnyRecord>(apiClient.post(`/api/jobs/${jobId}/proof/upload`, form));
  },
  submitProofOfDelivery: (jobId: string | number, payload: AnyRecord) =>
    unwrap<AnyRecord>(apiClient.post(`/api/jobs/${jobId}/proof`, payload)),
  verifyProofOfDelivery: (proofId: string | number) =>
    unwrap<AnyRecord>(apiClient.post(`/api/proof-of-delivery/${proofId}/verify`, {})),
  rejectProofOfDelivery: (proofId: string | number, reason: string) =>
    unwrap<AnyRecord>(apiClient.post(`/api/proof-of-delivery/${proofId}/reject`, { reason })),
  exportProofOfDelivery: (filters: Pick<ProofOfDeliveryFilters, "status" | "search"> = {}) => {
    const query = new URLSearchParams(podParams(filters) as Record<string, string>).toString();
    return downloadServerExport(`/api/proof-of-delivery/export${query ? `?${query}` : ""}`, `proof-of-delivery-${new Date().toISOString().slice(0, 10)}.csv`);
  },
};

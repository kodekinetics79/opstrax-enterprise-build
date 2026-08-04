import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

// Tax engine admin (ADR-008 P3): manage tax_profiles + decision-table rules, publish (maker-checker),
// and the seller VAT registration. Backend: TaxEndpoints.cs.
export const taxApi = {
  profiles: () => unwrap<AnyRecord[]>(apiClient.get("/api/tax/profiles")),
  profile: (id: number | string) => unwrap<AnyRecord>(apiClient.get(`/api/tax/profiles/${id}`)),
  upsertProfile: (body: Record<string, unknown>) => unwrap<AnyRecord>(apiClient.post("/api/tax/profiles", body)),
  addRule: (id: number | string, body: Record<string, unknown>) => unwrap<AnyRecord>(apiClient.post(`/api/tax/profiles/${id}/rules`, body)),
  publish: (id: number | string) => unwrap<AnyRecord>(apiClient.post(`/api/tax/profiles/${id}/publish`, {})),

  sellerRegistration: (jurisdiction: string, regime: string) =>
    unwrap<AnyRecord>(apiClient.get(`/api/tax/seller-registration?jurisdiction=${encodeURIComponent(jurisdiction)}&regime=${encodeURIComponent(regime)}`)),
  upsertSellerRegistration: (body: Record<string, unknown>) => unwrap<AnyRecord>(apiClient.post("/api/tax/seller-registration", body)),
};

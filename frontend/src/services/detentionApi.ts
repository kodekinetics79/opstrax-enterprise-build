import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

// Detention Recovery (the Provable Money Layer wedge). Backend: DetentionService/-ReviewService.
export const detentionApi = {
  funnel: (from?: string, to?: string) =>
    unwrap<AnyRecord>(apiClient.get("/api/detention/funnel", { params: { from, to } })),
  dwells: (status?: string) =>
    unwrap<AnyRecord[]>(apiClient.get("/api/detention/dwells", { params: status ? { status } : {} })),
  approve: (id: number | string, overrideNote?: string) =>
    unwrap<AnyRecord>(apiClient.post(`/api/detention/dwells/${id}/approve`, overrideNote ? { overrideNote } : {})),
  dismiss: (id: number | string, reason: string) =>
    unwrap<AnyRecord>(apiClient.post(`/api/detention/dwells/${id}/dismiss`, { reason })),
  setAppointment: (id: number | string, appointmentAt: string) =>
    unwrap<AnyRecord>(apiClient.put(`/api/detention/dwells/${id}/appointment`, { appointmentAt })),
  attestNoAppointment: (id: number | string) =>
    unwrap<AnyRecord>(apiClient.post(`/api/detention/dwells/${id}/attest-no-appointment`, {})),
  shareEvidence: (id: number | string) =>
    unwrap<AnyRecord>(apiClient.post(`/api/detention/dwells/${id}/evidence/share`, {})),
  stranded: () => unwrap<AnyRecord[]>(apiClient.get("/api/detention/stranded")),
  ruleCards: () => unwrap<AnyRecord[]>(apiClient.get("/api/detention/rule-cards")),
  saveRuleCard: (b: {
    customerId?: number; freeMinutes: number; ratePerHour: number;
    billingIncrementMinutes?: number; maxChargeAmount?: number; claimWindowDays?: number; noticePercent?: number;
  }) => unwrap<AnyRecord>(apiClient.post("/api/detention/rule-cards", b)),
  publicEvidence: (token: string) =>
    unwrap<AnyRecord>(apiClient.get(`/api/public/detention/evidence/${token}`)),
};

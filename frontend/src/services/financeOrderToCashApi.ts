import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export type RecordInvoicePaymentInput = {
  amount: number;
  currency: string;
  paymentReference: string;
  paymentMethod: string;
};

export type IssueInvoiceResult = AnyRecord & {
  approvalRequired?: boolean;
  approvalRequestId?: number;
  message?: string;
};

export const financeOrderToCashApi = {
  jobCharges: (jobId: string) =>
    unwrap<AnyRecord[]>(apiClient.get("/api/job-charges", { params: { jobId } })),

  createJobCharge: (input: { jobId: number; chargeCode: string; chargeName: string; description: string; quantity: number; unitRate: number; amount: number; currency: string }) =>
    unwrap<AnyRecord>(apiClient.post("/api/job-charges", { ...input, chargeType: "base", status: "pending" })),

  markReadyToBill: (jobId: string) =>
    unwrap<AnyRecord>(apiClient.post(`/api/jobs/${encodeURIComponent(jobId)}/mark-ready-to-bill`, {})),

  createInvoiceDraft: (jobId: string, idempotencyKey: string) =>
    unwrap<AnyRecord>(apiClient.post(`/api/jobs/${encodeURIComponent(jobId)}/invoice-draft`, {}, { headers: { "Idempotency-Key": idempotencyKey } })),

  pendingApprovals: () =>
    unwrap<AnyRecord[]>(apiClient.get("/api/approval-requests", { params: { status: "pending" } })),

  decideApproval: (requestId: number, decision: "approved" | "rejected", notes: string) =>
    unwrap<AnyRecord>(apiClient.post(`/api/approval-requests/${requestId}/decide`, { decision, notes })),

  invoiceDrafts: () =>
    unwrap<{ items: AnyRecord[] }>(apiClient.get("/api/invoice-drafts"))
      .then((result) => result.items ?? []),

  issueInvoiceDraft: (draftId: string, idempotencyKey: string) =>
    unwrap<IssueInvoiceResult>(apiClient.post(`/api/invoice-drafts/${encodeURIComponent(draftId)}/issue`, {
      idempotencyKey,
    })),

  recordInvoicePayment: (invoiceId: string, input: RecordInvoicePaymentInput) =>
    unwrap<AnyRecord>(apiClient.post(`/api/issued-invoices/${encodeURIComponent(invoiceId)}/payments`, input)),
};

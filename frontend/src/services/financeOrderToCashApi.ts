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

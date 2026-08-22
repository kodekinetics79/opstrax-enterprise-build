import { apiClient, unwrap } from "./apiClient";
import type { AnyRecord } from "@/types";

// Customer Portal — real backend calls only (session-auth, customer_portal:view).
// Every endpoint is scoped server-side to the authenticated customer_user's own
// customer_id; there is no seed/demo fallback anywhere in this client.
export const portalApi = {
  invoices: () =>
    unwrap<{ items: AnyRecord[] }>(apiClient.get("/api/portal/invoices")).then((r) => r.items ?? []),

  // One invoice with its line detail and tax breakdown. The list deliberately
  // carries only summary fields; this is what the customer opens to check what
  // they are actually being charged for.
  invoice: (invoiceId: string) =>
    unwrap<AnyRecord>(apiClient.get(`/api/portal/invoices/${invoiceId}`)),

  jobs: () =>
    unwrap<{ items: AnyRecord[] }>(apiClient.get("/api/portal/jobs")).then((r) => r.items ?? []),

  jobDetail: (jobId: number | string) =>
    unwrap<AnyRecord>(apiClient.get(`/api/portal/jobs/${jobId}`)),

  jobProofs: (jobId: number | string) =>
    unwrap<{ items: AnyRecord[] }>(apiClient.get(`/api/portal/jobs/${jobId}/proofs`)).then((r) => r.items ?? []),

  feedback: () =>
    unwrap<{ items: AnyRecord[] }>(apiClient.get("/api/portal/feedback")).then((r) => r.items ?? []),

  submitFeedback: (payload: AnyRecord) =>
    unwrap<AnyRecord>(apiClient.post("/api/portal/feedback", payload)),
};

import { apiClient, unwrap } from "@/services/apiClient";
import { getCustomerById, getCustomers } from "@/services/fleetDomainApi";
import type { AnyRecord } from "@/types";

// Every read here hits a real, tenant-scoped .NET endpoint. Nothing is recomputed
// client-side and nothing is fabricated: if the backend fails, the call throws and
// the page shows an honest error state.

/** GET /api/customers/summary — computed server-side, scoped by company_id. */
export interface CustomerSummary {
  total: number;
  active: number;
  atRisk: number;
  slaHealthScore: number;
  deliveryExperienceScore: number;
  platinumAccounts: number;
  // Structural index so the object still satisfies the generic AnyRecord-shaped
  // EntityApi contract consumed by EntityListPage.
  [key: string]: unknown;
}

/** GET /api/customers/{id}/timeline — audit/domain events for one account. */
export interface CustomerTimelineEntry {
  id?: string | number;
  eventType?: string;
  title?: string;
  description?: string;
  severity?: string;
  eventTime?: string;
  actor?: string;
  [key: string]: unknown;
}

/** GET /api/customers/{id}/recommendations — rows from ai_recommendations. */
export interface CustomerRecommendation {
  id?: string | number;
  title?: string;
  body?: string;
  score?: number;
  status?: string;
  moduleKey?: string;
  [key: string]: unknown;
}

export type BulkAction = "set-status" | "set-tier" | "delete" | "restore";
export type CustomerStatus = "Active" | "At Risk" | "Inactive";
export type CustomerSlaTier = "Standard" | "Gold" | "Platinum";

export interface BulkPatch {
  status?: CustomerStatus;
  slaTier?: CustomerSlaTier;
}

export interface BulkOptions {
  patch?: BulkPatch;
  /** Required literal "DELETE" for the soft-delete action. */
  confirm?: "DELETE";
}

export interface BulkResultRow {
  id: number;
  ok: boolean;
  error?: string;
}

/** POST /api/customers/bulk — partial success is normal: inspect `results` per id. */
export interface BulkResponse {
  action: BulkAction;
  requested: number;
  succeeded: number;
  failed: number;
  results: BulkResultRow[];
}

/** Max ids the backend accepts in one bulk batch. */
export const BULK_MAX_IDS = 200;

export const customersApi = {
  list: () => getCustomers(),

  // Real endpoint — the previous client-side recompute never produced `active`
  // and could not see rows outside the current page/filter.
  summary: () => unwrap<CustomerSummary>(apiClient.get("/api/customers/summary")),

  detail: (id: string | number) => getCustomerById(id),

  timeline: (id: string | number) =>
    unwrap<CustomerTimelineEntry[]>(apiClient.get(`/api/customers/${id}/timeline`)),

  recommendations: (id: string | number) =>
    unwrap<CustomerRecommendation[]>(apiClient.get(`/api/customers/${id}/recommendations`)),

  health: (id: string | number) =>
    unwrap<AnyRecord>(apiClient.get(`/api/customers/${id}/health`)),

  // Writes must be truthful — surface backend failures instead of faking success.
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/customers", payload)),
  update: (id: string | number, payload: AnyRecord) =>
    unwrap<AnyRecord>(apiClient.put(`/api/customers/${id}`, payload)),
  remove: (id: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/customers/${id}`)),

  // Batch write. Ids are de-duplicated and coerced to numbers client-side, but the
  // server re-validates and scopes every statement by company_id — ids alone are
  // never trusted. `delete` is a SOFT delete and requires confirm: "DELETE".
  bulk: (action: BulkAction, ids: Array<string | number>, opts: BulkOptions = {}) => {
    const uniqueIds = [...new Set(ids.map(Number))].filter((id) => Number.isFinite(id));
    if (uniqueIds.length === 0) throw new Error("Select at least one customer.");
    if (uniqueIds.length > BULK_MAX_IDS) {
      throw new Error(`Too many customers selected (${uniqueIds.length}). Maximum is ${BULK_MAX_IDS}.`);
    }
    return unwrap<BulkResponse>(
      apiClient.post("/api/customers/bulk", {
        action,
        ids: uniqueIds,
        ...(opts.patch ? { patch: opts.patch } : {}),
        ...(opts.confirm ? { confirm: opts.confirm } : {}),
      }),
    );
  },
};

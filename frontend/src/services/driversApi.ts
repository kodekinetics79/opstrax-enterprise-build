import { apiClient, unwrap } from "@/services/apiClient";
import { apiPaged, getDriverById, getDrivers } from "@/services/fleetDomainApi";
import type { AnyRecord } from "@/types";

export const driversApi = {
  list: () => getDrivers(),
  listPaged: (opts?: { limit?: number; offset?: number; search?: string }) => apiPaged("/api/drivers", opts),
  // Use the tenant-wide aggregate endpoint rather than calculating KPIs from the
  // capped driver list. Its camel-cased aliases match the existing page contract.
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/drivers/summary")),
  detail: (id: string | number) => getDriverById(id),
  // Recommendations come from the live detail envelope — never fabricated client-side.
  recommendations: (id: string | number) => getDriverById(id).then((detail) => (Array.isArray(detail.recommendations) ? detail.recommendations : [])),
  // Real CSV import pipeline — server-validated preview, then committed upsert.
  importPreview: (rows: AnyRecord[]) => unwrap<AnyRecord>(apiClient.post("/api/drivers/import-preview", { rows })),
  importCommit: (rows: AnyRecord[]) => unwrap<AnyRecord>(apiClient.post("/api/drivers/import", { rows }, { timeout: 120000 })),
  // Writes must be truthful — surface backend failures instead of faking success.
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/drivers", payload)),
  update: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/drivers/${id}`, payload)),
  remove: (id: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/drivers/${id}`)),
  assignVehicle: (id: string | number, vehicleId: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/drivers/${id}/assign-vehicle`, { vehicleId })),
  changeStatus: (id: string | number, status: string) => unwrap<AnyRecord>(apiClient.post(`/api/drivers/${id}/change-status`, { status })),

  // Driver-portal access. Creates the login behind a driver record and links
  // drivers.user_id — without this the driver app cannot identify the caller and every
  // /api/driver/* route 403s. Returns a temporary password to hand to the driver (SMTP is
  // not configured, so nothing is emailed; see the response `temporaryPassword`).
  portalInvite: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/drivers/${id}/portal-invite`, {})),
  portalInviteBulk: (driverIds: Array<string | number>) => unwrap<AnyRecord>(apiClient.post("/api/drivers/portal-invite/bulk", { driverIds })),
  portalRevoke: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/drivers/${id}/portal-revoke`, {})),
};

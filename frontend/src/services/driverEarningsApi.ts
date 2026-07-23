import { apiClient, unwrap } from "./apiClient";
import type { AnyRecord } from "@/types";

// Driver self-service earnings. Identity is derived from the auth session server-side; a driverId
// in the request is never authoritative. Pure-live: NO withFallback seed — a failed call must show
// an honest error, never fabricated pay numbers.

export const driverEarningsApi = {
  // The whole earnings screen in one call: live open-period preview, YTD/lifetime, detention pay,
  // last payment, and the recent committed statements list.
  earnings: () =>
    unwrap<AnyRecord>(apiClient.get("/api/driver/earnings")),

  // One owned statement's receipt (header + lines + payments). 404 for anything not the driver's own.
  statement: (id: number | string) =>
    unwrap<AnyRecord>(apiClient.get(`/api/driver/earnings/statements/${id}`)),
};

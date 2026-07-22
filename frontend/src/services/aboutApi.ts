import { apiClient, unwrap } from "./apiClient";
import type { AnyRecord } from "@/types";

// Uses the shared authenticated client: /api/about/* sits behind the bearer
// middleware, and apiClient carries the token plus the real base-URL env vars
// (a raw fetch with VITE_API_URL pointed every deployed build at localhost).
export const aboutApi = {
  platform:      (): Promise<AnyRecord> => unwrap<AnyRecord>(apiClient.get("/api/about/platform")),
  healthSummary: (): Promise<AnyRecord> => unwrap<AnyRecord>(apiClient.get("/api/about/health-summary")),
};

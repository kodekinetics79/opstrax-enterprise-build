import type { AnyRecord } from "@/types";
import { getDashboardSummary } from "@/services/fleetDomainApi";

export { getDashboardSummary };

export const commandCenterApi = {
  summary: () => getDashboardSummary() as Promise<AnyRecord>,
};

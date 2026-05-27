import { apiClient, unwrap } from "./apiClient";
import type { AnyRecord } from "@/types";

export type ModulePayload = {
  moduleKey?: string;
  sourceTable?: string;
  summary?: AnyRecord;
  records?: AnyRecord[];
  insights?: AnyRecord[];
};

const dedicatedEndpoints: Record<string, string> = {
  "route-planning": "/api/route-planning",
  assets: "/api/assets",
  maintenance: "/api/maintenance",
  "work-orders": "/api/work-orders",
  "fuel-idling": "/api/fuel-idling",
  safety: "/api/safety",
  dashcam: "/api/dashcam",
  compliance: "/api/compliance",
  "hos-eld": "/api/hos-eld",
  "dvir-inspections": "/api/dvir-inspections",
  "customer-portal": "/api/customer-portal",
  customers: "/api/customers",
  "contracts-rates": "/api/contracts-rates",
  "carrier-management": "/api/carrier-management",
  expenses: "/api/expenses",
  documents: "/api/documents",
  "reports-analytics": "/api/reports-analytics",
  "sla-kpi": "/api/sla-kpi",
  "predictive-margin": "/api/predictive-margin",
  "audit-logs": "/api/audit-logs",
  integrations: "/api/integrations",
  "user-management": "/api/user-management",
  settings: "/api/settings",
  billing: "/api/billing",
};

function endpointFor(moduleKey: string) {
  return dedicatedEndpoints[moduleKey] || `/api/modules/${moduleKey}`;
}

export const modulesApi = {
  get: (moduleKey: string) => unwrap<ModulePayload>(apiClient.get(endpointFor(moduleKey))),
  detail: (moduleKey: string, id: string | number) => unwrap<AnyRecord>(apiClient.get(`${endpointFor(moduleKey)}/${id}`)),
};

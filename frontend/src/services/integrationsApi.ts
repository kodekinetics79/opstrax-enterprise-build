import { apiClient as primaryApiClient, unwrap } from "./apiClient";
import type { AnyRecord } from "@/types";

// Integration calls go to the PRIMARY .NET API (Opstrax.Api). It owns the full
// connector lifecycle (list/detail/create/update/remove/connect/configure/sync/
// disconnect), is tenant-scoped, and — critically — hydrates the connector catalog
// per tenant on first read (IntegrationCatalog.EnsureTenantAsync), so the module is
// never empty. This used to point at the Node :8090 side-service, which is not part
// of the production (Render) deployment — when it was unreachable the page rendered
// blank. Routing to the primary API fixes that and keeps everything on one backend.
const apiClient = primaryApiClient;

export type IntegrationCategory =
  | "ERP & Accounting"
  | "Telematics & ELD"
  | "Fuel Cards"
  | "Maps & Routing"
  | "Messaging & Notifications"
  | "WMS & Shipment Ops"
  | "IoT & Sensors"
  | "Compliance";

export type IntegrationStatus = "Connected" | "Disconnected" | "Pending" | "Error";
export type IntegrationActivityStatus = "Success" | "Pending" | "Error";

export type IntegrationRecord = {
  id: number;
  key: string;
  name: string;
  category: IntegrationCategory;
  description: string;
  logo: string;
  status: IntegrationStatus;
  sync: string;
  lastSyncAt: string | null;
  relatedSystems: string[];
  connectedTo: string[];
  managedBy: string;
  scope: "tenant" | "platform";
  tenantId: number;
  config: Record<string, string | number | boolean | null>;
  // True for tenant-created connectors (fully editable/deletable). Built-in catalog
  // connectors are is_custom=false and are reset rather than deleted.
  isCustom?: boolean;
  // Connector health from the last real handshake (test-connection).
  lastTestedAt?: string | null;
  lastTestOk?: boolean | null;
  lastTestMessage?: string | null;
};

export type IntegrationActivity = {
  id: number;
  integrationId: number;
  integration: string;
  event: string;
  ts: string;
  status: IntegrationActivityStatus;
  records: number;
  details?: string;
};

export type IntegrationsSummary = {
  total: number;
  connected: number;
  pending: number;
  errors: number;
  categories: number;
  lastUpdated: string;
};

export type IntegrationsPayload = {
  moduleKey: "integrations";
  tenantId: number;
  summary: IntegrationsSummary;
  records: IntegrationRecord[];
  activity: IntegrationActivity[];
};

export type IntegrationDetailPayload = {
  record: IntegrationRecord;
  activity: IntegrationActivity[];
};

export type IntegrationConfig = Record<string, string | number | boolean | null>;

// Result of a real provider handshake / live action.
export type IntegrationTestResult = {
  success: boolean;
  status?: string;
  message: string;
  details?: Record<string, unknown> | null;
};

export const integrationsApi = {
  list: () => unwrap<IntegrationsPayload>(apiClient.get("/api/integrations")),
  detail: (id: number | string) =>
    unwrap<IntegrationDetailPayload>(apiClient.get(`/api/integrations/${id}`)),
  connect: (id: number | string) =>
    unwrap<IntegrationDetailPayload>(apiClient.post(`/api/integrations/${id}/connect`, {})),
  configure: (id: number | string, config: IntegrationConfig) =>
    unwrap<IntegrationDetailPayload>(apiClient.post(`/api/integrations/${id}/configure`, config)),
  sync: (id: number | string) =>
    unwrap<IntegrationDetailPayload>(apiClient.post(`/api/integrations/${id}/sync`, {})),
  disconnect: (id: number | string) =>
    unwrap<IntegrationDetailPayload>(apiClient.post(`/api/integrations/${id}/disconnect`, {})),
  // Real connectivity: performs an actual handshake with the provider and returns the
  // true result (success only when the provider accepts the credentials).
  testConnection: (id: number | string) =>
    unwrap<IntegrationTestResult>(apiClient.post(`/api/integrations/${id}/test-connection`, {})),
  // Provider-specific live action (e.g. Twilio { action: "send-test", to, body }).
  runAction: (id: number | string, body: Record<string, unknown>) =>
    unwrap<IntegrationTestResult>(apiClient.post(`/api/integrations/${id}/run-action`, body)),
  // Full CRUD (control-tower). create adds a custom connector; update edits any field on
  // any integration (built-in or custom); remove hard-deletes a custom one or resets a
  // built-in to catalog defaults.
  create: (payload: IntegrationWriteInput) =>
    unwrap<IntegrationDetailPayload>(apiClient.post("/api/integrations", payload)),
  update: (id: number | string, payload: IntegrationWriteInput) =>
    unwrap<IntegrationDetailPayload>(apiClient.put(`/api/integrations/${id}`, payload)),
  remove: (id: number | string) =>
    unwrap<IntegrationDetailPayload & { removed?: boolean; reset?: boolean }>(apiClient.delete(`/api/integrations/${id}`)),
};

export type IntegrationWriteInput = {
  name?: string;
  category?: IntegrationCategory | string;
  description?: string;
  logo?: string;
  status?: IntegrationStatus;
  scope?: "tenant" | "platform";
  managedBy?: string;
  relatedSystems?: string[];
  connectedTo?: string[];
  config?: IntegrationConfig;
};

export function isIntegrationConfig(value: AnyRecord): value is IntegrationConfig {
  return Object.values(value).every((entry) => ["string", "number", "boolean"].includes(typeof entry) || entry === null);
}

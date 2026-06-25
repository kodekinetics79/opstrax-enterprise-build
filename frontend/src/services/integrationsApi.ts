import { nodeApiClient, unwrap } from "./apiClient";
import type { AnyRecord } from "@/types";

// Integration calls go to the Node.js backend because it owns the live connector
// lifecycle operations (list/detail/connect/configure/sync/disconnect) and now
// receives the authenticated user session plus tenant header from the shared client.
const apiClient = nodeApiClient;

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
    unwrap<IntegrationDetailPayload>(apiClient.delete(`/api/integrations/${id}`)),
};

export function isIntegrationConfig(value: AnyRecord): value is IntegrationConfig {
  return Object.values(value).every((entry) => ["string", "number", "boolean"].includes(typeof entry) || entry === null);
}

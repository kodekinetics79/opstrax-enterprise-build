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


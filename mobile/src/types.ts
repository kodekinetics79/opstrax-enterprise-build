export type ApiEnvelope<T> = {
  success: boolean;
  data: T;
  message: string;
  errors?: string[];
};

export type JsonValue =
  | string
  | number
  | boolean
  | null
  | { [key: string]: JsonValue }
  | JsonValue[];

export type JsonRecord = Record<string, JsonValue | unknown>;

export type MobileSession = {
  token: string;
  csrfToken: string;
  role: string;
  user: {
    id: number | string;
    email: string;
    name: string;
  };
  company: {
    name: string;
    code: string;
    id?: number | string;
  };
  permissions: string[];
};

export type MobileSessionEnvelope = {
  session: MobileSession;
  expiresAt?: string;
};

export type WorkspaceRole =
  | "driverOperator"
  | "fieldWorker"
  | "dispatcherSupervisor"
  | "warehousePickup"
  | "customerClient"
  | "safetyMaintenance"
  | "tenantAdmin"
  | "platformAdmin"
  | "general";

export type WorkflowSummary = {
  jobId: number | null;
  executionSummary: JsonRecord | null;
  recommendations: JsonRecord[] | null;
  siteAccess: JsonRecord[] | null;
  pickupAuthorizations: JsonRecord[] | null;
  warehouseHandovers: JsonRecord[] | null;
  proofPackages: JsonRecord[] | null;
  proofArtifacts: JsonRecord[] | null;
  billingConfidence: JsonRecord | null;
  telemetry: JsonRecord | null;
  safety: JsonRecord | null;
  maintenance: JsonRecord | null;
};


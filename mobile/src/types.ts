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
    country?: string;
    currency?: string;
  };
  permissions: string[];
  entitlements?: Record<string, boolean>;
  entitlementPolicyMode?: string;
};

export type MobileSessionEnvelope = {
  session: MobileSession;
  expiresAt?: string;
};

export type MfaChallenge = {
  mfaRequired: true;
  challengeToken: string;
  email: string;
};

export type LoginResult = MobileSession | MobileSessionEnvelope | MfaChallenge;

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

export type DriverAssignment = JsonRecord & {
  id: number | string;
  assignmentStatus?: string;
  shipmentNumber?: string;
  customerName?: string;
  pickupAddress?: string;
  dropoffAddress?: string;
  plannedPickupAt?: string;
  plannedDeliveryAt?: string;
  vehicleCode?: string;
  vehicleOos?: boolean;
  openExceptions?: number;
};

export type DriverCurrentAssignment = {
  assignment?: DriverAssignment;
  driverNextStatuses?: string[];
};

export type DriverProfile = {
  driver?: JsonRecord & {
    id?: number | string;
    fullName?: string;
    status?: string;
    vehicleId?: number | string;
    vehicleCode?: string;
    vehicleOos?: boolean;
    vehicleAvailabilityStatus?: string;
  };
  currentAssignment?: DriverAssignment;
  vehicleBlocking?: {
    criticalDefects?: number;
    blocked?: boolean;
    reason?: string | null;
  };
  hos?: {
    dataAvailable?: boolean;
    remainingDriveHours?: number;
    remainingShiftHours?: number;
    hosStatus?: string;
  };
  coaching?: { pendingCount?: number };
  guidance?: { level?: string; message?: string; type?: string }[];
};

export type DriverProofArtifact = {
  kind: "photo" | "signature";
  reference: string;
  contentType?: string;
  size?: number;
  previewUrl?: string;
};

import { apiClient, unwrap } from "@/services/apiClient";
import { downloadServerExport } from "@/services/fleetDomainApi";
import { isCustomerPortalRole, isDriverPortalRole, resolveCustomerIdentity, resolveDriverIdentity } from "@/auth/accessScope";
import { hasPermission } from "@/auth/rbacConfig";
import { readRawSession } from "@/auth/sessionStorage";
import { integrationsApi } from "@/services/integrationsApi";
import { fleetColdChainApi, type TemperatureAlert, type TemperatureDevice, type TemperatureZone } from "@/services/fleetTmsApi";
import type { AnyRecord, UserSession } from "@/types";

type DeviceMutationPayload = Record<string, unknown>;

export type DeviceSuspensionReceipt = {
  id: string;
  status: "Suspended";
  deviceState: "Suspended";
  rowVersion: number;
};

export class DeviceSuspensionOutcomeError extends Error {
  constructor(public readonly outcome: "rejected" | "unconfirmed") {
    super(outcome === "rejected"
      ? "The suspension request was rejected. Refresh the device record before retrying."
      : "The suspension outcome could not be confirmed. Refresh the device record before trying again.");
    this.name = "DeviceSuspensionOutcomeError";
  }
}

export type DeviceActivationReceipt = {
  id: string;
  status: "Active";
  deviceState: "Registered" | "Installed" | "Verified" | null;
  rowVersion: number;
  idempotentReplay: boolean;
};

export class DeviceActivationOutcomeError extends Error {
  constructor(public readonly outcome: "rejected" | "unconfirmed") {
    super(outcome === "rejected"
      ? "The activation request was rejected. Refresh the device record before retrying."
      : "The activation outcome could not be confirmed. Refresh the device record before trying again.");
    this.name = "DeviceActivationOutcomeError";
  }
}

export type DeviceCommissioningReceipt = {
  /** Installation identity, not device identity. This acknowledges a recorded observation only. */
  id: string;
  commissioningResult: "Passed" | "Failed";
  status: "Verified" | "Failed";
  rowVersion: number;
};

export class DeviceCommissioningOutcomeError extends Error {
  constructor(public readonly outcome: "rejected" | "unconfirmed") {
    super(outcome === "rejected"
      ? "The commissioning request was rejected. Refresh the installation record before retrying."
      : "The commissioning recording outcome could not be confirmed. Check the installation record before trying again.");
    this.name = "DeviceCommissioningOutcomeError";
  }
}

export type DeviceRemovalReceipt = {
  /** Installation-record acknowledgement only, not proof of physical removal. */
  id: string;
  status: "Removed";
  effectiveTo: string;
};

export class DeviceRemovalOutcomeError extends Error {
  constructor(public readonly outcome: "rejected" | "unconfirmed") {
    super(outcome === "rejected"
      ? "The removal request was rejected. Inspect the current installation and history before submitting again."
      : "The removal recording outcome could not be confirmed. Inspect the current installation and history before any new manual submission.");
    this.name = "DeviceRemovalOutcomeError";
  }
}

// Removal's existing form supplies milliseconds. Refuse finer precision instead
// of rounding distinct request/receipt instants together; no persistence claim follows.
function removalEffectiveInstant(value: unknown): number | null {
  if (typeof value !== "string") return null;
  const parts = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?(Z|[+-]\d{2}:\d{2})$/.exec(value);
  if (!parts) return null;
  const [, yearText, monthText, dayText, hourText, minuteText, secondText, fraction, offset] = parts;
  const [year, month, day, hour, minute, second] = [yearText, monthText, dayText, hourText, minuteText, secondText].map(Number);
  const leapYear = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
  const monthDays = [31, leapYear ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
  if (year < 1 || month < 1 || month > 12 || day < 1 || day > monthDays[month - 1]
    || hour > 23 || minute > 59 || second > 59 || /[1-9]/.test((fraction ?? "").slice(3))) return null;
  if (offset !== "Z") {
    const hours = Number(offset.slice(1, 3));
    const minutes = Number(offset.slice(4, 6));
    if (offset === "-00:00" || hours > 14 || minutes > 59 || (hours === 14 && minutes !== 0)) return null;
  }
  const instant = Date.parse(value);
  const utcYear = new Date(instant).getUTCFullYear();
  return Number.isFinite(instant) && utcYear >= 1 && utcYear <= 9999 ? instant : null;
}

function canonicalDeviceLifecycleId(value: unknown): string | null {
  const text = typeof value === "number"
    ? Number.isSafeInteger(value) && value > 0 ? String(value) : ""
    : typeof value === "string" ? value : "";
  if (!/^[1-9]\d{0,18}$/.test(text) || (text.length === 19 && text > "9223372036854775807")) return null;
  return text;
}

// ── Exported record shapes (names/fields preserved — pages depend on them) ──────────
// These used to be inferred from seed fixtures. They are now defined explicitly so the
// telematics layer imports nothing from @/data/*. The field names match exactly what
// IotDevicesPage / TelematicsCommandPage read.

export type TelematicsTelemetrySeedRecord = {
  id: string;
  deviceId: string | number;
  vehicleId: string;
  driverId: string;
  latitude: string;
  longitude: string;
  speedMph: string | number;
  heading: string;
  engineStatus: string;
  odometer: string;
  fuelLevel: string;
  geofenceStatus: string;
  eventAt: string;
};

export type TelematicsHealthSeedRecord = {
  id: string;
  deviceId: string | number;
  tenantId: number;
  score: number | string;
  status: string;
  signalStrength: string;
  eventAt: string;
  summary: string;
};

export type TelematicsFirmwareSeedRecord = {
  id: string;
  deviceId: string | number;
  deviceIdentifier: string;
  tenantId: number;
  currentVersion: string;
  targetVersion: string;
  scheduledFor: string | null;
  status: string;
  releaseNotes: string;
  createdBy: string;
};

export type TelematicsDiagnosticSeedRecord = {
  id: string;
  deviceId: string | number;
  tenantId: number;
  result: string;
  batteryVoltage: string;
  modemStatus: string;
  gnssStatus: string;
  faultCode: string;
  runAt: string;
  runBy: string;
};

export type TelematicsInstallationSeedRecord = {
  id: string;
  deviceId: string | number;
  tenantId: number;
  installStatus: string;
  installerName: string;
  installedAt: string | null;
  removedAt?: string | null;
  vehicleId?: string;
  vehicleCode?: string;
  deviceRole?: string;
  isPrimary?: boolean;
  rowVersion?: number;
  activationVerifiedAt?: string | null;
  installationLocation?: string;
  odometerAtInstallation?: string;
  commissioningMethod?: string;
  commissioningResult?: string;
  verificationReference?: string;
  assignmentReason?: string;
  removalReason?: string;
  checklist: Array<{ item: string; status: string }>;
};

export type DeviceInstallationChecklistObservationRecord = {
  id: string;
  workPackageId: string;
  checklistItem: DeviceInstallationChecklistItem;
  observedResult: DeviceInstallationChecklistResult;
  evidenceReference: string;
  observationNotes: string;
  observedAt: string;
  assuranceStatus: "Unverified";
  physicalEvidenceClaim: false;
  certificationClaim: false;
  recordedByName: string;
};

export type DeviceInstallationArtifactReferenceRecord = {
  id: string;
  workPackageId: string;
  artifactType: DeviceInstallationArtifactType;
  objectKey: string;
  sha256: string;
  capturedAt: string;
  contentVerificationStatus: "Unverified";
  physicalEvidenceClaim: false;
  certificationClaim: false;
  recordedByName: string;
};

export type DeviceInstallationWorkPackageRecord = {
  id: string;
  deviceId: string;
  vehicleId: string;
  vehicleCode: string;
  assignedInstallerUserId: string;
  installerName: string;
  workOrderReference: string;
  appointmentStart: string;
  appointmentEnd: string;
  serviceLocation: string;
  workScope: string;
  readinessStatus: "AwaitingChecklist" | "BlockedByFailedCheck" | "ChecklistRecordedAwaitingArtifacts" | "RecordedAwaitingIndependentVerification" | "LinkedAwaitingIndependentVerification";
  requiredChecklistItems: DeviceInstallationChecklistItem[];
  latestChecklist: DeviceInstallationChecklistObservationRecord[];
  artifactReferences: DeviceInstallationArtifactReferenceRecord[];
  linkedInstallationId: string | null;
  linkedInstallationStatus: string | null;
  linkAssuranceStatus: "RecordedUnverified" | null;
  linkedAt: string | null;
  physicalAppointmentClaim: false;
  physicalWorkClaim: false;
  certificationClaim: false;
};

export type DeviceInstallationChecklistItem =
  | "DeviceIdentity" | "VehicleIdentity" | "Mounting" | "PrimaryPower" | "Ground" | "Ignition"
  | "GNSSAntenna" | "CellularAntenna" | "Harness" | "CANBus" | "CameraAlignment" | "SensorPlacement";
export type DeviceInstallationChecklistResult = "Pass" | "Fail" | "NotObserved" | "NotApplicable";
export type DeviceInstallationArtifactType =
  | "InstallationPhoto" | "SerialLabel" | "WiringPhoto" | "PowerReading" | "TechnicianChecklist"
  | "CommissioningReport" | "RemovalPhoto" | "OtherDocument";

export type DeviceInstallationWorkPackageInput = {
  vehicleId: string | number;
  workOrderReference: string;
  appointmentStart: string;
  appointmentEnd: string;
  serviceLocation: string;
  workScope: string;
  idempotencyKey: string;
};

export type DeviceInstallationChecklistObservationInput = {
  checklistItem: DeviceInstallationChecklistItem;
  observedResult: DeviceInstallationChecklistResult;
  evidenceReference: string;
  observationNotes: string;
  observedAt: string;
  idempotencyKey: string;
};

export type DeviceInstallationArtifactReferenceInput = {
  artifactType: DeviceInstallationArtifactType;
  objectKey: string;
  sha256: string;
  capturedAt: string;
  idempotencyKey: string;
};

export type TelematicsSensorSeedRecord = {
  id: string;
  deviceId: string | number;
  tenantId: number;
  temperature: string;
  humidity: string;
  doorStatus: string;
  tirePressure: string;
  fuelLevel: string;
  recordedAt: string;
};

export type TelematicsProviderSeedRecord = {
  id: string;
  name: string;
  category: string;
  integrationStatus: string;
  tenantId: number;
  deviceCount: number;
  lastSyncAt: string;
  supportTier: string;
  pendingDevices?: number;
  connectedTo?: string[];
  matchConfidence?: "exact" | "fuzzy" | "none" | "restricted";
  auditMessage?: string;
  isMatchedToDevice?: boolean;
  visibilitySource?: "connected" | "restricted" | "unmatched" | "unavailable";
};

export type DeviceCommandRecord = {
  id: string | number;
  rowVersion?: number;
  deviceId: string;
  deviceName: string;
  deviceType: string;
  deviceCategory: string;
  manufacturer: string;
  hardwareRevision: string;
  provider: string;
  providerCode: string;
  serialNumber: string;
  identifier: string;
  imei: string;
  simNumber: string;
  assignedVehicleId: string;
  vehicleId: string;
  assignedVehicleCode: string;
  assignedDriverId: string;
  driverId: string;
  assignedDriverName: string;
  shipmentId: string;
  tenantId: number;
  tenantName: string;
  firmwareVersion: string;
  targetFirmwareVersion: string;
  lastCheckIn: string;
  connectionStatus: string;
  powerStatus: string;
  signalStrength: string;
  dataHealthScore: number;
  dataHealthAvailable: boolean;
  installStatus: string;
  currentInstallationId?: string;
  currentInstallationRowVersion?: number;
  installationActivationVerifiedAt?: string | null;
  deviceRole?: string;
  complianceStatus: string;
  warrantyStatus: string;
  supportStatus: string;
  deviceOpsGaps: string[];
  deviceOpsAssessmentAvailable: boolean;
  openRmaCount: number;
  highestOpenRmaSeverity: string;
  nextSupportResponseDueAt: string | null;
  lifecycleStatus: string;
  // Governed installation/commissioning state from eld_devices.device_state.
  // This is distinct from lifecycle status: an Active device can still be
  // Provisioned, Installed, Verified, Suspended, or Quarantined.
  deviceState: string;
  // The raw eld_devices.status (Active / Diagnostic / Malfunction / Provisioning / …).
  // Recovery actions gate on THIS, not the derived connectionStatus, to match the
  // backend mark/resolve-malfunction status contract.
  eldStatus: string;
  archivedAt: string | null;
  linkedVehicleStatus: string;
  linkedVehicleLocation: string;
  linkedShipmentId: string;
  linkedShipmentStatus: string;
  openAlertCount: number;
  activeFaultCount: number;
  maintenanceStatus: string;
  complianceSummary: string;
};

export type DevicePageOptions = {
  page?: number;
  pageSize?: number;
  search?: string;
  view?: string;
  sort?: "serial" | "provider" | "model" | "status" | "lastCheckIn" | "vehicle" | "priority";
  direction?: "asc" | "desc";
};

export type DevicePageResult = {
  items: DeviceCommandRecord[];
  total: number;
  page: number;
  pageSize: number;
  summary: {
    active: number;
    archived: number;
    offline: number;
    attention: number;
    online: number;
    neverConnected: number;
    faulted: number | null;
    readinessGaps: number | null;
  };
};

export type TelemetryClusterPageOptions = {
  page?: number;
  pageSize?: number;
  search?: string;
  view?: string;
  purpose?: "view" | "export";
  sort?: "risk" | "freshness" | "lastFix" | "vehicle" | "serial" | "provider";
  direction?: "asc" | "desc";
};

export type TelemetryClusterPageResult = {
  items: TelematicsClusterRecord[];
  total: number;
  page: number;
  pageSize: number;
  exportComplete?: boolean;
  summary: {
    active: number;
    offline: number;
    attention: number;
    online: number;
    delayed: number;
    stale: number;
    noPosition: number;
  };
};

// ── Wire-format normalization ─────────────────────────────────────────────────────────
// The .NET API returns db.QueryAsync rows whose SQL aliases are camel-cased by
// Data/Database.cs (ToCamel: device_serial -> deviceSerial, row_version -> rowVersion, …),
// and System.Text.Json emits those dictionary keys verbatim (there is no
// DictionaryKeyPolicy override in Program.cs). This layer reads snake_case keys, so
// without normalization every multi-word field resolves to `undefined` and the whole
// device / GPS / diagnostics surface renders blank. normalizeKeys makes each row
// readable by BOTH casings (additive snake_case aliases) so the reads below work
// regardless of which casing the backend emits — and stays correct if it ever changes.
function snakeCaseKey(key: string): string {
  return key.replace(/([A-Z])/g, "_$1").replace(/_{2,}/g, "_").replace(/^_/, "").toLowerCase();
}

function normalizeKeys<T extends AnyRecord>(row: T): T {
  if (!row || typeof row !== "object" || Array.isArray(row)) return row;
  const out: AnyRecord = { ...row };
  for (const [key, value] of Object.entries(row)) {
    const snake = snakeCaseKey(key);
    if (snake !== key && out[snake] === undefined) out[snake] = value;
  }
  return out as T;
}

function deviceRowFromDetail(payload: AnyRecord): AnyRecord {
  const detail = normalizeKeys(payload);
  return normalizeKeys((detail.device ?? detail.record ?? detail) as AnyRecord);
}

function plainCheckInCarrier(value: unknown): value is AnyRecord {
  return value !== null && typeof value === "object" && !Array.isArray(value)
    && (Object.getPrototypeOf(value) === Object.prototype || Object.getPrototypeOf(value) === null);
}

function strictCheckInEnvelopePayload(value: unknown): unknown {
  if (!plainCheckInCarrier(value)) throw new Error("Device check-in response was invalid.");
  if (Object.keys(value).some((key) => {
    const normalized = key.replace(/_/g, "").toLowerCase();
    return (normalized === "success" && key !== "success") || (normalized === "data" && key !== "data");
  })) throw new Error("Device check-in response was invalid.");
  if (!Object.hasOwn(value, "success") || value.success !== true || !Object.hasOwn(value, "data")) {
    throw new Error("Device check-in response was invalid.");
  }
  return value.data;
}

// This inspection boundary consumes only JSON-object-shaped own fields.
function checkInDeviceRowFromDetail(payload: unknown): AnyRecord {
  const ownRecord = (value: unknown): AnyRecord | null => plainCheckInCarrier(value) ? value : null;
  const detail = ownRecord(payload);
  if (detail === null) return Object.create(null) as AnyRecord;
  if (Object.keys(detail).some((key) => {
    const normalized = key.replace(/_/g, "").toLowerCase();
    return (normalized === "device" && key !== "device") || (normalized === "record" && key !== "record");
  })) return Object.create(null) as AnyRecord;
  const hasDevice = Object.hasOwn(detail, "device");
  const hasRecord = Object.hasOwn(detail, "record");
  if (hasDevice && hasRecord) return Object.create(null) as AnyRecord;
  const nested = hasDevice
    ? detail.device
    : hasRecord
      ? detail.record
      : detail;
  return ownRecord(nested) ?? Object.create(null) as AnyRecord;
}

function exactCheckInField(row: AnyRecord, camel: string, snake: string): { valid: boolean; present: boolean; value: unknown } {
  const normalizedName = camel.replace(/_/g, "").toLowerCase();
  if (Object.keys(row).some((key) => key.replace(/_/g, "").toLowerCase() === normalizedName && key !== camel && key !== snake)) {
    return { valid: false, present: false, value: undefined };
  }
  const hasCamel = Object.hasOwn(row, camel);
  const hasSnake = Object.hasOwn(row, snake);
  if (camel !== snake && hasCamel && hasSnake && row[camel] !== row[snake]) return { valid: false, present: false, value: undefined };
  return { valid: true, present: hasCamel || hasSnake, value: hasSnake ? row[snake] : hasCamel ? row[camel] : undefined };
}

// This timestamp proves only that a check-in was recorded. It can originate
// from a provider or a legacy record, not necessarily an authenticated device.
function recordedDeviceCheckIn(value: unknown, observedAt: number): string | null {
  if (typeof value !== "string") return null;
  const parts = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?(Z|[+-]\d{2}:\d{2})$/.exec(value);
  if (!parts) return null;
  const [, yearText, monthText, dayText, hourText, minuteText, secondText, fraction, offset] = parts;
  const [year, month, day, hour, minute, second] = [yearText, monthText, dayText, hourText, minuteText, secondText].map(Number);
  const leapYear = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
  const monthDays = [31, leapYear ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
  if (year < 1 || month < 1 || month > 12 || day < 1 || day > monthDays[month - 1] || hour > 23 || minute > 59 || second > 59) return null;
  if (offset !== "Z") {
    const offsetHours = Number(offset.slice(1, 3));
    const offsetMinutes = Number(offset.slice(4, 6));
    if (offset === "-00:00" || offsetHours > 14 || offsetMinutes > 59 || (offsetHours === 14 && offsetMinutes !== 0)) return null;
  }
  const timestamp = Date.parse(value);
  // JavaScript truncates sub-millisecond precision. Round up only for the
  // comparison so a fraction after the observation time cannot be accepted.
  const comparisonTime = timestamp + (Number((fraction ?? "").slice(3)) > 0 ? 1 : 0);
  return Number.isFinite(timestamp) && comparisonTime <= observedAt ? value : null;
}

function parseRowVersion(value: unknown): number | undefined {
  if (value == null) return undefined;
  const numeric = Number(value);
  if (Number.isInteger(numeric) && numeric > 0) return numeric;
  return undefined;
}

// The backend binds vehicle/driver ids to `long?` and System.Text.Json does NOT coerce a
// JSON string to a number (no NumberHandling override), so any non-numeric id — or even a
// numeric id sent as a string — 400s. Coerce to a real number or null before sending.
function toNumericId(value: unknown): number | null {
  if (value == null || value === "") return null;
  const parsed = typeof value === "number" ? value : Number(String(value).trim());
  return Number.isInteger(parsed) && parsed > 0 ? parsed : null;
}

function normalizeMalfunctionInput(notes: string): { malfunctionCode: string; malfunctionDescription: string } {
  const source = String(notes ?? "").trim();
  if (!source) return { malfunctionCode: "MANUAL", malfunctionDescription: "Recovery review opened." };

  const pipeIndex = source.indexOf("|");
  const colonIndex = source.indexOf(":");
  const splitIndex = colonIndex >= 0 ? colonIndex : pipeIndex >= 0 ? pipeIndex : -1;

  if (splitIndex > 0 && splitIndex < source.length - 1) {
    const code = source.slice(0, splitIndex).trim();
    const description = source.slice(splitIndex + 1).trim();
    if (code && description) return { malfunctionCode: code.slice(0, 80), malfunctionDescription: description.slice(0, 2000) };
  }

  const firstSpace = source.indexOf(" ");
  if (firstSpace > 0 && firstSpace < Math.min(20, source.length - 1)) {
    const code = source.slice(0, firstSpace).trim();
    const description = source.slice(firstSpace + 1).trim();
    if (/^[A-Za-z0-9-]{1,16}$/.test(code) && description) {
      return { malfunctionCode: code.slice(0, 80), malfunctionDescription: description.slice(0, 2000) };
    }
  }

  return { malfunctionCode: "MANUAL", malfunctionDescription: source.slice(0, 2000) };
}

export type DeviceDetailRecord = {
  device: DeviceCommandRecord;
  compatibility: DeviceCompatibilityRecord;
  currentConnectivityProfile: DeviceConnectivityProfileRecord | null;
  connectivityProfiles: DeviceConnectivityProfileRecord[];
  connectivityObservations: DeviceConnectivityObservationRecord[];
  telemetry: TelematicsTelemetrySeedRecord[];
  healthEvents: TelematicsHealthSeedRecord[];
  firmwareUpdates: TelematicsFirmwareSeedRecord[];
  firmwareCampaigns: DeviceFirmwareCampaignRecord[];
  rmaCases: DeviceRmaCaseRecord[];
  sparePool: DeviceSparePoolRecord | null;
  supportTierEvents: DeviceSupportTierEventRecord[];
  remoteCommandCapabilities: DeviceRemoteCommandCapabilityRecord[];
  remoteCommandHistory: DeviceRemoteCommandRecord[];
  diagnostics: TelematicsDiagnosticSeedRecord[];
  currentInstallation: TelematicsInstallationSeedRecord | null;
  installations: TelematicsInstallationSeedRecord[];
  installationWorkPackages: DeviceInstallationWorkPackageRecord[];
  sensorReadings: TelematicsSensorSeedRecord[];
  providers: TelematicsProviderSeedRecord[];
  auditLog: AnyRecord[];
  retirementRecord: DeviceRetirementRecord | null;
  lifecycleHistory: DeviceLifecycleTransitionRecord[];
};

export type DeviceLifecycleTransitionRecord = {
  id: string;
  fromState: string | null;
  toState: string;
  reasonCode: string;
  reason: string | null;
  actorUserId: string | null;
  correlationId: string;
  occurredAt: string;
};

export type DeviceRetirementRecord = {
  id: string;
  deviceId: string;
  deviceSerial: string;
  retirementReason: string;
  dispositionPlan: "ReturnToVendor" | "Recycle" | "SecureStorage" | "Other";
  sourceReference: string;
  effectiveAt: string;
  priorStatus: string;
  priorDeviceState: string;
  rowVersionBefore: number;
  rowVersionAfter: number;
  endedConnectivityProfileId: string | null;
  credentialsRevoked: true;
  recordStatus: "OperatorRecorded";
  physicalDispositionStatus: "Unverified";
  physicalDispositionClaim: false;
  certificationClaim: false;
  retiredBy: string;
  createdAt: string;
};

export type DeviceRetirementInput = {
  retirementReason: string;
  dispositionPlan: DeviceRetirementRecord["dispositionPlan"];
  sourceReference: string;
  effectiveAt: string;
  expectedRowVersion: number;
  idempotencyKey: string;
  safetyConfirmation: string;
};

export type DeviceConnectivityProfileRecord = {
  id: string;
  deviceId: string;
  profileKind: "PhysicalSIM" | "eSIM" | "Unknown";
  carrierName: string;
  iccidLast4: string;
  msisdnLast4: string | null;
  apnConfigured: boolean;
  assignmentStatus: "Assigned" | "Ended" | "Unknown";
  effectiveFrom: string;
  effectiveTo: string | null;
  sourceReference: string;
  changeReason: string;
  endReason: string | null;
};

export type DeviceConnectivityProfileInput = {
  profileKind: "PhysicalSIM" | "eSIM";
  carrierName: string;
  iccid: string;
  msisdn?: string;
  apn?: string;
  effectiveAt: string;
  changeReason: string;
  sourceReference: string;
  idempotencyKey: string;
};

export type DeviceConnectivityObservationRecord = {
  id: string;
  deviceId: string;
  connectivityProfileId: string;
  profileIccidLast4: string;
  sourceProvider: string;
  sourceAuthenticationStatus: "Authenticated" | "Unverified";
  subscriptionStatus: "Unknown" | "Active" | "Suspended" | "Deactivated";
  networkRegistrationStatus: "Unknown" | "Registered" | "Roaming" | "Denied" | "Detached";
  dataSessionStatus: "Unknown" | "Attached" | "Detached" | "Blocked";
  usageBytes: number | null;
  roaming: boolean | null;
  observedAt: string;
  receivedAt: string;
  reconciliationStatus: "ExactCurrentProfile" | "Unavailable";
  softwareObservationAvailable: boolean;
  providerVerifiedClaim: false;
  physicalConnectivityClaim: false;
  certificationClaim: false;
};

export type DeviceFirmwareCampaignRecord = {
  campaignId: string;
  targetId: string;
  campaignName: string;
  targetFirmwareVersion: string;
  rollbackFirmwareVersion: string | null;
  rolloutStrategy: "Manual" | "Canary" | "Staged" | "Unknown";
  batchSize: number;
  scheduledFor: string;
  maintenanceWindowMinutes: number;
  executionStatus: "ExternalHold" | "Unknown";
  providerCapabilityStatus: "Unverified" | "Unknown";
  remoteUpgradeClaim: false;
  externalHoldReason: string;
  sourceReference: string;
  changeReason: string;
  createdAt: string;
  deviceId: string;
  deviceSerial: string;
  manufacturer: string | null;
  deviceModel: string | null;
  hardwareRevision: string | null;
  reportedFirmwareVersion: string | null;
  planningStatus: "ReadyForExternalEvidence" | "BlockedIdentity" | "AlreadyCurrent" | "Unknown";
  planningReason: string;
  rolloutBatch: number;
  deliveryStatus: "ExternalHold" | "Unknown";
};

export type DeviceFirmwareCampaignInput = {
  campaignName: string;
  targetFirmwareVersion: string;
  rollbackFirmwareVersion?: string;
  rolloutStrategy: "Manual" | "Canary" | "Staged";
  scheduledFor: string;
  maintenanceWindowMinutes: number;
  batchSize: number;
  deviceIds: Array<string | number>;
  changeReason: string;
  sourceReference: string;
  idempotencyKey: string;
};

export type DeviceRmaEventRecord = {
  id: string;
  caseId: string;
  sequenceNumber: number;
  eventType: "CaseOpened" | "ReturnAuthorized" | "Shipped" | "Received" | "VendorDisposition" | "ReplacementLinked" | "CaseClosed" | "Unknown";
  caseStatusAfter: "Open" | "AwaitingReturn" | "InTransit" | "UnderReview" | "ReplacementPlanned" | "Resolved" | "Unknown";
  occurredAt: string;
  custodyLocation: string | null;
  trackingReference: string | null;
  evidenceReference: string;
  evidenceStatus: "Unverified";
  notes: string;
  physicalCompletionClaim: false;
  recordedAt: string;
};

export type DeviceRmaReplacementRecord = {
  id: string;
  caseId: string;
  failedDeviceId: string;
  failedDeviceSerial: string;
  replacementDeviceId: string;
  replacementDeviceSerial: string;
  replacementManufacturer: string | null;
  replacementDeviceModel: string | null;
  replacementHardwareRevision: string | null;
  replacementFirmwareVersion: string | null;
  replacementStatus: "Planned";
  physicalSwapStatus: "ExternalHold";
  physicalSwapClaim: false;
  changeReason: string;
  sourceReference: string;
  createdAt: string;
};

export type DeviceRmaSupportActionRecord = {
  id: string;
  caseId: string;
  deviceId: string;
  actionType: "OwnershipClaimed" | "OwnershipReassigned" | "Escalated";
  ownerUserId: string;
  ownerNameSnapshot: string;
  supportQueue: string;
  escalationSeverity: "P0" | "P1" | "P2" | "P3" | null;
  actionReason: string;
  sourceReference: string;
  effectiveAt: string;
  supportActionStatus: "OperatorRecorded";
  supportResponseClaim: false;
  physicalOutcomeClaim: false;
  warrantyAcceptanceClaim: false;
  recordedBy: string;
  createdAt: string;
};

export type DeviceRmaCaseRecord = {
  id: string;
  deviceId: string;
  deviceSerial: string;
  manufacturer: string | null;
  deviceModel: string | null;
  hardwareRevision: string | null;
  reportedFirmwareVersion: string | null;
  severity: "P0" | "P1" | "P2" | "P3" | "Unknown";
  failureCategory: string;
  failureDescription: string;
  observedAt: string;
  warrantyPosture: "Unknown" | "ClaimedInWarranty" | "ClaimedOutOfWarranty" | "NotApplicable";
  warrantyReference: string | null;
  warrantyEvidenceStatus: "Unverified";
  supportSlaReference: string;
  responseDueAt: string;
  sourceReference: string;
  physicalEvidenceClaim: false;
  currentStatus: "Open" | "AwaitingReturn" | "InTransit" | "UnderReview" | "ReplacementPlanned" | "Resolved" | "Unknown";
  latestEventAt: string | null;
  createdAt: string;
  events: DeviceRmaEventRecord[];
  replacement: DeviceRmaReplacementRecord | null;
  supportActions: DeviceRmaSupportActionRecord[];
};

export type DeviceRmaCaseInput = {
  severity: "P0" | "P1" | "P2" | "P3";
  failureCategory: "Power" | "Connectivity" | "GNSS" | "CAN" | "Camera" | "Firmware" | "PhysicalDamage" | "Intermittent" | "Other";
  failureDescription: string;
  observedAt: string;
  warrantyPosture: "Unknown" | "ClaimedInWarranty" | "ClaimedOutOfWarranty" | "NotApplicable";
  warrantyReference?: string;
  supportSlaReference: string;
  responseDueAt: string;
  sourceReference: string;
  idempotencyKey: string;
};

export type DeviceRmaEventInput = {
  eventType: "ReturnAuthorized" | "Shipped" | "Received" | "VendorDisposition" | "CaseClosed";
  occurredAt: string;
  custodyLocation?: string;
  trackingReference?: string;
  evidenceReference: string;
  notes: string;
  idempotencyKey: string;
};

export type DeviceRmaReplacementInput = {
  replacementDeviceSerial: string;
  changeReason: string;
  sourceReference: string;
  idempotencyKey: string;
};

export type DeviceRmaSupportActionInput = {
  actionType: "TakeOwnership" | "Escalate";
  supportQueue: string;
  escalationSeverity?: "P0" | "P1" | "P2" | "P3";
  actionReason: string;
  sourceReference: string;
  effectiveAt: string;
  idempotencyKey: string;
};

export type DeviceSparePoolEventRecord = {
  id: string;
  entryId: string;
  deviceId: string;
  actionType: "Added" | "Reserved" | "Released" | "Removed";
  stateAfter: "Available" | "Reserved" | "Removed";
  rmaCaseId: string | null;
  failedDeviceId: string | null;
  actionReason: string;
  sourceReference: string;
  effectiveAt: string;
  eventStatus: "OperatorRecorded";
  physicalPossessionClaim: false;
  conditionVerifiedClaim: false;
  compatibilityClaim: false;
  certificationClaim: false;
  recordedBy: string;
  createdAt: string;
};

export type DeviceSparePoolRecord = {
  id: string;
  deviceId: string;
  deviceSerialSnapshot: string;
  poolName: string;
  entryReason: string;
  sourceReference: string;
  inventoryAssuranceStatus: "OperatorRecordedUnverified";
  physicalPossessionClaim: false;
  conditionVerifiedClaim: false;
  certificationClaim: false;
  addedBy: string;
  createdAt: string;
  currentState: "Available" | "Reserved" | "Removed";
  events: DeviceSparePoolEventRecord[];
};

export type DeviceSparePoolActionInput = {
  actionType: "Add" | "Reserve" | "Release" | "Remove";
  poolName?: string;
  rmaCaseId?: string;
  actionReason: string;
  sourceReference: string;
  effectiveAt: string;
  idempotencyKey: string;
};

export type DeviceSupportTierEventRecord = {
  id: string;
  deviceId: string;
  deviceSerialSnapshot: string;
  actionType: "Assigned" | "Changed" | "Ended";
  stateAfter: "Assigned" | "NotAssigned";
  tierCode: "Standard" | "Priority" | "CriticalOps" | "Custom";
  coverageWindow: "BusinessHours" | "ExtendedHours" | "AlwaysOn" | "Custom";
  routingResponseTargetMinutes: number;
  escalationPolicyReference: string;
  commercialReference: string;
  actionReason: string;
  sourceReference: string;
  effectiveAt: string;
  recordStatus: "OperatorRecordedUnverified";
  commercialEntitlementVerifiedClaim: false;
  providerSupportClaim: false;
  hardwareSupportabilityClaim: false;
  certificationClaim: false;
  recordedBy: string;
  createdAt: string;
};

export type DeviceSupportTierActionInput = {
  actionType: "Assign" | "Change" | "End";
  tierCode?: DeviceSupportTierEventRecord["tierCode"];
  coverageWindow?: DeviceSupportTierEventRecord["coverageWindow"];
  routingResponseTargetMinutes?: number;
  escalationPolicyReference?: string;
  commercialReference?: string;
  actionReason: string;
  sourceReference: string;
  effectiveAt: string;
  idempotencyKey: string;
};

export type DeviceRemoteCommandCapabilityRecord = {
  commandType: "RequestPosition" | "RequestDiagnostics" | "RestartDevice";
  displayName: string;
  commandClass: "Observation" | "Controlled";
  capabilityStatus: "Unverified" | "Verified" | "Rejected" | "Revoked" | "Unknown";
  evidenceSource: string | null;
  evidenceReference: string | null;
  observedAt: string | null;
  expiresAt: string | null;
  requestAdmissionAvailable: boolean;
  confirmationText: string;
  externalHold: boolean;
  externalHoldReason: string | null;
  certificationClaim: false;
};

export type DeviceRemoteCommandRecord = {
  id: string;
  commandType: string;
  commandClass: string;
  status: string;
  governanceStatus: string;
  purpose: string;
  sourceReference: string;
  attemptCount: number;
  maxAttempts: number;
  scheduledFor: string;
  dispatchedAt: string | null;
  acknowledgedAt: string | null;
  appliedAt: string | null;
  expiresAt: string | null;
  lastError: string | null;
  providerDeliveryClaim: false;
  physicalOutcomeClaim: false;
  createdAt: string;
};

export type DeviceRemoteCommandInput = {
  commandType: "RequestPosition" | "RequestDiagnostics" | "RestartDevice";
  payload: Record<string, unknown>;
  purpose: string;
  sourceReference: string;
  safetyConfirmation: string;
  idempotencyKey: string;
};

export type DeviceCompatibilityRecord = {
  manufacturer: string | null;
  deviceModel: string | null;
  hardwareRevision: string | null;
  firmwareVersion: string | null;
  exactTupleComplete: boolean;
  missingIdentityFields: string[];
  registryStatus: string;
  certificationStatus: "ExternalHold";
  maximumTier: "Unverified";
  candidateSha: string | null;
  externalHold: true;
  externalHoldReason: string;
  capabilityDeclarationStatus: "NotRecorded" | "EngineeringDeclaredUnverified";
  protocols: string[];
  supportedFields: string[];
  supportedEvents: string[];
  supportedCommands: string[];
  knownLimitations: string;
  declarationSourceReference: string | null;
  declaredAt: string | null;
  catalogSupportTier: "Unverified";
  certificationReference: null;
  certificationDate: null;
  physicalEvidenceClaim: false;
  providerEvidenceClaim: false;
  certificationClaim: false;
};

function mapConnectivityProfile(raw: AnyRecord): DeviceConnectivityProfileRecord {
  const row = normalizeKeys(raw);
  const iccidLast4 = typeof row.iccid_last4 === "string" && /^\d{4}$/.test(row.iccid_last4)
    ? row.iccid_last4 : "";
  const msisdnLast4 = typeof row.msisdn_last4 === "string" && /^\d{4}$/.test(row.msisdn_last4)
    ? row.msisdn_last4 : null;
  return {
    id: String(row.id ?? ""),
    deviceId: String(row.device_id ?? ""),
    profileKind: row.profile_kind === "PhysicalSIM" || row.profile_kind === "eSIM"
      ? row.profile_kind : "Unknown",
    carrierName: String(row.carrier_name ?? ""),
    iccidLast4,
    msisdnLast4,
    apnConfigured: row.apn_configured === true,
    assignmentStatus: row.assignment_status === "Assigned" || row.assignment_status === "Ended"
      ? row.assignment_status : "Unknown",
    effectiveFrom: String(row.effective_from ?? ""),
    effectiveTo: row.effective_to == null ? null : String(row.effective_to),
    sourceReference: String(row.source_reference ?? ""),
    changeReason: String(row.change_reason ?? ""),
    endReason: row.end_reason == null ? null : String(row.end_reason),
  };
}

function mapConnectivityObservation(raw: AnyRecord): DeviceConnectivityObservationRecord {
  const row = normalizeKeys(raw);
  const id = canonicalDeviceLifecycleId(row.id);
  const deviceId = canonicalDeviceLifecycleId(row.device_id);
  const connectivityProfileId = canonicalDeviceLifecycleId(row.connectivity_profile_id);
  const observedAt = recordedDeviceCheckIn(row.observed_at, Number.MAX_SAFE_INTEGER);
  const receivedAt = recordedDeviceCheckIn(row.received_at, Number.MAX_SAFE_INTEGER);
  const observationOrderValid = observedAt !== null && receivedAt !== null &&
    Date.parse(observedAt) <= Date.parse(receivedAt) + 5 * 60 * 1000;
  const requiredFieldsAvailable = [
    "id", "device_id", "connectivity_profile_id", "profile_iccid_last4", "source_provider",
    "source_authentication_status", "subscription_status", "network_registration_status",
    "data_session_status", "observed_at", "received_at", "reconciliation_status",
    "provider_verified_claim", "physical_connectivity_claim", "certification_claim",
  ].every((key) => Object.hasOwn(row, key));
  const providerClaimSafe = row.provider_verified_claim === false;
  const physicalClaimSafe = row.physical_connectivity_claim === false;
  const certificationClaimSafe = row.certification_claim === false;
  const subscriptionValid = ["Unknown", "Active", "Suspended", "Deactivated"].includes(String(row.subscription_status));
  const networkValid = ["Unknown", "Registered", "Roaming", "Denied", "Detached"].includes(String(row.network_registration_status));
  const sessionValid = ["Unknown", "Attached", "Detached", "Blocked"].includes(String(row.data_session_status));
  const softwareObservationAvailable = requiredFieldsAvailable && providerClaimSafe && physicalClaimSafe && certificationClaimSafe &&
    row.source_authentication_status === "Authenticated" &&
    row.reconciliation_status === "ExactCurrentProfile" &&
    subscriptionValid && networkValid && sessionValid &&
    id !== null && deviceId !== null && connectivityProfileId !== null &&
    typeof row.source_provider === "string" && /^[a-z0-9][a-z0-9._-]{0,79}$/.test(row.source_provider) &&
    typeof row.profile_iccid_last4 === "string" && /^\d{4}$/.test(row.profile_iccid_last4) &&
    observationOrderValid;
  const usage = Number(row.usage_bytes);
  return {
    id: softwareObservationAvailable ? id! : "",
    deviceId: softwareObservationAvailable ? deviceId! : "",
    connectivityProfileId: softwareObservationAvailable ? connectivityProfileId! : "",
    profileIccidLast4: typeof row.profile_iccid_last4 === "string" && /^\d{4}$/.test(row.profile_iccid_last4)
      ? row.profile_iccid_last4 : "",
    sourceProvider: softwareObservationAvailable ? String(row.source_provider ?? "") : "Unavailable",
    sourceAuthenticationStatus: softwareObservationAvailable ? "Authenticated" : "Unverified",
    subscriptionStatus: softwareObservationAvailable ? row.subscription_status as DeviceConnectivityObservationRecord["subscriptionStatus"] : "Unknown",
    networkRegistrationStatus: softwareObservationAvailable ? row.network_registration_status as DeviceConnectivityObservationRecord["networkRegistrationStatus"] : "Unknown",
    dataSessionStatus: softwareObservationAvailable ? row.data_session_status as DeviceConnectivityObservationRecord["dataSessionStatus"] : "Unknown",
    usageBytes: softwareObservationAvailable && row.usage_bytes != null && Number.isSafeInteger(usage) && usage >= 0 ? usage : null,
    roaming: softwareObservationAvailable && typeof row.roaming === "boolean" ? row.roaming : null,
    observedAt: softwareObservationAvailable ? String(observedAt) : "",
    receivedAt: softwareObservationAvailable ? String(receivedAt) : "",
    reconciliationStatus: softwareObservationAvailable ? "ExactCurrentProfile" : "Unavailable",
    softwareObservationAvailable,
    providerVerifiedClaim: false,
    physicalConnectivityClaim: false,
    certificationClaim: false,
  };
}

function mapDeviceLifecycleTransition(raw: AnyRecord): DeviceLifecycleTransitionRecord {
  const row = normalizeKeys(raw);
  const id = canonicalDeviceLifecycleId(row.id);
  const occurredAt = recordedDeviceCheckIn(row.occurred_at, Number.MAX_SAFE_INTEGER);
  if (id === null || occurredAt === null || typeof row.to_state !== "string" || !row.to_state.trim() ||
      typeof row.reason_code !== "string" || !row.reason_code.trim() ||
      typeof row.correlation_id !== "string" || !row.correlation_id.trim())
    throw new Error("Device lifecycle history was incomplete or malformed.");
  return {
    id,
    fromState: row.from_state == null ? null : String(row.from_state),
    toState: row.to_state,
    reasonCode: row.reason_code,
    reason: row.reason == null ? null : String(row.reason),
    actorUserId: row.actor_user_id == null ? null : String(row.actor_user_id),
    correlationId: row.correlation_id,
    occurredAt,
  };
}

function mapDeviceRetirement(raw: AnyRecord): DeviceRetirementRecord {
  const row = normalizeKeys(raw);
  const id = canonicalDeviceLifecycleId(row.id);
  const deviceId = canonicalDeviceLifecycleId(row.device_id);
  const retiredBy = canonicalDeviceLifecycleId(row.retired_by);
  const effectiveAt = recordedDeviceCheckIn(row.effective_at, Number.MAX_SAFE_INTEGER);
  const createdAt = recordedDeviceCheckIn(row.created_at, Number.MAX_SAFE_INTEGER);
  const before = parseRowVersion(row.row_version_before);
  const after = parseRowVersion(row.row_version_after);
  const disposition = row.disposition_plan;
  if (id === null || deviceId === null || retiredBy === null || effectiveAt === null || createdAt === null ||
      before == null || after !== before + 1 ||
      !["ReturnToVendor", "Recycle", "SecureStorage", "Other"].includes(String(disposition)) ||
      row.credentials_revoked !== true || row.record_status !== "OperatorRecorded" ||
      row.physical_disposition_status !== "Unverified" || row.physical_disposition_claim !== false ||
      row.certification_claim !== false)
    throw new Error("The server did not preserve the governed unverified retirement boundary.");
  return {
    id,
    deviceId,
    deviceSerial: String(row.device_serial_snapshot ?? ""),
    retirementReason: String(row.retirement_reason ?? ""),
    dispositionPlan: disposition as DeviceRetirementRecord["dispositionPlan"],
    sourceReference: String(row.source_reference ?? ""),
    effectiveAt,
    priorStatus: String(row.prior_status ?? ""),
    priorDeviceState: String(row.prior_device_state ?? ""),
    rowVersionBefore: before,
    rowVersionAfter: after,
    endedConnectivityProfileId: row.ended_connectivity_profile_id == null ? null : String(row.ended_connectivity_profile_id),
    credentialsRevoked: true,
    recordStatus: "OperatorRecorded",
    physicalDispositionStatus: "Unverified",
    physicalDispositionClaim: false,
    certificationClaim: false,
    retiredBy,
    createdAt,
  };
}

function mapFirmwareCampaign(raw: AnyRecord): DeviceFirmwareCampaignRecord {
  const row = normalizeKeys(raw);
  if (row.remote_upgrade_claim !== false)
    throw new Error("Firmware planning data did not include the required no-upgrade-claim marker.");
  return {
    campaignId: String(row.campaign_id ?? row.id ?? ""),
    targetId: String(row.target_id ?? row.id ?? ""),
    campaignName: String(row.campaign_name ?? ""),
    targetFirmwareVersion: String(row.target_firmware_version ?? ""),
    rollbackFirmwareVersion: row.rollback_firmware_version == null ? null : String(row.rollback_firmware_version),
    rolloutStrategy: row.rollout_strategy === "Manual" || row.rollout_strategy === "Canary" || row.rollout_strategy === "Staged"
      ? row.rollout_strategy : "Unknown",
    batchSize: Number.isInteger(Number(row.batch_size)) && Number(row.batch_size) > 0 ? Number(row.batch_size) : 0,
    scheduledFor: String(row.scheduled_for ?? ""),
    maintenanceWindowMinutes: Number.isInteger(Number(row.maintenance_window_minutes)) && Number(row.maintenance_window_minutes) > 0
      ? Number(row.maintenance_window_minutes) : 0,
    executionStatus: row.execution_status === "ExternalHold" ? "ExternalHold" : "Unknown",
    providerCapabilityStatus: row.provider_capability_status === "Unverified" ? "Unverified" : "Unknown",
    remoteUpgradeClaim: false,
    externalHoldReason: String(row.external_hold_reason ?? "Firmware delivery evidence is unavailable."),
    sourceReference: String(row.source_reference ?? ""),
    changeReason: String(row.change_reason ?? ""),
    createdAt: String(row.created_at ?? ""),
    deviceId: String(row.device_id ?? ""),
    deviceSerial: String(row.device_serial ?? ""),
    manufacturer: row.manufacturer == null ? null : String(row.manufacturer),
    deviceModel: row.device_model == null ? null : String(row.device_model),
    hardwareRevision: row.hardware_revision == null ? null : String(row.hardware_revision),
    reportedFirmwareVersion: row.reported_firmware_version == null ? null : String(row.reported_firmware_version),
    planningStatus: row.planning_status === "ReadyForExternalEvidence" || row.planning_status === "BlockedIdentity" || row.planning_status === "AlreadyCurrent"
      ? row.planning_status : "Unknown",
    planningReason: String(row.planning_reason ?? "Planning status unavailable."),
    rolloutBatch: Number.isInteger(Number(row.rollout_batch)) && Number(row.rollout_batch) > 0 ? Number(row.rollout_batch) : 0,
    deliveryStatus: row.delivery_status === "ExternalHold" ? "ExternalHold" : "Unknown",
  };
}

function mapRmaEvent(raw: AnyRecord): DeviceRmaEventRecord {
  const row = normalizeKeys(raw);
  if (row.physical_completion_claim !== false || row.evidence_status !== "Unverified")
    throw new Error("RMA event data crossed the unverified physical-evidence boundary.");
  const eventTypes = ["CaseOpened", "ReturnAuthorized", "Shipped", "Received", "VendorDisposition", "ReplacementLinked", "CaseClosed"];
  const statuses = ["Open", "AwaitingReturn", "InTransit", "UnderReview", "ReplacementPlanned", "Resolved"];
  return {
    id: String(row.id ?? ""), caseId: String(row.case_id ?? ""),
    sequenceNumber: Number.isSafeInteger(Number(row.sequence_number)) ? Number(row.sequence_number) : 0,
    eventType: eventTypes.includes(String(row.event_type)) ? String(row.event_type) as DeviceRmaEventRecord["eventType"] : "Unknown",
    caseStatusAfter: statuses.includes(String(row.case_status_after)) ? String(row.case_status_after) as DeviceRmaEventRecord["caseStatusAfter"] : "Unknown",
    occurredAt: String(row.occurred_at ?? ""),
    custodyLocation: row.custody_location == null ? null : String(row.custody_location),
    trackingReference: row.tracking_reference == null ? null : String(row.tracking_reference),
    evidenceReference: String(row.evidence_reference ?? ""), evidenceStatus: "Unverified",
    notes: String(row.notes ?? ""), physicalCompletionClaim: false,
    recordedAt: String(row.recorded_at ?? ""),
  };
}

function mapRmaReplacement(raw: AnyRecord): DeviceRmaReplacementRecord {
  const row = normalizeKeys(raw);
  if (row.physical_swap_claim !== false || row.physical_swap_status !== "ExternalHold" || row.replacement_status !== "Planned")
    throw new Error("RMA replacement data crossed the planning-only boundary.");
  return {
    id: String(row.id ?? ""), caseId: String(row.case_id ?? ""),
    failedDeviceId: String(row.failed_device_id ?? ""), failedDeviceSerial: String(row.failed_device_serial ?? ""),
    replacementDeviceId: String(row.replacement_device_id ?? ""), replacementDeviceSerial: String(row.replacement_device_serial ?? ""),
    replacementManufacturer: row.replacement_manufacturer == null ? null : String(row.replacement_manufacturer),
    replacementDeviceModel: row.replacement_device_model == null ? null : String(row.replacement_device_model),
    replacementHardwareRevision: row.replacement_hardware_revision == null ? null : String(row.replacement_hardware_revision),
    replacementFirmwareVersion: row.replacement_firmware_version == null ? null : String(row.replacement_firmware_version),
    replacementStatus: "Planned", physicalSwapStatus: "ExternalHold", physicalSwapClaim: false,
    changeReason: String(row.change_reason ?? ""), sourceReference: String(row.source_reference ?? ""),
    createdAt: String(row.created_at ?? ""),
  };
}

function mapRmaSupportAction(raw: AnyRecord): DeviceRmaSupportActionRecord {
  const row = normalizeKeys(raw);
  const actionTypes = ["OwnershipClaimed", "OwnershipReassigned", "Escalated"];
  const severity = row.escalation_severity == null ? null : String(row.escalation_severity);
  if (!actionTypes.includes(String(row.action_type)) ||
      (severity !== null && !["P0", "P1", "P2", "P3"].includes(severity)) ||
      row.support_action_status !== "OperatorRecorded" || row.support_response_claim !== false ||
      row.physical_outcome_claim !== false || row.warranty_acceptance_claim !== false)
    throw new Error("RMA support data crossed the operator-recorded no-outcome-claim boundary.");
  return {
    id: String(row.id ?? ""), caseId: String(row.case_id ?? ""), deviceId: String(row.device_id ?? ""),
    actionType: String(row.action_type) as DeviceRmaSupportActionRecord["actionType"],
    ownerUserId: String(row.owner_user_id ?? ""), ownerNameSnapshot: String(row.owner_name_snapshot ?? ""),
    supportQueue: String(row.support_queue ?? ""),
    escalationSeverity: severity as DeviceRmaSupportActionRecord["escalationSeverity"],
    actionReason: String(row.action_reason ?? ""), sourceReference: String(row.source_reference ?? ""),
    effectiveAt: String(row.effective_at ?? ""), supportActionStatus: "OperatorRecorded",
    supportResponseClaim: false, physicalOutcomeClaim: false, warrantyAcceptanceClaim: false,
    recordedBy: String(row.recorded_by ?? ""), createdAt: String(row.created_at ?? ""),
  };
}

function mapSparePoolEvent(raw: AnyRecord): DeviceSparePoolEventRecord {
  const row = normalizeKeys(raw);
  const actions = ["Added", "Reserved", "Released", "Removed"];
  const states = ["Available", "Reserved", "Removed"];
  const id = canonicalDeviceLifecycleId(row.id);
  const entryId = canonicalDeviceLifecycleId(row.entry_id);
  const deviceId = canonicalDeviceLifecycleId(row.device_id);
  const rmaCaseId = row.rma_case_id == null ? null : canonicalDeviceLifecycleId(row.rma_case_id);
  const failedDeviceId = row.failed_device_id == null ? null : canonicalDeviceLifecycleId(row.failed_device_id);
  if (!id || !entryId || !deviceId || !actions.includes(String(row.action_type)) || !states.includes(String(row.state_after)) ||
      (row.rma_case_id != null && !rmaCaseId) || (row.failed_device_id != null && !failedDeviceId) ||
      row.event_status !== "OperatorRecorded" || row.physical_possession_claim !== false ||
      row.condition_verified_claim !== false || row.compatibility_claim !== false || row.certification_claim !== false)
    throw new Error("Spare-pool event crossed the operator-recorded no-evidence-claim boundary.");
  return {
    id, entryId, deviceId,
    actionType: String(row.action_type) as DeviceSparePoolEventRecord["actionType"],
    stateAfter: String(row.state_after) as DeviceSparePoolEventRecord["stateAfter"],
    rmaCaseId, failedDeviceId, actionReason: String(row.action_reason ?? ""),
    sourceReference: String(row.source_reference ?? ""), effectiveAt: String(row.effective_at ?? ""),
    eventStatus: "OperatorRecorded", physicalPossessionClaim: false, conditionVerifiedClaim: false,
    compatibilityClaim: false, certificationClaim: false, recordedBy: String(row.recorded_by ?? ""),
    createdAt: String(row.created_at ?? ""),
  };
}

function mapSparePool(raw: AnyRecord, events: DeviceSparePoolEventRecord[]): DeviceSparePoolRecord {
  const row = normalizeKeys(raw);
  const id = canonicalDeviceLifecycleId(row.id);
  const deviceId = canonicalDeviceLifecycleId(row.device_id);
  if (!id || !deviceId || row.inventory_assurance_status !== "OperatorRecordedUnverified" ||
      row.physical_possession_claim !== false || row.condition_verified_claim !== false || row.certification_claim !== false ||
      events.length === 0 || events.some(event => event.entryId !== id || event.deviceId !== deviceId))
    throw new Error("Spare-pool entry crossed the unverified inventory-planning boundary.");
  return {
    id, deviceId, deviceSerialSnapshot: String(row.device_serial_snapshot ?? ""),
    poolName: String(row.pool_name ?? ""), entryReason: String(row.entry_reason ?? ""),
    sourceReference: String(row.source_reference ?? ""), inventoryAssuranceStatus: "OperatorRecordedUnverified",
    physicalPossessionClaim: false, conditionVerifiedClaim: false, certificationClaim: false,
    addedBy: String(row.added_by ?? ""), createdAt: String(row.created_at ?? ""),
    currentState: events[0].stateAfter, events,
  };
}

function mapDeviceSupportTierEvent(raw: AnyRecord): DeviceSupportTierEventRecord {
  const row = normalizeKeys(raw);
  const actions = ["Assigned", "Changed", "Ended"];
  const states = ["Assigned", "NotAssigned"];
  const tiers = ["Standard", "Priority", "CriticalOps", "Custom"];
  const coverage = ["BusinessHours", "ExtendedHours", "AlwaysOn", "Custom"];
  const id = canonicalDeviceLifecycleId(row.id);
  const deviceId = canonicalDeviceLifecycleId(row.device_id);
  const target = Number(row.routing_response_target_minutes);
  if (!id || !deviceId || !actions.includes(String(row.action_type)) || !states.includes(String(row.state_after)) ||
      !tiers.includes(String(row.tier_code)) || !coverage.includes(String(row.coverage_window)) ||
      !Number.isInteger(target) || target < 15 || target > 10080 ||
      row.record_status !== "OperatorRecordedUnverified" ||
      row.commercial_entitlement_verified_claim !== false || row.provider_support_claim !== false ||
      row.hardware_supportability_claim !== false || row.certification_claim !== false)
    throw new Error("Device support-tier data crossed the operator-recorded unverified boundary.");
  return {
    id, deviceId, deviceSerialSnapshot: String(row.device_serial_snapshot ?? ""),
    actionType: String(row.action_type) as DeviceSupportTierEventRecord["actionType"],
    stateAfter: String(row.state_after) as DeviceSupportTierEventRecord["stateAfter"],
    tierCode: String(row.tier_code) as DeviceSupportTierEventRecord["tierCode"],
    coverageWindow: String(row.coverage_window) as DeviceSupportTierEventRecord["coverageWindow"],
    routingResponseTargetMinutes: target,
    escalationPolicyReference: String(row.escalation_policy_reference ?? ""),
    commercialReference: String(row.commercial_reference ?? ""),
    actionReason: String(row.action_reason ?? ""), sourceReference: String(row.source_reference ?? ""),
    effectiveAt: String(row.effective_at ?? ""), recordStatus: "OperatorRecordedUnverified",
    commercialEntitlementVerifiedClaim: false, providerSupportClaim: false,
    hardwareSupportabilityClaim: false, certificationClaim: false,
    recordedBy: String(row.recorded_by ?? ""), createdAt: String(row.created_at ?? ""),
  };
}

function mapRmaCase(raw: AnyRecord, events: DeviceRmaEventRecord[] = [], replacement: DeviceRmaReplacementRecord | null = null,
  supportActions: DeviceRmaSupportActionRecord[] = []): DeviceRmaCaseRecord {
  const row = normalizeKeys(raw);
  if (row.physical_evidence_claim !== false || row.warranty_evidence_status !== "Unverified")
    throw new Error("RMA case data crossed the unverified evidence boundary.");
  const severities = ["P0", "P1", "P2", "P3"];
  const statuses = ["Open", "AwaitingReturn", "InTransit", "UnderReview", "ReplacementPlanned", "Resolved"];
  const warranty = ["Unknown", "ClaimedInWarranty", "ClaimedOutOfWarranty", "NotApplicable"];
  return {
    id: String(row.id ?? ""), deviceId: String(row.device_id ?? ""), deviceSerial: String(row.device_serial ?? ""),
    manufacturer: row.manufacturer == null ? null : String(row.manufacturer),
    deviceModel: row.device_model == null ? null : String(row.device_model),
    hardwareRevision: row.hardware_revision == null ? null : String(row.hardware_revision),
    reportedFirmwareVersion: row.reported_firmware_version == null ? null : String(row.reported_firmware_version),
    severity: severities.includes(String(row.severity)) ? String(row.severity) as DeviceRmaCaseRecord["severity"] : "Unknown",
    failureCategory: String(row.failure_category ?? ""), failureDescription: String(row.failure_description ?? ""),
    observedAt: String(row.observed_at ?? ""),
    warrantyPosture: warranty.includes(String(row.warranty_posture)) ? String(row.warranty_posture) as DeviceRmaCaseRecord["warrantyPosture"] : "Unknown",
    warrantyReference: row.warranty_reference == null ? null : String(row.warranty_reference),
    warrantyEvidenceStatus: "Unverified", supportSlaReference: String(row.support_sla_reference ?? ""),
    responseDueAt: String(row.response_due_at ?? ""), sourceReference: String(row.source_reference ?? ""),
    physicalEvidenceClaim: false,
    currentStatus: statuses.includes(String(row.current_status)) ? String(row.current_status) as DeviceRmaCaseRecord["currentStatus"] : "Unknown",
    latestEventAt: row.latest_event_at == null ? null : String(row.latest_event_at),
    createdAt: String(row.created_at ?? ""), events, replacement, supportActions,
  };
}

function mapRemoteCommandCapability(raw: AnyRecord): DeviceRemoteCommandCapabilityRecord {
  const row = normalizeKeys(raw);
  const commandTypes = ["RequestPosition", "RequestDiagnostics", "RestartDevice"];
  const commandType = commandTypes.includes(String(row.command_type))
    ? String(row.command_type) as DeviceRemoteCommandCapabilityRecord["commandType"]
    : null;
  if (commandType === null) throw new Error("The command catalog returned an unsupported command type.");
  const status = ["Unverified", "Verified", "Rejected", "Revoked"].includes(String(row.capability_status))
    ? String(row.capability_status) as DeviceRemoteCommandCapabilityRecord["capabilityStatus"] : "Unknown";
  if (row.command_class !== "Observation" && row.command_class !== "Controlled")
    throw new Error("The command catalog returned an unsupported command class.");
  const commandClass = row.command_class;
  const available = row.request_admission_available === true && status === "Verified" &&
    typeof row.evidence_reference === "string" && row.evidence_reference.trim().length > 0 &&
    typeof row.expires_at === "string" && row.expires_at.trim().length > 0;
  if (row.certification_claim !== false)
    throw new Error("Command capability data crossed the no-certification-claim boundary.");
  return {
    commandType, displayName: String(row.display_name ?? commandType), commandClass,
    capabilityStatus: status, evidenceSource: row.evidence_source == null ? null : String(row.evidence_source),
    evidenceReference: row.evidence_reference == null ? null : String(row.evidence_reference),
    observedAt: row.observed_at == null ? null : String(row.observed_at),
    expiresAt: row.expires_at == null ? null : String(row.expires_at),
    requestAdmissionAvailable: available,
    confirmationText: String(row.confirmation_text ?? ""),
    externalHold: !available,
    externalHoldReason: available ? null : String(row.external_hold_reason ?? "Verified capability evidence is unavailable."),
    certificationClaim: false,
  };
}

function mapRemoteCommand(raw: AnyRecord): DeviceRemoteCommandRecord {
  const row = normalizeKeys(raw);
  if (row.provider_delivery_claim !== false || row.physical_outcome_claim !== false)
    throw new Error("Command history crossed the unverified delivery or physical-outcome boundary.");
  return {
    id: String(row.id ?? ""), commandType: String(row.command_type ?? "Unknown"),
    commandClass: String(row.command_class ?? "Unknown"), status: String(row.status ?? "Unknown"),
    governanceStatus: String(row.governance_status ?? "LegacyUnverified"),
    purpose: String(row.purpose ?? ""), sourceReference: String(row.source_reference ?? ""),
    attemptCount: Number.isSafeInteger(Number(row.attempt_count)) ? Number(row.attempt_count) : 0,
    maxAttempts: Number.isSafeInteger(Number(row.max_attempts)) ? Number(row.max_attempts) : 0,
    scheduledFor: String(row.scheduled_for ?? ""),
    dispatchedAt: row.dispatched_at == null ? null : String(row.dispatched_at),
    acknowledgedAt: row.acknowledged_at == null ? null : String(row.acknowledged_at),
    appliedAt: row.applied_at == null ? null : String(row.applied_at),
    expiresAt: row.expires_at == null ? null : String(row.expires_at),
    lastError: row.last_error == null ? null : String(row.last_error),
    providerDeliveryClaim: false, physicalOutcomeClaim: false,
    createdAt: String(row.created_at ?? ""),
  };
}

// The one-time secrets a provisioned device uses to authenticate its live telemetry
// stream — the equivalent of a Render/Vercel deploy token. Shown once, never again.
export type DeviceConnectionCredentials = {
  deviceId: string;
  deviceSerial: string;
  apiKey: string;
  hmacSecret: string;
  note: string;
};

export type DeviceProvisionResult = {
  device: DeviceCommandRecord;
  credentials: DeviceConnectionCredentials;
  ingestUrl: string;
};

export type DeviceInstallationInput = {
  /** Captured UI intent only; never sent as a backend payload field. */
  intent: DeviceInstallationIntent;
  vehicleId: string | number;
  deviceRole: string;
  isPrimary: boolean;
  effectiveAt: string;
  installationLocation: string;
  odometerAtInstallation: number | null;
  commissioningMethod: string;
  assignmentReason: string;
  removalReason?: string;
};

export type DeviceInstallationIntent = { kind: "create" } | {
  kind: "transfer";
  currentInstallationId: string;
  expectedRowVersion: number;
  priorVehicleId: string;
};

export type DeviceInstallationReceipt = {
  operation: "create" | "transfer";
  acknowledgement: "recorded" | "already-recorded";
  installationId: string;
  vehicleId: string;
  priorInstallationId: string | null;
  recordedStatus: string | null;
  effectiveFrom: string | null;
};

export class DeviceInstallationOutcomeError extends Error {
  constructor(public readonly outcome: "rejected" | "unconfirmed") {
    super(outcome === "rejected"
      ? "The installation request was rejected. Inspect the current installation and history before submitting again."
      : "The installation recording outcome could not be confirmed. Inspect the current installation and history before any new manual submission.");
    this.name = "DeviceInstallationOutcomeError";
  }
}

// This assignment-only boundary preserves the actual numeric JSON long contract.
function installationBodyId(value: unknown): number | null {
  const canonical = canonicalDeviceLifecycleId(value);
  if (canonical === null) return null;
  const number = Number(canonical);
  return Number.isSafeInteger(number) && number > 0 ? number : null;
}
function installationVersion(value: unknown, maximum: number): value is number {
  return typeof value === "number" && Number.isInteger(value) && value >= 1 && value <= maximum;
}
function installationObject(value: unknown): value is AnyRecord {
  return value !== null && typeof value === "object" && !Array.isArray(value)
    && (Object.getPrototypeOf(value) === Object.prototype || Object.getPrototypeOf(value) === null);
}
function exactLifecycleEnvelope(value: unknown): value is AnyRecord {
  if (!installationObject(value)) return false;
  return !Object.keys(value).some((key) => {
    const normalized = key.replace(/_/g, "").toLowerCase();
    return (normalized === "success" && key !== "success") || (normalized === "data" && key !== "data");
  });
}
function hasExactReceiptField(receipt: AnyRecord, camel: string, snake: string): boolean {
  return Object.hasOwn(receipt, camel) || Object.hasOwn(receipt, snake);
}

/** Capture an internally consistent opened target, not a guessed assignment mode. */
export function getInstallationIntent(target: Pick<DeviceCommandRecord, "id" | "currentInstallationId" | "currentInstallationRowVersion" | "assignedVehicleId">): DeviceInstallationIntent | null {
  if (canonicalDeviceLifecycleId(target.id) === null) return null;
  if (target.currentInstallationId == null && target.currentInstallationRowVersion == null && target.assignedVehicleId === "") return { kind: "create" };
  const priorId = installationBodyId(target.currentInstallationId);
  const priorVehicleId = canonicalDeviceLifecycleId(target.assignedVehicleId);
  if (priorId === null || priorVehicleId === null || !installationVersion(target.currentInstallationRowVersion, 2147483646)) return null;
  return { kind: "transfer", currentInstallationId: String(priorId), expectedRowVersion: target.currentInstallationRowVersion, priorVehicleId };
}

// The installation form supplies milliseconds. Never round finer request or
// DB-read replay times into equality; this is not a persistence precision claim.
function installationEffectiveInstant(value: unknown): number | null {
  if (typeof value !== "string") return null;
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?(Z|[+-]\d{2}:\d{2})$/.exec(value);
  if (!match) return null;
  const [, y, m, d, h, min, sec, fraction, offset] = match;
  const [year, month, day, hour, minute, second] = [y, m, d, h, min, sec].map(Number);
  const days = [31, year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0) ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
  if (year < 1 || month < 1 || month > 12 || day < 1 || day > days[month - 1] || hour > 23 || minute > 59 || second > 59
    || /[1-9]/.test((fraction ?? "").slice(3))) return null;
  if (offset !== "Z") {
    const hours = Number(offset.slice(1, 3));
    const minutes = Number(offset.slice(4, 6));
    if (offset === "-00:00" || hours > 14 || minutes > 59 || (hours === 14 && minutes !== 0)) return null;
  }
  const instant = Date.parse(value);
  const utcYear = new Date(instant).getUTCFullYear();
  return Number.isFinite(instant) && utcYear >= 1 && utcYear <= 9999 ? instant : null;
}

const installationRoles = ["GPS", "ELD", "Dashcam", "OBD-II", "J1939/CAN", "Temperature", "Fuel", "Tire", "BLE Gateway", "Other"];
const recordedInstallationStates = ["Provisioned", "Installed", "Verified", "Removed", "Failed", "Quarantined"];

export type DeviceInstallationRemovalInput = {
  effectiveTo: string;
  removalReason: string;
};

export type DeviceCommissioningInput = {
  result: "Passed" | "Failed";
  verificationReference: string;
};

export type DeviceCredentialRotationResult = {
  deviceId: string;
  apiKey: string;
  hmacSecret: string;
  previousCredentialsValidUntil: string | null;
  note: string;
};

export type DeviceIdentityQuarantineRecord = {
  id: string | number;
  deviceId?: string | number | null;
  vehicleId?: string | number | null;
  installationId?: string | number | null;
  reasonCode: string;
  evidenceJson?: AnyRecord;
  detectedAt?: string;
  deviceSerial?: string;
  imei?: string;
  deviceState?: string;
  vehicleCode?: string;
};

export type TelematicsClusterRecord = {
  id: string;
  deviceId: string | number;
  deviceName: string;
  serialNumber: string;
  deviceType: string;
  provider: string;
  vehicleId: string;
  vehicleCode: string;
  driverId: string;
  driverName: string;
  shipmentId: string;
  shipmentStatus: string;
  routeAssociation: string;
  locationLabel: string;
  latitude: string;
  longitude: string;
  speedMph: string;
  heading: string;
  geofenceStatus: string;
  lastPingAt: string;
  staleGps: string;
  offlineWarning: boolean;
  deviceHealth: number;
  deviceHealthAvailable: boolean;
  protocolType: "GPS" | "OBD-II" | "J1939" | "CAN" | "SENSOR" | "Unknown";
  positionAvailable: boolean;
  positionSource: string;
  positionProvider: string;
  positionAccuracy: string;
  positionConfidence: string;
  deviceFixAt: string;
  gatewayReceivedAt: string;
  routingReadiness: string;
  engineHours: string;
  odometer: string;
  fuelLevel: string;
  batteryVoltage: string;
  troubleCodes: string[];
  engineStatus: string;
  emissionsStatus: string;
  lastEngineDataAt: string;
  dataFreshnessStatus: string;
  sensorType: string;
  latestReading: string;
  expectedRange: string;
  sensorStatus: string;
  powerStatus: string;
  signalStrength: string;
  calibrationStatus: string;
  alertStatus: string;
  recommendedAction: string;
};

// ── Session / scoping helpers ───────────────────────────────────────────────────────

function getSession(): UserSession | null {
  if (typeof window === "undefined") return null;
  const raw = readRawSession();
  if (!raw) return null;
  try {
    return JSON.parse(raw) as UserSession;
  } catch {
    return null;
  }
}

function isSuperAdmin(session: UserSession | null) {
  return String(session?.role ?? "").toLowerCase().includes("super");
}

function getTenantId(session: UserSession | null) {
  const raw = session?.company?.id ?? session?.company?.companyId ?? session?.user?.companyId ?? session?.user?.company_id;
  const parsed = Number(raw);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : 0;
}

function ensureManagementAccess(session: UserSession | null) {
  const role = String(session?.role ?? "").toLowerCase();
  if (isDriverPortalRole(role) || isCustomerPortalRole(role)) {
    throw new Error("Permission denied");
  }
}

// The backend already scopes /api/telemetry/devices by tenant via the auth token.
// Here we apply the remaining PORTAL narrowing (driver / customer) using real row
// fields only — never seed lookups. A driver portal sees only devices whose real
// driver_name matches their identity; a customer portal has no device-level linkage
// field in the contract, so it honestly sees nothing.
function scopeDevicesForSession(rows: DeviceCommandRecord[], session: UserSession | null) {
  if (!session) return rows;
  if (isSuperAdmin(session)) return rows;

  const role = String(session.role ?? "").toLowerCase();

  if (isDriverPortalRole(role)) {
    const driverIdentity = resolveDriverIdentity(session);
    if (!driverIdentity) return [];
    return rows.filter((row) =>
      String(row.assignedDriverName ?? "").toLowerCase().includes(String(driverIdentity).toLowerCase()),
    );
  }

  if (isCustomerPortalRole(role)) {
    // No device→customer linkage exists in the verified device contract; surfacing
    // any device to a customer portal would be a fabricated association.
    const customerIdentity = resolveCustomerIdentity(session);
    if (!customerIdentity) return [];
    return [];
  }

  return rows;
}

function relativeAge(timestamp: string | null | undefined) {
  if (!timestamp) return "—";
  const parsed = new Date(timestamp).getTime();
  if (!Number.isFinite(parsed)) return "—";
  const deltaMinutes = Math.max(1, Math.round((Date.now() - parsed) / 60000));
  if (deltaMinutes < 60) return `${deltaMinutes} min ago`;
  const hours = Math.floor(deltaMinutes / 60);
  const mins = deltaMinutes % 60;
  return `${hours}h ${mins}m ago`;
}

// ── Honest device-health derivation (no dedicated table) ─────────────────────────────
// Derived from the real signals only: eld_devices.status + seconds_since_ping (>900 =
// stale) + open telemetry_alerts + active fault_codes. Start at 100 and subtract for
// each real degradation signal; never invent a number where no signal exists.
type HealthSignals = {
  status: string;
  secondsSincePing: number | null;
  hasCheckedIn: boolean;
  revoked: boolean;
  openAlerts: number;
  activeFaults: number;
};

function isStale(secondsSincePing: number | null) {
  return secondsSincePing != null && secondsSincePing > 900;
}

function deriveConnectionStatus(signals: HealthSignals): string {
  const status = String(signals.status ?? "").toLowerCase();
  if (signals.revoked || /revoked|suspend/i.test(status)) return "Offline";
  if (isStale(signals.secondsSincePing)) return "Offline";
  if (/malfunction|diagnostic/i.test(status) || signals.openAlerts > 0 || signals.activeFaults > 0) {
    return "Needs attention";
  }
  if (!signals.hasCheckedIn) return /provision|await/i.test(status) ? "Awaiting first check-in" : "Unknown";
  if (/active|online/i.test(status) || status === "") return "Online";
  // Fall back to the raw backend token (e.g. "Provisioning") rather than guessing.
  return String(signals.status ?? "Unknown");
}

function deriveHealthScore(signals: HealthSignals): number {
  let score = 100;
  if (isStale(signals.secondsSincePing)) score -= 40;
  if (signals.revoked || /revoked|suspend/i.test(String(signals.status ?? ""))) score -= 60;
  if (/malfunction|diagnostic/i.test(String(signals.status ?? ""))) score -= 25;
  score -= Math.min(30, signals.openAlerts * 10);
  score -= Math.min(30, signals.activeFaults * 8);
  return Math.max(0, Math.min(100, score));
}

// ── Backend row → DeviceCommandRecord mapping ────────────────────────────────────────
// Every field is sourced from the real /api/telemetry/devices row (snake_case) or an
// honest "—"/"Unknown" marker. No seed consultation, no fabricated telemetry defaults.
function mapDeviceRow(
  rawRow: AnyRecord,
  faultCountBySerial: Map<string, number>,
  alertCountBySerial: Map<string, number>,
  session: UserSession | null,
): DeviceCommandRecord {
  const row = normalizeKeys(rawRow);
  const serial = String(row.device_serial ?? "");
  const secondsSincePing = row.seconds_since_ping == null ? null : Number(row.seconds_since_ping);
  const revoked = Boolean(row.revoked_at);
  const openAlerts = Object.hasOwn(row, "open_alert_count")
    ? Number(row.open_alert_count ?? 0)
    : alertCountBySerial.get(serial) ?? 0;
  const activeFaults = Object.hasOwn(row, "active_fault_count")
    ? Number(row.active_fault_count ?? 0)
    : faultCountBySerial.get(serial) ?? 0;

  const signals: HealthSignals = {
    status: String(row.status ?? ""),
    secondsSincePing,
    hasCheckedIn: Boolean(row.last_seen_at),
    revoked,
    openAlerts,
    activeFaults,
  };
  const connectionStatus = deriveConnectionStatus(signals);
  const healthScore = deriveHealthScore(signals);
  const healthAvailable = signals.hasCheckedIn || secondsSincePing != null || revoked || openAlerts > 0 || activeFaults > 0;
  const hasExplicitInstallationContract = Object.hasOwn(row, "current_installation_id") ||
    Object.hasOwn(row, "current_installation_status") || Object.hasOwn(row, "installation_status");
  const hasCurrentInstallation = hasExplicitInstallationContract
    ? row.current_installation_id != null || /installed|verified/i.test(String(row.current_installation_status ?? row.installation_status ?? ""))
    : row.vehicle_id != null;

  const firmware = row.firmware_version == null ? "Unknown" : String(row.firmware_version);
  const deviceOpsAssessmentFieldsAvailable = [
    "exact_device_tuple_complete", "current_installation_recorded", "current_connectivity_profile_recorded",
    "current_telemetry_observed", "software_lifecycle_clear", "open_rma_count", "deviceops_gap_count",
  ].every((key) => Object.hasOwn(row, key));
  const openRmaCount = deviceOpsAssessmentFieldsAvailable ? Number(row.open_rma_count ?? 0) : 0;
  const derivedDeviceOpsGaps = deviceOpsAssessmentFieldsAvailable ? [
      row.exact_device_tuple_complete === false ? "Complete exact hardware identity" : null,
      row.current_installation_recorded === false ? "Record current installation" : null,
      row.current_connectivity_profile_recorded === false ? "Record SIM/eSIM profile" : null,
      row.current_telemetry_observed === false ? "Restore current telemetry observation" : null,
      row.software_lifecycle_clear === false ? "Resolve device lifecycle hold" : null,
      openRmaCount > 0 ? "Resolve open RMA" : null,
    ].filter((value): value is string => value !== null) : [];
  const projectedDeviceOpsGapCount = Number(row.deviceops_gap_count);
  const deviceOpsAssessmentAvailable = deviceOpsAssessmentFieldsAvailable &&
    Number.isInteger(projectedDeviceOpsGapCount) && projectedDeviceOpsGapCount >= 0 &&
    projectedDeviceOpsGapCount === derivedDeviceOpsGaps.length;
  const deviceOpsGaps = deviceOpsAssessmentAvailable ? derivedDeviceOpsGaps : [];

  return {
    id: (typeof row.id === "string" || typeof row.id === "number") ? row.id : serial,
    rowVersion: parseRowVersion(row.row_version),
    deviceId: serial,
    deviceName: String(row.device_model ?? serial ?? "Telematics device"),
    deviceType: String(row.device_model ?? row.device_category ?? "Unknown device"),
    deviceCategory: String(row.device_category ?? "Unknown"),
    manufacturer: String(row.manufacturer ?? ""),
    hardwareRevision: String(row.hardware_revision ?? ""),
    provider: String(row.provider ?? "Unknown"),
    // No provider registry endpoint — derive a stable code from the real provider name.
    providerCode: String(row.provider ?? "").toLowerCase().replace(/[^a-z0-9]+/g, "-"),
    serialNumber: serial,
    identifier: serial,
    imei: String(row.imei ?? ""),
    simNumber: "",
    assignedVehicleId: hasCurrentInstallation && row.vehicle_id != null ? String(row.vehicle_id) : "",
    vehicleId: hasCurrentInstallation && row.vehicle_id != null ? String(row.vehicle_id) : "",
    assignedVehicleCode: hasCurrentInstallation ? String(row.vehicle_code ?? "") : "",
    assignedDriverId: row.driver_id == null ? "" : String(row.driver_id),
    driverId: row.driver_id == null ? "" : String(row.driver_id),
    assignedDriverName: String(row.driver_name ?? ""),
    // No shipment linkage in the device contract.
    shipmentId: "",
    tenantId: getTenantId(session),
    tenantName: String(session?.company?.name ?? ""),
    firmwareVersion: firmware,
    // No OTA/target-firmware endpoint — equal to current so the "firmware pending" tab
    // never flags a fabricated pending update.
    targetFirmwareVersion: firmware,
    lastCheckIn: row.last_seen_at ? String(row.last_seen_at) : "—",
    connectionStatus,
    // No power/signal telemetry in the device contract — honest "—".
    powerStatus: "—",
    signalStrength: "—",
    dataHealthScore: healthScore,
    dataHealthAvailable: healthAvailable,
    installStatus: String(row.current_installation_status ?? row.installation_status ?? "Unknown"),
    currentInstallationId: row.current_installation_id == null ? undefined : String(row.current_installation_id),
    currentInstallationRowVersion: parseRowVersion(row.current_installation_row_version ?? row.installation_row_version),
    installationActivationVerifiedAt: row.activation_verified_at == null ? null : String(row.activation_verified_at),
    deviceRole: String(row.current_installation_role ?? row.device_role ?? ""),
    complianceStatus: "Not assessed",
    warrantyStatus: !deviceOpsAssessmentAvailable ? "Unassessed" : openRmaCount > 0 ? String(row.highest_open_rma_severity ?? "Open") : "No open RMA",
    supportStatus: !deviceOpsAssessmentAvailable ? "Assessment unavailable" : deviceOpsGaps.length > 0 ? `${deviceOpsGaps.length} listed software gap${deviceOpsGaps.length === 1 ? "" : "s"}` : "No listed software gap",
    deviceOpsGaps,
    deviceOpsAssessmentAvailable,
    openRmaCount,
    highestOpenRmaSeverity: openRmaCount > 0 ? String(row.highest_open_rma_severity ?? "Unknown") : "None",
    nextSupportResponseDueAt: row.next_support_response_due_at == null ? null : String(row.next_support_response_due_at),
    lifecycleStatus: revoked ? "Archived" : String(row.status ?? "Unknown"),
    deviceState: String(row.device_state ?? "Unknown"),
    eldStatus: String(row.status ?? "Unknown"),
    archivedAt: row.revoked_at ? String(row.revoked_at) : null,
    // Cross-links: only real fault/alert counts are honest here.
    linkedVehicleStatus: String(row.vehicle_status ?? "—"),
    linkedVehicleLocation: "—",
    linkedShipmentId: "",
    linkedShipmentStatus: "No active shipment",
    openAlertCount: openAlerts,
    activeFaultCount: activeFaults,
    maintenanceStatus: activeFaults > 0 ? `${activeFaults} active fault${activeFaults === 1 ? "" : "s"}` : "—",
    complianceSummary: "Not assessed",
  };
}

function mapInstallationRow(rawRow: AnyRecord, tenantId: number): TelematicsInstallationSeedRecord {
  const row = normalizeKeys(rawRow);
  return {
    id: canonicalDeviceLifecycleId(row.id ?? row.installation_id) ?? "",
    deviceId: (row.device_id as string | number | undefined) ?? "",
    tenantId,
    installStatus: String(row.status ?? row.installation_status ?? "Unknown"),
    installerName: String(row.installer_name ?? row.installed_by_name ?? ""),
    installedAt: row.effective_from != null
      ? String(row.effective_from)
      : row.installed_at != null
        ? String(row.installed_at)
        : null,
    removedAt: row.effective_to != null
      ? String(row.effective_to)
      : row.removed_at != null
        ? String(row.removed_at)
        : null,
    vehicleId: row.vehicle_id == null ? "" : String(row.vehicle_id),
    vehicleCode: String(row.vehicle_code ?? ""),
    deviceRole: String(row.device_role ?? ""),
    isPrimary: Boolean(row.is_primary),
    rowVersion: typeof row.row_version === "number" || typeof row.row_version === "string"
      ? parseRowVersion(row.row_version) : undefined,
    activationVerifiedAt: row.activation_verified_at == null ? null : String(row.activation_verified_at),
    installationLocation: String(row.installation_location ?? ""),
    odometerAtInstallation: row.odometer_at_installation == null ? "" : String(row.odometer_at_installation),
    commissioningMethod: String(row.commissioning_method ?? ""),
    commissioningResult: String(row.commissioning_result ?? ""),
    verificationReference: String(row.verification_reference ?? ""),
    assignmentReason: String(row.assignment_reason ?? ""),
    removalReason: String(row.removal_reason ?? ""),
    checklist: Array.isArray(row.checklist) ? row.checklist as Array<{ item: string; status: string }> : [],
  };
}

const installationChecklistItems: DeviceInstallationChecklistItem[] = [
  "DeviceIdentity", "VehicleIdentity", "Mounting", "PrimaryPower", "Ground", "Ignition",
  "GNSSAntenna", "CellularAntenna", "Harness", "CANBus", "CameraAlignment", "SensorPlacement",
];
const installationChecklistResults: DeviceInstallationChecklistResult[] = ["Pass", "Fail", "NotObserved", "NotApplicable"];
const installationArtifactTypes: DeviceInstallationArtifactType[] = [
  "InstallationPhoto", "SerialLabel", "WiringPhoto", "PowerReading", "TechnicianChecklist",
  "CommissioningReport", "RemovalPhoto", "OtherDocument",
];

function requiredInstallationChecklistItems(category: string): DeviceInstallationChecklistItem[] {
  const required: DeviceInstallationChecklistItem[] = [
    "DeviceIdentity", "VehicleIdentity", "Mounting", "PrimaryPower", "Ground", "Ignition", "Harness",
  ];
  if (/gps|eld|telematics/i.test(category)) required.push("GNSSAntenna", "CellularAntenna");
  if (/j1939|can|obd/i.test(category)) required.push("CANBus");
  if (/camera|dashcam|video/i.test(category)) required.push("CameraAlignment");
  if (/temperature|fuel|tire|sensor/i.test(category)) required.push("SensorPlacement");
  return required;
}

function mapInstallationChecklistObservation(raw: AnyRecord): DeviceInstallationChecklistObservationRecord | null {
  const row = normalizeKeys(raw);
  const checklistItem = installationChecklistItems.find(value => value === row.checklist_item);
  const observedResult = installationChecklistResults.find(value => value === row.observed_result);
  if (!checklistItem || !observedResult || row.assurance_status !== "Unverified" ||
      row.physical_evidence_claim !== false || row.certification_claim !== false) return null;
  const id = canonicalDeviceLifecycleId(row.id);
  const workPackageId = canonicalDeviceLifecycleId(row.work_package_id);
  const observedAt = typeof row.observed_at === "string" ? row.observed_at : "";
  if (!id || !workPackageId || installationEffectiveInstant(observedAt) === null ||
      typeof row.evidence_reference !== "string" || row.evidence_reference.trim().length < 3 ||
      typeof row.observation_notes !== "string" || row.observation_notes.trim().length < 3) return null;
  return {
    id, workPackageId, checklistItem, observedResult,
    evidenceReference: String(row.evidence_reference ?? ""),
    observationNotes: String(row.observation_notes ?? ""),
    observedAt,
    assuranceStatus: "Unverified", physicalEvidenceClaim: false, certificationClaim: false,
    recordedByName: String(row.recorded_by_name ?? ""),
  };
}

function mapInstallationArtifactReference(raw: AnyRecord): DeviceInstallationArtifactReferenceRecord | null {
  const row = normalizeKeys(raw);
  const artifactType = installationArtifactTypes.find(value => value === row.artifact_type);
  if (!artifactType || row.content_verification_status !== "Unverified" ||
      row.physical_evidence_claim !== false || row.certification_claim !== false) return null;
  const id = canonicalDeviceLifecycleId(row.id);
  const workPackageId = canonicalDeviceLifecycleId(row.work_package_id);
  const capturedAt = typeof row.captured_at === "string" ? row.captured_at : "";
  const objectKey = typeof row.object_key === "string" ? row.object_key : "";
  if (!id || !workPackageId || installationEffectiveInstant(capturedAt) === null ||
      !objectKey.trim() || objectKey.startsWith("/") || objectKey.includes("\\") ||
      objectKey.includes("..") || /%2e/i.test(objectKey) || /^[a-z][a-z0-9+.-]*:/i.test(objectKey) ||
      typeof row.sha256 !== "string" || !/^[0-9a-f]{64}$/.test(row.sha256)) return null;
  return {
    id, workPackageId, artifactType,
    objectKey, sha256: row.sha256, capturedAt,
    contentVerificationStatus: "Unverified", physicalEvidenceClaim: false, certificationClaim: false,
    recordedByName: String(row.recorded_by_name ?? ""),
  };
}

function mapInstallationWorkPackage(
  raw: AnyRecord,
  checklistRows: DeviceInstallationChecklistObservationRecord[],
  artifactRows: DeviceInstallationArtifactReferenceRecord[],
  deviceCategory: string,
): DeviceInstallationWorkPackageRecord | null {
  const row = normalizeKeys(raw);
  if (row.physical_appointment_claim !== false || row.physical_work_claim !== false || row.certification_claim !== false)
    return null;
  const id = canonicalDeviceLifecycleId(row.id);
  const deviceId = canonicalDeviceLifecycleId(row.device_id);
  const vehicleId = canonicalDeviceLifecycleId(row.vehicle_id);
  const installerId = canonicalDeviceLifecycleId(row.assigned_installer_user_id);
  const appointmentStartText = typeof row.appointment_start === "string" ? row.appointment_start : "";
  const appointmentEndText = typeof row.appointment_end === "string" ? row.appointment_end : "";
  const appointmentStart = installationEffectiveInstant(appointmentStartText);
  const appointmentEnd = installationEffectiveInstant(appointmentEndText);
  if (!id || !deviceId || !vehicleId || !installerId || appointmentStart === null || appointmentEnd === null ||
      appointmentEnd <= appointmentStart || typeof row.work_order_reference !== "string" || !row.work_order_reference.trim() ||
      typeof row.service_location !== "string" || !row.service_location.trim() ||
      typeof row.work_scope !== "string" || row.work_scope.trim().length < 5)
    return null;
  const observations = checklistRows.filter(observation => observation.workPackageId === id);
  const latest = new Map<DeviceInstallationChecklistItem, DeviceInstallationChecklistObservationRecord>();
  for (const observation of observations) if (!latest.has(observation.checklistItem)) latest.set(observation.checklistItem, observation);
  const latestChecklist = [...latest.values()];
  const requiredChecklistItems = requiredInstallationChecklistItems(deviceCategory);
  const requiredResults = requiredChecklistItems.map(item => latest.get(item));
  const hasFailure = requiredResults.some(observation => observation?.observedResult === "Fail");
  const allRecorded = requiredResults.every(observation => observation && ["Pass", "NotApplicable"].includes(observation.observedResult));
  const artifactReferences = artifactRows.filter(artifact => artifact.workPackageId === id);
  const linkedInstallationId = row.linked_installation_id == null
    ? null : canonicalDeviceLifecycleId(row.linked_installation_id);
  if (row.linked_installation_id != null && (!linkedInstallationId || row.link_assurance_status !== "RecordedUnverified" ||
      row.link_physical_work_claim !== false || row.link_certification_claim !== false ||
      installationEffectiveInstant(row.linked_at) === null || typeof row.linked_installation_status !== "string" ||
      !row.linked_installation_status.trim())) return null;
  const readinessStatus: DeviceInstallationWorkPackageRecord["readinessStatus"] = hasFailure
    ? "BlockedByFailedCheck"
    : !allRecorded
      ? "AwaitingChecklist"
      : artifactReferences.length === 0
        ? "ChecklistRecordedAwaitingArtifacts"
        : linkedInstallationId
          ? "LinkedAwaitingIndependentVerification"
          : "RecordedAwaitingIndependentVerification";
  return {
    id, deviceId, vehicleId, vehicleCode: String(row.vehicle_code ?? ""),
    assignedInstallerUserId: installerId, installerName: String(row.installer_name ?? ""),
    workOrderReference: row.work_order_reference, appointmentStart: appointmentStartText,
    appointmentEnd: appointmentEndText, serviceLocation: row.service_location,
    workScope: String(row.work_scope ?? ""), readinessStatus, requiredChecklistItems, latestChecklist,
    artifactReferences, linkedInstallationId,
    linkedInstallationStatus: linkedInstallationId ? String(row.linked_installation_status) : null,
    linkAssuranceStatus: linkedInstallationId ? "RecordedUnverified" : null,
    linkedAt: linkedInstallationId ? String(row.linked_at) : null,
    physicalAppointmentClaim: false, physicalWorkClaim: false, certificationClaim: false,
  };
}

function installationMutationKey(deviceId: string | number) {
  const uuid = globalThis.crypto?.randomUUID?.();
  return uuid ? `device-${deviceId}-${uuid}` : `device-${deviceId}-${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

// Group active fault codes by device SERIAL (fault_codes.device_id is the serial string).
function countFaultsBySerial(faultRows: AnyRecord[]): Map<string, number> {
  const map = new Map<string, number>();
  for (const fault of faultRows) {
    const serial = String(fault.device_id ?? "");
    if (!serial) continue;
    map.set(serial, (map.get(serial) ?? 0) + 1);
  }
  return map;
}

// Group open alerts by device serial (telemetry_alerts expose device_serial).
function countAlertsBySerial(alertRows: AnyRecord[]): Map<string, number> {
  const map = new Map<string, number>();
  for (const alert of alertRows) {
    if (String(alert.status ?? "").toLowerCase() !== "open") continue;
    const serial = String(alert.device_serial ?? "");
    if (!serial) continue;
    map.set(serial, (map.get(serial) ?? 0) + 1);
  }
  return map;
}

// ── Protocol / sensor classification (from real device_model text) ───────────────────

function deriveProtocolType(device: DeviceCommandRecord): TelematicsClusterRecord["protocolType"] {
  if (/j1939/i.test(device.deviceType)) return "J1939";
  if (/obd/i.test(device.deviceType)) return "OBD-II";
  if (/\bcan\b/i.test(device.deviceType)) return "CAN";
  if (/sensor|temperature|door|fuel|tire/i.test(device.deviceType)) return "SENSOR";
  if (/gps|tracker|location/i.test(device.deviceType)) return "GPS";
  return "Unknown";
}

function isValidPosition(position: AnyRecord | undefined) {
  if (!position) return false;
  const lat = Number(position.lat);
  const lng = Number(position.lng);
  return Number.isFinite(lat) && Number.isFinite(lng) && lat >= -90 && lat <= 90 && lng >= -180 && lng <= 180;
}

function readableSource(source: unknown) {
  const value = String(source ?? "").trim();
  if (!value) return "Unknown source";
  return value.replace(/[_-]+/g, " ").replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function normalizeProviderToken(value: string) {
  return String(value ?? "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "")
    .trim();
}

function buildProviderNameSearchTerms(value: string) {
  const normalized = String(value ?? "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, " ")
    .trim();
  return normalized ? normalized.split(/\s+/).filter((term) => term.length > 2) : [];
}

function deriveProviderIntegrationAudit(
  device: DeviceCommandRecord,
  providerCatalog: TelematicsProviderSeedRecord[],
): TelematicsProviderSeedRecord[] {
  const providerName = String(device.provider ?? "").trim();
  const normalizedDeviceProvider = normalizeProviderToken(providerName);
  const searchTerms = buildProviderNameSearchTerms(providerName);
  const seen = new Set<string>();

  if (!providerCatalog.length) {
    return [
      {
        id: "provider-catalog-empty",
        name: providerName || "Unknown provider",
        category: "Telematics & ELD",
        integrationStatus: "Disconnected",
        tenantId: device.tenantId ?? 0,
        deviceCount: 0,
        lastSyncAt: "—",
        supportTier: "tenant",
        pendingDevices: 0,
        isMatchedToDevice: false,
        matchConfidence: "none",
        visibilitySource: "unmatched",
        auditMessage: "No telematics connectors are available for this tenant.",
      },
    ];
  }

  if (!providerName) {
    const firstConnected = providerCatalog.find((provider) => /connected/i.test(provider.integrationStatus));
    if (firstConnected) {
      return [
        {
          ...firstConnected,
          id: `provider-audit-${String(firstConnected.id)}-${device.id}-missing-device`,
          isMatchedToDevice: false,
          matchConfidence: "none",
          visibilitySource: "unmatched",
          auditMessage: "Device provider field is empty; cannot map to a specific connector.",
        },
      ];
    }
    return [
      {
        id: "provider-not-provided",
        name: "No provider on device",
        category: "Telematics & ELD",
        integrationStatus: "Disconnected",
        tenantId: device.tenantId ?? 0,
        deviceCount: 0,
        lastSyncAt: "—",
        supportTier: "tenant",
        pendingDevices: 0,
        isMatchedToDevice: false,
        matchConfidence: "none",
        visibilitySource: "unmatched",
        auditMessage: "Device provider value is missing; cannot validate integration linkage.",
      },
    ];
  }

  const exactMatches = providerCatalog.filter((provider) =>
    normalizeProviderToken(provider.name) && normalizeProviderToken(provider.name) === normalizedDeviceProvider,
  );
  const fuzzyMatches = exactMatches.length
    ? []
    : providerCatalog.filter((provider) => {
        const normalizedCandidate = normalizeProviderToken(provider.name);
        return (
          normalizedCandidate.includes(normalizedDeviceProvider)
          || normalizedDeviceProvider.includes(normalizedCandidate)
          || searchTerms.some((term) => normalizedCandidate.includes(term))
        );
      });

  const matched = [...exactMatches, ...fuzzyMatches];
  const confidence: TelematicsProviderSeedRecord["matchConfidence"] = exactMatches.length ? "exact" : fuzzyMatches.length ? "fuzzy" : "none";

  if (!matched.length) {
    const visibleIntegration = providerCatalog.find((provider) => /connected/i.test(provider.integrationStatus));
    return [
      {
        id: "provider-unmatched",
        name: providerName,
        category: "Telematics & ELD",
        integrationStatus: "Disconnected",
        tenantId: device.tenantId ?? 0,
        deviceCount: 0,
        lastSyncAt: "—",
        supportTier: "tenant",
        pendingDevices: 0,
        isMatchedToDevice: false,
        matchConfidence: "none",
        visibilitySource: "unmatched",
        auditMessage: visibleIntegration
          ? "No connector name matches this device provider."
          : "Provider catalog is visible, but no matching connector exists.",
      },
    ];
  }

  return matched
    .filter((provider) => {
      if (seen.has(provider.id)) return false;
      seen.add(provider.id);
      return true;
    })
    .map((provider) => ({
      ...provider,
      id: `provider-audit-${provider.id}-${device.id}`,
      // A catalog-name match is discovery evidence only. It does not prove that this
      // persisted device is installed/mapped to the provider record.
      isMatchedToDevice: false,
      matchConfidence: confidence,
      visibilitySource: "unmatched",
      auditMessage:
        confidence === "exact"
          ? "Provider name matches the device field, but no persisted provider-device mapping has been verified."
          : "Provider name is similar to the device field, but no persisted provider-device mapping has been verified.",
    }));
}

async function loadProviderCatalog(): Promise<TelematicsProviderSeedRecord[]> {
  const payload = await integrationsApi.list();
  const records = payload.records ?? [];
  return records.map((record) => {
    const providerRecord = record as AnyRecord;
    const connectedTo = Array.isArray(providerRecord.connectedTo) ? (providerRecord.connectedTo as unknown[]) : [];
    const connectedToCount = connectedTo.reduce((count: number, _entry: unknown) => count + (Boolean(_entry) ? 1 : 0), 0);
    const status = String(providerRecord.status ?? "Disconnected");
    const isError = /error/i.test(status);
    return {
      id: String(providerRecord.id ?? ""),
      name: String(providerRecord.name ?? "Unknown provider"),
      category: String(providerRecord.category ?? "Telematics & ELD"),
      integrationStatus: status,
      tenantId: Number(providerRecord.tenantId ?? 0) || 0,
      deviceCount: Number(providerRecord.deviceCount ?? connectedToCount ?? 0) || connectedToCount || 0,
      lastSyncAt: String(providerRecord.lastSyncAt ?? "—"),
      supportTier: String(providerRecord.scope ?? "tenant"),
      pendingDevices: isError || /pending/i.test(status) ? connectedToCount : 0,
      connectedTo: connectedTo.length ? connectedTo.map(String) : [],
      matchConfidence: "none",
      visibilitySource: "connected",
      isMatchedToDevice: false,
      auditMessage: "",
    };
  });
}

export function canReadProviderCatalog(session: UserSession | null): boolean {
  const permissions = session?.permissions ?? [];
  const permitted = ["integrations:view", "integrations:manage", "telematics:providers:manage"]
    .some((permission) => hasPermission(permissions, permission));
  if (!permitted) return false;

  // RequireIntegrationsModule checks the authoritative `fleet.integrations` key.
  // Legacy tenants inherit access; allowlist tenants must carry that exact enabled
  // entitlement or a request would deterministically receive 403.
  return session?.entitlementPolicyMode !== "package_allowlist"
    || session.entitlements?.["fleet.integrations"] === true;
}

function restrictedProviderAudit(device: DeviceCommandRecord): TelematicsProviderSeedRecord[] {
  return [{
    id: "provider-audit-restricted",
    name: "Integrations visibility restricted",
    category: "Telematics & ELD",
    integrationStatus: "Restricted",
    tenantId: device.tenantId ?? 0,
    deviceCount: 0,
    lastSyncAt: "—",
    supportTier: "tenant",
    pendingDevices: 0,
    isMatchedToDevice: false,
    visibilitySource: "restricted",
    matchConfidence: "restricted",
    auditMessage: "Integration connector evidence is restricted for this role or tenant plan.",
  }];
}

async function buildProviderAuditForDevice(device: DeviceCommandRecord, session: UserSession | null): Promise<TelematicsProviderSeedRecord[]> {
  if (!canReadProviderCatalog(session)) return restrictedProviderAudit(device);
  try {
    const catalog = await loadProviderCatalog();
    return deriveProviderIntegrationAudit(device, catalog);
  } catch {
    return [
      {
        id: "provider-audit-unavailable",
        name: "Integration evidence unavailable",
        category: "Telematics & ELD",
        integrationStatus: "Unavailable",
        tenantId: device.tenantId ?? 0,
        deviceCount: 0,
        lastSyncAt: "—",
        supportTier: "tenant",
        pendingDevices: 0,
        isMatchedToDevice: false,
        visibilitySource: "unavailable",
        matchConfidence: "none",
        auditMessage: "Integration connector evidence is temporarily unavailable.",
      },
    ];
  }
}

function deriveSensorType(device: DeviceCommandRecord) {
  if (/temperature/i.test(device.deviceType)) return "temperature";
  if (/door/i.test(device.deviceType)) return "door open/close";
  if (/fuel/i.test(device.deviceType)) return "fuel";
  if (/tire/i.test(device.deviceType)) return "tire pressure";
  if (/humidity/i.test(device.deviceType)) return "humidity";
  if (/reefer|cold/i.test(device.deviceType)) return "reefer/cold-chain";
  if (/battery|power/i.test(device.deviceType)) return "battery/power";
  return "Unknown sensor channel";
}

// Match a live position snapshot to a device by real ids (device_id numeric, or the
// shared vehicle_id). Returns undefined when no live position exists for the device.
function positionForDevice(device: DeviceCommandRecord, positions: AnyRecord[]): AnyRecord | undefined {
  return positions.find((pos) => {
    if (device.id != null && pos.device_id != null && String(pos.device_id) === String(device.id)) return true;
    if (device.assignedVehicleId && pos.vehicle_id != null && String(pos.vehicle_id) === device.assignedVehicleId) return true;
    return false;
  });
}

// Build a cluster row from a real device + its real live position + real fault codes.
// Every value is either a live field or an honest "—"/empty marker — no fake defaults.
function toClusterRecord(
  device: DeviceCommandRecord,
  positions: AnyRecord[],
  faultRows: AnyRecord[],
  evidenceKind: "position" | "diagnostics" = "position",
): TelematicsClusterRecord {
  const position = positionForDevice(device, positions);
  const deviceFaults = faultRows.filter((fault) => String(fault.device_id ?? "") === device.serialNumber);
  const troubleCodes = deviceFaults.map((fault) => {
    const code = String(fault.code ?? "").trim();
    const type = String(fault.code_type ?? "").trim();
    return code ? [type, code].filter(Boolean).join(" ") : "";
  }).filter(Boolean);
  const explicitProtocol = String(position?.protocol ?? "").toLowerCase();
  const protocolType: TelematicsClusterRecord["protocolType"] = /j1939/.test(explicitProtocol)
    ? "J1939"
    : /obd/.test(explicitProtocol)
      ? "OBD-II"
      : /can/.test(explicitProtocol)
        ? "CAN"
        : /gps|gt06/.test(explicitProtocol)
          ? "GPS"
          : deriveProtocolType(device);
  const sensorType = deriveSensorType(device);

  const positionAvailable = isValidPosition(position);
  const serverFreshness = String(position?.freshness ?? "").toLowerCase();
  const isStalePosition = position ? String(position.is_stale) === "1" || serverFreshness === "stale" : false;
  const deviceFixAt = position?.device_fix_time ?? position?.event_time;
  const gatewayReceivedAt = position?.gateway_received_at;
  const lastPingAt = deviceFixAt ? String(deviceFixAt) : device.lastCheckIn;
  const engineStatus = position?.engine_status ? String(position.engine_status) : "—";
  const hasEngineEvidence = troubleCodes.length > 0 || [position?.engine_status, position?.odometer_miles, position?.fuel_level, position?.battery_voltage].some((value) => value != null && String(value).trim() !== "");
  const requiredEvidenceAvailable = evidenceKind === "diagnostics" ? hasEngineEvidence : positionAvailable;
  // GPS and diagnostics have different evidence contracts. A vehicle-level fix is
  // never substituted for a source device's own evidence by the API.
  const offlineWarning = /offline/i.test(device.connectionStatus) || isStalePosition || !requiredEvidenceAvailable;
  const dataFreshnessStatus = !requiredEvidenceAvailable
    ? "No data"
    : offlineWarning
      ? "Stale"
      : serverFreshness === "delayed" || /attention|warning/i.test(device.connectionStatus)
        ? "Watch"
        : serverFreshness === "live"
          ? "Fresh"
          : "Unknown";
  const sensorStatus = offlineWarning ? "Alerting" : /attention/i.test(device.connectionStatus) ? "Watch" : position ? "Nominal" : "No data";
  const routingReadiness = !positionAvailable
    ? "Blocked — no valid position"
    : dataFreshnessStatus !== "Fresh"
      ? "Blocked — position is not current"
      : "Current fix available — route linkage not evaluated";

  return {
    id: `${protocolType.toLowerCase()}-${device.id}`,
    deviceId: device.id,
    deviceName: device.deviceName,
    serialNumber: device.serialNumber,
    deviceType: device.deviceType,
    provider: device.provider,
    vehicleId: device.assignedVehicleId,
    vehicleCode: device.assignedVehicleCode || "Unassigned",
    driverId: device.assignedDriverId,
    driverName: device.assignedDriverName || "Unassigned",
    shipmentId: device.linkedShipmentId || "No active shipment",
    shipmentStatus: device.linkedShipmentStatus,
    routeAssociation: "Not linked",
    locationLabel: positionAvailable ? String(position?.address ?? `${position?.lat}, ${position?.lng}`) : "No valid fix",
    latitude: position?.lat != null ? String(position.lat) : "—",
    longitude: position?.lng != null ? String(position.lng) : "—",
    speedMph: position?.speed_mph != null ? String(position.speed_mph) : "—",
    heading: position?.heading != null ? String(position.heading) : "—",
    geofenceStatus: !positionAvailable ? "No fix" : isStalePosition ? "Last known" : serverFreshness === "live" ? "Current fix" : serverFreshness === "delayed" ? "Delayed fix" : "Freshness unknown",
    lastPingAt,
    staleGps: relativeAge(lastPingAt),
    offlineWarning,
    deviceHealth: device.dataHealthScore,
    deviceHealthAvailable: device.dataHealthAvailable,
    protocolType,
    positionAvailable,
    positionSource: readableSource(position?.source),
    positionProvider: position?.provider ? String(position.provider) : "Unknown",
    positionAccuracy: position?.accuracy_meters != null ? `${position.accuracy_meters} m` : "Unknown",
    positionConfidence: position?.confidence != null && Number.isFinite(Number(position.confidence))
      ? `${Math.round(Number(position.confidence) * 100)}%`
      : "Unknown",
    deviceFixAt: deviceFixAt ? String(deviceFixAt) : "—",
    gatewayReceivedAt: gatewayReceivedAt ? String(gatewayReceivedAt) : "—",
    routingReadiness,
    engineHours: "—",
    odometer: position?.odometer_miles != null ? String(position.odometer_miles) : "—",
    fuelLevel: position?.fuel_level != null ? String(position.fuel_level) : "—",
    batteryVoltage: position?.battery_voltage != null ? String(position.battery_voltage) : "—",
    troubleCodes,
    engineStatus,
    // A generic DTC is not automatically an emissions fault. That classification
    // requires a server-supplied diagnostic category which is not in this feed.
    emissionsStatus: "Not evaluated",
    lastEngineDataAt: hasEngineEvidence ? lastPingAt : "—",
    dataFreshnessStatus,
    sensorType,
    // No standalone sensor-reading feed in the verified backend contract, so we
    // NEVER fabricate a reading or an expected-range setpoint. Both stay honest "—".
    latestReading: "—",
    expectedRange: "—",
    sensorStatus,
    powerStatus: device.powerStatus,
    signalStrength: device.signalStrength,
    calibrationStatus: "—",
    alertStatus: device.openAlertCount > 0 || offlineWarning || troubleCodes.length > 0 ? "Open" : "Clear",
    recommendedAction: offlineWarning
      ? "Use last known location and investigate the device link before the next trip."
      : troubleCodes.length > 0
        ? "Active fault codes present — review diagnostics before assignment."
        : /attention/i.test(device.connectionStatus)
          ? "Refresh the stream and validate data quality before assignment."
          : positionAvailable && dataFreshnessStatus === "Fresh"
            ? "Current position is available; confirm route and assignment context before dispatch use."
            : "No evidence-backed operator action is available from the current signals.",
  };
}

function toColdChainClusterRecord(
  device: TemperatureDevice,
  zones: TemperatureZone[],
  alerts: TemperatureAlert[],
): TelematicsClusterRecord {
  const zone = zones.find((candidate) => String(candidate.id) === String(device.zoneId));
  const deviceAlerts = alerts.filter((alert) =>
    String(alert.deviceId) === String(device.id) && !/resolved/i.test(String(alert.status)),
  );
  const measurementSource = String(device.lastMeasurementSource ?? "");
  const hasAuthoritativeMeasurement = /^(Sensor|Gateway)$/i.test(measurementSource);
  const temperature = device.lastReportedTemperatureCelsius;
  const hasTemperature = hasAuthoritativeMeasurement && temperature !== null && temperature !== undefined && Number.isFinite(Number(temperature));
  const battery = device.batteryPercent;
  const hasBattery = hasAuthoritativeMeasurement && battery !== null && battery !== undefined && Number.isFinite(Number(battery));
  const lastPingAt = hasAuthoritativeMeasurement && device.lastPingAtUtc ? String(device.lastPingAtUtc) : "";
  const measurementObservedAt = hasAuthoritativeMeasurement && device.lastMeasurementObservedAtUtc
    ? String(device.lastMeasurementObservedAtUtc)
    : "";
  const lastPingMs = lastPingAt ? new Date(lastPingAt).getTime() : Number.NaN;
  const stale = !Number.isFinite(lastPingMs) || Date.now() - lastPingMs > 15 * 60 * 1000;
  const inactive = !/active|online/i.test(String(device.status));
  const outsideZone = Boolean(zone && hasTemperature && (Number(temperature) < zone.minCelsius || Number(temperature) > zone.maxCelsius));
  const alerting = deviceAlerts.length > 0 || outsideZone;
  const lowBattery = hasBattery && Number(battery) <= 20;
  const offlineWarning = stale || inactive;
  const sensorStatus = offlineWarning ? "Offline" : alerting ? "Alerting" : lowBattery ? "Watch" : "Nominal";
  const freshness = !lastPingAt ? "No data" : stale ? "Stale" : "Fresh";
  const expectedRange = zone ? `${zone.minCelsius}–${zone.maxCelsius} °C` : "Not configured";
  const shipmentLabel = device.shipmentNumber ? String(device.shipmentNumber) : "No active shipment";

  return {
    id: `cold-chain-${device.id}`,
    deviceId: device.id,
    deviceName: device.name || device.deviceCode,
    serialNumber: device.deviceCode,
    deviceType: "Cold-chain sensor",
    provider: hasAuthoritativeMeasurement && device.sourceChannel ? String(device.sourceChannel) : "Unverified",
    vehicleId: "",
    vehicleCode: device.vehicleNumber || "Unassigned",
    driverId: "",
    driverName: "Unassigned",
    shipmentId: shipmentLabel,
    shipmentStatus: device.shipmentNumber ? "Linked" : "Not linked",
    routeAssociation: device.shipmentNumber ? `Shipment ${device.shipmentNumber}` : "Not linked",
    locationLabel: device.zoneName || zone?.name || "No configured zone",
    latitude: "—",
    longitude: "—",
    speedMph: "—",
    heading: "—",
    geofenceStatus: "Not applicable",
    lastPingAt: lastPingAt || "—",
    staleGps: relativeAge(lastPingAt),
    offlineWarning,
    deviceHealth: 0,
    deviceHealthAvailable: false,
    protocolType: "SENSOR",
    positionAvailable: false,
    positionSource: measurementObservedAt ? `${measurementSource} measurement` : "No authenticated measurement evidence",
    positionProvider: hasAuthoritativeMeasurement && device.sourceChannel ? String(device.sourceChannel) : "Unverified",
    positionAccuracy: "Not reported",
    positionConfidence: "Not reported",
    deviceFixAt: "—",
    gatewayReceivedAt: lastPingAt || "—",
    routingReadiness: "Not applicable",
    engineHours: "—",
    odometer: "—",
    fuelLevel: "—",
    batteryVoltage: "—",
    troubleCodes: [],
    engineStatus: "Not applicable",
    emissionsStatus: "Not applicable",
    lastEngineDataAt: "—",
    dataFreshnessStatus: freshness,
    sensorType: zone?.name || device.zoneName || "Temperature",
    latestReading: hasTemperature ? `${Number(temperature).toFixed(1)} °C` : "—",
    expectedRange,
    sensorStatus,
    powerStatus: hasBattery ? `${Math.round(Number(battery))}% battery` : "Not reported",
    signalStrength: "Not reported",
    calibrationStatus: device.calibrationStatus ? `Reported: ${device.calibrationStatus}` : "Not reported",
    alertStatus: alerting ? "Open" : "Clear",
    recommendedAction: !hasAuthoritativeMeasurement
      ? "No authenticated sensor or gateway measurement is available; keep the device out of automated cold-chain decisions."
      : offlineWarning
      ? "Restore the device heartbeat before relying on this shipment's temperature posture."
      : alerting
        ? `Investigate the active breach against ${expectedRange}.`
        : lowBattery
          ? "Plan a battery service before the device stops reporting."
          : "Reading is current and within the configured zone.",
  };
}

// ── Shared reads ─────────────────────────────────────────────────────────────────────

async function fetchDeviceRows(): Promise<AnyRecord[]> {
  return unwrap<AnyRecord[]>(apiClient.get("/api/telemetry/devices"));
}

async function fetchActiveFaults(): Promise<AnyRecord[]> {
  return (await unwrap<AnyRecord[]>(apiClient.get("/api/maintenance/fault-codes", { params: { status: "active" } }))).map(normalizeKeys);
}

async function fetchOpenAlerts(): Promise<AnyRecord[]> {
  return (await unwrap<AnyRecord[]>(apiClient.get("/api/telemetry/alerts", { params: { status: "Open" } }))).map(normalizeKeys);
}

function canReadEntitledFeed(session: UserSession | null, permission: string, entitlement: "maintenance" | "telematics") {
  if (!hasPermission(session?.permissions ?? [], permission)) return false;
  return session?.entitlementPolicyMode !== "package_allowlist" || session.entitlements?.[entitlement] === true;
}

async function fetchActiveFaultsIfAuthorized(session: UserSession | null): Promise<AnyRecord[]> {
  return canReadEntitledFeed(session, "maintenance:view", "maintenance") ? fetchActiveFaults() : [];
}

async function fetchOpenAlertsIfAuthorized(session: UserSession | null): Promise<AnyRecord[]> {
  return canReadEntitledFeed(session, "telemetry.alerts.read", "telematics") ? fetchOpenAlerts() : [];
}

async function fetchPositions(): Promise<AnyRecord[]> {
  return (await unwrap<AnyRecord[]>(apiClient.get("/api/telemetry/positions"))).map(normalizeKeys);
}

async function fetchPositionsIfAuthorized(session: UserSession | null): Promise<AnyRecord[]> {
  return canReadEntitledFeed(session, "telemetry.live_state.read", "telematics") ? fetchPositions() : [];
}

// Assemble scoped DeviceCommandRecord[] from the live device + fault + alert feeds.
async function loadScopedDevices(session: UserSession | null): Promise<DeviceCommandRecord[]> {
  const [rows, faults, alerts] = await Promise.all([
    fetchDeviceRows(),
    fetchActiveFaultsIfAuthorized(session),
    fetchOpenAlertsIfAuthorized(session),
  ]);
  const faultCounts = countFaultsBySerial(faults);
  const alertCounts = countAlertsBySerial(alerts);
  const mapped = rows.map((row) => mapDeviceRow(row, faultCounts, alertCounts, session));
  return scopeDevicesForSession(mapped, session);
}

export const telematicsService = {
  async getIdentityQuarantine(): Promise<DeviceIdentityQuarantineRecord[]> {
    const rows = await unwrap<AnyRecord[]>(apiClient.get("/api/telemetry/installation-quarantine"));
    return rows.map((row) => normalizeKeys(row) as DeviceIdentityQuarantineRecord);
  },

  async resolveIdentityQuarantine(
    id: string | number,
    payload: { resolutionNotes: string; correctedDeviceSerial?: string; correctedImei?: string },
  ) {
    const session = getSession();
    ensureManagementAccess(session);
    return unwrap<AnyRecord>(apiClient.post(`/api/telemetry/installation-quarantine/${id}/resolve`, payload));
  },

  async getDevices(): Promise<DeviceCommandRecord[]> {
    const session = getSession();
    return loadScopedDevices(session);
  },

  async getDevicePage(options: DevicePageOptions = {}): Promise<DevicePageResult> {
    const session = getSession();
    const payload = await unwrap<{
      items: AnyRecord[];
      total: number;
      page: number;
      pageSize: number;
      exportComplete?: boolean;
      summary?: AnyRecord;
    }>(apiClient.get("/api/telemetry/devices/page", {
      params: {
        page: options.page ?? 1,
        pageSize: Math.min(100, Math.max(1, options.pageSize ?? 100)),
        search: options.search?.trim() || undefined,
        view: options.view ?? "all",
        sort: options.sort ?? "serial",
        direction: options.direction ?? "asc",
      },
    }));
    const summary = payload.summary ?? {};
    return {
      items: (payload.items ?? []).map((row) => mapDeviceRow(row, new Map(), new Map(), session)),
      total: Number(payload.total ?? 0),
      page: Number(payload.page ?? 1),
      pageSize: Number(payload.pageSize ?? 100),
      summary: {
        active: Number(summary.active ?? 0),
        archived: Number(summary.archived ?? 0),
        offline: Number(summary.offline ?? 0),
        attention: Number(summary.attention ?? 0),
        online: Number(summary.online ?? 0),
        neverConnected: Number(summary.neverConnected ?? summary.never_connected ?? 0),
        faulted: summary.faulted == null ? null : Number(summary.faulted),
        readinessGaps: summary.readinessGaps == null && summary.readiness_gaps == null
          ? null
          : Number(summary.readinessGaps ?? summary.readiness_gaps),
      },
    };
  },

  async getTelemetryClusterPage(
    kind: "gps-tracking" | "obd-j1939",
    options: TelemetryClusterPageOptions = {},
  ): Promise<TelemetryClusterPageResult> {
    const session = getSession();
    const payload = await unwrap<{
      items: AnyRecord[];
      total: number;
      page: number;
      pageSize: number;
      exportComplete?: boolean;
      summary?: AnyRecord;
    }>(apiClient.get("/api/telemetry/devices/page", {
      params: {
        page: options.page ?? 1,
        pageSize: Math.min(options.purpose === "export" ? 10_000 : 100, Math.max(1, options.pageSize ?? 50)),
        search: options.search?.trim() || undefined,
        view: options.view ?? "all",
        cluster: kind === "obd-j1939" ? "diagnostics" : "gps",
        purpose: options.purpose ?? "view",
        sort: options.sort ?? "risk",
        direction: options.direction ?? "desc",
      },
    }));
    const normalized = (payload.items ?? []).map(normalizeKeys);
    const positions = normalized
      .filter((row) => kind === "obd-j1939"
        ? row.position_event_time != null || row.position_device_fix_time != null
        : row.position_lat != null && row.position_lng != null)
      .map((row) => ({
        device_id: row.id,
        vehicle_id: row.vehicle_id,
        lat: row.position_lat,
        lng: row.position_lng,
        speed_mph: row.position_speed_mph,
        heading: row.position_heading,
        accuracy_meters: row.position_accuracy_meters,
        engine_status: row.position_engine_status,
        odometer_miles: row.position_odometer_miles,
        fuel_level: row.position_fuel_level,
        battery_voltage: row.position_battery_voltage,
        event_time: row.position_event_time,
        address: row.position_address,
        source: row.position_source,
        provider: row.position_provider,
        protocol: row.position_protocol,
        confidence: row.position_confidence,
        device_fix_time: row.position_device_fix_time,
        gateway_received_at: row.position_gateway_received_at,
        freshness: row.position_freshness,
        is_stale: row.position_freshness === "stale" ? "1" : "0",
      }));
    const faults = normalized.flatMap((row) => String(row.active_fault_codes ?? "")
      .split(",")
      .map((code) => code.trim())
      .filter(Boolean)
      .map((code) => ({ device_id: row.device_serial, code })));
    const devices = normalized.map((row) => mapDeviceRow(row, new Map(), new Map(), session));
    const summary = normalizeKeys(payload.summary ?? {});
    return {
      items: devices.map((device) => toClusterRecord(device, positions, faults, kind === "obd-j1939" ? "diagnostics" : "position")),
      total: Number(payload.total ?? 0),
      page: Number(payload.page ?? 1),
      pageSize: Number(payload.pageSize ?? 50),
      exportComplete: payload.exportComplete,
      summary: {
        active: Number(summary.active ?? 0),
        offline: Number(summary.offline ?? 0),
        attention: Number(summary.attention ?? 0),
        online: Number(summary.online ?? 0),
        delayed: Number(summary.delayed ?? 0),
        stale: Number(summary.stale ?? 0),
        noPosition: Number(summary.noPosition ?? summary.no_position ?? 0),
      },
    };
  },

  async getDeviceById(id: string | number): Promise<DeviceDetailRecord> {
    const session = getSession();
    // Real single-device read + the cross-feeds needed to populate the detail drawer.
    const [detailPayload, faults, alerts, positions] = await Promise.all([
      unwrap<AnyRecord>(apiClient.get(`/api/telemetry/devices/${id}`)),
      fetchActiveFaultsIfAuthorized(session),
      canReadEntitledFeed(session, "telemetry.alerts.read", "telematics")
        ? unwrap<AnyRecord[]>(apiClient.get("/api/telemetry/alerts", { params: { status: "All" } })).then((rows) => rows.map(normalizeKeys))
        : Promise.resolve([]),
      fetchPositionsIfAuthorized(session),
    ]);

    const detail = normalizeKeys(detailPayload);
    // The detail route remains backward-compatible: device inventory fields stay at
    // the top level while installation/current/history are added alongside them.
    // Accept nested record/device shapes too so the UI survives envelope evolution.
    const row = normalizeKeys((detail.device ?? detail.record ?? detail) as AnyRecord);
    const tenantId = getTenantId(session);
    const installationRows = Array.isArray(detail.installation_history)
      ? detail.installation_history as AnyRecord[]
      : Array.isArray(detail.installations)
        ? detail.installations as AnyRecord[]
        : [];
    const installations = installationRows.map((installation) => mapInstallationRow(installation, tenantId));
    const currentInstallation = detail.current_installation && typeof detail.current_installation === "object"
      ? mapInstallationRow(detail.current_installation as AnyRecord, tenantId)
      : installations.find((installation) =>
          installation.removedAt == null && /installed|verified/i.test(installation.installStatus),
        ) ?? null;

    const faultCounts = countFaultsBySerial(faults);
    const openAlertCounts = countAlertsBySerial(alerts);
    const mappedDevice = mapDeviceRow(row, faultCounts, openAlertCounts, session);
    const device = currentInstallation
      ? {
          ...mappedDevice,
          assignedVehicleId: currentInstallation.vehicleId || mappedDevice.assignedVehicleId,
          vehicleId: currentInstallation.vehicleId || mappedDevice.vehicleId,
          assignedVehicleCode: currentInstallation.vehicleCode || mappedDevice.assignedVehicleCode,
          installStatus: currentInstallation.installStatus,
          currentInstallationId: currentInstallation.id,
          currentInstallationRowVersion: currentInstallation.rowVersion,
          installationActivationVerifiedAt: currentInstallation.activationVerifiedAt,
          deviceRole: currentInstallation.deviceRole || mappedDevice.deviceRole,
        }
      : { ...mappedDevice, installStatus: "Not installed" };

    // Enforce portal scoping on the single-device read too.
    const [scoped] = scopeDevicesForSession([device], session);
    if (!scoped) throw new Error("Device not found");

    const installationChecklist = (Array.isArray(detail.installation_checklist_observations)
      ? detail.installation_checklist_observations as AnyRecord[] : [])
      .map(mapInstallationChecklistObservation)
      .filter((row): row is DeviceInstallationChecklistObservationRecord => row !== null);
    const installationArtifacts = (Array.isArray(detail.installation_artifact_references)
      ? detail.installation_artifact_references as AnyRecord[] : [])
      .map(mapInstallationArtifactReference)
      .filter((row): row is DeviceInstallationArtifactReferenceRecord => row !== null);
    const installationWorkPackages = (Array.isArray(detail.installation_work_packages)
      ? detail.installation_work_packages as AnyRecord[] : [])
      .map(row => mapInstallationWorkPackage(row, installationChecklist, installationArtifacts, scoped.deviceCategory))
      .filter((row): row is DeviceInstallationWorkPackageRecord => row !== null);

    const serial = scoped.serialNumber;
    const deviceFaults = faults.filter((fault) => String(fault.device_id ?? "") === serial);
    const deviceAlerts = alerts.filter((alert) => String(alert.device_serial ?? "") === serial);
    const position = positionForDevice(scoped, positions);
    const compatibilityRow = normalizeKeys(
      detail.compatibility && typeof detail.compatibility === "object"
        ? detail.compatibility as AnyRecord
        : {},
    );
    const capabilityDeclarationStatus = compatibilityRow.capability_declaration_status === "EngineeringDeclaredUnverified"
      ? "EngineeringDeclaredUnverified" as const
      : "NotRecorded" as const;
    const compatibilityList = (value: unknown): string[] => capabilityDeclarationStatus === "EngineeringDeclaredUnverified"
      && Array.isArray(value)
      ? [...new Set(value.filter((item): item is string => typeof item === "string")
        .map(item => item.trim()).filter(item => item.length > 0))]
      : [];
    const compatibility: DeviceCompatibilityRecord = {
      manufacturer: typeof compatibilityRow.manufacturer === "string" && compatibilityRow.manufacturer.trim()
        ? compatibilityRow.manufacturer.trim() : null,
      deviceModel: typeof compatibilityRow.device_model === "string" && compatibilityRow.device_model.trim()
        ? compatibilityRow.device_model.trim() : null,
      hardwareRevision: typeof compatibilityRow.hardware_revision === "string" && compatibilityRow.hardware_revision.trim()
        ? compatibilityRow.hardware_revision.trim() : null,
      firmwareVersion: typeof compatibilityRow.firmware_version === "string" && compatibilityRow.firmware_version.trim()
        ? compatibilityRow.firmware_version.trim() : null,
      exactTupleComplete: compatibilityRow.exact_tuple_complete === true,
      missingIdentityFields: Array.isArray(compatibilityRow.missing_identity_fields)
        ? compatibilityRow.missing_identity_fields.map(String) : ["compatibility status unavailable"],
      registryStatus: String(compatibilityRow.registry_status ?? "Unregistered"),
      // Fail closed if an older or malformed API omits the Stage115 projection.
      certificationStatus: "ExternalHold",
      maximumTier: "Unverified",
      candidateSha: typeof compatibilityRow.candidate_sha === "string" && /^[0-9a-f]{40}$/.test(compatibilityRow.candidate_sha)
        ? compatibilityRow.candidate_sha : null,
      externalHold: true,
      externalHoldReason: String(compatibilityRow.external_hold_reason
        ?? "Compatibility evidence is unavailable. Hardware certification remains on external hold."),
      capabilityDeclarationStatus,
      protocols: compatibilityList(compatibilityRow.protocols),
      supportedFields: compatibilityList(compatibilityRow.supported_fields),
      supportedEvents: compatibilityList(compatibilityRow.supported_events),
      supportedCommands: compatibilityList(compatibilityRow.supported_commands),
      knownLimitations: typeof compatibilityRow.known_limitations === "string" && compatibilityRow.known_limitations.trim()
        ? compatibilityRow.known_limitations.trim()
        : "Capability metadata has not been recorded for this candidate.",
      declarationSourceReference: capabilityDeclarationStatus === "EngineeringDeclaredUnverified"
        && typeof compatibilityRow.declaration_source_reference === "string"
        && compatibilityRow.declaration_source_reference.trim()
        ? compatibilityRow.declaration_source_reference.trim() : null,
      declaredAt: capabilityDeclarationStatus === "EngineeringDeclaredUnverified"
        && typeof compatibilityRow.declared_at === "string"
        && Number.isFinite(Date.parse(compatibilityRow.declared_at))
        ? compatibilityRow.declared_at : null,
      catalogSupportTier: "Unverified",
      certificationReference: null,
      certificationDate: null,
      physicalEvidenceClaim: false,
      providerEvidenceClaim: false,
      certificationClaim: false,
    };
    const connectivityRows = Array.isArray(detail.connectivity_profiles)
      ? detail.connectivity_profiles as AnyRecord[]
      : [];
    const connectivityProfiles = connectivityRows.map(mapConnectivityProfile);
    const connectivityObservations = (Array.isArray(detail.connectivity_observations)
      ? detail.connectivity_observations as AnyRecord[] : []).map(mapConnectivityObservation);
    const firmwareCampaignRows = Array.isArray(detail.firmware_campaigns)
      ? detail.firmware_campaigns as AnyRecord[]
      : [];
    const firmwareCampaigns = firmwareCampaignRows.map(mapFirmwareCampaign);
    const rmaEvents = (Array.isArray(detail.rma_events) ? detail.rma_events as AnyRecord[] : []).map(mapRmaEvent);
    const rmaReplacements = (Array.isArray(detail.rma_replacements) ? detail.rma_replacements as AnyRecord[] : []).map(mapRmaReplacement);
    const rmaSupportActions = (Array.isArray(detail.rma_support_actions) ? detail.rma_support_actions as AnyRecord[] : []).map(mapRmaSupportAction);
    const rmaCases = (Array.isArray(detail.rma_cases) ? detail.rma_cases as AnyRecord[] : []).map(rawCase => {
      const caseId = String(normalizeKeys(rawCase).id ?? "");
      return mapRmaCase(rawCase,
        rmaEvents.filter(event => event.caseId === caseId),
        rmaReplacements.find(replacement => replacement.caseId === caseId) ?? null,
        rmaSupportActions.filter(action => action.caseId === caseId));
    });
    const sparePoolEvents = (Array.isArray(detail.spare_pool_events) ? detail.spare_pool_events as AnyRecord[] : []).map(mapSparePoolEvent);
    const sparePool = detail.spare_pool_entry && typeof detail.spare_pool_entry === "object"
      ? mapSparePool(detail.spare_pool_entry as AnyRecord, sparePoolEvents)
      : null;
    const supportTierEvents = (Array.isArray(detail.support_tier_events)
      ? detail.support_tier_events as AnyRecord[] : []).map(mapDeviceSupportTierEvent);
    const hasRemoteCommandGovernance = detail.remote_command_governance !== null &&
      typeof detail.remote_command_governance === "object";
    const remoteCommandGovernance = normalizeKeys(hasRemoteCommandGovernance
      ? detail.remote_command_governance as AnyRecord : {});
    if (hasRemoteCommandGovernance && (remoteCommandGovernance.provider_delivery_claim !== false ||
        remoteCommandGovernance.physical_outcome_claim !== false))
      throw new Error("Remote-command governance crossed the unverified outcome boundary.");
    const remoteCommandCapabilities = (Array.isArray(remoteCommandGovernance.capabilities)
      ? remoteCommandGovernance.capabilities as AnyRecord[] : []).map(mapRemoteCommandCapability);
    const remoteCommandHistory = (Array.isArray(remoteCommandGovernance.history)
      ? remoteCommandGovernance.history as AnyRecord[] : []).map(mapRemoteCommand);
    const responseCurrentConnectivityProfile = detail.current_connectivity_profile && typeof detail.current_connectivity_profile === "object"
      ? mapConnectivityProfile(detail.current_connectivity_profile as AnyRecord)
      : null;
    const currentConnectivityProfile = responseCurrentConnectivityProfile !== null
      && responseCurrentConnectivityProfile.effectiveTo == null
      && responseCurrentConnectivityProfile.assignmentStatus === "Assigned"
      ? responseCurrentConnectivityProfile
      : connectivityProfiles.find((profile) => profile.effectiveTo == null && profile.assignmentStatus === "Assigned") ?? null;

    // Telemetry: derived from the single live position snapshot (one point, or none).
    const telemetry: TelematicsTelemetrySeedRecord[] = position
      ? [{
          id: `position-${scoped.id}`,
          deviceId: scoped.id,
          vehicleId: scoped.assignedVehicleId,
          driverId: scoped.assignedDriverId,
          latitude: position.lat != null ? String(position.lat) : "—",
          longitude: position.lng != null ? String(position.lng) : "—",
          speedMph: position.speed_mph != null ? String(position.speed_mph) : "—",
          heading: position.heading != null ? String(position.heading) : "—",
          engineStatus: position.engine_status ? String(position.engine_status) : "—",
          odometer: position.odometer_miles != null ? String(position.odometer_miles) : "—",
          fuelLevel: position.fuel_level != null ? String(position.fuel_level) : "—",
          geofenceStatus: String(position.is_stale) === "1" ? "Last known" : "Live",
          eventAt: position.event_time ? String(position.event_time) : scoped.lastCheckIn,
        }]
      : [];

    // Diagnostics: real active fault codes for this device (by serial).
    const diagnostics: TelematicsDiagnosticSeedRecord[] = deviceFaults.map((fault) => ({
      id: String(fault.id),
      deviceId: scoped.id,
      tenantId: scoped.tenantId,
      result: String(fault.severity ?? "Info"),
      // These voltage/modem/gnss channels have no source in the fault-code contract.
      batteryVoltage: "—",
      modemStatus: "—",
      gnssStatus: "—",
      faultCode: `${String(fault.code_type ?? "")} ${String(fault.code ?? "")}`.trim(),
      runAt: String(fault.last_seen_at ?? fault.first_seen_at ?? ""),
      runBy: String(fault.description ?? ""),
    }));

    // Health timeline: real telemetry alerts for this device.
    const healthEvents: TelematicsHealthSeedRecord[] = deviceAlerts.map((alert) => ({
      id: String(alert.id),
      deviceId: scoped.id,
      tenantId: scoped.tenantId,
      score: scoped.dataHealthScore,
      status: String(alert.status ?? "Open"),
      signalStrength: scoped.signalStrength,
      eventAt: String(alert.created_at ?? ""),
      summary: `${String(alert.severity ?? "")} · ${String(alert.message ?? alert.alert_type ?? "")}`.trim(),
    }));

    return {
      device: scoped,
      compatibility,
      currentConnectivityProfile,
      connectivityProfiles,
      connectivityObservations,
      telemetry,
      healthEvents,
      diagnostics,
      firmwareUpdates: [], // no executed OTA result feed; plans remain separate and ExternalHold
      firmwareCampaigns,
      rmaCases,
      sparePool,
      supportTierEvents,
      remoteCommandCapabilities,
      remoteCommandHistory,
      currentInstallation,
      installations,
      installationWorkPackages,
      sensorReadings: [], // no standalone sensor-reading endpoint
      providers: await buildProviderAuditForDevice(scoped, session),
      auditLog: [], // no device audit-log endpoint
      retirementRecord: detail.retirement_record && typeof detail.retirement_record === "object"
        ? mapDeviceRetirement(detail.retirement_record as AnyRecord) : null,
      lifecycleHistory: (Array.isArray(detail.lifecycle_history)
        ? detail.lifecycle_history as AnyRecord[]
        : Array.isArray(detail.assignment_history) ? detail.assignment_history as AnyRecord[] : [])
        .map(mapDeviceLifecycleTransition),
    };
  },

  async createInstallationWorkPackage(deviceId: string | number, input: DeviceInstallationWorkPackageInput) {
    const session = getSession();
    ensureManagementAccess(session);
    const requestedId = installationBodyId(deviceId);
    const vehicleId = installationBodyId(input.vehicleId);
    const start = Date.parse(input.appointmentStart);
    const end = Date.parse(input.appointmentEnd);
    if (requestedId === null || vehicleId === null) throw new Error("Select a valid device and vehicle.");
    if (!Number.isFinite(start) || !Number.isFinite(end) || end <= start)
      throw new Error("Enter an appointment end after the appointment start.");
    if (input.workOrderReference.trim().length < 2 || input.workOrderReference.trim().length > 120 ||
        input.serviceLocation.trim().length < 2 || input.serviceLocation.trim().length > 160 ||
        input.workScope.trim().length < 5 || input.workScope.trim().length > 1000)
      throw new Error("Enter a work-order reference, service location, and work scope within the supported lengths.");
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(input.idempotencyKey))
      throw new Error("The installation work-package form session is invalid. Close and reopen it.");
    const row = normalizeKeys(await unwrap<AnyRecord>(apiClient.post(
      `/api/telemetry/devices/${requestedId}/installation-work-packages`, {
        ...input, vehicleId, workOrderReference: input.workOrderReference.trim(),
        serviceLocation: input.serviceLocation.trim(), workScope: input.workScope.trim(),
      })));
    if (row.physical_appointment_claim !== false || row.physical_work_claim !== false || row.certification_claim !== false)
      throw new Error("The server did not preserve the unverified installation-work boundary.");
    return { id: String(row.id ?? ""), note: "Installation work package recorded. Attendance and physical work remain unverified." };
  },

  async recordInstallationChecklistObservation(
    deviceId: string | number, workPackageId: string | number,
    input: DeviceInstallationChecklistObservationInput,
  ) {
    const session = getSession();
    ensureManagementAccess(session);
    const requestedId = installationBodyId(deviceId);
    const packageId = installationBodyId(workPackageId);
    if (requestedId === null || packageId === null) throw new Error("A valid device and work package are required.");
    if (!installationChecklistItems.includes(input.checklistItem) || !installationChecklistResults.includes(input.observedResult))
      throw new Error("Select the checklist item and the result actually observed.");
    if (input.evidenceReference.trim().length < 3 || input.evidenceReference.trim().length > 240 ||
        input.observationNotes.trim().length < 3 || input.observationNotes.trim().length > 1000)
      throw new Error("Enter an evidence reference and observation notes within the supported lengths.");
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(input.idempotencyKey))
      throw new Error("The checklist form session is invalid. Close and reopen it.");
    const row = normalizeKeys(await unwrap<AnyRecord>(apiClient.post(
      `/api/telemetry/devices/${requestedId}/installation-work-packages/${packageId}/checklist-observations`, {
        ...input, evidenceReference: input.evidenceReference.trim(), observationNotes: input.observationNotes.trim(),
      })));
    if (row.assurance_status !== "Unverified" || row.physical_evidence_claim !== false || row.certification_claim !== false)
      throw new Error("The server did not preserve the unverified checklist boundary.");
    return { id: String(row.id ?? ""), note: "Operator checklist observation recorded. Independent physical verification remains outstanding." };
  },

  async recordInstallationArtifactReference(
    deviceId: string | number, workPackageId: string | number,
    input: DeviceInstallationArtifactReferenceInput,
  ) {
    const session = getSession();
    ensureManagementAccess(session);
    const requestedId = installationBodyId(deviceId);
    const packageId = installationBodyId(workPackageId);
    const objectKey = input.objectKey.trim();
    const sha256 = input.sha256.trim().toLowerCase();
    if (requestedId === null || packageId === null) throw new Error("A valid device and work package are required.");
    if (!installationArtifactTypes.includes(input.artifactType)) throw new Error("Select a supported artifact type.");
    if (!objectKey || objectKey.length > 1024 || objectKey.startsWith("/") || objectKey.includes("\\") ||
        objectKey.includes("..") || /%2e/i.test(objectKey) || /^[a-z][a-z0-9+.-]*:/i.test(objectKey))
      throw new Error("Enter a relative governed-storage key.");
    if (!/^[0-9a-f]{64}$/.test(sha256)) throw new Error("SHA-256 must contain exactly 64 hexadecimal characters.");
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(input.idempotencyKey))
      throw new Error("The artifact-reference form session is invalid. Close and reopen it.");
    const row = normalizeKeys(await unwrap<AnyRecord>(apiClient.post(
      `/api/telemetry/devices/${requestedId}/installation-work-packages/${packageId}/artifact-references`, {
        ...input, objectKey, sha256,
      })));
    if (row.content_verification_status !== "Unverified" || row.physical_evidence_claim !== false || row.certification_claim !== false)
      throw new Error("The server did not preserve the unverified artifact boundary.");
    return { id: String(row.id ?? ""), note: "Artifact reference recorded. Content and physical work remain unverified." };
  },

  async linkInstallationWorkPackage(
    deviceId: string | number, workPackageId: string | number, installationId: string | number,
  ) {
    const session = getSession();
    ensureManagementAccess(session);
    const requestedId = installationBodyId(deviceId);
    const packageId = installationBodyId(workPackageId);
    const targetInstallationId = installationBodyId(installationId);
    if (requestedId === null || packageId === null || targetInstallationId === null)
      throw new Error("A valid device, work package, and persisted installation are required.");
    const row = normalizeKeys(await unwrap<AnyRecord>(apiClient.post(
      `/api/telemetry/devices/${requestedId}/installation-work-packages/${packageId}/installation-links`, {
        installationId: targetInstallationId,
        idempotencyKey: installationMutationKey(`work-${packageId}-installation-${targetInstallationId}`),
      })));
    if (canonicalDeviceLifecycleId(row.work_package_id) !== String(packageId) ||
        canonicalDeviceLifecycleId(row.installation_id) !== String(targetInstallationId) ||
        row.link_assurance_status !== "RecordedUnverified" || row.physical_work_claim !== false ||
        row.certification_claim !== false)
      throw new Error("The server did not preserve the unverified installation-link boundary.");
    return {
      id: String(row.id ?? ""),
      note: "Work package linked to the persisted installation. Physical work and certification remain unverified.",
    };
  },

  async replaceDeviceConnectivityProfile(deviceId: string | number, input: DeviceConnectivityProfileInput) {
    const session = getSession();
    ensureManagementAccess(session);
    const requestedId = String(deviceId);
    if (!/^\d+$/.test(requestedId) || Number(requestedId) <= 0) throw new Error("A valid device is required.");
    if (!/^[0-9]{18,22}$/.test(input.iccid.trim())) throw new Error("ICCID must contain 18-22 digits.");
    if (input.msisdn?.trim() && !/^\+[1-9][0-9]{7,14}$/.test(input.msisdn.trim()))
      throw new Error("MSISDN must use E.164 format, for example +14165550123.");
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(input.idempotencyKey))
      throw new Error("The connectivity form session is invalid. Close and reopen it.");
    const payload = await unwrap<AnyRecord>(apiClient.post(
      `/api/telemetry/devices/${requestedId}/connectivity-profiles`,
      {
        ...input,
        carrierName: input.carrierName.trim(),
        iccid: input.iccid.trim(),
        msisdn: input.msisdn?.trim() || null,
        apn: input.apn?.trim() || null,
        changeReason: input.changeReason.trim(),
        sourceReference: input.sourceReference.trim(),
      },
    ));
    const normalized = normalizeKeys(payload);
    if (normalized.connectivity_claim !== false)
      throw new Error("The server did not return the required fail-closed connectivity acknowledgement.");
    if (!normalized.profile || typeof normalized.profile !== "object")
      throw new Error("The server did not return the recorded connectivity profile.");
    return {
      profile: mapConnectivityProfile(normalized.profile as AnyRecord),
      idempotentReplay: normalized.idempotent_replay === true,
      note: String(normalized.note ?? "Profile inventory recorded; connectivity remains unverified."),
    };
  },

  async createFirmwareCampaign(input: DeviceFirmwareCampaignInput) {
    const session = getSession();
    ensureManagementAccess(session);
    const deviceIds = input.deviceIds.map(installationBodyId);
    if (deviceIds.length < 1 || deviceIds.length > 500 || deviceIds.some(id => id === null))
      throw new Error("A firmware campaign requires 1-500 valid devices.");
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(input.idempotencyKey))
      throw new Error("The firmware planning form session is invalid. Close and reopen it.");
    const payload = await unwrap<AnyRecord>(apiClient.post("/api/telemetry/firmware-campaigns", {
      ...input,
      deviceIds,
      campaignName: input.campaignName.trim(),
      targetFirmwareVersion: input.targetFirmwareVersion.trim(),
      rollbackFirmwareVersion: input.rollbackFirmwareVersion?.trim() || null,
      changeReason: input.changeReason.trim(),
      sourceReference: input.sourceReference.trim(),
    }));
    const normalized = normalizeKeys(payload);
    if (normalized.remote_upgrade_claim !== false)
      throw new Error("The server did not return the required no-upgrade-claim acknowledgement.");
    if (!normalized.campaign || typeof normalized.campaign !== "object" || !Array.isArray(normalized.targets))
      throw new Error("The server did not return the recorded firmware plan.");
    const campaignRow = normalizeKeys(normalized.campaign as AnyRecord);
    return {
      campaignId: String(campaignRow.id ?? ""),
      targets: (normalized.targets as AnyRecord[]).map(target => mapFirmwareCampaign({ ...campaignRow, ...normalizeKeys(target) })),
      idempotentReplay: normalized.idempotent_replay === true,
      note: String(normalized.note ?? "Firmware planning recorded; no command was dispatched."),
    };
  },

  async createDeviceRmaCase(deviceId: string | number, input: DeviceRmaCaseInput) {
    const session = getSession();
    ensureManagementAccess(session);
    const canonicalId = canonicalDeviceLifecycleId(deviceId);
    if (canonicalId === null) throw new Error("The RMA device identity is invalid.");
    const normalizedInput = {
      ...input,
      failureDescription: input.failureDescription.trim(),
      warrantyReference: input.warrantyReference?.trim() || null,
      supportSlaReference: input.supportSlaReference.trim(),
      sourceReference: input.sourceReference.trim(),
    };
    const payload = normalizeKeys(await unwrap<AnyRecord>(apiClient.post(
      `/api/telemetry/devices/${canonicalId}/rma-cases`, normalizedInput)));
    if (payload.physical_evidence_claim !== false || !payload.rma_case || typeof payload.rma_case !== "object")
      throw new Error("The server did not return a fail-closed RMA acknowledgement.");
    return {
      rmaCase: mapRmaCase(payload.rma_case as AnyRecord),
      idempotentReplay: payload.idempotent_replay === true,
      note: String(payload.note ?? "RMA case recorded; physical and warranty evidence remain unverified."),
    };
  },

  async appendDeviceRmaEvent(caseId: string | number, input: DeviceRmaEventInput) {
    const session = getSession();
    ensureManagementAccess(session);
    const canonicalId = canonicalDeviceLifecycleId(caseId);
    if (canonicalId === null) throw new Error("The RMA case identity is invalid.");
    const payload = normalizeKeys(await unwrap<AnyRecord>(apiClient.post(
      `/api/telemetry/rma-cases/${canonicalId}/events`, {
        ...input,
        custodyLocation: input.custodyLocation?.trim() || null,
        trackingReference: input.trackingReference?.trim() || null,
        evidenceReference: input.evidenceReference.trim(), notes: input.notes.trim(),
      })));
    if (payload.physical_completion_claim !== false || !payload.rma_event || typeof payload.rma_event !== "object")
      throw new Error("The server did not return a fail-closed custody acknowledgement.");
    return {
      event: mapRmaEvent(payload.rma_event as AnyRecord),
      idempotentReplay: payload.idempotent_replay === true,
      note: String(payload.note ?? "Custody event recorded; physical verification remains external."),
    };
  },

  async planDeviceRmaReplacement(caseId: string | number, input: DeviceRmaReplacementInput) {
    const session = getSession();
    ensureManagementAccess(session);
    const canonicalId = canonicalDeviceLifecycleId(caseId);
    if (canonicalId === null) throw new Error("The RMA case identity is invalid.");
    const payload = normalizeKeys(await unwrap<AnyRecord>(apiClient.post(
      `/api/telemetry/rma-cases/${canonicalId}/replacement`, {
        ...input,
        replacementDeviceSerial: input.replacementDeviceSerial.trim(),
        changeReason: input.changeReason.trim(), sourceReference: input.sourceReference.trim(),
      })));
    if (payload.physical_swap_claim !== false || !payload.replacement || typeof payload.replacement !== "object")
      throw new Error("The server did not return a planning-only replacement acknowledgement.");
    return {
      replacement: mapRmaReplacement(payload.replacement as AnyRecord),
      idempotentReplay: payload.idempotent_replay === true,
      note: String(payload.note ?? "Replacement planned; no physical swap is claimed."),
    };
  },

  async recordDeviceRmaSupportAction(caseId: string | number, input: DeviceRmaSupportActionInput) {
    const session = getSession();
    ensureManagementAccess(session);
    const canonicalId = canonicalDeviceLifecycleId(caseId);
    if (canonicalId === null) throw new Error("The RMA case identity is invalid.");
    const payload = normalizeKeys(await unwrap<AnyRecord>(apiClient.post(
      `/api/telemetry/rma-cases/${canonicalId}/support-actions`, {
        ...input,
        supportQueue: input.supportQueue.trim(),
        escalationSeverity: input.actionType === "Escalate" ? input.escalationSeverity : null,
        actionReason: input.actionReason.trim(), sourceReference: input.sourceReference.trim(),
      })));
    if (payload.support_response_claim !== false || payload.physical_outcome_claim !== false ||
        payload.warranty_acceptance_claim !== false || !payload.support_action || typeof payload.support_action !== "object")
      throw new Error("The server did not return an operator-recorded, no-outcome-claim support acknowledgement.");
    const supportAction = mapRmaSupportAction(payload.support_action as AnyRecord);
    const expectedStoredType = input.actionType === "Escalate" ? "Escalated" : null;
    if ((expectedStoredType && supportAction.actionType !== expectedStoredType) ||
        (!expectedStoredType && supportAction.actionType !== "OwnershipClaimed" && supportAction.actionType !== "OwnershipReassigned") ||
        supportAction.supportQueue !== input.supportQueue.trim() ||
        supportAction.escalationSeverity !== (input.actionType === "Escalate" ? input.escalationSeverity ?? null : null) ||
        supportAction.actionReason !== input.actionReason.trim() || supportAction.sourceReference !== input.sourceReference.trim())
      throw new Error("The recorded RMA support action does not match the submitted facts.");
    return {
      supportAction,
      idempotentReplay: payload.idempotent_replay === true,
      note: String(payload.note ?? "RMA support routing recorded; response and outcomes remain unverified."),
    };
  },

  async recordDeviceSparePoolAction(deviceId: string | number, input: DeviceSparePoolActionInput) {
    const session = getSession();
    ensureManagementAccess(session);
    const canonicalId = canonicalDeviceLifecycleId(deviceId);
    if (canonicalId === null) throw new Error("The spare-pool device identity is invalid.");
    if (input.rmaCaseId !== undefined && canonicalDeviceLifecycleId(input.rmaCaseId) === null)
      throw new Error("The RMA case identity is invalid.");
    const payload = normalizeKeys(await unwrap<AnyRecord>(apiClient.post(
      `/api/telemetry/devices/${canonicalId}/spare-pool-actions`, {
        ...input,
        poolName: input.actionType === "Add" ? input.poolName?.trim() : null,
        rmaCaseId: input.actionType === "Reserve" ? input.rmaCaseId : null,
        actionReason: input.actionReason.trim(), sourceReference: input.sourceReference.trim(),
      })));
    if (payload.physical_possession_claim !== false || payload.condition_verified_claim !== false ||
        payload.compatibility_claim !== false || payload.certification_claim !== false ||
        !payload.entry || typeof payload.entry !== "object" || !payload.pool_event || typeof payload.pool_event !== "object")
      throw new Error("The server did not return a fail-closed spare-pool planning acknowledgement.");
    const poolEvent = mapSparePoolEvent(payload.pool_event as AnyRecord);
    const entry = mapSparePool(payload.entry as AnyRecord, [poolEvent]);
    const expectedAction = { Add: "Added", Reserve: "Reserved", Release: "Released", Remove: "Removed" }[input.actionType];
    if (poolEvent.actionType !== expectedAction || poolEvent.actionReason !== input.actionReason.trim() ||
        poolEvent.sourceReference !== input.sourceReference.trim() ||
        (input.actionType === "Add" && entry.poolName !== input.poolName?.trim()) ||
        (input.actionType === "Reserve" && poolEvent.rmaCaseId !== input.rmaCaseId))
      throw new Error("The recorded spare-pool action does not match the submitted facts.");
    return { entry, poolEvent, idempotentReplay: payload.idempotent_replay === true,
      note: String(payload.note ?? "Spare-pool planning recorded; physical state remains unverified.") };
  },

  async recordDeviceSupportTierAction(deviceId: string | number, input: DeviceSupportTierActionInput) {
    const session = getSession();
    ensureManagementAccess(session);
    const canonicalId = canonicalDeviceLifecycleId(deviceId);
    if (canonicalId === null) throw new Error("The support-tier device identity is invalid.");
    const hasPlan = input.actionType !== "End";
    const payload = normalizeKeys(await unwrap<AnyRecord>(apiClient.post(
      `/api/telemetry/devices/${canonicalId}/support-tier-actions`, {
        ...input,
        tierCode: hasPlan ? input.tierCode : null,
        coverageWindow: hasPlan ? input.coverageWindow : null,
        routingResponseTargetMinutes: hasPlan ? input.routingResponseTargetMinutes : null,
        escalationPolicyReference: hasPlan ? input.escalationPolicyReference?.trim() : null,
        commercialReference: hasPlan ? input.commercialReference?.trim() : null,
        actionReason: input.actionReason.trim(), sourceReference: input.sourceReference.trim(),
      })));
    if (payload.commercial_entitlement_verified_claim !== false || payload.provider_support_claim !== false ||
        payload.hardware_supportability_claim !== false || payload.certification_claim !== false ||
        !payload.support_tier_event || typeof payload.support_tier_event !== "object")
      throw new Error("The server did not return a fail-closed support-tier acknowledgement.");
    const supportTierEvent = mapDeviceSupportTierEvent(payload.support_tier_event as AnyRecord);
    const expectedAction = { Assign: "Assigned", Change: "Changed", End: "Ended" }[input.actionType];
    if (supportTierEvent.deviceId !== canonicalId || supportTierEvent.actionType !== expectedAction ||
        supportTierEvent.actionReason !== input.actionReason.trim() ||
        supportTierEvent.sourceReference !== input.sourceReference.trim() ||
        (hasPlan && (supportTierEvent.tierCode !== input.tierCode ||
          supportTierEvent.coverageWindow !== input.coverageWindow ||
          supportTierEvent.routingResponseTargetMinutes !== input.routingResponseTargetMinutes ||
          supportTierEvent.escalationPolicyReference !== input.escalationPolicyReference?.trim() ||
          supportTierEvent.commercialReference !== input.commercialReference?.trim())))
      throw new Error("The recorded support-tier action does not match the submitted facts.");
    return { supportTierEvent, idempotentReplay: payload.idempotent_replay === true,
      note: String(payload.note ?? "Support routing recorded; entitlement and supportability remain unverified.") };
  },

  async requestDeviceRemoteCommand(deviceId: string | number, input: DeviceRemoteCommandInput) {
    const session = getSession();
    ensureManagementAccess(session);
    const canonicalId = canonicalDeviceLifecycleId(deviceId);
    if (canonicalId === null) throw new Error("The remote-command device identity is invalid.");
    const payload = normalizeKeys(await unwrap<AnyRecord>(apiClient.post(
      `/api/telemetry/devices/${canonicalId}/commands`, {
        commandType: input.commandType,
        payload: input.payload,
        purpose: input.purpose.trim(), sourceReference: input.sourceReference.trim(),
        safetyConfirmation: input.safetyConfirmation.trim(),
        idempotencyKey: input.idempotencyKey,
      })));
    if (payload.request_recorded !== true || payload.dispatched !== false || payload.acknowledged !== false ||
        payload.applied !== false || payload.provider_delivery_claim !== false || payload.physical_outcome_claim !== false ||
        !payload.command || typeof payload.command !== "object")
      throw new Error("The server did not return a truthful command-request acknowledgement.");
    return {
      command: mapRemoteCommand(payload.command as AnyRecord),
      idempotentReplay: payload.idempotent_replay === true,
      note: String(payload.note ?? "Command request recorded; dispatch and outcome remain unverified."),
    };
  },

  async getGpsTrackingRecords(): Promise<TelematicsClusterRecord[]> {
    const session = getSession();
    const [devices, positions, faults] = await Promise.all([
      loadScopedDevices(session),
      fetchPositions(),
      fetchActiveFaultsIfAuthorized(session),
    ]);
    return devices.map((device) => toClusterRecord(device, positions, faults));
  },

  async getDiagnosticsRecords(): Promise<TelematicsClusterRecord[]> {
    const session = getSession();
    const [devices, positions, faults] = await Promise.all([
      loadScopedDevices(session),
      fetchPositions(),
      fetchActiveFaultsIfAuthorized(session),
    ]);
    return devices
      .map((device) => toClusterRecord(device, positions, faults))
      .filter((record) => record.protocolType !== "Unknown" || record.troubleCodes.length > 0);
  },

  async getSensorHealthRecords(): Promise<TelematicsClusterRecord[]> {
    const session = getSession();
    const [devices, positions, faults] = await Promise.all([
      loadScopedDevices(session),
      fetchPositions(),
      fetchActiveFaultsIfAuthorized(session),
    ]);
    return devices
      .filter((device) => /sensor|temperature|door|fuel|tire|reefer|cold/i.test(device.deviceType))
      .map((device) => toClusterRecord(device, positions, faults));
  },

  async getColdChainRecords(): Promise<TelematicsClusterRecord[]> {
    const [devicesPayload, alertsPayload, summary] = await Promise.all([
      fleetColdChainApi.devices(),
      fleetColdChainApi.alerts(),
      fleetColdChainApi.summary(),
    ]);
    return devicesPayload.items.map((device) =>
      toColdChainClusterRecord(device, summary.zones, alertsPayload.items),
    );
  },

  // ── Mutations backed by real endpoints ────────────────────────────────────────────

  async previewDeviceImport(rows: AnyRecord[]): Promise<AnyRecord> {
    return unwrap<AnyRecord>(apiClient.post("/api/telemetry/devices/import-preview", { rows }));
  },

  async commitDeviceImport(rows: AnyRecord[]): Promise<AnyRecord> {
    return unwrap<AnyRecord>(apiClient.post("/api/telemetry/devices/import-commit", { rows }, { timeout: 120000 }));
  },

  async previewDeviceInstallationImport(rows: AnyRecord[]): Promise<AnyRecord> {
    return unwrap<AnyRecord>(apiClient.post("/api/telemetry/device-installations/import-preview", { rows }));
  },

  async commitDeviceInstallationImport(rows: AnyRecord[]): Promise<AnyRecord> {
    return unwrap<AnyRecord>(apiClient.post("/api/telemetry/device-installations/import-commit", { rows }, { timeout: 120000 }));
  },

  // Provision a device = INITIATE A REAL CONNECTION (the Render/Vercel model), not a
  // data save. The backend generates a real apiKey + HMAC secret that authenticate the
  // physical device's telemetry POSTs to /api/telemetry/ingest. Those credentials are
  // returned to the caller ONCE and never retrievable again — exactly like a platform
  // deploy token — so the connect dialog can display them and the device can start
  // streaming. Returns the credentials + the live device record + the ingest endpoint.
  async provisionDevice(payload: DeviceMutationPayload): Promise<DeviceProvisionResult> {
    const session = getSession();
    ensureManagementAccess(session);
    // IMEI is its own field for hardware GPS trackers (GT06/Concox/PT40-class). It falls
    // back to being the serial only when no separate serial was given, so device_serial
    // (NOT NULL) is always populated and the device resolves by either key at ingest.
    const imei = String(payload.imei ?? "").trim();
    const serial = String(payload.serialNumber ?? payload.identifier ?? imei ?? "").trim();
    if (!serial) throw new Error("A device serial or IMEI is required to establish a connection.");
    const deviceCategory = String(payload.deviceCategory ?? "").trim();
    if (!deviceCategory) throw new Error("Select the governed hardware category for this device.");
    // POST /api/telemetry/devices/provision -> {id, deviceSerial, apiKey, hmacSecret, note}
    const provisioned = await unwrap<AnyRecord>(apiClient.post("/api/telemetry/devices/provision", {
      deviceSerial: serial,
      imei: imei || null,
      deviceCategory,
      deviceModel: payload.deviceName ?? payload.deviceType ?? "",
      manufacturer: payload.manufacturer ?? "",
      hardwareRevision: payload.hardwareRevision ?? "",
      provider: payload.provider ?? "",
      firmwareVersion: payload.firmwareVersion ?? "",
      notes: payload.notes ?? "",
    }));

    // Re-read the freshly provisioned device so the returned record is fully live.
    const created = deviceRowFromDetail(await unwrap<AnyRecord>(apiClient.get(`/api/telemetry/devices/${provisioned.id}`)));
    const device = mapDeviceRow(created, new Map(), new Map(), session);
    return {
      device,
      credentials: {
        deviceId: String(provisioned.id ?? device.id),
        deviceSerial: String(provisioned.deviceSerial ?? serial),
        apiKey: String(provisioned.apiKey ?? ""),
        hmacSecret: String(provisioned.hmacSecret ?? ""),
        note: String(provisioned.note ?? "Store these credentials securely — they will not be shown again."),
      },
      // The endpoint the device authenticates to and streams telemetry into. Built from
      // the same base the app talks to so it is correct in every environment (localhost
      // in dev, the Render URL in prod). Trailing slash on the base is normalized away.
      ingestUrl: `${String(apiClient.defaults.baseURL ?? "").replace(/\/+$/, "")}/api/telemetry/ingest`,
    };
  },

  // Backward-compatible wrapper: some callers only need the device record.
  async createDevice(payload: DeviceMutationPayload): Promise<DeviceCommandRecord> {
    return (await this.provisionDevice(payload)).device;
  },

  // Inspect the stored check-in record. Neither this read nor a valid timestamp
  // establishes current connectivity, device authentication, or physical pairing.
  async getDeviceConnectionState(deviceId: string | number): Promise<{ hasRecordedCheckIn: boolean; lastSeenAt: string | null; status: string; deviceState: string; lifecycleBlocked: boolean }> {
    const response = await apiClient.get(`/api/telemetry/devices/${deviceId}`);
    const row = checkInDeviceRowFromDetail(strictCheckInEnvelopePayload(response.data));
    const checkInField = exactCheckInField(row, "lastSeenAt", "last_seen_at");
    const statusField = exactCheckInField(row, "status", "status");
    const stateField = exactCheckInField(row, "deviceState", "device_state");
    const revokedField = exactCheckInField(row, "revokedAt", "revoked_at");
    const lastSeenAt = recordedDeviceCheckIn(checkInField.valid && checkInField.present ? checkInField.value : undefined, Date.now());
    const status = statusField.valid && typeof statusField.value === "string" && statusField.value.trim() ? statusField.value.trim() : "Unknown";
    const deviceState = stateField.valid && typeof stateField.value === "string" && stateField.value.trim() ? stateField.value.trim() : "Unknown";
    const restrictedLifecycle = /^(revoked|retired|suspended|quarantined|decommissioned)$/i;
    const lifecycleBlocked = !statusField.valid || !stateField.valid || !revokedField.valid
      || (revokedField.present && revokedField.value != null) || restrictedLifecycle.test(status)
      || restrictedLifecycle.test(deviceState);
    return { hasRecordedCheckIn: lastSeenAt !== null, lastSeenAt, status, deviceState, lifecycleBlocked };
  },

  async assignDeviceToVehicle(deviceId: string | number, input: DeviceInstallationInput): Promise<DeviceInstallationReceipt> {
    const session = getSession();
    ensureManagementAccess(session);
    const requestedId = canonicalDeviceLifecycleId(deviceId);
    const vehicleId = installationBodyId(input.vehicleId);
    const effectiveAt = typeof input.effectiveAt === "string" ? input.effectiveAt.trim() : "";
    const instant = installationEffectiveInstant(effectiveAt);
    const deviceRole = typeof input.deviceRole === "string" ? installationRoles.find(role => role.toLowerCase() === input.deviceRole.trim().toLowerCase()) : undefined;
    const isPrimary = input.isPrimary;
    const assignmentReason = typeof input.assignmentReason === "string" ? input.assignmentReason.trim() : "";
    const removalReason = typeof input.removalReason === "string" ? input.removalReason.trim() : "";
    const installationLocation = typeof input.installationLocation === "string" ? input.installationLocation.trim() : undefined;
    const commissioningMethod = typeof input.commissioningMethod === "string" ? input.commissioningMethod.trim() : undefined;
    const odometerAtInstallation = input.odometerAtInstallation;
    const intent = input.intent;
    const operation = installationObject(intent) ? intent.kind : undefined;
    const priorId = operation === "transfer" ? installationBodyId((intent as AnyRecord).currentInstallationId) : null;
    const priorVehicleId = operation === "transfer" ? canonicalDeviceLifecycleId((intent as AnyRecord).priorVehicleId) : null;
    const version = operation === "transfer" ? (intent as AnyRecord).expectedRowVersion : null;
    if (requestedId === null || vehicleId === null) throw new Error("Select valid device and vehicle identities before submitting the installation.");
    if (instant === null || instant > Date.now()) throw new Error("Enter a valid, non-future installation time with an explicit time zone and millisecond precision.");
    if (!deviceRole || typeof isPrimary !== "boolean" || assignmentReason.length < 4 || assignmentReason.length > 500
      || installationLocation === undefined || installationLocation.length > 160 || commissioningMethod === undefined || commissioningMethod.length > 80
      || (odometerAtInstallation !== null && (typeof odometerAtInstallation !== "number" || !Number.isFinite(odometerAtInstallation) || odometerAtInstallation < 0 || odometerAtInstallation > 9999999999.99))) {
      throw new Error("Enter a supported role, primary designation and valid installation metadata before submitting.");
    }
    if ((operation !== "create" && operation !== "transfer")
      || (operation === "create" && ["currentInstallationId", "expectedRowVersion", "priorVehicleId"].some(key => Object.hasOwn(intent, key)))
      || (operation === "transfer" && (priorId === null || priorVehicleId === null || !installationVersion(version, 2147483646) || removalReason.length < 4 || removalReason.length > 500))) {
      throw new Error("Installation intent is unavailable or inconsistent. Refresh the device before submitting; this attempt was not submitted.");
    }
    let detail: DeviceDetailRecord;
    try { detail = await this.getDeviceById(requestedId); }
    catch { throw new Error("Unable to read the installation before submitting. Refresh the device; this attempt was not submitted."); }
    const current = detail.currentInstallation;
    if (canonicalDeviceLifecycleId(detail.device.id) !== requestedId || (operation === "create" && current !== null)
      || (operation === "transfer" && (!current || installationBodyId(current.id) !== priorId
        || canonicalDeviceLifecycleId(current.deviceId) !== requestedId || canonicalDeviceLifecycleId(current.vehicleId) !== priorVehicleId
        || !installationVersion(current.rowVersion, 2147483646) || current.rowVersion !== version))) {
      throw new Error("Installation identity or version changed. Refresh the device and history; this attempt was not submitted.");
    }
    if (operation === "transfer" && priorVehicleId === String(vehicleId)) throw new Error("Select a different vehicle to transfer this installation; this attempt was not submitted.");
    const installation = {
      vehicleId, deviceRole, isPrimary, installationLocation: installationLocation || null,
      odometerAtInstallation, commissioningMethod: commissioningMethod || null, assignmentReason,
      idempotencyKey: installationMutationKey(requestedId),
    };
    let response: { status: number; data: unknown };
    try {
      response = operation === "transfer"
        ? await apiClient.post(`/api/telemetry/devices/${requestedId}/installations/transfer`, { ...installation, effectiveAt, currentInstallationId: priorId, expectedRowVersion: version, removalReason })
        : await apiClient.post(`/api/telemetry/devices/${requestedId}/installations`, { ...installation, effectiveFrom: effectiveAt });
    } catch (error) {
      const rejected = error && typeof error === "object" ? (error as { response?: { status?: number; data?: unknown } }).response : undefined;
      if (exactLifecycleEnvelope(rejected?.data) && Object.hasOwn(rejected.data, "success") && rejected.data.success === false
        && [400, 401, 403, 404, 409, 422].includes(rejected.status ?? 0)) throw new DeviceInstallationOutcomeError("rejected");
      throw new DeviceInstallationOutcomeError("unconfirmed");
    }
    if (!exactLifecycleEnvelope(response?.data) || !Object.hasOwn(response.data, "success")) throw new DeviceInstallationOutcomeError("unconfirmed");
    const { success, data } = response.data;
    if (success === false) throw new DeviceInstallationOutcomeError("rejected");
    if (success !== true || !Object.hasOwn(response.data, "data") || !installationObject(data)) throw new DeviceInstallationOutcomeError("unconfirmed");
    const receiptAliases = [["id", "id"], ["status", "status"], ["deviceId", "device_id"], ["vehicleId", "vehicle_id"], ["rowVersion", "row_version"], ["effectiveFrom", "effective_from"],
      ["deviceRole", "device_role"], ["isPrimary", "is_primary"], ["priorInstallationId", "prior_installation_id"], ["replacedInstallationId", "replaced_installation_id"]];
    for (const [camel, snake] of receiptAliases) {
      // Only the actual camel/snake spellings may provide a contract field.
      // Other casing must not hide a contradictory identity or discriminant.
      if (Object.keys(data).some(key => key.replace(/_/g, "").toLowerCase() === camel.toLowerCase() && key !== camel && key !== snake)) throw new DeviceInstallationOutcomeError("unconfirmed");
      if (Object.hasOwn(data, camel) && Object.hasOwn(data, snake) && data[camel] !== data[snake]) throw new DeviceInstallationOutcomeError("unconfirmed");
    }
    const receipt = normalizeKeys(data);
    const id = canonicalDeviceLifecycleId(receipt.id);
    const returnedVehicleId = canonicalDeviceLifecycleId(receipt.vehicle_id);
    const has = (key: string) => Object.hasOwn(receipt, key);
    const all = (...keys: string[]) => keys.every(has);
    const none = (...keys: string[]) => keys.every(key => !has(key));
    const receiptTime = typeof receipt.effective_from === "string" ? receipt.effective_from : null;
    const matchingTime = receiptTime !== null && installationEffectiveInstant(receiptTime) === instant;
    const matchingDevice = canonicalDeviceLifecycleId(receipt.device_id) === requestedId;
    const existingStatus = typeof receipt.status === "string" && receipt.status.trim().length > 0;
    const existingVersion = installationVersion(receipt.row_version, 2147483647);
    let acknowledgement: DeviceInstallationReceipt["acknowledgement"];
    let returnedPriorId: string | null = null;
    let returnedTime: string | null = null;
    if (!all("id", "vehicle_id", "status") || id === null || returnedVehicleId !== String(vehicleId)) throw new DeviceInstallationOutcomeError("unconfirmed");
    if (operation === "create" && response.status === 201 && matchingDevice && receipt.status === "Installed" && receipt.row_version === 1 && matchingTime
      && all("device_id", "row_version", "effective_from")
      && none("device_role", "is_primary", "prior_installation_id", "replaced_installation_id")) {
      acknowledgement = "recorded"; returnedTime = receiptTime;
    } else if (operation === "create" && response.status === 200 && matchingDevice && receipt.device_role === deviceRole && receipt.is_primary === isPrimary
      && all("device_id", "device_role", "is_primary", "row_version")
      && existingVersion && existingStatus && none("effective_from", "prior_installation_id", "replaced_installation_id")) {
      acknowledgement = "already-recorded";
    } else if (operation === "transfer" && response.status === 200 && id !== String(priorId) && has("prior_installation_id")
      && has("effective_from")
      && canonicalDeviceLifecycleId(receipt.prior_installation_id) === String(priorId) && receipt.status === "Installed" && matchingTime
      && none("replaced_installation_id", "device_id", "row_version", "device_role", "is_primary")) {
      acknowledgement = "recorded"; returnedPriorId = canonicalDeviceLifecycleId(receipt.prior_installation_id); returnedTime = receiptTime;
    } else if (operation === "transfer" && response.status === 200 && id !== String(priorId) && has("replaced_installation_id")
      && all("device_id", "effective_from", "row_version")
      && canonicalDeviceLifecycleId(receipt.replaced_installation_id) === String(priorId) && matchingDevice && matchingTime && existingVersion && existingStatus
      && none("prior_installation_id", "device_role", "is_primary")) {
      acknowledgement = "already-recorded"; returnedPriorId = canonicalDeviceLifecycleId(receipt.replaced_installation_id); returnedTime = receiptTime;
    } else { throw new DeviceInstallationOutcomeError("unconfirmed"); }
    return { operation, acknowledgement, installationId: id, vehicleId: returnedVehicleId, priorInstallationId: returnedPriorId,
      recordedStatus: typeof receipt.status === "string" && recordedInstallationStates.includes(receipt.status) ? receipt.status : null, effectiveFrom: returnedTime };
  },

  async markDeviceAttention(
    deviceId: string | number,
    notes: string,
    rowVersionFromRow?: number,
  ): Promise<DeviceCommandRecord> {
    const session = getSession();
    ensureManagementAccess(session);
    const currentVersion = rowVersionFromRow ?? parseRowVersion((await this.getDeviceById(deviceId)).device.rowVersion);
    if (currentVersion == null) throw new Error("Unable to open recovery: device row version is not available.");
    const payload = normalizeMalfunctionInput(notes);
    // POST /api/eld/devices/{id}/mark-malfunction (notes have no backend field here).
    await unwrap<AnyRecord>(apiClient.post(`/api/eld/devices/${deviceId}/mark-malfunction`, {
      rowVersion: currentVersion,
      malfunctionCode: payload.malfunctionCode,
      malfunctionDescription: payload.malfunctionDescription,
    }));
    const updated = deviceRowFromDetail(await unwrap<AnyRecord>(apiClient.get(`/api/telemetry/devices/${deviceId}`)));
    return mapDeviceRow(updated, new Map(), new Map(), session);
  },

  async resolveDeviceAttention(
    deviceId: string | number,
    rowVersionFromRow?: number,
    evidence = "Recovery evidence captured in Device Health operations.",
  ): Promise<DeviceCommandRecord> {
    const session = getSession();
    ensureManagementAccess(session);
    const currentVersion = rowVersionFromRow ?? parseRowVersion((await this.getDeviceById(deviceId)).device.rowVersion);
    if (currentVersion == null) throw new Error("Unable to resolve recovery: device row version is not available.");
    // POST /api/eld/devices/{id}/resolve-malfunction
    await unwrap<AnyRecord>(apiClient.post(`/api/eld/devices/${deviceId}/resolve-malfunction`, {
      rowVersion: currentVersion,
      resolutionEvidence: String(evidence).slice(0, 2000),
    }));
    const updated = deviceRowFromDetail(await unwrap<AnyRecord>(apiClient.get(`/api/telemetry/devices/${deviceId}`)));
    return mapDeviceRow(updated, new Map(), new Map(), session);
  },

  async archiveDevice(id: string | number) {
    const session = getSession();
    ensureManagementAccess(session);
    // POST /api/telemetry/devices/{id}/revoke (revocation is the real "archive").
    await unwrap<AnyRecord>(apiClient.post(`/api/telemetry/devices/${id}/revoke`, {}));
    return { success: true };
  },

  async retireDevice(deviceId: string | number, input: DeviceRetirementInput) {
    const session = getSession();
    ensureManagementAccess(session);
    const requestedId = canonicalDeviceLifecycleId(deviceId);
    if (requestedId === null) throw new Error("A valid device is required before retirement.");
    if (!Number.isInteger(input.expectedRowVersion) || input.expectedRowVersion < 1)
      throw new Error("Reload the device to obtain its current revision before retirement.");
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(input.idempotencyKey))
      throw new Error("The retirement form session is invalid. Close and reopen it.");
    const payload = normalizeKeys(await unwrap<AnyRecord>(apiClient.post(
      `/api/telemetry/devices/${requestedId}/retire`, {
        ...input,
        retirementReason: input.retirementReason.trim(),
        sourceReference: input.sourceReference.trim(),
        safetyConfirmation: input.safetyConfirmation.trim(),
      })));
    if (payload.credentials_revoked !== true || payload.physical_disposition_claim !== false ||
        payload.certification_claim !== false || !payload.retirement || typeof payload.retirement !== "object")
      throw new Error("The server did not return a truthful retirement acknowledgement.");
    const retirement = mapDeviceRetirement(payload.retirement as AnyRecord);
    if (retirement.deviceId !== requestedId || retirement.rowVersionBefore !== input.expectedRowVersion ||
        retirement.retirementReason !== input.retirementReason.trim() ||
        retirement.dispositionPlan !== input.dispositionPlan ||
        retirement.sourceReference !== input.sourceReference.trim())
      throw new Error("The retirement receipt did not match the submitted device revision and facts.");
    return {
      retirement,
      idempotentReplay: payload.idempotent_replay === true,
      note: String(payload.note ?? "Software retirement recorded; physical disposition remains unverified."),
    };
  },

  async unassignDevice(deviceId: string | number, input: DeviceInstallationRemovalInput): Promise<DeviceRemovalReceipt> {
    const session = getSession();
    ensureManagementAccess(session);
    const requestedId = canonicalDeviceLifecycleId(deviceId);
    if (requestedId === null) throw new Error("A valid device identifier is required before removal.");
    const effectiveTo = typeof input.effectiveTo === "string" ? input.effectiveTo.trim() : "";
    const requestedInstant = removalEffectiveInstant(effectiveTo);
    const removalReason = typeof input.removalReason === "string" ? input.removalReason.trim() : "";
    if (requestedInstant === null || requestedInstant > Date.now()) {
      throw new Error("Enter a valid, non-future removal time with an explicit time zone and millisecond precision.");
    }
    if (removalReason.length < 4 || removalReason.length > 500) throw new Error("Enter an installation removal reason of 4 to 500 characters.");
    let detail: DeviceDetailRecord;
    try {
      detail = await this.getDeviceById(requestedId);
    } catch {
      throw new Error("Unable to read the installation before removal. Refresh the device and try again.");
    }
    const current = detail.currentInstallation;
    if (!current) throw new Error("This device has no active installation to remove.");
    const installationId = canonicalDeviceLifecycleId(current.id);
    if (installationId === null) throw new Error("Unable to identify the installation. Reload the device before removal.");
    const expectedRowVersion = current.rowVersion;
    if (typeof expectedRowVersion !== "number" || !Number.isInteger(expectedRowVersion)
      || expectedRowVersion < 1 || expectedRowVersion >= 2147483647) {
      throw new Error("Unable to remove installation: a valid row version is not available. Reload the device and try again.");
    }
    const plainCarrier = (value: unknown): value is AnyRecord => value !== null && typeof value === "object" && !Array.isArray(value)
      && (Object.getPrototypeOf(value) === Object.prototype || Object.getPrototypeOf(value) === null);
    let response: { status: number; data: unknown };
    try {
      response = await apiClient.post(`/api/telemetry/devices/${requestedId}/installations/${installationId}/remove`, {
        removalReason, effectiveTo, expectedRowVersion,
      });
    } catch (error) {
      const rejected = error && typeof error === "object"
        ? (error as { response?: { status?: number; data?: unknown } }).response
        : undefined;
      if (exactLifecycleEnvelope(rejected?.data) && Object.hasOwn(rejected.data, "success") && rejected.data.success === false
        && [400, 401, 403, 404, 409, 422].includes(rejected?.status ?? 0)) throw new DeviceRemovalOutcomeError("rejected");
      throw new DeviceRemovalOutcomeError("unconfirmed");
    }
    const envelope = response?.data;
    if (response?.status !== 200 || !exactLifecycleEnvelope(envelope) || !Object.hasOwn(envelope, "success")) throw new DeviceRemovalOutcomeError("unconfirmed");
    const { success, data } = envelope;
    if (success === false) throw new DeviceRemovalOutcomeError("rejected");
    if (success !== true || !Object.hasOwn(envelope, "data") || !plainCarrier(data)) throw new DeviceRemovalOutcomeError("unconfirmed");
    const rawReceipt = data;
    for (const [camel, snake] of [["id", "id"], ["status", "status"], ["effectiveTo", "effective_to"]]) {
      if (Object.keys(rawReceipt).some(key => key.replace(/_/g, "").toLowerCase() === camel.toLowerCase() && key !== camel && key !== snake)) {
        throw new DeviceRemovalOutcomeError("unconfirmed");
      }
      if (camel !== snake && Object.hasOwn(rawReceipt, camel) && Object.hasOwn(rawReceipt, snake) && rawReceipt[camel] !== rawReceipt[snake]) {
        throw new DeviceRemovalOutcomeError("unconfirmed");
      }
    }
    if (!["id", "status", "effectiveTo"].every((camel) => hasExactReceiptField(rawReceipt, camel, snakeCaseKey(camel)))) {
      throw new DeviceRemovalOutcomeError("unconfirmed");
    }
    const receipt = normalizeKeys(rawReceipt);
    const receiptId = canonicalDeviceLifecycleId(receipt.id);
    if (receiptId !== installationId || receipt.status !== "Removed" || typeof receipt.effective_to !== "string"
      || removalEffectiveInstant(receipt.effective_to) !== requestedInstant) throw new DeviceRemovalOutcomeError("unconfirmed");
    // The handler returns its captured effective instant, not a persisted row or
    // device lifecycle. Optional display reads have a separate caller-owned outcome.
    return { id: receiptId, status: "Removed", effectiveTo: receipt.effective_to };
  },

  async markInstalled(deviceId: string | number, input: DeviceCommissioningInput): Promise<DeviceCommissioningReceipt> {
    const session = getSession();
    ensureManagementAccess(session);
    const requestedId = canonicalDeviceLifecycleId(deviceId);
    if (requestedId === null) throw new Error("A valid device identifier is required before commissioning.");
    const verificationReference = String(input.verificationReference ?? "").trim();
    if (input.result !== "Passed" && input.result !== "Failed") throw new Error("Select the observed commissioning result.");
    const requestedResult = input.result;
    if (!verificationReference) throw new Error("Enter the commissioning evidence or failure reference.");
    if (verificationReference.length > 500 || (requestedResult === "Failed" && verificationReference.length < 8)) {
      throw new Error("Use a reference of at most 500 characters, with at least 8 characters for a Failed observation.");
    }
    // Preserve the complete preflight. No command is sent when any required read fails.
    let detail: DeviceDetailRecord;
    try {
      detail = await this.getDeviceById(requestedId);
    } catch {
      throw new Error("Unable to read the installation before commissioning. Refresh the device and try again.");
    }
    const current = detail.currentInstallation;
    if (!current) throw new Error("Install this device on a vehicle before commissioning it.");
    const installationId = canonicalDeviceLifecycleId(current.id);
    if (installationId === null) throw new Error("Unable to identify the installation. Reload the device before commissioning.");
    if (requestedResult === "Passed" && !current.activationVerifiedAt) {
      throw new Error("Commissioning requires an authenticated device heartbeat that verifies activation.");
    }
    const expectedRowVersion = current.rowVersion;
    if (typeof expectedRowVersion !== "number" || !Number.isInteger(expectedRowVersion)
      || expectedRowVersion < 1 || expectedRowVersion >= 2147483647) {
      throw new Error("Unable to commission installation: a valid row version is not available. Reload the device and try again.");
    }
    const plainCarrier = (value: unknown): value is AnyRecord => value !== null && typeof value === "object" && !Array.isArray(value)
      && (Object.getPrototypeOf(value) === Object.prototype || Object.getPrototypeOf(value) === null);
    let response: { status: number; data: unknown };
    try {
      response = await apiClient.post(`/api/telemetry/devices/${requestedId}/installations/${installationId}/commission`, {
        result: requestedResult,
        verificationReference,
        expectedRowVersion,
      });
    } catch (error) {
      const rejected = error && typeof error === "object"
        ? (error as { response?: { status?: number; data?: unknown } }).response
        : undefined;
      if (exactLifecycleEnvelope(rejected?.data) && Object.hasOwn(rejected.data, "success") && rejected.data.success === false
        && [400, 401, 403, 404, 409, 422].includes(rejected?.status ?? 0)) {
        throw new DeviceCommissioningOutcomeError("rejected");
      }
      throw new DeviceCommissioningOutcomeError("unconfirmed");
    }
    const envelope = response?.data;
    if (response?.status !== 200 || !exactLifecycleEnvelope(envelope) || !Object.hasOwn(envelope, "success")) throw new DeviceCommissioningOutcomeError("unconfirmed");
    const { success, data } = envelope;
    if (success === false) throw new DeviceCommissioningOutcomeError("rejected");
    if (success !== true || !Object.hasOwn(envelope, "data") || !plainCarrier(data)) throw new DeviceCommissioningOutcomeError("unconfirmed");
    const rawReceipt = data;
    for (const [camel, snake] of [["id", "id"], ["status", "status"], ["commissioningResult", "commissioning_result"], ["rowVersion", "row_version"]]) {
      if (Object.keys(rawReceipt).some(key => key.replace(/_/g, "").toLowerCase() === camel.toLowerCase() && key !== camel && key !== snake)) {
        throw new DeviceCommissioningOutcomeError("unconfirmed");
      }
      if (camel !== snake && Object.hasOwn(rawReceipt, camel) && Object.hasOwn(rawReceipt, snake) && rawReceipt[camel] !== rawReceipt[snake]) {
        throw new DeviceCommissioningOutcomeError("unconfirmed");
      }
    }
    if (!["id", "status", "commissioningResult", "rowVersion"].every((camel) => hasExactReceiptField(rawReceipt, camel, snakeCaseKey(camel)))) {
      throw new DeviceCommissioningOutcomeError("unconfirmed");
    }
    const receipt = normalizeKeys(rawReceipt);
    const receiptId = canonicalDeviceLifecycleId(receipt.id);
    const expectedStatus = requestedResult === "Passed" ? "Verified" : "Failed";
    if (receiptId !== installationId || receipt.commissioning_result !== requestedResult || receipt.status !== expectedStatus
      || typeof receipt.row_version !== "number" || receipt.row_version !== expectedRowVersion + 1) {
      throw new DeviceCommissioningOutcomeError("unconfirmed");
    }
    // A saved Failed observation is still a saved record. Neither status nor optional
    // metadata certifies hardware; display reads must not erase this acknowledgement.
    return { id: receiptId, commissioningResult: requestedResult, status: expectedStatus, rowVersion: receipt.row_version };
  },

  async suspendDevice(deviceId: string | number): Promise<DeviceSuspensionReceipt> {
    const session = getSession();
    ensureManagementAccess(session);
    const requestedId = canonicalDeviceLifecycleId(deviceId);
    if (requestedId === null) throw new Error("A valid device identifier is required before suspension.");

    const plainCarrier = (value: unknown): value is AnyRecord => value !== null && typeof value === "object" && !Array.isArray(value)
      && (Object.getPrototypeOf(value) === Object.prototype || Object.getPrototypeOf(value) === null);
    let response: { status: number; data: unknown };
    try {
      response = await apiClient.post(`/api/telemetry/devices/${requestedId}/suspend`, {});
    } catch (error) {
      const rejected = error && typeof error === "object"
        ? (error as { response?: { status?: number; data?: unknown } }).response
        : undefined;
      // Only an explicit client-error response establishes a rejected request.
      // A timeout, lost response or server failure leaves the outcome unconfirmed.
      if (exactLifecycleEnvelope(rejected?.data) && Object.hasOwn(rejected.data, "success") && rejected.data.success === false
        && [400, 401, 403, 404, 409, 422].includes(rejected?.status ?? 0)) {
        throw new DeviceSuspensionOutcomeError("rejected");
      }
      throw new DeviceSuspensionOutcomeError("unconfirmed");
    }
    const envelope = response?.data;
    if (response?.status !== 200 || !exactLifecycleEnvelope(envelope) || !Object.hasOwn(envelope, "success")) throw new DeviceSuspensionOutcomeError("unconfirmed");
    const { success, data } = envelope;
    if (success === false) throw new DeviceSuspensionOutcomeError("rejected");
    if (success !== true || !Object.hasOwn(envelope, "data") || !plainCarrier(data)) throw new DeviceSuspensionOutcomeError("unconfirmed");
    const rawReceipt = data;
    for (const [camel, snake] of [["id", "id"], ["status", "status"], ["deviceState", "device_state"], ["rowVersion", "row_version"]]) {
      if (Object.keys(rawReceipt).some(key => key.replace(/_/g, "").toLowerCase() === camel.toLowerCase() && key !== camel && key !== snake)) {
        throw new DeviceSuspensionOutcomeError("unconfirmed");
      }
      if (camel !== snake && Object.hasOwn(rawReceipt, camel) && Object.hasOwn(rawReceipt, snake) && rawReceipt[camel] !== rawReceipt[snake]) {
        throw new DeviceSuspensionOutcomeError("unconfirmed");
      }
    }
    if (!["id", "status", "deviceState", "rowVersion"].every((camel) => hasExactReceiptField(rawReceipt, camel, snakeCaseKey(camel)))) {
      throw new DeviceSuspensionOutcomeError("unconfirmed");
    }
    const receipt = normalizeKeys(data as AnyRecord);
    const receiptId = canonicalDeviceLifecycleId(receipt.id);
    if (receiptId !== requestedId || receipt.status !== "Suspended" || receipt.device_state !== "Suspended"
      || typeof receipt.row_version !== "number" || !Number.isSafeInteger(receipt.row_version) || receipt.row_version < 0) {
      throw new DeviceSuspensionOutcomeError("unconfirmed");
    }
    // This is the server's command receipt, not a reconstructed device snapshot.
    // Optional display reads belong to a separate refresh outcome in the caller.
    return { id: receiptId, status: receipt.status, deviceState: receipt.device_state, rowVersion: receipt.row_version };
  },

  async activateDevice(deviceId: string | number): Promise<DeviceActivationReceipt> {
    const session = getSession();
    ensureManagementAccess(session);
    const requestedId = canonicalDeviceLifecycleId(deviceId);
    if (requestedId === null) throw new Error("A valid device identifier is required before activation.");

    const plainCarrier = (value: unknown): value is AnyRecord => value !== null && typeof value === "object" && !Array.isArray(value)
      && (Object.getPrototypeOf(value) === Object.prototype || Object.getPrototypeOf(value) === null);
    let response: { status: number; data: unknown };
    try {
      response = await apiClient.post(`/api/telemetry/devices/${requestedId}/activate`, {});
    } catch (error) {
      const rejected = error && typeof error === "object"
        ? (error as { response?: { status?: number; data?: unknown } }).response
        : undefined;
      if (exactLifecycleEnvelope(rejected?.data) && Object.hasOwn(rejected.data, "success") && rejected.data.success === false
        && [400, 401, 403, 404, 409, 422].includes(rejected?.status ?? 0)) {
        throw new DeviceActivationOutcomeError("rejected");
      }
      throw new DeviceActivationOutcomeError("unconfirmed");
    }
    const envelope = response?.data;
    if (response?.status !== 200 || !exactLifecycleEnvelope(envelope) || !Object.hasOwn(envelope, "success")) throw new DeviceActivationOutcomeError("unconfirmed");
    const { success, data } = envelope;
    if (success === false) throw new DeviceActivationOutcomeError("rejected");
    if (success !== true || !Object.hasOwn(envelope, "data") || !plainCarrier(data)) throw new DeviceActivationOutcomeError("unconfirmed");
    const rawReceipt = data;
    for (const [camel, snake] of [["id", "id"], ["status", "status"], ["deviceState", "device_state"], ["rowVersion", "row_version"], ["idempotentReplay", "idempotent_replay"]]) {
      if (Object.keys(rawReceipt).some(key => key.replace(/_/g, "").toLowerCase() === camel.toLowerCase() && key !== camel && key !== snake)) {
        throw new DeviceActivationOutcomeError("unconfirmed");
      }
      if (camel !== snake && Object.hasOwn(rawReceipt, camel) && Object.hasOwn(rawReceipt, snake) && rawReceipt[camel] !== rawReceipt[snake]) {
        throw new DeviceActivationOutcomeError("unconfirmed");
      }
    }
    if (!["id", "status", "rowVersion"].every((camel) => hasExactReceiptField(rawReceipt, camel, snakeCaseKey(camel)))) {
      throw new DeviceActivationOutcomeError("unconfirmed");
    }
    const hasDeviceState = hasExactReceiptField(rawReceipt, "deviceState", "device_state");
    const hasReplay = hasExactReceiptField(rawReceipt, "idempotentReplay", "idempotent_replay");
    const receipt = normalizeKeys(rawReceipt);
    const receiptId = canonicalDeviceLifecycleId(receipt.id);
    const replay = hasReplay ? receipt.idempotent_replay : undefined;
    if (receiptId !== requestedId || receipt.status !== "Active"
      || typeof receipt.row_version !== "number" || !Number.isSafeInteger(receipt.row_version) || receipt.row_version < 0
      || (hasReplay && typeof replay !== "boolean")) {
      throw new DeviceActivationOutcomeError("unconfirmed");
    }
    const state = hasDeviceState ? receipt.device_state : undefined;
    const deviceState = state === "Registered" || state === "Installed" || state === "Verified" ? state : null;
    if (replay !== true && deviceState === null) throw new DeviceActivationOutcomeError("unconfirmed");
    // An already-Active receipt may lack usable installation state. Keep only
    // known software tokens; none is evidence of physical readiness or delivery.
    return { id: receiptId, status: "Active", deviceState, rowVersion: receipt.row_version, idempotentReplay: replay === true };
  },

  async rotateDeviceSecret(deviceId: string | number): Promise<DeviceCredentialRotationResult> {
    const session = getSession();
    ensureManagementAccess(session);
    const result = normalizeKeys(await unwrap<AnyRecord>(apiClient.post(`/api/telemetry/devices/${deviceId}/rotate-secret`, {})));
    return {
      deviceId: String(result.id ?? deviceId),
      apiKey: String(result.api_key ?? ""),
      hmacSecret: String(result.hmac_secret ?? ""),
      previousCredentialsValidUntil: result.previous_credentials_valid_until == null
        ? null
        : String(result.previous_credentials_valid_until),
      note: String(result.note ?? "Store the replacement credentials securely; they will not be shown again."),
    };
  },

  async refreshDeviceStatus(deviceId: string | number) {
    const session = getSession();
    const updated = deviceRowFromDetail(await unwrap<AnyRecord>(apiClient.get(`/api/telemetry/devices/${deviceId}`)));
    return mapDeviceRow(updated, new Map(), new Map(), session);
  },

  async createMaintenanceTask(deviceId: string | number, sourceTitle: string) {
    const session = getSession();
    ensureManagementAccess(session);
    // The telematics layer has no maintenance-task endpoint of its own; the caller
    // persists the task through the governed work-order API. Here we resolve the real device so
    // the returned title/note reference the actual unit (no fabricated data). The task
    // itself is created against the real maintenance API downstream.
    const device = deviceRowFromDetail(await unwrap<AnyRecord>(apiClient.get(`/api/telemetry/devices/${deviceId}`)));
    const label = String(device.vehicle_code ?? device.device_serial ?? deviceId);
    return {
      success: true as const,
      vehicleId: String(device.vehicle_id ?? ""),
      vehicleCode: String(device.vehicle_code ?? ""),
      title: `Telematics follow-up for ${label}`,
      note: `Created from ${sourceTitle}; the device assignment was re-read at handoff as ${label}. Telemetry and diagnostic evidence is point-in-time and must be revalidated before service.`,
    };
  },

  async syncProvider(providerId: string | number) {
    const session = getSession();
    ensureManagementAccess(session);
    const detail = await integrationsApi.sync(providerId);
    return detail;
  },

  async getProviders(): Promise<TelematicsProviderSeedRecord[]> {
    return loadProviderCatalog();
  },

  async getDeviceTelemetry(deviceId: string | number) {
    // Derives the single live position point from the device detail read.
    const detail = await this.getDeviceById(deviceId);
    return detail.telemetry;
  },

  async getDeviceHealth(deviceId: string | number) {
    // Derives the health timeline (real alerts) from the device detail read.
    const detail = await this.getDeviceById(deviceId);
    return detail.healthEvents;
  },

  async exportDevicesCsv() {
    const rows = await this.getDevices();
    const columns = [
      "deviceName",
      "deviceType",
      "provider",
      "serialNumber",
      "identifier",
      "assignedVehicleCode",
      "assignedDriverName",
      "tenantName",
      "firmwareVersion",
      "lastCheckIn",
      "connectionStatus",
      "powerStatus",
      "signalStrength",
      "dataHealthScore",
      "installStatus",
      "complianceStatus",
      "warrantyStatus",
      "supportStatus",
    ];
    return [columns.join(","), ...rows.map((row) => columns.map((column) => JSON.stringify(row[column as keyof DeviceCommandRecord] ?? "")).join(","))].join("\n");
  },

  async exportDevices() {
    return downloadServerExport("/api/telemetry/devices/export", `opstrax-device-command-center_${new Date().toISOString().slice(0, 10)}.csv`);
  },

  async exportTelemetryClusterCsv(
    kind: "gps-tracking" | "obd-j1939",
    options: Pick<TelemetryClusterPageOptions, "search" | "view" | "sort" | "direction">,
    columns: string[],
  ) {
    // One bounded server query gives the export a single item snapshot. Walking
    // mutable OFFSET pages could otherwise duplicate or omit identities when a
    // device changes state between requests.
    const batch = await this.getTelemetryClusterPage(kind, {
      ...options,
      page: 1,
      pageSize: 10_000,
      purpose: "export",
      sort: "serial",
      direction: "asc",
    });
    const identities = batch.items.map((row) => String(row.deviceId));
    if (new Set(identities).size !== identities.length) {
      throw new Error("Export contained duplicate device identities. Retry the export.");
    }
    if (!batch.exportComplete || batch.items.length !== batch.total) {
      throw new Error(`Export snapshot is incomplete (${batch.items.length} of ${batch.total} authorized rows). Narrow the filter or contact support.`);
    }
    return this.exportClusterCsv(batch.items, columns);
  },

  exportClusterCsv(rows: TelematicsClusterRecord[], columns: string[]) {
    const csvCell = (value: unknown) => {
      const text = Array.isArray(value) ? value.join(", ") : String(value ?? "");
      // Neutralize spreadsheet formulas from provider/address/device-controlled
      // fields before applying RFC-4180 quoting.
      const safe = /^[=+\-@\t\r]/.test(text) ? `'${text}` : text;
      return `"${safe.replaceAll('"', '""')}"`;
    };
    return [
      columns.map(csvCell).join(","),
      ...rows.map((row) => columns.map((column) => csvCell(row[column as keyof TelematicsClusterRecord])).join(",")),
    ].join("\n");
  },
};

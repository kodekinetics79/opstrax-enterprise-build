import { FormEvent, useEffect, useId, useMemo, useRef, useState } from "react";
import type { ReactNode } from "react";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Activity,
  AlertTriangle,
  ArrowRightLeft,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  Copy,
  Cpu,
  Download,
  KeyRound,
  PlugZap,
  Plus,
  RadioTower,
  RefreshCw,
  Search,
  Settings2,
  ShieldCheck,
  Terminal,
  Trash2,
  Truck,
  WifiOff,
  X,
} from "lucide-react";
import { useNavigate } from "react-router";
import { EmptyState, ErrorState, KpiCard, LoadingState, PageHeader, RiskBadge, StatusBadge } from "@/components/ui";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import { EntityImportExport } from "@/components/EntityImportExport";
import { PERMISSIONS } from "@/auth/rbacConfig";
import { useHasDirectPermission, useHasPermission } from "@/hooks/usePermission";
import { vehiclesApi } from "@/services/vehiclesApi";
import {
  canReadProviderCatalog,
  DeviceActivationOutcomeError,
  DeviceCommissioningOutcomeError,
  DeviceInstallationOutcomeError,
  DeviceRemovalOutcomeError,
  DeviceSuspensionOutcomeError,
  telematicsService,
  getInstallationIntent,
  type DeviceCommandRecord,
  type DeviceCommissioningInput,
  type DeviceConnectivityProfileInput,
  type DeviceCredentialRotationResult,
  type DeviceDetailRecord,
  type DeviceFirmwareCampaignInput,
  type DeviceRmaCaseInput,
  type DeviceRmaEventInput,
  type DeviceRmaReplacementInput,
  type DeviceRmaSupportActionInput,
  type DeviceSparePoolActionInput,
  type DeviceSupportTierActionInput,
  type DeviceSupportTierEventRecord,
  type DeviceRemoteCommandInput,
  type DeviceRetirementInput,
  type DeviceIdentityQuarantineRecord,
  type DeviceInstallationInput,
  type DeviceInstallationArtifactReferenceInput,
  type DeviceInstallationChecklistObservationInput,
  type DeviceInstallationIntent,
  type DeviceInstallationReceipt,
  type DeviceInstallationRemovalInput,
  type DeviceInstallationWorkPackageInput,
  type DeviceProvisionResult,
} from "@/services/telematicsService";
import type { AnyRecord } from "@/types";
import { apiErrorMessage } from "@/utils/apiErrorMessage";
import { useAuth } from "@/hooks/useAuth";
import { useSingleFlight } from "@/hooks/useSingleFlight";

type DeviceTab =
  | "all"
  | "archived"
  | "unassigned"
  | "offline"
  | "attention"
  | "firmware"
  | "provisioning"
  | "diagnostics"
  | "quarantine"
  | "installations"
  | "data-health"
  | "readiness"
  | "providers";

type InstallationFormState = {
  vehicleId: string;
  deviceRole: string;
  primaryDesignation: "" | "primary" | "secondary";
  effectiveAt: string;
  installationLocation: string;
  odometerAtInstallation: string;
  commissioningMethod: string;
  assignmentReason: string;
  removalReason: string;
};

type RemovalFormState = {
  effectiveTo: string;
  removalReason: string;
};

type AssignmentMutationVariables = { deviceId: string; input: DeviceInstallationInput; sessionGeneration: number; target: DeviceCommandRecord; formSnapshot: InstallationFormState };
type AssignmentRecordedOutcome = DeviceInstallationReceipt & { deviceId: string };

type RemovalMutationVariables = {
  deviceId: string | number;
  input: DeviceInstallationRemovalInput;
  sessionGeneration: number;
  target: DeviceCommandRecord;
  formSnapshot: RemovalFormState;
};

type RemovalRecordedOutcome = { deviceId: string; installationId: string; effectiveTo: string };

type CommissioningFormState = {
  result: "" | DeviceCommissioningInput["result"];
  verificationReference: string;
};

type CommissioningMutationVariables = {
  deviceId: string | number;
  input: DeviceCommissioningInput;
  sessionGeneration: number;
  target: DeviceCommandRecord;
  formSnapshot: CommissioningFormState;
};

type CommissioningRecordedOutcome = {
  deviceId: string;
  installationId: string;
  result: DeviceCommissioningInput["result"];
  rowVersion: number;
};

type ConnectivityProfileFormState = {
  profileKind: DeviceConnectivityProfileInput["profileKind"];
  carrierName: string;
  iccid: string;
  msisdn: string;
  apn: string;
  effectiveAt: string;
  changeReason: string;
  sourceReference: string;
  idempotencyKey: string;
};

type InstallationWorkPackageFormState = Omit<DeviceInstallationWorkPackageInput, "appointmentStart" | "appointmentEnd"> & {
  vehicleId: string;
  appointmentStart: string;
  appointmentEnd: string;
};

type InstallationChecklistFormState = Omit<DeviceInstallationChecklistObservationInput, "observedAt"> & {
  workPackageId: string;
  observedAt: string;
};

type InstallationArtifactFormState = Omit<DeviceInstallationArtifactReferenceInput, "capturedAt"> & {
  workPackageId: string;
  capturedAt: string;
};

function newConnectivityProfileForm(): ConnectivityProfileFormState {
  return {
    profileKind: "PhysicalSIM",
    carrierName: "",
    iccid: "",
    msisdn: "",
    apn: "",
    effectiveAt: currentLocalMinute(),
    changeReason: "",
    sourceReference: "",
    idempotencyKey: crypto.randomUUID(),
  };
}

function newInstallationWorkPackageForm(vehicleId = ""): InstallationWorkPackageFormState {
  return {
    vehicleId, workOrderReference: "", appointmentStart: localMinuteAfter(24), appointmentEnd: localMinuteAfter(26),
    serviceLocation: "", workScope: "", idempotencyKey: crypto.randomUUID(),
  };
}

function newInstallationChecklistForm(workPackageId = ""): InstallationChecklistFormState {
  return {
    workPackageId, checklistItem: "DeviceIdentity", observedResult: "NotObserved", evidenceReference: "",
    observationNotes: "", observedAt: currentLocalMinute(), idempotencyKey: crypto.randomUUID(),
  };
}

function newInstallationArtifactForm(workPackageId = ""): InstallationArtifactFormState {
  return {
    workPackageId, artifactType: "InstallationPhoto", objectKey: "", sha256: "",
    capturedAt: currentLocalMinute(), idempotencyKey: crypto.randomUUID(),
  };
}

type FirmwareCampaignFormState = {
  campaignName: string;
  targetFirmwareVersion: string;
  rollbackFirmwareVersion: string;
  rolloutStrategy: DeviceFirmwareCampaignInput["rolloutStrategy"];
  scheduledFor: string;
  maintenanceWindowMinutes: string;
  changeReason: string;
  sourceReference: string;
  idempotencyKey: string;
};

function newFirmwareCampaignForm(): FirmwareCampaignFormState {
  return {
    campaignName: "",
    targetFirmwareVersion: "",
    rollbackFirmwareVersion: "",
    rolloutStrategy: "Canary",
    scheduledFor: currentLocalMinute(),
    maintenanceWindowMinutes: "60",
    changeReason: "",
    sourceReference: "",
    idempotencyKey: crypto.randomUUID(),
  };
}

type RmaCaseFormState = Omit<DeviceRmaCaseInput, "observedAt" | "responseDueAt"> & { observedAt: string; responseDueAt: string; warrantyReference: string };
type RmaEventFormState = Omit<DeviceRmaEventInput, "occurredAt"> & { occurredAt: string; custodyLocation: string; trackingReference: string };
type RmaReplacementFormState = DeviceRmaReplacementInput;
type RmaSupportFormState = Omit<DeviceRmaSupportActionInput, "effectiveAt"> & { effectiveAt: string };

function localMinuteAfter(hours: number) {
  const date = new Date(Date.now() + hours * 60 * 60_000);
  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
}

function newRmaCaseForm(): RmaCaseFormState {
  return {
    severity: "P2", failureCategory: "Other", failureDescription: "", observedAt: currentLocalMinute(),
    warrantyPosture: "Unknown", warrantyReference: "", supportSlaReference: "", responseDueAt: localMinuteAfter(4),
    sourceReference: "", idempotencyKey: crypto.randomUUID(),
  };
}

function newRmaEventForm(): RmaEventFormState {
  return {
    eventType: "ReturnAuthorized", occurredAt: currentLocalMinute(), custodyLocation: "", trackingReference: "",
    evidenceReference: "", notes: "", idempotencyKey: crypto.randomUUID(),
  };
}

function newRmaReplacementForm(): RmaReplacementFormState {
  return { replacementDeviceSerial: "", changeReason: "", sourceReference: "", idempotencyKey: crypto.randomUUID() };
}

function newRmaSupportForm(actionType: DeviceRmaSupportActionInput["actionType"], supportQueue = "Device Support"): RmaSupportFormState {
  return {
    actionType, supportQueue, escalationSeverity: actionType === "Escalate" ? "P2" : undefined,
    actionReason: "", sourceReference: "", effectiveAt: currentLocalMinute(), idempotencyKey: crypto.randomUUID(),
  };
}

type SparePoolFormState = Omit<DeviceSparePoolActionInput, "effectiveAt"> & { effectiveAt: string; poolName: string; rmaCaseId: string };

function newSparePoolForm(actionType: DeviceSparePoolActionInput["actionType"], poolName = "Primary Spares"): SparePoolFormState {
  return {
    actionType, poolName: actionType === "Add" ? poolName : "", rmaCaseId: "", actionReason: "",
    sourceReference: "", effectiveAt: currentLocalMinute(), idempotencyKey: crypto.randomUUID(),
  };
}

type SupportTierFormState = Omit<DeviceSupportTierActionInput, "effectiveAt"> & {
  effectiveAt: string;
  tierCode: DeviceSupportTierEventRecord["tierCode"];
  coverageWindow: DeviceSupportTierEventRecord["coverageWindow"];
  routingResponseTargetMinutes: number;
  escalationPolicyReference: string;
  commercialReference: string;
};

function newSupportTierForm(actionType: DeviceSupportTierActionInput["actionType"], current?: DeviceSupportTierEventRecord): SupportTierFormState {
  return {
    actionType,
    tierCode: current?.tierCode ?? "Standard",
    coverageWindow: current?.coverageWindow ?? "BusinessHours",
    routingResponseTargetMinutes: current?.routingResponseTargetMinutes ?? 240,
    escalationPolicyReference: current?.escalationPolicyReference ?? "Device Support",
    commercialReference: current?.commercialReference ?? "",
    actionReason: "", sourceReference: "", effectiveAt: currentLocalMinute(),
    idempotencyKey: crypto.randomUUID(),
  };
}

type RemoteCommandFormState = {
  commandType: DeviceRemoteCommandInput["commandType"];
  purpose: string;
  sourceReference: string;
  safetyConfirmation: string;
  delaySeconds: string;
  idempotencyKey: string;
};

function newRemoteCommandForm(commandType: DeviceRemoteCommandInput["commandType"]): RemoteCommandFormState {
  return { commandType, purpose: "", sourceReference: "", safetyConfirmation: "", delaySeconds: "0", idempotencyKey: crypto.randomUUID() };
}

type RetirementFormState = Pick<DeviceRetirementInput,
  "retirementReason" | "dispositionPlan" | "sourceReference" | "safetyConfirmation"> & {
  idempotencyKey: string;
};

function newRetirementForm(): RetirementFormState {
  return {
    retirementReason: "",
    dispositionPlan: "ReturnToVendor",
    sourceReference: "",
    safetyConfirmation: "",
    idempotencyKey: crypto.randomUUID(),
  };
}

type SuspensionMutationVariables = { deviceId: string; sessionGeneration: number; target: ConfirmActionTarget };
type ActivationMutationVariables = { deviceId: string; sessionGeneration: number; target: DeviceCommandRecord };

// DEF-023: destructive/lifecycle actions confirm through the in-app accessible
// ConfirmDialog (native window.confirm cannot be completed by automation and is
// invisible to assistive technology).
type ConfirmableLifecycleAction = "suspend" | "rotate-credentials";

type ConfirmActionTarget = {
  action: ConfirmableLifecycleAction;
  device: DeviceCommandRecord;
  /** The control that opened the dialog, so focus can be restored to it on close.
      Captured at open time: the row menu unmounts in the same commit that mounts
      the dialog, so reading document.activeElement inside the dialog yields <body>. */
  opener?: HTMLElement | null;
};

const CONFIRM_ACTION_COPY: Record<
  ConfirmableLifecycleAction,
  { title: string; confirmLabel: string; variant: "default" | "danger"; message: (deviceName: string) => string }
> = {
  suspend: {
    title: "Suspend device",
    confirmLabel: "Suspend Device",
    variant: "danger",
    message: (deviceName) => `Suspend ${deviceName}? New device ingestion will be blocked until the device is activated again.`,
  },
  "rotate-credentials": {
    title: "Rotate credentials",
    confirmLabel: "Rotate Credentials",
    variant: "default",
    message: (deviceName) => `Rotate credentials for ${deviceName}? The replacement secrets will be shown only once.`,
  },
};

function lifecycleFailureHeading(error: unknown) {
  if (error instanceof DeviceInstallationOutcomeError) {
    return error.outcome === "unconfirmed" ? "Installation outcome unconfirmed" : "Installation request rejected";
  }
  if (error instanceof DeviceRemovalOutcomeError) {
    return error.outcome === "unconfirmed" ? "Removal outcome unconfirmed" : "Removal request rejected";
  }
  if (error instanceof DeviceCommissioningOutcomeError) {
    return error.outcome === "unconfirmed" ? "Commissioning outcome unconfirmed" : "Commissioning request rejected";
  }
  if (error instanceof DeviceActivationOutcomeError) {
    return error.outcome === "unconfirmed" ? "Activation outcome unconfirmed" : "Activation request rejected";
  }
  if (error instanceof DeviceSuspensionOutcomeError) {
    return error.outcome === "unconfirmed" ? "Suspension outcome unconfirmed" : "Suspension request rejected";
  }
  return "Device lifecycle action failed";
}

function assignmentRecordedMessage(record: AssignmentRecordedOutcome) {
  return `${record.operation === "transfer" ? "Transfer" : "Installation"} ${record.acknowledgement === "already-recorded" ? "already recorded" : "recorded"}: device ${record.deviceId}, installation ${record.installationId}, vehicle ${record.vehicleId}${record.priorInstallationId ? `, prior installation ${record.priorInstallationId}` : ""}. Returned record status: ${record.recordedStatus ?? "unavailable"}.${record.effectiveFrom ? ` Recorded effective time: ${record.effectiveFrom}.` : " This acknowledgement contains no effective time."} This acknowledgement does not establish current installation state or physical readiness.`;
}

function AssignmentRefreshNotice({ record, busy, onRetry }: { record: AssignmentRecordedOutcome; busy: boolean; onRetry: () => void }) {
  return (
    <div role="status" className="mt-4 flex items-center justify-between gap-4 rounded-xl border border-amber-300/30 bg-amber-500/10 p-4 text-sm text-amber-100">
      <span>{assignmentRecordedMessage(record)} The display could not be refreshed. The shown device details may be out of date.</span>
      <button type="button" className="btn-secondary shrink-0" disabled={busy} onClick={onRetry}>
        {busy ? "Refreshing display…" : "Retry display refresh"}
      </button>
    </div>
  );
}

function RemovalRefreshNotice({ record, busy, onRetry }: { record: RemovalRecordedOutcome; busy: boolean; onRetry: () => void }) {
  return (
    <div role="status" className="mt-4 flex items-center justify-between gap-4 rounded-xl border border-amber-300/30 bg-amber-500/10 p-4 text-sm text-amber-100">
      <span>Removal was recorded for device {record.deviceId}, installation {record.installationId}, effective {record.effectiveTo}, but the display could not be refreshed. The shown device details may be out of date.</span>
      <button type="button" className="btn-secondary shrink-0" disabled={busy} onClick={onRetry}>
        {busy ? "Refreshing display…" : "Retry display refresh"}
      </button>
    </div>
  );
}

function CommissioningRefreshNotice({ record, busy, onRetry }: { record: CommissioningRecordedOutcome; busy: boolean; onRetry: () => void }) {
  return (
    <div role="status" className="mt-4 flex items-center justify-between gap-4 rounded-xl border border-amber-300/30 bg-amber-500/10 p-4 text-sm text-amber-100">
      <span>Commissioning result {record.result} was recorded for device {record.deviceId}, installation {record.installationId}, but the display could not be refreshed. The shown device details may be out of date.</span>
      <button type="button" className="btn-secondary shrink-0" disabled={busy} onClick={onRetry}>
        {busy ? "Refreshing display…" : "Retry display refresh"}
      </button>
    </div>
  );
}

function ActivationRefreshNotice({ deviceId, busy, onRetry }: { deviceId: string; busy: boolean; onRetry: () => void }) {
  return (
    <div role="status" className="mt-4 flex items-center justify-between gap-4 rounded-xl border border-amber-300/30 bg-amber-500/10 p-4 text-sm text-amber-100">
      <span>Activation for device {deviceId} is recorded, but the display could not be refreshed. The shown device details may be out of date.</span>
      <button type="button" className="btn-secondary shrink-0" disabled={busy} onClick={onRetry}>
        {busy ? "Refreshing display…" : "Retry display refresh"}
      </button>
    </div>
  );
}

function SuspensionRefreshNotice({ deviceId, busy, onRetry }: { deviceId: string; busy: boolean; onRetry: () => void }) {
  return (
    <div role="status" className="mt-4 flex items-center justify-between gap-4 rounded-xl border border-amber-300/30 bg-amber-500/10 p-4 text-sm text-amber-100">
      <span>Suspension for device {deviceId} was recorded, but the display could not be refreshed. The shown device details may be out of date.</span>
      <button type="button" className="btn-secondary shrink-0" disabled={busy} onClick={onRetry}>
        {busy ? "Refreshing display…" : "Retry display refresh"}
      </button>
    </div>
  );
}

/**
 * Render a MEASURED count honestly.
 *
 * `?? 0` / `|| 0` on a count the API did not send turns "we never measured this
 * connector" into a confident "0 devices need follow-up" — a plausible default is
 * the hardest kind of lie to spot. A real zero still renders "0"; an absent or
 * unparseable value renders an em dash.
 */
function measuredCount(value: unknown): string {
  if (value === null || value === undefined || value === "") return "—";
  const parsed = Number(value);
  return Number.isFinite(parsed) ? String(parsed) : "—";
}

const INSTALLATION_ROLES = ["GPS", "ELD", "Dashcam", "OBD-II", "J1939/CAN", "Temperature", "Fuel", "Tire", "BLE Gateway", "Other"];

const defaultInstallationForm: InstallationFormState = {
  vehicleId: "",
  deviceRole: "",
  primaryDesignation: "",
  effectiveAt: "",
  installationLocation: "",
  odometerAtInstallation: "",
  commissioningMethod: "",
  assignmentReason: "",
  removalReason: "",
};

const defaultRemovalForm: RemovalFormState = { effectiveTo: "", removalReason: "" };
const defaultCommissioningForm: CommissioningFormState = { result: "", verificationReference: "" };

function toUtcIso(localDateTime: string, field: string) {
  const parsed = new Date(localDateTime);
  if (!localDateTime || Number.isNaN(parsed.getTime())) throw new Error(`Enter a valid ${field}.`);
  return parsed.toISOString();
}

function currentLocalMinute() {
  const now = new Date();
  const local = new Date(now.getTime() - now.getTimezoneOffset() * 60_000);
  return local.toISOString().slice(0, 16);
}

// Minimal, honest inputs for INITIATING A CONNECTION (the Render/Vercel model).
// The device serial is the real key and the governed hardware category is required;
// the product must not infer GPS merely because a device is connected here.
// IMEI is collected too (optional): hardware GPS trackers (GT06/Concox/PT40-class)
// are resolved by IMEI at the trusted-gateway ingest, so onboarding one end-to-end
// needs it. SIM/firmware/power/compliance remain out of the connection handshake.
type ConnectFormState = {
  serialNumber: string;
  imei: string;
  deviceCategory: string;
  provider: string;
  deviceModel: string;
  manufacturer: string;
  hardwareRevision: string;
  firmwareVersion: string;
};

const defaultConnectForm: ConnectFormState = {
  serialNumber: "",
  imei: "",
  deviceCategory: "",
  provider: "",
  deviceModel: "",
  manufacturer: "",
  hardwareRevision: "",
  firmwareVersion: "",
};

const DEVICE_TABS: Array<{ key: DeviceTab; label: string }> = [
  { key: "all", label: "All Devices" },
  { key: "archived", label: "Archived" },
  { key: "unassigned", label: "Unassigned" },
  { key: "offline", label: "Offline" },
  { key: "attention", label: "Needs Attention" },
  { key: "firmware", label: "Firmware planning" },
  { key: "provisioning", label: "Provisioning" },
  { key: "diagnostics", label: "Diagnostics evidence" },
  { key: "quarantine", label: "Identity Quarantine" },
  { key: "installations", label: "Installations" },
  { key: "data-health", label: "Data Health" },
  { key: "readiness", label: "Software Gaps" },
  { key: "providers", label: "Provider Connections" },
];

function emptyStateForTab(tab: DeviceTab) {
  if (tab === "archived") return { title: "No archived devices", subtitle: "Revoked and retired devices remain visible here with their lifecycle history." };
  if (tab === "offline") return { title: "No offline devices", subtitle: "Every scoped device is checking in within the current monitoring window." };
  if (tab === "firmware") return { title: "No devices available for firmware planning", subtitle: "Campaign plans require real devices with reported inventory. Planning does not dispatch an OTA command." };
  if (tab === "providers") return { title: "No providers found", subtitle: "Integrations are pulled from your connected provider catalog." };
  if (tab === "quarantine") return { title: "No unresolved identity conflicts", subtitle: "Every device and installation identity in this fleet is currently unambiguous." };
  if (tab === "readiness") return { title: "No listed software gaps", subtitle: "No active device matches the persisted software gap rules. Hardware, provider, and certification holds remain separate." };
  return { title: "No devices found", subtitle: "Refine the search, switch tabs, or register a device for this fleet." };
}

function activeTabCount(tab: DeviceTab, row: DeviceCommandRecord) {
  const archived = row.lifecycleStatus === "Archived" || /revoked|retired/i.test(String(row.eldStatus));
  if (tab === "archived") return archived;
  if (archived) return false;
  if (tab === "all") return true;
  if (tab === "unassigned") return !row.assignedVehicleCode;
  if (tab === "offline") return /offline/i.test(row.connectionStatus);
  if (tab === "attention") return /attention|offline/i.test(row.connectionStatus) || row.openAlertCount > 0;
  if (tab === "firmware") return true;
  if (tab === "provisioning") return /provision|awaiting/i.test(row.connectionStatus) || /awaiting|warning/i.test(row.installStatus);
  // Diagnostics is a separate evidence feed, not a device-inventory filter.
  // Never count every registered device as though it had diagnostic evidence.
  if (tab === "diagnostics") return false;
  if (tab === "installations") return true;
  if (tab === "data-health") return true;
  if (tab === "readiness") return row.deviceOpsAssessmentAvailable && row.deviceOpsGaps.length > 0;
  return false;
}

function actionTitle(allowed: boolean, allowedTitle: string) {
  return allowed ? allowedTitle : "You do not have permission to perform this action.";
}

function boolText(value: boolean) {
  return value ? "Yes" : "No";
}

type ActionContractState = "ready" | "permission-blocked" | "state-blocked" | "unsupported";

type DeviceActionContract = {
  key: string;
  label: string;
  icon: ReactNode;
  visible: boolean;
  state: ActionContractState;
  reason: string;
  onClick: () => void;
};

function buildActionContracts(
  device: DeviceCommandRecord,
  {
    canUpdate,
    canAssign,
    canManageLifecycle,
    canRecover,
    onAssign,
    onUnassign,
    onRetire,
    onMarkInstalled,
    onRefresh,
    onFlagAttention,
    onResolve,
    onSuspend,
    onActivate,
    onRotateSecret,
  }: {
    canUpdate: boolean;
    canAssign: boolean;
    canManageLifecycle: boolean;
    canRecover: boolean;
    onAssign: () => void;
    onUnassign: () => void;
    onRetire: () => void;
    onMarkInstalled: () => void;
    onRefresh: () => void;
    onFlagAttention: () => void;
    onResolve: () => void;
    onSuspend: () => void;
    onActivate: () => void;
    onRotateSecret: () => void;
  },
) {
  // Recovery actions gate on the device's REAL ELD status, not the derived
  // connectionStatus: resolve-malfunction accepts only Malfunction/Diagnostic and
  // mark-malfunction accepts only Active/Diagnostic. Gating on connectionStatus made an
  // Active-but-stale device offer "Resolve" -> 409, and left "Needs Attention" perpetually
  // disabled. inRecovery drives Resolve; markEligible drives Needs Attention.
  const eldStatus = String(device.eldStatus ?? "").toLowerCase();
  const archived = device.lifecycleStatus === "Archived" || /revoked|retired/.test(eldStatus);
  const inRecovery = /malfunction|diagnostic/.test(eldStatus);
  const markEligible = /active|diagnostic/.test(eldStatus);
  const hasCurrentVehicle = Boolean(device.assignedVehicleCode && device.assignedVehicleCode !== "Unassigned");

  const contracts: DeviceActionContract[] = [
    {
      key: "edit",
      label: "Metadata read-only",
      icon: <Settings2 className="h-4 w-4" />,
      visible: canUpdate,
      state: canUpdate ? "unsupported" : "permission-blocked",
      reason: canUpdate ? "No metadata PATCH contract is available; inventory fields are read-only." : "Requires TELEMATICS_DEVICES_UPDATE.",
      onClick: () => undefined,
    },
    {
      key: "assign",
      label: hasCurrentVehicle ? "Transfer" : "Install",
      icon: <ArrowRightLeft className="h-4 w-4" />,
      visible: canAssign,
      state: archived ? "state-blocked" : canAssign ? "ready" : "permission-blocked",
      reason: archived ? "Archived devices cannot be installed or transferred." : canAssign ? "Governed installation permission is available." : "Requires device assignment plus TELEMETRY_DEVICES_MANAGE.",
      onClick: onAssign,
    },
    {
      key: "unassign",
      label: "Remove installation",
      icon: <Truck className="h-4 w-4" />,
      visible: canAssign,
      state: archived ? "state-blocked" : !canAssign ? "permission-blocked" : hasCurrentVehicle ? "ready" : "state-blocked",
      reason: archived
        ? "Archived device installation history is read-only."
        : !canAssign
        ? "Requires device assignment plus TELEMETRY_DEVICES_MANAGE."
        : hasCurrentVehicle
          ? "Ready."
          : "No active vehicle assignment to remove.",
      onClick: onUnassign,
    },
    {
      key: "install",
      label: "Record commissioning",
      icon: <CheckCircle2 className="h-4 w-4" />,
      visible: canAssign,
      state: archived
        ? "state-blocked"
        : !canAssign
        ? "permission-blocked"
        : !hasCurrentVehicle
          ? "state-blocked"
          : /verified/i.test(device.installStatus)
            ? "state-blocked"
            : device.currentInstallationRowVersion == null
              ? "state-blocked"
            : "ready",
      reason: archived
        ? "Archived devices cannot be commissioned."
        : !canAssign
        ? "Requires device assignment plus TELEMETRY_DEVICES_MANAGE."
        : !hasCurrentVehicle
          ? "Install the device on a vehicle first."
          : /verified/i.test(device.installStatus)
            ? "The current installation is already verified."
            : device.currentInstallationRowVersion == null
              ? "Reload the device to obtain the installation row version before commissioning."
              : device.installationActivationVerifiedAt
                ? "Record the observed Passed or Failed result with evidence."
                : "Record a Failed result now; Passed remains gated by an authenticated device heartbeat.",
      onClick: onMarkInstalled,
    },
    {
      key: "refresh",
      label: "Reload Snapshot",
      icon: <RefreshCw className="h-4 w-4" />,
      visible: true,
      state: "ready",
      reason: "Read endpoint available for a fresh status read.",
      onClick: onRefresh,
    },
    {
      key: "device-state",
      label: /suspended/.test(eldStatus) ? "Activate device" : "Suspend device",
      icon: <ShieldCheck className="h-4 w-4" />,
      visible: canManageLifecycle,
      state: archived
        ? "state-blocked"
        : !canManageLifecycle
        ? "permission-blocked"
        : /suspended/.test(eldStatus) || /active|diagnostic|malfunction/.test(eldStatus)
          ? "ready"
          : "state-blocked",
      reason: archived
        ? "Archived devices cannot be suspended or activated."
        : !canManageLifecycle
        ? "Requires TELEMETRY_DEVICES_MANAGE."
        : /suspended/.test(eldStatus)
          ? "Activate the suspended device through the persisted lifecycle endpoint."
          : /active|diagnostic|malfunction/.test(eldStatus)
            ? "Suspend ingestion for this device without deleting its history."
            : `Suspend/activate is unavailable while the device is ${device.eldStatus || "in this state"}.`,
      onClick: /suspended/.test(eldStatus) ? onActivate : onSuspend,
    },
    {
      key: "rotate-secret",
      label: "Rotate credentials",
      icon: <KeyRound className="h-4 w-4" />,
      visible: canManageLifecycle,
      state: canManageLifecycle && !/revoked|retired/.test(eldStatus) ? "ready" : canManageLifecycle ? "state-blocked" : "permission-blocked",
      reason: !canManageLifecycle
        ? "Requires TELEMETRY_DEVICES_MANAGE."
        : /revoked|retired/.test(eldStatus)
          ? "Credentials cannot be rotated for a revoked or retired device."
          : "Generates a replacement API key and HMAC secret that are shown only once.",
      onClick: onRotateSecret,
    },
    {
      key: inRecovery ? "resolve" : "needs-attention",
      label: inRecovery ? "Resolve" : "Needs Attention",
      icon: inRecovery ? <ShieldCheck className="h-4 w-4" /> : <Activity className="h-4 w-4" />,
      visible: canRecover,
      state: archived ? "state-blocked" : !canRecover ? "permission-blocked" : inRecovery || markEligible ? "ready" : "state-blocked",
      reason: archived
        ? "Archived devices cannot enter or leave recovery."
        : !canRecover
        ? "Requires a compliance (compliance:update / compliance:manage) or telematics:manage permission."
        : inRecovery
          ? "Device is in recovery (Malfunction/Diagnostic) — resolve is available."
          : markEligible
            ? "Device is Active — you can flag it for malfunction review."
            : `Recovery is not available while the device is ${device.eldStatus || "in this state"}.`,
      onClick: inRecovery ? onResolve : onFlagAttention,
    },
    {
      key: "retire",
      label: "Retire device",
      icon: <Trash2 className="h-4 w-4" />,
      visible: canManageLifecycle,
      state: archived ? "state-blocked"
        : !canManageLifecycle ? "permission-blocked"
        : hasCurrentVehicle || device.currentInstallationId ? "state-blocked"
        : device.rowVersion == null ? "state-blocked"
        : "ready",
      reason: archived ? "This device is already retired or revoked."
        : !canManageLifecycle ? "Requires TELEMETRY_DEVICES_MANAGE."
        : hasCurrentVehicle || device.currentInstallationId
          ? "Record physical removal from the current installation before retirement."
          : device.rowVersion == null ? "Reload the device to obtain its current revision."
          : "Records software retirement, ends the current connectivity profile, and permanently invalidates device credentials.",
      onClick: onRetire,
    },
  ];

  return contracts;
}

function actionContractTone(state: ActionContractState) {
  if (state === "ready") return "border-emerald-300/60 bg-emerald-500/10 text-emerald-100";
  if (state === "permission-blocked") return "border-amber-300/60 bg-amber-500/10 text-amber-100";
  if (state === "state-blocked") return "border-sky-300/60 bg-sky-500/10 text-sky-100";
  return "border-rose-300/60 bg-rose-500/10 text-rose-100";
}

function actionContractLabel(state: ActionContractState) {
  if (state === "ready") return "Ready";
  if (state === "permission-blocked") return "Permission";
  if (state === "state-blocked") return "State";
  return "Unavailable";
}

function DeviceLoadingState() {
  return (
    <div className="space-y-4">
      <div>
        <p className="section-title text-teal-300">Loading devices</p>
        <p className="mt-2 text-sm text-slate-400">Refreshing tenant-scoped inventory, assignments, telemetry, diagnostics, and provider status.</p>
      </div>
      <LoadingState />
    </div>
  );
}

export function IotDevicesPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const hasPermission = useHasPermission();
  const hasDirectPermission = useHasDirectPermission();
  const { session } = useAuth();

  const canDiagnostics = hasPermission(PERMISSIONS.TELEMATICS_DEVICES_DIAGNOSTICS);
  // Provision, import, retirement, installation, commissioning, suspension,
  // activation, and credential rotation all share this exact server guard.
  const canManageDeviceLifecycle = hasPermission(PERMISSIONS.TELEMETRY_DEVICES_MANAGE);
  const canPlanFirmware = hasPermission(PERMISSIONS.TELEMATICS_DEVICES_FIRMWARE);
  const canManageRma = hasPermission(PERMISSIONS.TELEMATICS_DEVICES_RMA);
  const canRequestRemoteCommand = hasPermission(PERMISSIONS.TELEMATICS_DEVICES_COMMAND);
  const canCreate = canManageDeviceLifecycle;
  const canUpdate = canManageDeviceLifecycle;
  const canGovernInstallations = canManageDeviceLifecycle;
  const lifecyclePermissionRef = useRef(canManageDeviceLifecycle);
  lifecyclePermissionRef.current = canManageDeviceLifecycle;
  const canBulkInstall = hasDirectPermission(PERMISSIONS.TELEMETRY_DEVICES_MANAGE);
  // Recovery (mark/resolve malfunction) is gated server-side on
  // compliance:update | compliance:manage | telematics:manage — NOT maintenance:manage,
  // which the broader DIAGNOSTICS alias set includes. Gate the UI on the compliance set so
  // a maintenance-manager never sees an enabled recovery button that then 403s. (A pure
  // telematics:manage admin without any compliance grant sees a disabled button rather than
  // a 403 — the safe failure direction.)
  const canRecover = hasPermission(PERMISSIONS.COMPLIANCE_UPDATE);
  const canExport = hasPermission(PERMISSIONS.TELEMATICS_DEVICES_EXPORT);
  const canManageProviders = hasPermission(PERMISSIONS.TELEMATICS_PROVIDERS_MANAGE);
  const canViewProviderCatalog = canReadProviderCatalog(session);

  const [tab, setTab] = useState<DeviceTab>("all");
  const [search, setSearch] = useState("");
  const [deviceSearchQuery, setDeviceSearchQuery] = useState("");
  const [devicePage, setDevicePage] = useState(1);
  const [deviceSort, setDeviceSort] = useState<"serial" | "provider" | "model" | "status" | "lastCheckIn" | "vehicle">("serial");
  const [deviceDirection, setDeviceDirection] = useState<"asc" | "desc">("asc");
  const [selectedId, setSelectedId] = useState<string | number | null>(null);
  // Step 1 of the connect flow — the minimal register-connection form.
  const [connectOpen, setConnectOpen] = useState(false);
  const [connectForm, setConnectForm] = useState<ConnectFormState>(defaultConnectForm);
  // Step 2 of the connect flow — one-time credentials and the recorded check-in.
  // Populated ONLY by a real provisionDevice() response.
  const [provisionResult, setProvisionResult] = useState<DeviceProvisionResult | null>(null);
  const [assignTarget, setAssignTarget] = useState<DeviceCommandRecord | null>(null);
  const assignmentTargetRef = useRef<DeviceCommandRecord | null>(null);
  assignmentTargetRef.current = assignTarget;
  const [installationForm, setInstallationForm] = useState<InstallationFormState>(defaultInstallationForm);
  const assignmentFormSnapshot = useRef(defaultInstallationForm);
  const [assignmentError, setAssignmentError] = useState<Error | null>(null);
  const [assignmentRecord, setAssignmentRecord] = useState<AssignmentRecordedOutcome | null>(null);
  const [assignmentRefreshWarning, setAssignmentRefreshWarning] = useState(false);
  const [assignmentRefreshPending, setAssignmentRefreshPending] = useState(false);
  const assignmentSession = useRef<{ deviceId: string | null; intent: DeviceInstallationIntent | null; generation: number; pending: boolean }>({ deviceId: null, intent: null, generation: 0, pending: false });
  const assignmentRenderGeneration = assignmentSession.current.generation;
  const assignmentRefreshContext = useRef<{ record: AssignmentRecordedOutcome | null; generation: number; sessionGeneration: number | null; target: DeviceCommandRecord | null }>({ record: null, generation: 0, sessionGeneration: null, target: null });
  const assignmentSingleFlight = useSingleFlight();
  const [removalTarget, setRemovalTarget] = useState<DeviceCommandRecord | null>(null);
  const [removalForm, setRemovalForm] = useState<RemovalFormState>(defaultRemovalForm);
  const removalTargetRef = useRef<DeviceCommandRecord | null>(null);
  const removalFormRef = useRef<RemovalFormState>(removalForm);
  removalTargetRef.current = removalTarget;
  removalFormRef.current = removalForm;
  const [removalError, setRemovalError] = useState<Error | null>(null);
  const [removalRecord, setRemovalRecord] = useState<RemovalRecordedOutcome | null>(null);
  const [removalRefreshWarning, setRemovalRefreshWarning] = useState(false);
  const [removalRefreshPending, setRemovalRefreshPending] = useState(false);
  const removalSession = useRef<{ deviceId: string | null; generation: number; pending: boolean }>({ deviceId: null, generation: 0, pending: false });
  const removalRenderGeneration = removalSession.current.generation;
  const removalRefreshContext = useRef<{ record: RemovalRecordedOutcome | null; generation: number; sessionGeneration: number | null; target: DeviceCommandRecord | null }>({ record: null, generation: 0, sessionGeneration: null, target: null });
  const removalSingleFlight = useSingleFlight();
  const [commissionTarget, setCommissionTarget] = useState<DeviceCommandRecord | null>(null);
  const [commissioningForm, setCommissioningForm] = useState<CommissioningFormState>(defaultCommissioningForm);
  const commissioningTargetRef = useRef<DeviceCommandRecord | null>(null);
  const commissioningFormRef = useRef<CommissioningFormState>(commissioningForm);
  commissioningTargetRef.current = commissionTarget;
  commissioningFormRef.current = commissioningForm;
  const [commissioningError, setCommissioningError] = useState<Error | null>(null);
  const [commissioningRecord, setCommissioningRecord] = useState<CommissioningRecordedOutcome | null>(null);
  const [commissioningRefreshWarning, setCommissioningRefreshWarning] = useState(false);
  const [commissioningRefreshPending, setCommissioningRefreshPending] = useState(false);
  const commissioningSession = useRef<{ deviceId: string | null; generation: number; pending: boolean }>({ deviceId: null, generation: 0, pending: false });
  const commissioningRenderGeneration = commissioningSession.current.generation;
  const commissioningRefreshContext = useRef<{ record: CommissioningRecordedOutcome | null; generation: number; sessionGeneration: number | null; target: DeviceCommandRecord | null }>({ record: null, generation: 0, sessionGeneration: null, target: null });
  const commissionSingleFlight = useSingleFlight();
  const [rotatedCredentials, setRotatedCredentials] = useState<DeviceCredentialRotationResult | null>(null);
  const [attentionTarget, setAttentionTarget] = useState<DeviceCommandRecord | null>(null);
  const [attentionNotes, setAttentionNotes] = useState("");
  const [suspensionRefreshWarning, setSuspensionRefreshWarning] = useState(false);
  const [suspensionRefreshPending, setSuspensionRefreshPending] = useState(false);
  const [suspensionReceiptId, setSuspensionReceiptId] = useState<string | null>(null);
  const suspensionRefreshContext = useRef<{ deviceId: string | null; generation: number; sessionGeneration: number | null; target: ConfirmActionTarget | null }>({ deviceId: null, generation: 0, sessionGeneration: null, target: null });
  const suspensionSession = useRef<{ deviceId: string | null; generation: number; pending: boolean; target: ConfirmActionTarget | null }>({ deviceId: null, generation: 0, pending: false, target: null });
  const [suspensionError, setSuspensionError] = useState<Error | null>(null);
  const suspendSingleFlight = useSingleFlight();
  const [activationRefreshWarning, setActivationRefreshWarning] = useState(false);
  const [activationRefreshPending, setActivationRefreshPending] = useState(false);
  const [activationReceiptId, setActivationReceiptId] = useState<string | null>(null);
  const activationRefreshContext = useRef<{ deviceId: string | null; generation: number; sessionGeneration: number | null; target: DeviceCommandRecord | null }>({ deviceId: null, generation: 0, sessionGeneration: null, target: null });
  const activationSession = useRef<{ deviceId: string | null; generation: number; pending: boolean; target: DeviceCommandRecord | null }>({ deviceId: null, generation: 0, pending: false, target: null });
  const activationTargetRef = useRef<DeviceCommandRecord | null>(null);
  const [activationError, setActivationError] = useState<Error | null>(null);
  const activateSingleFlight = useSingleFlight();
  const [quarantineTarget, setQuarantineTarget] = useState<DeviceIdentityQuarantineRecord | null>(null);
  const [quarantineResolution, setQuarantineResolution] = useState({ resolutionNotes: "", correctedDeviceSerial: "", correctedImei: "" });
  const [notice, setNotice] = useState<string | null>(null);
  // DEF-022: client-side validation failures must be visible, never swallowed.
  const [formError, setFormError] = useState<string | null>(null);
  // DEF-023: pending in-app confirmation for a destructive lifecycle action.
  const [confirmTarget, setConfirmTarget] = useState<ConfirmActionTarget | null>(null);
  const confirmTargetRef = useRef<ConfirmActionTarget | null>(null);
  confirmTargetRef.current = confirmTarget;
  const [retirementTarget, setRetirementTarget] = useState<DeviceCommandRecord | null>(null);
  const [retirementForm, setRetirementForm] = useState<RetirementFormState>(newRetirementForm);

  useEffect(() => {
    if (canManageDeviceLifecycle) return;
    assignmentSession.current = { deviceId: null, intent: null, generation: assignmentSession.current.generation + 1, pending: false };
    removalSession.current = { deviceId: null, generation: removalSession.current.generation + 1, pending: false };
    commissioningSession.current = { deviceId: null, generation: commissioningSession.current.generation + 1, pending: false };
    suspensionSession.current = { deviceId: null, generation: suspensionSession.current.generation + 1, pending: false, target: null };
    activationSession.current = { deviceId: null, generation: activationSession.current.generation + 1, pending: false, target: null };
    assignmentRefreshContext.current = { record: null, generation: assignmentRefreshContext.current.generation + 1, sessionGeneration: null, target: null };
    removalRefreshContext.current = { record: null, generation: removalRefreshContext.current.generation + 1, sessionGeneration: null, target: null };
    commissioningRefreshContext.current = { record: null, generation: commissioningRefreshContext.current.generation + 1, sessionGeneration: null, target: null };
    suspensionRefreshContext.current = { deviceId: null, generation: suspensionRefreshContext.current.generation + 1, sessionGeneration: null, target: null };
    activationRefreshContext.current = { deviceId: null, generation: activationRefreshContext.current.generation + 1, sessionGeneration: null, target: null };
    setAssignTarget(null); setRemovalTarget(null); setCommissionTarget(null); setConfirmTarget(null); setRetirementTarget(null);
    setAssignmentError(null); setRemovalError(null); setCommissioningError(null); setSuspensionError(null); setActivationError(null);
    setAssignmentRecord(null); setRemovalRecord(null); setCommissioningRecord(null); setSuspensionReceiptId(null); setActivationReceiptId(null);
    setAssignmentRefreshWarning(false); setRemovalRefreshWarning(false); setCommissioningRefreshWarning(false); setSuspensionRefreshWarning(false); setActivationRefreshWarning(false);
    setAssignmentRefreshPending(false); setRemovalRefreshPending(false); setCommissioningRefreshPending(false); setSuspensionRefreshPending(false); setActivationRefreshPending(false);
  }, [canManageDeviceLifecycle]);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      setDeviceSearchQuery(search.trim());
      setDevicePage(1);
    }, 250);
    return () => window.clearTimeout(timer);
  }, [search]);

  const devicesQ = useQuery({
    queryKey: ["telematics", "devices", "page", devicePage, deviceSearchQuery, tab, deviceSort, deviceDirection],
    queryFn: () => telematicsService.getDevicePage({
      page: devicePage,
      pageSize: 100,
      search: deviceSearchQuery,
      view: tab,
      sort: deviceSort,
      direction: deviceDirection,
    }),
    placeholderData: keepPreviousData,
    staleTime: 20_000,
  });
  const providersQ = useQuery({
    queryKey: ["telematics", "providers"],
    queryFn: telematicsService.getProviders,
    enabled: canViewProviderCatalog,
    staleTime: 20_000,
  });
  const vehiclesQ = useQuery({ queryKey: ["vehicles", "list"], queryFn: vehiclesApi.list, staleTime: 20_000 });
  const quarantineQ = useQuery({
    queryKey: ["telematics", "identity-quarantine"],
    queryFn: telematicsService.getIdentityQuarantine,
    staleTime: 20_000,
  });
  const detailQ = useQuery({
    queryKey: ["telematics", "device", selectedId],
    queryFn: () => telematicsService.getDeviceById(String(selectedId)),
    enabled: selectedId != null,
    staleTime: 20_000,
  });

  const refreshAll = async () => {
    await queryClient.invalidateQueries({ queryKey: ["telematics"] });
    await queryClient.invalidateQueries({ queryKey: ["iot-devices"] });
  };

  const refreshReceiptQueries = async () => {
    const cache = queryClient.getQueryCache();
    const targets = [
      ...cache.findAll({ queryKey: ["telematics"], type: "active" }),
      ...cache.findAll({ queryKey: ["iot-devices"], type: "active" }),
    ];
    if (targets.length === 0) return false;
    const isCurrent = (query: (typeof targets)[number]) => cache.get(query.queryHash) === query && query.isActive();
    const isPausedOrReplaced = () => targets.some(query => !isCurrent(query) || query.state.fetchStatus === "paused");
    if (isPausedOrReplaced()) return false;
    let unsubscribe = () => {};
    const invalidationBecameUnsafe = new Promise<false>(resolve => {
      unsubscribe = cache.subscribe(() => { if (isPausedOrReplaced()) resolve(false); });
    });
    try {
      return await Promise.race([
        Promise.all([
          queryClient.invalidateQueries({ queryKey: ["telematics"] }, { throwOnError: true }),
          queryClient.invalidateQueries({ queryKey: ["iot-devices"] }, { throwOnError: true }),
        ]).then(() => targets.every(query => isCurrent(query) && query.state.fetchStatus === "idle" && query.state.status === "success")),
        invalidationBecameUnsafe,
      ]);
    } catch {
      return false;
    } finally {
      unsubscribe();
    }
  };

  const refreshSuspensionDisplay = async (deviceId: string) => {
    if (!lifecyclePermissionRef.current || suspensionRefreshContext.current.deviceId !== deviceId) return;
    const owner = suspensionRefreshContext.current;
    const ownsRefresh = () => suspensionRefreshContext.current.deviceId === deviceId
      && suspensionRefreshContext.current.sessionGeneration === owner.sessionGeneration
      && suspensionRefreshContext.current.target === owner.target;
    const generation = ++suspensionRefreshContext.current.generation;
    setSuspensionRefreshPending(true);
    try {
      const refreshed = await refreshReceiptQueries();
      if (lifecyclePermissionRef.current && ownsRefresh() && suspensionRefreshContext.current.generation === generation) setSuspensionRefreshWarning(!refreshed);
    } finally {
      if ((!lifecyclePermissionRef.current || ownsRefresh()) && suspensionRefreshContext.current.generation === generation) setSuspensionRefreshPending(false);
    }
  };

  const refreshActivationDisplay = async (deviceId: string) => {
    if (!lifecyclePermissionRef.current || activationRefreshContext.current.deviceId !== deviceId) return;
    const owner = activationRefreshContext.current;
    const ownsRefresh = () => activationRefreshContext.current.deviceId === deviceId
      && activationRefreshContext.current.sessionGeneration === owner.sessionGeneration
      && activationRefreshContext.current.target === owner.target;
    const generation = ++activationRefreshContext.current.generation;
    setActivationRefreshPending(true);
    try {
      const refreshed = await refreshReceiptQueries();
      if (lifecyclePermissionRef.current && ownsRefresh() && activationRefreshContext.current.generation === generation) setActivationRefreshWarning(!refreshed);
    } finally {
      if ((!lifecyclePermissionRef.current || ownsRefresh()) && activationRefreshContext.current.generation === generation) setActivationRefreshPending(false);
    }
  };

  const refreshAssignmentDisplay = async (record: AssignmentRecordedOutcome) => {
    if (!lifecyclePermissionRef.current || assignmentRefreshContext.current.record !== record) return;
    const owner = assignmentRefreshContext.current;
    const ownsRefresh = () => assignmentRefreshContext.current.record === record
      && assignmentRefreshContext.current.sessionGeneration === owner.sessionGeneration
      && assignmentRefreshContext.current.target === owner.target;
    const generation = ++assignmentRefreshContext.current.generation;
    setAssignmentRefreshPending(true);
    try {
      const refreshed = await refreshReceiptQueries();
      if (lifecyclePermissionRef.current && ownsRefresh() && assignmentRefreshContext.current.generation === generation) setAssignmentRefreshWarning(!refreshed);
    } catch {
      if (lifecyclePermissionRef.current && ownsRefresh() && assignmentRefreshContext.current.generation === generation) setAssignmentRefreshWarning(true);
    } finally {
      if ((!lifecyclePermissionRef.current || ownsRefresh()) && assignmentRefreshContext.current.generation === generation) setAssignmentRefreshPending(false);
    }
  };
  const ownsAssignmentSession = (variables: AssignmentMutationVariables) =>
    lifecyclePermissionRef.current && assignmentSession.current.generation === variables.sessionGeneration
    && assignmentSession.current.deviceId === variables.deviceId
    && assignmentTargetRef.current === variables.target
    && assignmentFormSnapshot.current === variables.formSnapshot;

  const refreshRemovalDisplay = async (record: RemovalRecordedOutcome) => {
    if (!lifecyclePermissionRef.current || removalRefreshContext.current.record !== record) return;
    const owner = removalRefreshContext.current;
    const ownsRefresh = () => removalRefreshContext.current.record === record
      && removalRefreshContext.current.sessionGeneration === owner.sessionGeneration
      && removalRefreshContext.current.target === owner.target;
    const generation = ++removalRefreshContext.current.generation;
    setRemovalRefreshPending(true);
    try {
      const refreshed = await refreshReceiptQueries();
      if (lifecyclePermissionRef.current && ownsRefresh() && removalRefreshContext.current.generation === generation) setRemovalRefreshWarning(!refreshed);
    } finally {
      if ((!lifecyclePermissionRef.current || ownsRefresh()) && removalRefreshContext.current.generation === generation) setRemovalRefreshPending(false);
    }
  };
  const ownsRemovalSession = (variables: RemovalMutationVariables) =>
    lifecyclePermissionRef.current && removalSession.current.generation === variables.sessionGeneration
    && removalSession.current.deviceId === String(variables.deviceId)
    && removalTargetRef.current === variables.target
    && removalFormRef.current === variables.formSnapshot;

  const refreshCommissioningDisplay = async (record: CommissioningRecordedOutcome) => {
    // Receipt object identity also distinguishes a replacement installation on the same device.
    if (!lifecyclePermissionRef.current || commissioningRefreshContext.current.record !== record) return;
    const owner = commissioningRefreshContext.current;
    const ownsRefresh = () => commissioningRefreshContext.current.record === record
      && commissioningRefreshContext.current.sessionGeneration === owner.sessionGeneration
      && commissioningRefreshContext.current.target === owner.target;
    const generation = ++commissioningRefreshContext.current.generation;
    setCommissioningRefreshPending(true);
    try {
      const refreshed = await refreshReceiptQueries();
      if (lifecyclePermissionRef.current && ownsRefresh() && commissioningRefreshContext.current.generation === generation) setCommissioningRefreshWarning(!refreshed);
    } finally {
      if ((!lifecyclePermissionRef.current || ownsRefresh()) && commissioningRefreshContext.current.generation === generation) setCommissioningRefreshPending(false);
    }
  };
  const ownsCommissioningSession = (variables: CommissioningMutationVariables) =>
    lifecyclePermissionRef.current && commissioningSession.current.generation === variables.sessionGeneration
    && commissioningSession.current.deviceId === String(variables.deviceId)
    && commissioningTargetRef.current === variables.target
    && commissioningFormRef.current === variables.formSnapshot;

  // Provisioning mints one-time credentials. Keep the result in state to render
  // those credentials and the check-in record; neither establishes a physical connection.
  const provisionMut = useMutation({
    mutationFn: (payload: ConnectFormState) =>
      telematicsService.provisionDevice({
        serialNumber: payload.serialNumber.trim(),
        imei: payload.imei.trim(),
        deviceCategory: payload.deviceCategory,
        provider: payload.provider.trim(),
        deviceName: payload.deviceModel.trim(),
        deviceType: payload.deviceModel.trim(),
        manufacturer: payload.manufacturer.trim(),
        hardwareRevision: payload.hardwareRevision.trim(),
        firmwareVersion: payload.firmwareVersion.trim(),
      }),
    onSuccess: async (result) => {
      setProvisionResult(result);
      setConnectOpen(false);
      setConnectForm(defaultConnectForm);
      // Surface the new (not-yet-streaming) device in the list immediately.
      await refreshAll();
    },
  });
  const retirementMut = useMutation({
    mutationFn: ({ device, form }: { device: DeviceCommandRecord; form: RetirementFormState }) => {
      if (device.rowVersion == null) throw new Error("Reload the device to obtain its current revision before retirement.");
      return telematicsService.retireDevice(device.id, {
        ...form,
        expectedRowVersion: device.rowVersion,
        effectiveAt: new Date().toISOString(),
      });
    },
    retry: false,
    onSuccess: async (result) => {
      setRetirementTarget(null);
      setRetirementForm(newRetirementForm());
      setNotice(result.idempotentReplay
        ? `Retirement receipt ${result.retirement.id} was already recorded.`
        : `Device retired with receipt ${result.retirement.id}. Physical disposition remains unverified.`);
      await refreshAll();
    },
  });
  const assignMut = useMutation({
    mutationFn: ({ deviceId, input }: AssignmentMutationVariables) => telematicsService.assignDeviceToVehicle(deviceId, input),
    retry: false,
    onMutate: (variables) => {
      if (!ownsAssignmentSession(variables)) return;
      assignmentRefreshContext.current = { record: null, generation: assignmentRefreshContext.current.generation + 1, sessionGeneration: null, target: null };
      setAssignmentRecord(null); setAssignmentRefreshWarning(false); setAssignmentRefreshPending(false);
      setAssignmentError(null); setNotice(null);
    },
    onSuccess: async (receipt, variables) => {
      if (!ownsAssignmentSession(variables)) return;
      const record: AssignmentRecordedOutcome = { ...receipt, deviceId: variables.deviceId };
      assignmentRefreshContext.current = { record, generation: assignmentRefreshContext.current.generation + 1, sessionGeneration: variables.sessionGeneration, target: variables.target };
      setAssignmentRecord(record);
      assignmentSession.current.deviceId = null;
      assignmentSession.current.intent = null;
      assignmentSession.current.generation += 1;
      setAssignTarget(null);
      assignmentFormSnapshot.current = defaultInstallationForm;
      setInstallationForm(defaultInstallationForm); setFormError(null);
      setNotice(assignmentRecordedMessage(record));
      await refreshAssignmentDisplay(record);
    },
    onError: (error, variables) => {
      if (ownsAssignmentSession(variables)) setAssignmentError(error);
    },
  });
  const unassignMut = useMutation({
    mutationFn: ({ deviceId, input }: RemovalMutationVariables) => telematicsService.unassignDevice(deviceId, input),
    retry: false,
    onMutate: (variables) => {
      if (!ownsRemovalSession(variables)) return;
      removalRefreshContext.current = { record: null, generation: removalRefreshContext.current.generation + 1, sessionGeneration: null, target: null };
      setRemovalRecord(null); setRemovalRefreshWarning(false); setRemovalRefreshPending(false);
      setRemovalError(null); setNotice(null);
    },
    onSuccess: async (receipt, variables) => {
      if (!ownsRemovalSession(variables)) return;
      const record: RemovalRecordedOutcome = { deviceId: String(variables.deviceId), installationId: receipt.id, effectiveTo: receipt.effectiveTo };
      removalRefreshContext.current = { record, generation: removalRefreshContext.current.generation + 1, sessionGeneration: variables.sessionGeneration, target: variables.target };
      setRemovalRecord(record);
      // Revoke the acknowledged form's admission before any later form opens.
      removalSession.current.deviceId = null;
      removalSession.current.generation += 1;
      setRemovalTarget(null);
      setRemovalForm(defaultRemovalForm); setFormError(null);
      setNotice(`Removal recorded for device ${record.deviceId}, installation ${record.installationId}, effective ${record.effectiveTo}.`);
      await refreshRemovalDisplay(record);
    },
    onError: (error, variables) => {
      if (ownsRemovalSession(variables)) setRemovalError(error);
    },
  });
  const installMut = useMutation({
    mutationFn: ({ deviceId, input }: CommissioningMutationVariables) => telematicsService.markInstalled(deviceId, input),
    retry: false,
    onMutate: (variables) => {
      if (!ownsCommissioningSession(variables)) return;
      commissioningRefreshContext.current = { record: null, generation: commissioningRefreshContext.current.generation + 1, sessionGeneration: null, target: null };
      setCommissioningRecord(null); setCommissioningRefreshWarning(false); setCommissioningRefreshPending(false);
      setCommissioningError(null); setNotice(null);
    },
    onSuccess: async (receipt, variables) => {
      if (!ownsCommissioningSession(variables)) return;
      const record: CommissioningRecordedOutcome = {
        deviceId: String(variables.deviceId), installationId: receipt.id,
        result: receipt.commissioningResult, rowVersion: receipt.rowVersion,
      };
      commissioningRefreshContext.current = { record, generation: commissioningRefreshContext.current.generation + 1, sessionGeneration: variables.sessionGeneration, target: variables.target };
      setCommissioningRecord(record);
      // Revoke this form's admission immediately; its captured handlers must not
      // submit again after acknowledgement, even before another form is opened.
      commissioningSession.current.deviceId = null;
      commissioningSession.current.generation += 1;
      setCommissionTarget(null);
      setCommissioningForm(defaultCommissioningForm); setFormError(null);
      setNotice(`Commissioning result ${record.result} recorded for device ${record.deviceId}, installation ${record.installationId}.`);
      await refreshCommissioningDisplay(record);
    },
    onError: (error, variables) => {
      if (ownsCommissioningSession(variables)) setCommissioningError(error);
    },
  });
  const quarantineResolveMut = useMutation({
    mutationFn: ({ id, payload }: { id: string | number; payload: typeof quarantineResolution }) =>
      telematicsService.resolveIdentityQuarantine(id, {
        resolutionNotes: payload.resolutionNotes.trim(),
        correctedDeviceSerial: payload.correctedDeviceSerial.trim() || undefined,
        correctedImei: payload.correctedImei.trim() || undefined,
      }),
    onSuccess: async () => {
      setQuarantineTarget(null);
      setQuarantineResolution({ resolutionNotes: "", correctedDeviceSerial: "", correctedImei: "" });
      setNotice("Fleet identity quarantine resolved with retained audit evidence.");
      await refreshAll();
    },
  });
  const refreshMut = useMutation({
    mutationFn: (deviceId: string | number) => telematicsService.refreshDeviceStatus(deviceId),
    onSuccess: async () => {
      setNotice("Device status refreshed.");
      await refreshAll();
    },
  });
  const ownsSuspensionSession = (variables: SuspensionMutationVariables) => lifecyclePermissionRef.current
    && suspensionSession.current.deviceId === variables.deviceId
    && suspensionSession.current.generation === variables.sessionGeneration
    && suspensionSession.current.target === variables.target
    && confirmTargetRef.current === variables.target
    && variables.target.action === "suspend"
    && String(variables.target.device.id) === variables.deviceId;
  const ownsActivationSession = (variables: ActivationMutationVariables) => lifecyclePermissionRef.current
    && activationSession.current.deviceId === variables.deviceId
    && activationSession.current.generation === variables.sessionGeneration
    && activationSession.current.target === variables.target
    && activationTargetRef.current === variables.target
    && String(variables.target.id) === variables.deviceId;
  const suspendMut = useMutation({
    mutationFn: ({ deviceId }: SuspensionMutationVariables) => telematicsService.suspendDevice(deviceId),
    retry: false,
    onMutate: (variables) => {
      if (!ownsSuspensionSession(variables)) return;
      suspensionRefreshContext.current = { deviceId: null, generation: suspensionRefreshContext.current.generation + 1, sessionGeneration: null, target: null };
      setNotice(null); setSuspensionError(null); setSuspensionReceiptId(null); setSuspensionRefreshWarning(false); setSuspensionRefreshPending(false);
    },
    onSuccess: async (receipt, variables) => {
      if (!ownsSuspensionSession(variables)) return;
      suspensionRefreshContext.current = { deviceId: receipt.id, generation: suspensionRefreshContext.current.generation + 1, sessionGeneration: variables.sessionGeneration, target: variables.target };
      setConfirmTarget(null); setSuspensionReceiptId(receipt.id); setNotice(`Suspension recorded for device ${receipt.id}.`);
      await refreshSuspensionDisplay(receipt.id);
    },
    onError: (error, variables) => {
      if (ownsSuspensionSession(variables)) setSuspensionError(error);
    },
  });
  const activateMut = useMutation({
    mutationFn: ({ deviceId }: ActivationMutationVariables) => telematicsService.activateDevice(deviceId),
    retry: false,
    onMutate: (variables) => {
      if (!ownsActivationSession(variables)) return;
      activationRefreshContext.current = { deviceId: null, generation: activationRefreshContext.current.generation + 1, sessionGeneration: null, target: null };
      setNotice(null); setActivationError(null); setActivationReceiptId(null); setActivationRefreshWarning(false); setActivationRefreshPending(false);
    },
    onSuccess: async (receipt, variables) => {
      if (!ownsActivationSession(variables)) return;
      activationRefreshContext.current = { deviceId: receipt.id, generation: activationRefreshContext.current.generation + 1, sessionGeneration: variables.sessionGeneration, target: variables.target };
      setActivationReceiptId(receipt.id);
      setNotice(receipt.idempotentReplay ? `Activation already recorded for device ${receipt.id}.` : `Activation recorded for device ${receipt.id}.`);
      await refreshActivationDisplay(receipt.id);
    },
    onError: (error, variables) => {
      if (ownsActivationSession(variables)) setActivationError(error);
    },
  });
  const runActivation = (target: DeviceCommandRecord) => {
    if (!canManageDeviceLifecycle || !lifecyclePermissionRef.current || activationSession.current.pending) return;
    if (activationTargetRef.current !== target) return;
    const admitted = { deviceId: String(target.id), generation: activationSession.current.generation + 1, pending: true, target };
    activationSession.current = admitted;
    void activateSingleFlight(() => activateMut.mutateAsync({ deviceId: admitted.deviceId, sessionGeneration: admitted.generation, target })).finally(() => {
      if (activationSession.current === admitted) admitted.pending = false;
    });
  };
  const rotateSecretMut = useMutation({
    mutationFn: (deviceId: string | number) => telematicsService.rotateDeviceSecret(deviceId),
    onSuccess: (result) => { setConfirmTarget(null); setRotatedCredentials(result); },
  });
  const lifecycleError = assignmentError ?? removalError ?? commissioningError ?? suspensionError ?? activationError ?? rotateSecretMut.error;
  const clearLifecycleError = () => {
    if (!assignmentSession.current.pending) { assignMut.reset(); setAssignmentError(null); }
    if (!removalSession.current.pending) { unassignMut.reset(); setRemovalError(null); }
    if (!commissioningSession.current.pending) { installMut.reset(); setCommissioningError(null); }
    if (!suspensionSession.current.pending) { suspendMut.reset(); setSuspensionError(null); }
    if (!activationSession.current.pending) { activateMut.reset(); setActivationError(null); }
    rotateSecretMut.reset();
  };
  const providerSyncMut = useMutation({
    mutationFn: (providerId: string | number) => telematicsService.syncProvider(providerId),
    onSuccess: async () => {
      setNotice("Provider sync completed.");
      await refreshAll();
    },
  });
  const attentionMut = useMutation({
    mutationFn: ({ id, rowVersion, notes }: { id: string | number; rowVersion?: number; notes: string }) =>
      telematicsService.markDeviceAttention(id, notes, rowVersion),
    onSuccess: async () => {
      setAttentionTarget(null);
      setAttentionNotes("");
      setNotice("Recovery workflow opened.");
      await refreshAll();
    },
  });
  const resolveMut = useMutation({
    mutationFn: ({ id, rowVersion }: { id: string | number; rowVersion?: number }) => telematicsService.resolveDeviceAttention(id, rowVersion),
    onSuccess: async () => {
      setNotice("Recovery cleared and device returned to service.");
      await refreshAll();
    },
  });

  // Open the confirmation, capturing the control that opened it. This has to happen
  // HERE: the row menu (and often the row itself) unmounts in the same commit that
  // mounts the dialog, so by the time the dialog reads document.activeElement the
  // opener is already `<body>` and focus restore has nowhere to go.
  const openConfirm = (target: Omit<ConfirmActionTarget, "opener">) => {
    if (suspensionSession.current.pending) return;
    suspensionSession.current = { deviceId: null, generation: suspensionSession.current.generation + 1, pending: false, target: null };
    setSuspensionError(null);
    setConfirmTarget({
      ...target,
      opener: document.activeElement instanceof HTMLElement ? document.activeElement : null,
    });
  };

  const openRetirement = (target: DeviceCommandRecord) => {
    if (!canManageDeviceLifecycle || target.currentInstallationId || target.assignedVehicleId || target.rowVersion == null) return;
    retirementMut.reset();
    setRetirementForm(newRetirementForm());
    setRetirementTarget(target);
  };

  const submitRetirement = (event: FormEvent) => {
    event.preventDefault();
    if (!canManageDeviceLifecycle || !retirementTarget || retirementMut.isPending) return;
    const reason = retirementForm.retirementReason.trim();
    const source = retirementForm.sourceReference.trim();
    const confirmation = retirementForm.safetyConfirmation.trim();
    retirementMut.mutate({ device: retirementTarget, form: { ...retirementForm,
      retirementReason: reason, sourceReference: source, safetyConfirmation: confirmation } });
  };

  // DEF-023: run/cancel the confirmed lifecycle action through the existing mutations.
  const runConfirmedAction = () => {
    if (!confirmTarget) return;
    if (confirmTarget.action === "suspend" && canManageDeviceLifecycle && lifecyclePermissionRef.current && !suspensionSession.current.pending) {
      if (confirmTargetRef.current !== confirmTarget) return;
      const target = confirmTarget;
      const admitted = { deviceId: String(target.device.id), generation: suspensionSession.current.generation + 1, pending: true, target };
      suspensionSession.current = admitted;
      void suspendSingleFlight(() => suspendMut.mutateAsync({ deviceId: admitted.deviceId, sessionGeneration: admitted.generation, target })).finally(() => {
        if (suspensionSession.current === admitted) admitted.pending = false;
      });
    }
    else if (confirmTarget.action === "rotate-credentials" && canManageDeviceLifecycle) rotateSecretMut.mutate(confirmTarget.device.id);
  };
  const cancelConfirmedAction = () => {
    if (!confirmTarget) return;
    if (confirmTarget.action === "suspend") {
      if (suspensionSession.current.pending) return;
      suspensionSession.current = { deviceId: null, generation: suspensionSession.current.generation + 1, pending: false, target: null };
      setSuspensionError(null); suspendMut.reset();
    }
    else rotateSecretMut.reset();
    setConfirmTarget(null);
  };
  const confirmBusy =
    confirmTarget?.action === "suspend" ? suspendMut.isPending
    : confirmTarget?.action === "rotate-credentials" ? rotateSecretMut.isPending
    : false;
  const confirmActionError =
    confirmTarget?.action === "suspend" ? suspensionError
    : confirmTarget?.action === "rotate-credentials" ? rotateSecretMut.error
    : null;
  const confirmAllowed = confirmTarget?.action === "suspend" || confirmTarget?.action === "rotate-credentials"
      ? canManageDeviceLifecycle
      : false;

  // DEF-022: any field-level change clears the visible validation error.
  //
  // The dialogs render `formError ?? mutation.error`, so clearing ONLY formError
  // un-masks a stale server error from an earlier submit: the user fixes a
  // client-side complaint and the previous 400 reappears in the same role="alert",
  // announced as though the field they just corrected caused it. Reset the
  // matching mutation alongside the form error.
  const updateInstallationForm = (updater: (form: InstallationFormState) => InstallationFormState) => {
    if (assignmentSession.current.pending || assignmentRenderGeneration !== assignmentSession.current.generation
      || assignmentFormSnapshot.current !== installationForm) return;
    setFormError(null);
    setAssignmentError(null);
    assignMut.reset();
    const next = updater(installationForm);
    assignmentFormSnapshot.current = next;
    setInstallationForm(next);
  };
  const closeInstallation = () => {
    if (assignmentSession.current.pending || assignmentRenderGeneration !== assignmentSession.current.generation
      || assignmentFormSnapshot.current !== installationForm) return;
    assignmentSession.current = { deviceId: null, intent: null, generation: assignmentSession.current.generation + 1, pending: false };
    setAssignTarget(null); assignmentFormSnapshot.current = defaultInstallationForm; setInstallationForm(defaultInstallationForm);
    setFormError(null); setAssignmentError(null); assignMut.reset();
  };
  const submitInstallation = (event: FormEvent) => {
    event.preventDefault();
    if (!canGovernInstallations || !assignTarget || assignmentSession.current.pending
      || assignmentRenderGeneration !== assignmentSession.current.generation
      || assignmentFormSnapshot.current !== installationForm
      || assignmentSession.current.deviceId !== String(assignTarget.id) || !assignmentSession.current.intent) return;
    setFormError(null);
    const intent = { ...assignmentSession.current.intent };
    let effectiveAtIso = "";
    let odometer: number | null = null;
    try {
      const odometerText = installationForm.odometerAtInstallation.trim();
      odometer = odometerText ? Number(odometerText) : null;
      if (odometerText && (!Number.isFinite(odometer) || Number(odometer) < 0 || Number(odometer) > 9999999999.99)) throw new Error("Enter a valid odometer at installation (0 to 9999999999.99).");
      if (installationForm.primaryDesignation !== "primary" && installationForm.primaryDesignation !== "secondary") throw new Error("Select the primary or secondary designation.");
      effectiveAtIso = toUtcIso(installationForm.effectiveAt, intent.kind === "transfer" ? "transfer effective time" : "installation effective time");
      if (Date.parse(effectiveAtIso) > Date.now()) throw new Error("Installation effective time cannot be in the future. Choose the current or an earlier time.");
    } catch (validationError) {
      setFormError(validationError instanceof Error ? validationError.message : "Enter valid installation details before submitting.");
      return;
    }
    const admittedSession = assignmentSession.current;
    admittedSession.pending = true;
    const variables: AssignmentMutationVariables = {
      deviceId: admittedSession.deviceId!, sessionGeneration: admittedSession.generation, target: assignTarget, formSnapshot: installationForm,
      input: { intent, vehicleId: installationForm.vehicleId, deviceRole: installationForm.deviceRole,
        isPrimary: installationForm.primaryDesignation === "primary", effectiveAt: effectiveAtIso,
        installationLocation: installationForm.installationLocation, odometerAtInstallation: odometer,
        commissioningMethod: installationForm.commissioningMethod, assignmentReason: installationForm.assignmentReason,
        removalReason: intent.kind === "transfer" ? installationForm.removalReason : undefined },
    };
    void assignmentSingleFlight(() => assignMut.mutateAsync(variables)).finally(() => {
      if (assignmentSession.current === admittedSession) admittedSession.pending = false;
    });
  };
  const updateRemovalForm = (updater: (form: RemovalFormState) => RemovalFormState) => {
    if (removalSession.current.pending || removalRenderGeneration !== removalSession.current.generation) return;
    setFormError(null);
    setRemovalError(null);
    unassignMut.reset();
    setRemovalForm(updater);
  };
  const openRemoval = (target: DeviceCommandRecord) => {
    if (!canGovernInstallations || removalSession.current.pending) return;
    removalSession.current = { deviceId: String(target.id), generation: removalSession.current.generation + 1, pending: false };
    setRemovalTarget(target); setRemovalForm(defaultRemovalForm);
    setFormError(null); setRemovalError(null); unassignMut.reset();
  };
  const closeRemoval = () => {
    if (removalSession.current.pending || removalRenderGeneration !== removalSession.current.generation) return;
    removalSession.current = { deviceId: null, generation: removalSession.current.generation + 1, pending: false };
    setRemovalTarget(null); setRemovalForm(defaultRemovalForm);
    setFormError(null); setRemovalError(null); unassignMut.reset();
  };
  const submitRemoval = (event: FormEvent) => {
    event.preventDefault();
    if (!canGovernInstallations || !removalTarget || removalSession.current.pending
      || removalRenderGeneration !== removalSession.current.generation
      || removalSession.current.deviceId !== String(removalTarget.id)) return;
    setFormError(null);
    let effectiveToIso = "";
    try {
      effectiveToIso = toUtcIso(removalForm.effectiveTo, "removal effective time");
    } catch (validationError) {
      setFormError(validationError instanceof Error ? validationError.message : "Enter a valid removal effective time.");
      return;
    }
    removalSession.current.pending = true;
    const admittedSession = removalSession.current;
    const variables: RemovalMutationVariables = {
      deviceId: removalTarget.id, sessionGeneration: removalSession.current.generation, target: removalTarget, formSnapshot: removalForm,
      input: { effectiveTo: effectiveToIso, removalReason: removalForm.removalReason },
    };
    void removalSingleFlight(() => unassignMut.mutateAsync(variables)).finally(() => {
      // Keep ownership through the shared guard's entire promise settlement.
      if (removalSession.current === admittedSession) admittedSession.pending = false;
    });
  };
  const updateCommissioningForm = (updater: (form: CommissioningFormState) => CommissioningFormState) => {
    if (commissioningSession.current.pending || commissioningRenderGeneration !== commissioningSession.current.generation) return;
    setFormError(null);
    setCommissioningError(null);
    installMut.reset();
    setCommissioningForm(updater);
  };

  const openCommissioning = (target: DeviceCommandRecord) => {
    if (!canGovernInstallations || commissioningSession.current.pending) return;
    commissioningSession.current = { deviceId: String(target.id), generation: commissioningSession.current.generation + 1, pending: false };
    setCommissionTarget(target); setCommissioningForm(defaultCommissioningForm);
    setFormError(null); setCommissioningError(null); installMut.reset();
  };
  const closeCommissioning = () => {
    if (commissioningSession.current.pending || commissioningRenderGeneration !== commissioningSession.current.generation) return;
    commissioningSession.current = { deviceId: null, generation: commissioningSession.current.generation + 1, pending: false };
    setCommissionTarget(null); setCommissioningForm(defaultCommissioningForm);
    setFormError(null); setCommissioningError(null); installMut.reset();
  };
  const submitCommissioning = (event: FormEvent) => {
    event.preventDefault();
    if (!canGovernInstallations || !commissionTarget || commissioningSession.current.pending
      || commissioningRenderGeneration !== commissioningSession.current.generation
      || commissioningSession.current.deviceId !== String(commissionTarget.id)) return;
    setFormError(null);
    if (!commissioningForm.result) {
      setFormError("Select the observed commissioning result (Passed or Failed).");
      return;
    }
    // A synchronous admission guard covers duplicate events before a pending rerender.
    commissioningSession.current.pending = true;
    const admittedSession = commissioningSession.current;
    const variables: CommissioningMutationVariables = {
      deviceId: commissionTarget.id, sessionGeneration: commissioningSession.current.generation, target: commissionTarget, formSnapshot: commissioningForm,
      input: { result: commissioningForm.result, verificationReference: commissioningForm.verificationReference },
    };
    void commissionSingleFlight(() => installMut.mutateAsync(variables)).finally(() => {
      // Release only after the shared guard has fully settled, not in the
      // operation's inner finally where a microtask could re-admit too soon.
      if (commissioningSession.current === admittedSession) admittedSession.pending = false;
    });
  };

  const deviceRows = useMemo(() => {
    return devicesQ.data?.items ?? [];
  }, [devicesQ.data?.items]);
  const vehicleOptions = (vehiclesQ.data ?? []) as AnyRecord[];

  const selectedRecord = detailQ.data?.device ?? deviceRows.find((row) => String(row.id) === String(selectedId)) ?? null;
  activationTargetRef.current = selectedRecord;
  if (activationSession.current.target && activationSession.current.target !== selectedRecord) {
    activationSession.current = { deviceId: null, generation: activationSession.current.generation + 1, pending: false, target: null };
    activationRefreshContext.current = { deviceId: null, generation: activationRefreshContext.current.generation + 1, sessionGeneration: null, target: null };
  }

  const currentPageActiveDevices = (devicesQ.data?.items ?? []).filter((row) => activeTabCount("all", row));
  const archivedCount = devicesQ.data?.summary.archived ?? 0;
  const offlineCount = devicesQ.data?.summary.offline ?? 0;
  const attentionCount = devicesQ.data?.summary.attention ?? 0;
  const readinessGapCount = devicesQ.data?.summary.readinessGaps ?? null;
  const measuredHealth = currentPageActiveDevices.filter((row) => row.dataHealthAvailable);
  const managedCount = devicesQ.data?.summary.active ?? 0;
  const avgHealth = measuredHealth.length
    ? Math.round(measuredHealth.reduce((sum, row) => sum + Number(row.dataHealthScore), 0) / measuredHealth.length)
    : null;
  const devicePageCount = Math.max(1, Math.ceil((devicesQ.data?.total ?? 0) / 100));

  if (devicesQ.isLoading) return <DeviceLoadingState />;
  if (devicesQ.isError) return <ErrorState message="Unable to load the device command center right now." />;

  const openConnect = () => {
    setConnectForm(defaultConnectForm);
    setConnectOpen(true);
  };

  const openInstallation = (device: DeviceCommandRecord) => {
    if (!canGovernInstallations || assignmentSession.current.pending) return;
    const intent = getInstallationIntent(device);
    if (!intent) { setAssignmentError(new Error("Installation context is incomplete or changed. Refresh the device and its history before opening this form.")); return; }
    assignmentSession.current = { deviceId: String(device.id), intent, generation: assignmentSession.current.generation + 1, pending: false };
    setAssignTarget({ ...device });
    assignmentFormSnapshot.current = defaultInstallationForm;
    setInstallationForm(defaultInstallationForm); setFormError(null); setAssignmentError(null); assignMut.reset();
  };

  // Close the pairing panel and land the user on the freshly connected device.
  const finishConnect = async () => {
    const deviceId = provisionResult?.credentials.deviceId ?? provisionResult?.device.id ?? null;
    setProvisionResult(null);
    provisionMut.reset();
    await refreshAll();
    if (deviceId != null) setSelectedId(deviceId);
  };

  const exportCurrent = async () => {
    await telematicsService.exportDevices();
  };

  const emptyState = emptyStateForTab(tab);

  return (
    <div className="fleet-console space-y-3">
      <PageHeader
        eyebrow="Telematics & IoT"
        title="Device Health"
        description="Evidence-backed device connectivity, assignment, reported firmware, and active diagnostic exceptions. Unsupported controls are labelled explicitly."
        actions={
          <>
            <EntityImportExport
              canImport={canCreate}
              canExport={false}
              config={{
                entity: "devices",
                columns: ["deviceSerial", "branchCode", "imei", "deviceCategory", "manufacturer", "deviceModel", "hardwareRevision", "provider", "firmwareVersion", "notes"],
                requiredColumns: ["deviceSerial", "deviceCategory"],
                templateEndpoint: "/api/telemetry/devices/import-template",
                importPreview: telematicsService.previewDeviceImport,
                importCommit: telematicsService.commitDeviceImport,
                invalidateKey: "telematics",
                onImported: refreshAll,
                toolbarLabel: "Device",
              }}
            />
            {canBulkInstall ? <EntityImportExport
              canImport={canBulkInstall}
              canExport={false}
              config={{
                entity: "device installations",
                columns: ["deviceSerial", "branchCode", "vehicleCode", "deviceRole", "isPrimary", "effectiveFrom", "installationLocation", "odometerAtInstallation", "commissioningMethod", "assignmentReason", "idempotencyKey"],
                requiredColumns: ["deviceSerial", "branchCode", "vehicleCode", "deviceRole", "isPrimary", "effectiveFrom", "assignmentReason", "idempotencyKey"],
                templateEndpoint: "/api/telemetry/device-installations/import-template",
                importPreview: telematicsService.previewDeviceInstallationImport,
                importCommit: telematicsService.commitDeviceInstallationImport,
                invalidateKey: "telematics",
                onImported: refreshAll,
                atomic: true,
                toolbarLabel: "Installation",
                importHelp: "Up to 500 rows. This create-only workflow records new installations. Exact idempotent replays are skipped; use the governed Transfer action for reassignment.",
              }}
            /> : null}
            {canExport ? <button className="btn-ghost" title="Export the current device inventory to CSV." onClick={() => void exportCurrent()}>
              <Download className="h-4 w-4" /> Export Devices CSV
            </button> : null}
            {canCreate ? <button className="btn-primary" title="Connect a new device and generate its live credentials." onClick={openConnect}>
              <PlugZap className="h-4 w-4" /> Connect Device
            </button> : null}
          </>
        }
      />

      {notice ? (
        <div className="panel flex items-center justify-between gap-4 border border-emerald-400/20 bg-emerald-500/10 p-4 text-sm text-emerald-100">
          <span>{notice}</span>
          <button className="icon-btn" onClick={() => setNotice(null)}><X className="h-4 w-4" /></button>
        </div>
      ) : null}

      {lifecycleError ? (
        <div role="alert" className="panel flex items-start justify-between gap-4 border border-red-300/30 bg-red-500/10 p-4 text-sm text-red-100">
          <span>
            <strong className="block">{lifecycleFailureHeading(lifecycleError)}</strong>
            {lifecycleError instanceof Error ? lifecycleError.message : "The installation change was not completed. Reload the device and try again."}
          </span>
          <button className="icon-btn" aria-label="Dismiss lifecycle error" onClick={clearLifecycleError}><X className="h-4 w-4" /></button>
        </div>
      ) : null}

      {canManageDeviceLifecycle && suspensionRefreshWarning && suspensionReceiptId && suspensionRefreshContext.current.target ? (
        <SuspensionRefreshNotice deviceId={suspensionReceiptId} busy={suspensionRefreshPending} onRetry={() => { void refreshSuspensionDisplay(suspensionReceiptId); }} />
      ) : null}
      {canManageDeviceLifecycle && activationRefreshWarning && activationReceiptId && activationRefreshContext.current.target === selectedRecord ? (
        <ActivationRefreshNotice deviceId={activationReceiptId} busy={activationRefreshPending} onRetry={() => { void refreshActivationDisplay(activationReceiptId); }} />
      ) : null}
      {canManageDeviceLifecycle && commissioningRefreshWarning && commissioningRecord ? (
        <CommissioningRefreshNotice record={commissioningRecord} busy={commissioningRefreshPending} onRetry={() => { void refreshCommissioningDisplay(commissioningRecord); }} />
      ) : null}
      {canManageDeviceLifecycle && removalRefreshWarning && removalRecord ? (
        <RemovalRefreshNotice record={removalRecord} busy={removalRefreshPending} onRetry={() => { void refreshRemovalDisplay(removalRecord); }} />
      ) : null}
      {canManageDeviceLifecycle && assignmentRefreshWarning && assignmentRecord ? (
        <AssignmentRefreshNotice record={assignmentRecord} busy={assignmentRefreshPending} onRetry={() => { void refreshAssignmentDisplay(assignmentRecord); }} />
      ) : null}

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-5">
        <KpiCard label="Active Managed Devices" value={managedCount} status={managedCount ? "Active" : "Pending"} icon={<RadioTower className="h-4 w-4" />} />
        <KpiCard label="Offline" value={offlineCount} status={!managedCount ? "Pending" : offlineCount ? "Critical" : "Healthy"} icon={<WifiOff className="h-4 w-4" />} />
        <KpiCard label="Needs Attention" value={attentionCount} status={!managedCount ? "Pending" : attentionCount ? "Watch" : "Healthy"} icon={<Activity className="h-4 w-4" />} />
        <KpiCard label="Page Data Health" value={avgHealth == null ? "Unknown" : `${avgHealth}%`} status={avgHealth == null ? "Pending" : avgHealth >= 85 ? "Healthy" : avgHealth >= 70 ? "Watch" : "Critical"} icon={<Cpu className="h-4 w-4" />} />
        <KpiCard label="Devices with gaps" value={readinessGapCount ?? "Unknown"} status={!managedCount || readinessGapCount == null ? "Pending" : readinessGapCount ? "Watch" : "Healthy"} icon={<ShieldCheck className="h-4 w-4" />} />
      </div>
      <p className="text-xs text-slate-500">Data health is a derived signal score for active devices: stale check-in, malfunction state, open telemetry alerts, and active faults reduce the score. Software gaps are persisted operational facts: incomplete exact identity, missing installation or SIM/eSIM profile, stale or absent telemetry, or an open RMA. Neither measure is certification evidence. <button type="button" className="font-semibold text-teal-700 hover:underline" onClick={() => setTab("archived")}>{archivedCount} archived</button> devices are retained separately.</p>

      <div className="panel space-y-4 p-4">
        <div className="flex flex-col gap-3 xl:flex-row xl:items-center xl:justify-between">
          <div className="relative xl:min-w-[360px]">
            <Search className="absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-slate-500" />
            <input
              className="field w-full pl-9"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              aria-label="Search devices by provider, serial, IMEI, vehicle, driver, or tenant"
            />
          </div>
          <div className="flex flex-wrap gap-2">
            <select
              aria-label="Sort devices"
              className="field min-w-36"
              value={deviceSort}
              onChange={(event) => { setDeviceSort(event.target.value as typeof deviceSort); setDevicePage(1); }}
            >
              <option value="serial">Serial</option>
              <option value="provider">Provider</option>
              <option value="model">Model</option>
              <option value="status">Status</option>
              <option value="lastCheckIn">Last check-in</option>
              <option value="vehicle">Vehicle</option>
            </select>
            <button type="button" className="btn-ghost py-2 text-xs" onClick={() => { setDeviceDirection((current) => current === "asc" ? "desc" : "asc"); setDevicePage(1); }}>
              {deviceDirection === "asc" ? "Ascending" : "Descending"}
            </button>
          </div>
          <div className="flex flex-wrap gap-2" role="tablist" aria-label="Device workspace views">
            {DEVICE_TABS.map((item) => (
              <button key={item.key} role="tab" aria-selected={tab === item.key} className={tab === item.key ? "btn-primary py-2 text-xs" : "btn-ghost py-2 text-xs"} onClick={() => { setTab(item.key); setDevicePage(1); }}>
                {item.label}
              </button>
            ))}
          </div>
        </div>

        {tab === "diagnostics" ? (
          <section role="region" aria-labelledby="diagnostics-evidence-title" className="rounded-2xl border border-slate-200 bg-slate-50 p-6">
            <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
              <div className="max-w-3xl">
                <h2 id="diagnostics-evidence-title" className="text-lg font-semibold text-slate-900">Diagnostics evidence is separate from device inventory</h2>
                <p className="mt-2 text-sm text-slate-600">
                  Only devices with received OBD, J1939, or CAN evidence appear in Diagnostics. Device Health does not infer diagnostic coverage for every registered device.
                </p>
                {!canDiagnostics ? <p role="status" className="mt-3 text-sm font-medium text-amber-700">Diagnostics evidence is not available for this role. Ask a tenant administrator for diagnostics access.</p> : null}
              </div>
              <button
                type="button"
                className="btn-primary shrink-0"
                disabled={!canDiagnostics}
                title={actionTitle(canDiagnostics, "Open received OBD, J1939, and CAN evidence.")}
                onClick={() => canDiagnostics && navigate("/obd-j1939")}
              >
                <Activity className="h-4 w-4" /> Open OBD / J1939 evidence
              </button>
            </div>
          </section>
        ) : tab === "quarantine" ? (
          quarantineQ.isLoading ? (
            <LoadingState />
          ) : quarantineQ.isError ? (
            <ErrorState message="Unable to load fleet identity quarantine right now." />
          ) : !(quarantineQ.data ?? []).length ? (
            <EmptyState title={emptyState.title} subtitle={emptyState.subtitle} />
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead><tr className="border-b border-slate-200">
                  {["Detected", "Reason", "Device", "Vehicle", "State", "Resolution"].map((header) => (
                    <th key={header} className="px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500">{header}</th>
                  ))}
                </tr></thead>
                <tbody className="divide-y divide-slate-100">
                  {(quarantineQ.data ?? []).map((row) => (
                    <tr key={String(row.id)}>
                      <td className="px-4 py-3 text-slate-600">{row.detectedAt ? new Date(row.detectedAt).toLocaleString() : "—"}</td>
                      <td className="px-4 py-3"><StatusBadge status={row.reasonCode.replaceAll("_", " ")} /></td>
                      <td className="px-4 py-3 text-slate-700">{row.deviceSerial || "Unlabeled device"}<div className="text-xs text-slate-500">{row.imei || "No IMEI"}</div></td>
                      <td className="px-4 py-3 text-slate-700">{row.vehicleCode || "Unlabeled vehicle"}</td>
                      <td className="px-4 py-3"><StatusBadge status={row.deviceState || "Quarantined"} /></td>
                      <td className="px-4 py-3">
                        {canGovernInstallations ? <button
                          className="btn-primary py-2 text-xs"
                          title="Review evidence and resolve this quarantined identity."
                          onClick={() => {
                            setQuarantineTarget(row);
                            setQuarantineResolution({ resolutionNotes: "", correctedDeviceSerial: row.deviceSerial ?? "", correctedImei: row.imei ?? "" });
                          }}
                        >Resolve conflict</button> : null}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )
        ) : tab === "providers" ? (
          !canViewProviderCatalog ? (
            <EmptyState title="Provider integrations restricted" subtitle="Connector evidence is not available for this role or tenant plan." />
          ) : providersQ.isLoading ? (
            <LoadingState />
          ) : providersQ.isError ? (
            <ErrorState message="Unable to load provider integrations right now." />
          ) : !(providersQ.data ?? []).length ? (
            <EmptyState title={emptyState.title} subtitle={emptyState.subtitle} />
          ) : (
            <div className="grid gap-4 lg:grid-cols-2">
              {(providersQ.data ?? []).map((provider) => (
                <div key={String(provider.id)} className="rounded-2xl border border-slate-200 bg-slate-50 p-5">
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <p className="text-lg font-semibold text-slate-900">{String(provider.name)}</p>
                      <p className="mt-1 text-sm text-slate-400">{String(provider.category)} · {String(provider.supportTier)} support</p>
                    </div>
                    <RiskBadge risk={String(provider.integrationStatus)} />
                  </div>
                  <div className="mt-4 grid gap-2 text-sm text-slate-700">
                    <div className="flex justify-between"><span>Last sync</span><span>{String(provider.lastSyncAt)}</span></div>
                    <div className="flex justify-between"><span>Scoped devices</span><span>{measuredCount(provider.deviceCount)}</span></div>
                    <div className="flex justify-between"><span>Needs follow-up</span><span>{measuredCount((provider as AnyRecord).pendingDevices)}</span></div>
                  </div>
                  {canManageProviders ? <div className="mt-4 flex flex-wrap gap-2">
                    <button
                      className="btn-ghost"
                      title="Open provider management settings."
                      onClick={() => navigate("/integrations")}
                    >
                      <Settings2 className="h-4 w-4" /> Manage Provider
                    </button>
                    <button
                      className="btn-ghost"
                      disabled={providerSyncMut.isPending}
                      title="Run provider sync for the selected integration scope."
                      onClick={() => providerSyncMut.mutate(String(provider.id))}
                    >
                      <PlugZap className="h-4 w-4" /> Sync Provider
                    </button>
                  </div> : null}
                </div>
              ))}
            </div>
          )
        ) : !deviceRows.length ? (
          <EmptyState title={emptyState.title} subtitle={emptyState.subtitle} />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200">
                  {["Device", "Provider", "Identifier", "Vehicle", "Driver", "Firmware", "Check-in", "Connection", "Lifecycle", "Power", "Signal", "Health", "Install", "Compliance", "Operations gaps", "Actions"].map((header) => (
                    <th key={header} className="px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500">{header}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {deviceRows.map((row) => (
                  <tr key={String(row.id)} className="transition hover:bg-slate-50">
                    <td className="px-4 py-3">
                      <button className="text-left" onClick={() => setSelectedId(row.id)}>
                        <p className="font-semibold text-slate-900">{row.deviceName}</p>
                        <p className="text-xs text-slate-400">{row.deviceCategory} · {row.deviceType} · {row.serialNumber || row.identifier}</p>
                      </button>
                    </td>
                    <td className="px-4 py-3 text-slate-700">{row.provider}</td>
                    <td className="px-4 py-3 text-slate-700">
                      <div>{row.serialNumber}</div>
                      <div className="text-xs text-slate-500">{row.imei || row.identifier}</div>
                    </td>
                    <td className="px-4 py-3 text-slate-700">{row.assignedVehicleCode || "Unassigned"}</td>
                    <td className="px-4 py-3 text-slate-700">{row.assignedDriverName || "Unassigned"}</td>
                    <td className="px-4 py-3 text-slate-700">
                      <div>{row.firmwareVersion}</div>
                      {row.firmwareVersion !== row.targetFirmwareVersion ? <div className="text-xs text-amber-700">Target {row.targetFirmwareVersion}</div> : null}
                    </td>
                    <td className="px-4 py-3 text-xs text-slate-400">{row.lastCheckIn}</td>
                    <td className="px-4 py-3"><StatusBadge status={row.connectionStatus} /></td>
                    <td className="px-4 py-3"><StatusBadge status={row.lifecycleStatus} />{row.archivedAt ? <div className="mt-1 text-xs text-slate-500">{new Date(row.archivedAt).toLocaleString()}</div> : null}</td>
                    <td className="px-4 py-3 text-slate-700">{row.powerStatus}</td>
                    <td className="px-4 py-3"><RiskBadge risk={row.signalStrength} /></td>
                    <td className="px-4 py-3 text-slate-700">{row.dataHealthAvailable ? `${row.dataHealthScore}%` : "Unknown"}</td>
                    <td className="px-4 py-3"><StatusBadge status={row.installStatus} /></td>
                    <td className="px-4 py-3"><StatusBadge status={row.complianceStatus} /></td>
                    <td className="px-4 py-3 text-slate-700">
                      <div>{row.supportStatus}</div>
                      <div className="text-xs text-slate-500">{!row.deviceOpsAssessmentAvailable ? "Reload after the DeviceOps assessment API is available" : row.openRmaCount > 0 ? `${row.highestOpenRmaSeverity} · ${row.openRmaCount} open RMA${row.openRmaCount === 1 ? "" : "s"}` : row.deviceOpsGaps.join(" · ") || "Hardware/provider certification holds tracked separately"}</div>
                    </td>
                    <td className="px-4 py-3">
                      <button
                        type="button"
                        className="btn-ghost h-8 px-3"
                        aria-label={`${canManageDeviceLifecycle ? "Manage" : "View details for"} ${row.deviceName}`}
                        aria-haspopup="dialog"
                        onClick={() => setSelectedId(row.id)}
                      >
                        {canManageDeviceLifecycle ? "Manage" : "View details"}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            <div className="flex items-center justify-between border-t border-slate-200 px-4 py-3 text-sm text-slate-600">
              <span>Page {devicePage} of {devicePageCount} · {deviceRows.length} shown · {devicesQ.data?.total ?? 0} matching</span>
              <div className="flex gap-2">
                <button type="button" aria-label="Previous device page" className="btn-ghost h-9 px-3" disabled={devicePage <= 1} onClick={() => setDevicePage((current) => Math.max(1, current - 1))}><ChevronLeft className="h-4 w-4" /></button>
                <button type="button" aria-label="Next device page" className="btn-ghost h-9 px-3" disabled={devicePage >= devicePageCount} onClick={() => setDevicePage((current) => Math.min(devicePageCount, current + 1))}><ChevronRight className="h-4 w-4" /></button>
              </div>
            </div>
          </div>
        )}
      </div>

      {selectedId ? (
        <div className="fixed inset-0 z-50 flex justify-end bg-black/55 backdrop-blur-sm" onClick={() => setSelectedId(null)}>
          <aside className="h-full w-full max-w-5xl overflow-y-auto border-l border-white/[0.09] bg-slate-950 p-6 shadow-2xl" onClick={(event) => event.stopPropagation()}>
            <button className="float-right icon-btn" aria-label="Close device details" onClick={() => setSelectedId(null)}><X className="h-4 w-4" /></button>
            {canManageDeviceLifecycle && suspensionRefreshWarning && suspensionReceiptId && suspensionRefreshContext.current.target ? (
              <SuspensionRefreshNotice deviceId={suspensionReceiptId} busy={suspensionRefreshPending} onRetry={() => { void refreshSuspensionDisplay(suspensionReceiptId); }} />
            ) : null}
            {canManageDeviceLifecycle && activationRefreshWarning && activationReceiptId && activationRefreshContext.current.target === selectedRecord ? (
              <ActivationRefreshNotice deviceId={activationReceiptId} busy={activationRefreshPending} onRetry={() => { void refreshActivationDisplay(activationReceiptId); }} />
            ) : null}
            {canManageDeviceLifecycle && commissioningRefreshWarning && commissioningRecord ? (
              <CommissioningRefreshNotice record={commissioningRecord} busy={commissioningRefreshPending} onRetry={() => { void refreshCommissioningDisplay(commissioningRecord); }} />
            ) : null}
            {canManageDeviceLifecycle && removalRefreshWarning && removalRecord ? (
              <RemovalRefreshNotice record={removalRecord} busy={removalRefreshPending} onRetry={() => { void refreshRemovalDisplay(removalRecord); }} />
            ) : null}
            {canManageDeviceLifecycle && assignmentRefreshWarning && assignmentRecord ? (
              <AssignmentRefreshNotice record={assignmentRecord} busy={assignmentRefreshPending} onRetry={() => { void refreshAssignmentDisplay(assignmentRecord); }} />
            ) : null}
            {detailQ.isLoading ? (
              <LoadingState />
            ) : detailQ.isError || !detailQ.data ? (
              <ErrorState message="Unable to load this device." />
	            ) : (
	              <DeviceDetailDrawer
	                detail={detailQ.data}
	                vehicleOptions={vehicleOptions}
	                canManageConnectivity={canManageDeviceLifecycle}
	                canPlanFirmware={canPlanFirmware}
	                canManageRma={canManageRma}
	                canRequestRemoteCommand={canRequestRemoteCommand}
	                lifecycleError={lifecycleError}
	                onDismissLifecycleError={clearLifecycleError}
	                actionContracts={
	                  selectedRecord
	                    ? buildActionContracts(selectedRecord, {
	                      canUpdate,
	                      canAssign: canGovernInstallations,
	                      canManageLifecycle: canManageDeviceLifecycle,
	                      canRecover,
	                      onAssign: () => openInstallation(selectedRecord),
	                      onUnassign: () => openRemoval(selectedRecord),
	                      onRetire: () => openRetirement(selectedRecord),
	                      onMarkInstalled: () => openCommissioning(selectedRecord),
	                      onRefresh: () => selectedRecord && void refreshMut.mutate(selectedRecord.id),
	                      onFlagAttention: () => canRecover && setAttentionTarget(selectedRecord),
	                      onResolve: () =>
	                        canRecover && resolveMut.mutate({ id: selectedRecord.id, rowVersion: selectedRecord.rowVersion }),
	                      onSuspend: () => canManageDeviceLifecycle && openConfirm({ action: "suspend", device: selectedRecord }),
                        onActivate: () => runActivation(selectedRecord),
	                      onRotateSecret: () => canManageDeviceLifecycle && openConfirm({ action: "rotate-credentials", device: selectedRecord }),
	                    })
	                    : []
	                }
	              />
	            )}
          </aside>
        </div>
      ) : null}

      {/* STEP 1 — Register connection. Minimal, honest inputs; the serial is the real key. */}
      {connectOpen && canCreate ? (
        <ConnectDeviceDialog
          form={connectForm}
          onChange={setConnectForm}
          onClose={() => { setConnectOpen(false); provisionMut.reset(); }}
          onSubmit={() => provisionMut.mutate(connectForm)}
          busy={provisionMut.isPending}
          error={provisionMut.isError ? (provisionMut.error as Error)?.message : null}
        />
      ) : null}

      {/* STEP 2 — One-time credentials and the historical check-in record. */}
      {provisionResult ? (
        <DeviceCredentialsDialog
          result={provisionResult}
          onDone={() => void finishConnect()}
        />
      ) : null}

      {assignTarget && canGovernInstallations ? (
        <ModalForm
          title={`${assignmentSession.current.intent?.kind === "transfer" ? "Transfer" : "Install"} ${assignTarget.deviceName}`}
          onClose={closeInstallation}
          onSubmit={submitInstallation}
          submitLabel={assignmentSession.current.intent?.kind === "transfer" ? "Transfer Device" : "Install Device"}
          busy={assignMut.isPending}
          error={formError ?? assignmentError?.message ?? null}
        >
          <p className="mb-4 rounded-xl border border-sky-200 bg-sky-50 p-3 text-sm text-sky-800">
            This creates effective-dated installation history. Enter the observed facts; OpsTrax will not infer the role, primary designation, time, location, odometer, method, or reasons.
          </p>
          <div className="grid gap-4 md:grid-cols-2">
            <FormField label="Vehicle (required)">
              <select disabled={assignMut.isPending} className="field w-full" value={installationForm.vehicleId} onChange={(event) => updateInstallationForm((form) => ({ ...form, vehicleId: event.target.value }))} required>
                <option value="">Select a vehicle</option>
                {vehicleOptions.map((vehicle) => (
                  <option key={String(vehicle.id ?? vehicle.vehicleId)} value={String(vehicle.id ?? vehicle.vehicleId)} disabled={String(vehicle.id ?? vehicle.vehicleId) === assignTarget.assignedVehicleId}>
                    {String(vehicle.vehicleCode ?? vehicle.vehicleId)} · {String(vehicle.status ?? "Fleet asset")}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Device role (required)">
              <select disabled={assignMut.isPending} className="field w-full" value={installationForm.deviceRole} onChange={(event) => updateInstallationForm((form) => ({ ...form, deviceRole: event.target.value }))} required>
                <option value="">Select the installed role</option>
                {INSTALLATION_ROLES.map((role) => <option key={role} value={role}>{role}</option>)}
              </select>
            </FormField>
            <FormField label="Vehicle role designation (required)">
              <select disabled={assignMut.isPending} className="field w-full" value={installationForm.primaryDesignation} onChange={(event) => updateInstallationForm((form) => ({ ...form, primaryDesignation: event.target.value as InstallationFormState["primaryDesignation"] }))} required>
                <option value="">Select primary or secondary</option>
                <option value="primary">Primary for this role</option>
                <option value="secondary">Secondary for this role</option>
              </select>
            </FormField>
            <FormField label={`${assignmentSession.current.intent?.kind === "transfer" ? "Transfer" : "Installation"} effective time (required)`}>
              <input disabled={assignMut.isPending} className="field w-full" type="datetime-local" max={currentLocalMinute()} value={installationForm.effectiveAt} onChange={(event) => updateInstallationForm((form) => ({ ...form, effectiveAt: event.target.value }))} required />
            </FormField>
            <FormField label="Installation location (required)">
              <input disabled={assignMut.isPending} className="field w-full" maxLength={160} value={installationForm.installationLocation} onChange={(event) => updateInstallationForm((form) => ({ ...form, installationLocation: event.target.value }))} placeholder="Bay, depot, or service location" required />
            </FormField>
            <FormField label="Odometer at installation (required)">
              <input disabled={assignMut.isPending} className="field w-full" type="number" min="0" step="0.1" value={installationForm.odometerAtInstallation} onChange={(event) => updateInstallationForm((form) => ({ ...form, odometerAtInstallation: event.target.value }))} required />
            </FormField>
            <FormField label="Commissioning method (required)">
              <input disabled={assignMut.isPending} className="field w-full" maxLength={80} value={installationForm.commissioningMethod} onChange={(event) => updateInstallationForm((form) => ({ ...form, commissioningMethod: event.target.value }))} placeholder="e.g. authenticated heartbeat + GNSS fix" required />
            </FormField>
            <FormField label="Assignment reason (required)">
              <input disabled={assignMut.isPending} className="field w-full" minLength={4} maxLength={500} value={installationForm.assignmentReason} onChange={(event) => updateInstallationForm((form) => ({ ...form, assignmentReason: event.target.value }))} required />
            </FormField>
            {assignmentSession.current.intent?.kind === "transfer" ? (
              <FormField label="Prior installation removal reason (required)">
                <input disabled={assignMut.isPending} className="field w-full" minLength={4} maxLength={500} value={installationForm.removalReason} onChange={(event) => updateInstallationForm((form) => ({ ...form, removalReason: event.target.value }))} required />
              </FormField>
            ) : null}
          </div>
        </ModalForm>
      ) : null}

      {removalTarget && canGovernInstallations ? (
        <ModalForm
          title={`Remove ${removalTarget.deviceName} installation`}
          onClose={closeRemoval}
          onSubmit={submitRemoval}
          submitLabel="Record Removal"
          busy={unassignMut.isPending}
          error={formError ?? removalError?.message ?? null}
        >
          <div className="grid gap-4 md:grid-cols-2">
            <FormField label="Removal effective time (required)"><input className="field w-full" disabled={unassignMut.isPending} type="datetime-local" value={removalForm.effectiveTo} onChange={(event) => updateRemovalForm((form) => ({ ...form, effectiveTo: event.target.value }))} required /></FormField>
            <FormField label="Removal reason (required)"><input className="field w-full" disabled={unassignMut.isPending} minLength={4} maxLength={500} value={removalForm.removalReason} onChange={(event) => updateRemovalForm((form) => ({ ...form, removalReason: event.target.value }))} required /></FormField>
          </div>
        </ModalForm>
      ) : null}

      {commissionTarget && canGovernInstallations ? (
        <ModalForm
          title={`Commission ${commissionTarget.deviceName}`}
          onClose={closeCommissioning}
          onSubmit={submitCommissioning}
          submitLabel="Record Commissioning Result"
          busy={installMut.isPending}
          error={formError ?? commissioningError?.message ?? null}
        >
          <p className="mb-4 rounded-xl border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
            Select the result actually observed. Passed requires an authenticated device heartbeat; no result is selected automatically.
          </p>
          <div className="grid gap-4 md:grid-cols-2">
            <FormField label="Observed result (required)">
              <select className="field w-full" disabled={installMut.isPending} value={commissioningForm.result} onChange={(event) => updateCommissioningForm((form) => ({ ...form, result: event.target.value as DeviceCommissioningInput["result"] }))} required>
                <option value="">Select Passed or Failed</option>
                <option value="Failed">Failed</option>
                <option value="Passed">Passed</option>
              </select>
            </FormField>
            <FormField label="Evidence / failure reference (required)"><input className="field w-full" disabled={installMut.isPending} maxLength={500} value={commissioningForm.verificationReference} onChange={(event) => updateCommissioningForm((form) => ({ ...form, verificationReference: event.target.value }))} placeholder="Work order, test report, or failure details" required /></FormField>
          </div>
        </ModalForm>
      ) : null}

      {rotatedCredentials && canManageDeviceLifecycle ? (
        <RotatedCredentialsDialog credentials={rotatedCredentials} onDone={() => { setRotatedCredentials(null); rotateSecretMut.reset(); }} />
      ) : null}

      {attentionTarget && canRecover ? (
        <ModalForm
          title={`Open recovery for ${attentionTarget.deviceName}`}
          onClose={() => setAttentionTarget(null)}
          onSubmit={(event) => {
            event.preventDefault();
            attentionMut.mutate({
              id: attentionTarget.id,
              rowVersion: attentionTarget.rowVersion,
              notes: attentionNotes,
            });
          }}
          submitLabel="Open Recovery"
          busy={attentionMut.isPending}
          error={attentionMut.error instanceof Error ? attentionMut.error.message : null}
        >
          <FormField label="Recovery Notes">
            <textarea className="field h-24 w-full resize-none" value={attentionNotes} onChange={(event) => setAttentionNotes(event.target.value)} required />
          </FormField>
        </ModalForm>
      ) : null}

      {quarantineTarget && canGovernInstallations ? (
        <ModalForm
          title={`Resolve ${quarantineTarget.deviceSerial || quarantineTarget.reasonCode.replaceAll("_", " ")}`}
          onClose={() => { setQuarantineTarget(null); quarantineResolveMut.reset(); }}
          onSubmit={(event) => {
            event.preventDefault();
            quarantineResolveMut.mutate({ id: quarantineTarget.id, payload: quarantineResolution });
          }}
          submitLabel="Resolve with audit evidence"
          busy={quarantineResolveMut.isPending}
          error={quarantineResolveMut.error instanceof Error ? quarantineResolveMut.error.message : null}
        >
          <p className="rounded-xl border border-amber-300/30 bg-amber-500/10 p-3 text-sm text-amber-100">
            The quarantine record and original evidence are retained. Identifier conflicts require a corrected serial or IMEI; installation conflicts are released for a new governed installation.
          </p>
          <InfoBlock title="Governed conflict evidence" items={[
            ["Reason", quarantineTarget.reasonCode.replaceAll("_", " ")],
            ["Device", quarantineTarget.deviceSerial || "Unlabeled device"],
            ["Vehicle", quarantineTarget.vehicleCode || "Unlabeled vehicle"],
            ["Detected", quarantineTarget.detectedAt ? new Date(quarantineTarget.detectedAt).toLocaleString() : "Unavailable"],
            ["Evidence", quarantineTarget.evidenceJson ? JSON.stringify(quarantineTarget.evidenceJson) : "No additional evidence supplied"],
          ]} />
          <div className="grid gap-4 md:grid-cols-2">
            <FormField label="Corrected device serial"><input className="field w-full" value={quarantineResolution.correctedDeviceSerial} onChange={(event) => setQuarantineResolution((value) => ({ ...value, correctedDeviceSerial: event.target.value }))} /></FormField>
            <FormField label="Corrected IMEI"><input className="field w-full" value={quarantineResolution.correctedImei} onChange={(event) => setQuarantineResolution((value) => ({ ...value, correctedImei: event.target.value }))} /></FormField>
          </div>
          <FormField label="Resolution notes"><textarea className="field h-28 w-full resize-none" minLength={8} maxLength={2000} required value={quarantineResolution.resolutionNotes} onChange={(event) => setQuarantineResolution((value) => ({ ...value, resolutionNotes: event.target.value }))} /></FormField>
        </ModalForm>
      ) : null}

      {retirementTarget && canManageDeviceLifecycle ? (
        <ModalForm
          title={`Retire ${retirementTarget.deviceName}`}
          onClose={() => { if (!retirementMut.isPending) { setRetirementTarget(null); retirementMut.reset(); } }}
          onSubmit={submitRetirement}
          submitLabel="Retire device permanently"
          busy={retirementMut.isPending}
          error={retirementMut.error instanceof Error ? retirementMut.error.message : null}
        >
          <p className="rounded-xl border border-amber-300/30 bg-amber-500/10 p-3 text-sm text-amber-100">
            This records software retirement, invalidates all device credentials, and ends the current SIM/eSIM profile. The disposition selection is a plan only; physical disposal and certification remain unverified.
          </p>
          <InfoBlock title="Retirement preconditions" items={[
            ["Device serial", retirementTarget.serialNumber],
            ["Current installation", "None recorded"],
            ["Device revision", String(retirementTarget.rowVersion ?? "Unavailable")],
            ["Connectivity profile", "Will end at the retirement time if one is active"],
          ]} />
          <FormField label="Disposition plan">
            <select className="field w-full" required value={retirementForm.dispositionPlan}
              onChange={event => setRetirementForm(form => ({ ...form, dispositionPlan: event.target.value as RetirementFormState["dispositionPlan"] }))}
              disabled={retirementMut.isPending}>
              <option value="ReturnToVendor">Return to vendor</option>
              <option value="Recycle">Recycle</option>
              <option value="SecureStorage">Secure storage</option>
              <option value="Other">Other</option>
            </select>
          </FormField>
          <FormField label="Retirement reason">
            <textarea className="field h-24 w-full resize-none" required minLength={5} maxLength={500}
              value={retirementForm.retirementReason}
              onChange={event => setRetirementForm(form => ({ ...form, retirementReason: event.target.value }))}
              disabled={retirementMut.isPending} />
          </FormField>
          <FormField label="Source reference">
            <input className="field w-full" required minLength={3} maxLength={240}
              value={retirementForm.sourceReference}
              onChange={event => setRetirementForm(form => ({ ...form, sourceReference: event.target.value }))}
              disabled={retirementMut.isPending} />
          </FormField>
          <FormField label={`Type RETIRE ${retirementTarget.serialNumber} to confirm`}>
            <input className="field w-full font-mono" required autoComplete="off"
              pattern={(`RETIRE ${retirementTarget.serialNumber}`).replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}
              value={retirementForm.safetyConfirmation}
              onChange={event => setRetirementForm(form => ({ ...form, safetyConfirmation: event.target.value }))}
              disabled={retirementMut.isPending} />
          </FormField>
        </ModalForm>
      ) : null}

      {confirmTarget && confirmAllowed ? (
        <ConfirmDialog
          title={CONFIRM_ACTION_COPY[confirmTarget.action].title}
          message={CONFIRM_ACTION_COPY[confirmTarget.action].message(confirmTarget.device.deviceName)}
          confirmLabel={CONFIRM_ACTION_COPY[confirmTarget.action].confirmLabel}
          variant={CONFIRM_ACTION_COPY[confirmTarget.action].variant}
          busy={confirmBusy}
          error={confirmActionError instanceof Error ? confirmActionError.message : null}
          returnFocusTo={confirmTarget.opener ?? null}
          onConfirm={runConfirmedAction}
          onCancel={cancelConfirmedAction}
        />
      ) : null}
    </div>
  );
}

function DeviceDetailDrawer({
  detail,
  vehicleOptions,
  canManageConnectivity,
  canPlanFirmware,
  canManageRma,
  canRequestRemoteCommand,
  actionContracts,
  lifecycleError,
  onDismissLifecycleError,
}: {
  detail: DeviceDetailRecord;
  vehicleOptions: AnyRecord[];
  canManageConnectivity: boolean;
  canPlanFirmware: boolean;
  canManageRma: boolean;
  canRequestRemoteCommand: boolean;
  actionContracts: DeviceActionContract[];
  lifecycleError?: unknown;
  onDismissLifecycleError: () => void;
}) {
  const { device } = detail;
  const queryClient = useQueryClient();
  const [workPackageOpen, setWorkPackageOpen] = useState(false);
  const [workPackageForm, setWorkPackageForm] = useState<InstallationWorkPackageFormState>(() =>
    newInstallationWorkPackageForm(detail.currentInstallation?.vehicleId ?? ""));
  const [checklistForm, setChecklistForm] = useState<InstallationChecklistFormState | null>(null);
  const [artifactForm, setArtifactForm] = useState<InstallationArtifactFormState | null>(null);
  const [installationEvidenceError, setInstallationEvidenceError] = useState<string | null>(null);
  const [installationEvidenceNotice, setInstallationEvidenceNotice] = useState<string | null>(null);
  const installationEvidenceSubmitting = useRef(false);
  const refreshInstallationEvidence = async () => {
    await queryClient.invalidateQueries({ queryKey: ["telematics", "device"] });
  };
  const workPackageMut = useMutation({
    mutationFn: (input: DeviceInstallationWorkPackageInput) => telematicsService.createInstallationWorkPackage(device.id, input),
    retry: false,
    onSuccess: async (result) => {
      setWorkPackageForm(newInstallationWorkPackageForm(detail.currentInstallation?.vehicleId ?? ""));
      setWorkPackageOpen(false); setInstallationEvidenceError(null); setInstallationEvidenceNotice(result.note);
      await refreshInstallationEvidence();
    },
    onError: (error) => setInstallationEvidenceError(apiErrorMessage(error, "The installation work package was not recorded.")),
    onSettled: () => { installationEvidenceSubmitting.current = false; },
  });
  const checklistMut = useMutation({
    mutationFn: ({ workPackageId, input }: { workPackageId: string; input: DeviceInstallationChecklistObservationInput }) =>
      telematicsService.recordInstallationChecklistObservation(device.id, workPackageId, input),
    retry: false,
    onSuccess: async (result) => {
      setChecklistForm(null); setInstallationEvidenceError(null); setInstallationEvidenceNotice(result.note);
      await refreshInstallationEvidence();
    },
    onError: (error) => setInstallationEvidenceError(apiErrorMessage(error, "The checklist observation was not recorded.")),
    onSettled: () => { installationEvidenceSubmitting.current = false; },
  });
  const artifactMut = useMutation({
    mutationFn: ({ workPackageId, input }: { workPackageId: string; input: DeviceInstallationArtifactReferenceInput }) =>
      telematicsService.recordInstallationArtifactReference(device.id, workPackageId, input),
    retry: false,
    onSuccess: async (result) => {
      setArtifactForm(null); setInstallationEvidenceError(null); setInstallationEvidenceNotice(result.note);
      await refreshInstallationEvidence();
    },
    onError: (error) => setInstallationEvidenceError(apiErrorMessage(error, "The artifact reference was not recorded.")),
    onSettled: () => { installationEvidenceSubmitting.current = false; },
  });
  const linkWorkPackageMut = useMutation({
    mutationFn: ({ workPackageId, installationId }: { workPackageId: string; installationId: string }) =>
      telematicsService.linkInstallationWorkPackage(device.id, workPackageId, installationId),
    retry: false,
    onSuccess: async (result) => {
      setInstallationEvidenceError(null); setInstallationEvidenceNotice(result.note);
      await refreshInstallationEvidence();
    },
    onError: (error) => setInstallationEvidenceError(apiErrorMessage(error, "The work package was not linked to the installation.")),
    onSettled: () => { installationEvidenceSubmitting.current = false; },
  });
  const installationEvidenceBusy = workPackageMut.isPending || checklistMut.isPending || artifactMut.isPending || linkWorkPackageMut.isPending;
  const submitWorkPackage = (event: FormEvent) => {
    event.preventDefault();
    if (!canManageConnectivity || installationEvidenceSubmitting.current || installationEvidenceBusy) return;
    setInstallationEvidenceError(null);
    try {
      installationEvidenceSubmitting.current = true;
      workPackageMut.mutate({
        ...workPackageForm,
        vehicleId: workPackageForm.vehicleId,
        appointmentStart: toUtcIso(workPackageForm.appointmentStart, "appointment start"),
        appointmentEnd: toUtcIso(workPackageForm.appointmentEnd, "appointment end"),
      });
    } catch (error) {
      installationEvidenceSubmitting.current = false;
      setInstallationEvidenceError(error instanceof Error ? error.message : "Installation work-package validation failed.");
    }
  };
  const submitChecklistObservation = (event: FormEvent) => {
    event.preventDefault();
    if (!canManageConnectivity || !checklistForm || installationEvidenceSubmitting.current || installationEvidenceBusy) return;
    setInstallationEvidenceError(null);
    try {
      installationEvidenceSubmitting.current = true;
      const { workPackageId, ...form } = checklistForm;
      checklistMut.mutate({ workPackageId, input: { ...form, observedAt: toUtcIso(form.observedAt, "checklist observation time") } });
    } catch (error) {
      installationEvidenceSubmitting.current = false;
      setInstallationEvidenceError(error instanceof Error ? error.message : "Checklist validation failed.");
    }
  };
  const submitArtifactReference = (event: FormEvent) => {
    event.preventDefault();
    if (!canManageConnectivity || !artifactForm || installationEvidenceSubmitting.current || installationEvidenceBusy) return;
    setInstallationEvidenceError(null);
    try {
      installationEvidenceSubmitting.current = true;
      const { workPackageId, ...form } = artifactForm;
      artifactMut.mutate({ workPackageId, input: { ...form, capturedAt: toUtcIso(form.capturedAt, "artifact capture time") } });
    } catch (error) {
      installationEvidenceSubmitting.current = false;
      setInstallationEvidenceError(error instanceof Error ? error.message : "Artifact-reference validation failed.");
    }
  };
  const [connectivityOpen, setConnectivityOpen] = useState(false);
  const [connectivityForm, setConnectivityForm] = useState<ConnectivityProfileFormState>(newConnectivityProfileForm);
  const [connectivityError, setConnectivityError] = useState<string | null>(null);
  const [connectivityNotice, setConnectivityNotice] = useState<string | null>(null);
  const connectivitySubmitting = useRef(false);
  const connectivityMut = useMutation({
    mutationFn: (input: DeviceConnectivityProfileInput) => telematicsService.replaceDeviceConnectivityProfile(device.id, input),
    retry: false,
    onSuccess: async (result) => {
      // Clear plaintext SIM inventory immediately after the server acknowledges it.
      setConnectivityForm(newConnectivityProfileForm());
      setConnectivityOpen(false);
      setConnectivityError(null);
      setConnectivityNotice(result.note);
      await queryClient.invalidateQueries({ queryKey: ["telematics", "device"] });
    },
    onError: (error) => setConnectivityError(apiErrorMessage(error, "The connectivity profile was not recorded.")),
    onSettled: () => { connectivitySubmitting.current = false; },
  });
  const openConnectivityForm = () => {
    setConnectivityForm(newConnectivityProfileForm());
    setConnectivityError(null);
    setConnectivityNotice(null);
    connectivityMut.reset();
    setConnectivityOpen(true);
  };
  const submitConnectivityProfile = (event: FormEvent) => {
    event.preventDefault();
    if (!canManageConnectivity || connectivitySubmitting.current || connectivityMut.isPending) return;
    setConnectivityError(null);
    try {
      const effectiveAt = toUtcIso(connectivityForm.effectiveAt, "connectivity profile effective time");
      if (Date.parse(effectiveAt) > Date.now() + 5 * 60_000)
        throw new Error("Connectivity profile effective time cannot be more than five minutes in the future.");
      connectivitySubmitting.current = true;
      connectivityMut.mutate({
        ...connectivityForm,
        effectiveAt,
        carrierName: connectivityForm.carrierName.trim(),
        iccid: connectivityForm.iccid.trim(),
        msisdn: connectivityForm.msisdn.trim() || undefined,
        apn: connectivityForm.apn.trim() || undefined,
        changeReason: connectivityForm.changeReason.trim(),
        sourceReference: connectivityForm.sourceReference.trim(),
      });
    } catch (error) {
      connectivitySubmitting.current = false;
      setConnectivityError(error instanceof Error ? error.message : "Connectivity profile validation failed.");
    }
  };
  const [firmwareOpen, setFirmwareOpen] = useState(false);
  const [firmwareForm, setFirmwareForm] = useState<FirmwareCampaignFormState>(newFirmwareCampaignForm);
  const [firmwareError, setFirmwareError] = useState<string | null>(null);
  const [firmwareNotice, setFirmwareNotice] = useState<string | null>(null);
  const firmwareSubmitting = useRef(false);
  const firmwareMut = useMutation({
    mutationFn: (input: DeviceFirmwareCampaignInput) => telematicsService.createFirmwareCampaign(input),
    retry: false,
    onSuccess: async (result) => {
      setFirmwareForm(newFirmwareCampaignForm());
      setFirmwareOpen(false);
      setFirmwareError(null);
      setFirmwareNotice(result.note);
      await queryClient.invalidateQueries({ queryKey: ["telematics", "device"] });
    },
    onError: (error) => setFirmwareError(apiErrorMessage(error, "The firmware plan was not recorded.")),
    onSettled: () => { firmwareSubmitting.current = false; },
  });
  const openFirmwareForm = () => {
    setFirmwareForm(newFirmwareCampaignForm());
    setFirmwareError(null);
    setFirmwareNotice(null);
    firmwareMut.reset();
    setFirmwareOpen(true);
  };
  const submitFirmwareCampaign = (event: FormEvent) => {
    event.preventDefault();
    if (!canPlanFirmware || firmwareSubmitting.current || firmwareMut.isPending) return;
    setFirmwareError(null);
    try {
      const scheduledFor = toUtcIso(firmwareForm.scheduledFor, "firmware campaign schedule");
      const maintenanceWindowMinutes = Number(firmwareForm.maintenanceWindowMinutes);
      if (!Number.isInteger(maintenanceWindowMinutes) || maintenanceWindowMinutes < 15 || maintenanceWindowMinutes > 720)
        throw new Error("Maintenance window must be between 15 and 720 minutes.");
      firmwareSubmitting.current = true;
      firmwareMut.mutate({
        campaignName: firmwareForm.campaignName.trim(),
        targetFirmwareVersion: firmwareForm.targetFirmwareVersion.trim(),
        rollbackFirmwareVersion: firmwareForm.rollbackFirmwareVersion.trim() || undefined,
        rolloutStrategy: firmwareForm.rolloutStrategy,
        scheduledFor,
        maintenanceWindowMinutes,
        batchSize: 1,
        deviceIds: [device.id],
        changeReason: firmwareForm.changeReason.trim(),
        sourceReference: firmwareForm.sourceReference.trim(),
        idempotencyKey: firmwareForm.idempotencyKey,
      });
    } catch (error) {
      firmwareSubmitting.current = false;
      setFirmwareError(error instanceof Error ? error.message : "Firmware campaign validation failed.");
    }
  };
  const [rmaCaseOpen, setRmaCaseOpen] = useState(false);
  const [rmaCaseForm, setRmaCaseForm] = useState<RmaCaseFormState>(newRmaCaseForm);
  const [rmaEventCaseId, setRmaEventCaseId] = useState<string | null>(null);
  const [rmaEventForm, setRmaEventForm] = useState<RmaEventFormState>(newRmaEventForm);
  const [rmaReplacementCaseId, setRmaReplacementCaseId] = useState<string | null>(null);
  const [rmaReplacementForm, setRmaReplacementForm] = useState<RmaReplacementFormState>(newRmaReplacementForm);
  const [rmaSupportCaseId, setRmaSupportCaseId] = useState<string | null>(null);
  const [rmaSupportForm, setRmaSupportForm] = useState<RmaSupportFormState>(() => newRmaSupportForm("TakeOwnership"));
  const [rmaError, setRmaError] = useState<string | null>(null);
  const [rmaNotice, setRmaNotice] = useState<string | null>(null);
  const rmaSubmitting = useRef(false);
  const refreshRma = async () => { await queryClient.invalidateQueries({ queryKey: ["telematics", "device"] }); };
  const rmaCaseMut = useMutation({
    mutationFn: (input: DeviceRmaCaseInput) => telematicsService.createDeviceRmaCase(device.id, input), retry: false,
    onSuccess: async (result) => { setRmaCaseForm(newRmaCaseForm()); setRmaCaseOpen(false); setRmaError(null); setRmaNotice(result.note); await refreshRma(); },
    onError: (error) => setRmaError(apiErrorMessage(error, "The RMA case was not recorded.")),
    onSettled: () => { rmaSubmitting.current = false; },
  });
  const rmaEventMut = useMutation({
    mutationFn: ({ caseId, input }: { caseId: string; input: DeviceRmaEventInput }) => telematicsService.appendDeviceRmaEvent(caseId, input), retry: false,
    onSuccess: async (result) => { setRmaEventForm(newRmaEventForm()); setRmaEventCaseId(null); setRmaError(null); setRmaNotice(result.note); await refreshRma(); },
    onError: (error) => setRmaError(apiErrorMessage(error, "The custody event was not recorded.")),
    onSettled: () => { rmaSubmitting.current = false; },
  });
  const rmaReplacementMut = useMutation({
    mutationFn: ({ caseId, input }: { caseId: string; input: DeviceRmaReplacementInput }) => telematicsService.planDeviceRmaReplacement(caseId, input), retry: false,
    onSuccess: async (result) => { setRmaReplacementForm(newRmaReplacementForm()); setRmaReplacementCaseId(null); setRmaError(null); setRmaNotice(result.note); await refreshRma(); },
    onError: (error) => setRmaError(apiErrorMessage(error, "The replacement plan was not recorded.")),
    onSettled: () => { rmaSubmitting.current = false; },
  });
  const rmaSupportMut = useMutation({
    mutationFn: ({ caseId, input }: { caseId: string; input: DeviceRmaSupportActionInput }) =>
      telematicsService.recordDeviceRmaSupportAction(caseId, input), retry: false,
    onSuccess: async (result) => {
      setRmaSupportForm(newRmaSupportForm("TakeOwnership")); setRmaSupportCaseId(null);
      setRmaError(null); setRmaNotice(result.note); await refreshRma();
    },
    onError: (error) => setRmaError(apiErrorMessage(error, "The RMA support action was not recorded.")),
    onSettled: () => { rmaSubmitting.current = false; },
  });
  const rmaBusy = rmaCaseMut.isPending || rmaEventMut.isPending || rmaReplacementMut.isPending || rmaSupportMut.isPending;
  const submitRmaCase = (event: FormEvent) => {
    event.preventDefault();
    if (!canManageRma || rmaSubmitting.current || rmaBusy) return;
    setRmaError(null);
    try {
      rmaSubmitting.current = true;
      rmaCaseMut.mutate({
        ...rmaCaseForm,
        observedAt: toUtcIso(rmaCaseForm.observedAt, "failure observation time"),
        responseDueAt: toUtcIso(rmaCaseForm.responseDueAt, "support response due time"),
        warrantyReference: rmaCaseForm.warrantyReference.trim() || undefined,
      });
    } catch (error) { rmaSubmitting.current = false; setRmaError(error instanceof Error ? error.message : "RMA validation failed."); }
  };
  const submitRmaEvent = (event: FormEvent) => {
    event.preventDefault();
    if (!canManageRma || !rmaEventCaseId || rmaSubmitting.current || rmaBusy) return;
    setRmaError(null);
    try {
      rmaSubmitting.current = true;
      rmaEventMut.mutate({ caseId: rmaEventCaseId, input: {
        ...rmaEventForm, occurredAt: toUtcIso(rmaEventForm.occurredAt, "custody event time"),
        custodyLocation: rmaEventForm.custodyLocation.trim() || undefined,
        trackingReference: rmaEventForm.trackingReference.trim() || undefined,
      } });
    } catch (error) { rmaSubmitting.current = false; setRmaError(error instanceof Error ? error.message : "Custody event validation failed."); }
  };
  const submitRmaReplacement = (event: FormEvent) => {
    event.preventDefault();
    if (!canManageRma || !rmaReplacementCaseId || rmaSubmitting.current || rmaBusy) return;
    rmaSubmitting.current = true;
    setRmaError(null);
    rmaReplacementMut.mutate({ caseId: rmaReplacementCaseId, input: rmaReplacementForm });
  };
  const submitRmaSupport = (event: FormEvent) => {
    event.preventDefault();
    if (!canManageRma || !rmaSupportCaseId || rmaSubmitting.current || rmaBusy) return;
    setRmaError(null);
    try {
      rmaSubmitting.current = true;
      rmaSupportMut.mutate({ caseId: rmaSupportCaseId, input: {
        ...rmaSupportForm,
        effectiveAt: toUtcIso(rmaSupportForm.effectiveAt, "support action time"),
        escalationSeverity: rmaSupportForm.actionType === "Escalate" ? rmaSupportForm.escalationSeverity : undefined,
      } });
    } catch (error) {
      rmaSubmitting.current = false;
      setRmaError(error instanceof Error ? error.message : "RMA support action validation failed.");
    }
  };
  const [sparePoolForm, setSparePoolForm] = useState<SparePoolFormState | null>(null);
  const [sparePoolError, setSparePoolError] = useState<string | null>(null);
  const [sparePoolNotice, setSparePoolNotice] = useState<string | null>(null);
  const sparePoolSubmitting = useRef(false);
  const sparePoolMut = useMutation({
    mutationFn: (input: DeviceSparePoolActionInput) => telematicsService.recordDeviceSparePoolAction(device.id, input),
    retry: false,
    onSuccess: async (result) => {
      setSparePoolForm(null); setSparePoolError(null); setSparePoolNotice(result.note);
      await queryClient.invalidateQueries({ queryKey: ["telematics", "device"] });
    },
    onError: (error) => setSparePoolError(apiErrorMessage(error, "The spare-pool action was not recorded.")),
    onSettled: () => { sparePoolSubmitting.current = false; },
  });
  const submitSparePool = (event: FormEvent) => {
    event.preventDefault();
    if (!canManageRma || !sparePoolForm || sparePoolSubmitting.current || sparePoolMut.isPending) return;
    setSparePoolError(null);
    try {
      sparePoolSubmitting.current = true;
      sparePoolMut.mutate({
        actionType: sparePoolForm.actionType,
        poolName: sparePoolForm.actionType === "Add" ? sparePoolForm.poolName : undefined,
        rmaCaseId: sparePoolForm.actionType === "Reserve" ? sparePoolForm.rmaCaseId : undefined,
        actionReason: sparePoolForm.actionReason,
        sourceReference: sparePoolForm.sourceReference,
        effectiveAt: toUtcIso(sparePoolForm.effectiveAt, "spare-pool action time"),
        idempotencyKey: sparePoolForm.idempotencyKey,
      });
    } catch (error) {
      sparePoolSubmitting.current = false;
      setSparePoolError(error instanceof Error ? error.message : "Spare-pool validation failed.");
    }
  };
  const latestSupportTier = detail.supportTierEvents[0] ?? null;
  const activeSupportTier = latestSupportTier?.stateAfter === "Assigned" ? latestSupportTier : null;
  const [supportTierForm, setSupportTierForm] = useState<SupportTierFormState | null>(null);
  const [supportTierError, setSupportTierError] = useState<string | null>(null);
  const [supportTierNotice, setSupportTierNotice] = useState<string | null>(null);
  const supportTierSubmitting = useRef(false);
  const supportTierMut = useMutation({
    mutationFn: (input: DeviceSupportTierActionInput) => telematicsService.recordDeviceSupportTierAction(device.id, input),
    retry: false,
    onSuccess: async (result) => {
      setSupportTierForm(null); setSupportTierError(null); setSupportTierNotice(result.note);
      await queryClient.invalidateQueries({ queryKey: ["telematics", "device"] });
    },
    onError: (error) => setSupportTierError(apiErrorMessage(error, "The support-tier action was not recorded.")),
    onSettled: () => { supportTierSubmitting.current = false; },
  });
  const submitSupportTier = (event: FormEvent) => {
    event.preventDefault();
    if (!canManageRma || !supportTierForm || supportTierSubmitting.current || supportTierMut.isPending) return;
    setSupportTierError(null);
    try {
      supportTierSubmitting.current = true;
      const hasPlan = supportTierForm.actionType !== "End";
      supportTierMut.mutate({
        actionType: supportTierForm.actionType,
        tierCode: hasPlan ? supportTierForm.tierCode : undefined,
        coverageWindow: hasPlan ? supportTierForm.coverageWindow : undefined,
        routingResponseTargetMinutes: hasPlan ? supportTierForm.routingResponseTargetMinutes : undefined,
        escalationPolicyReference: hasPlan ? supportTierForm.escalationPolicyReference : undefined,
        commercialReference: hasPlan ? supportTierForm.commercialReference : undefined,
        actionReason: supportTierForm.actionReason,
        sourceReference: supportTierForm.sourceReference,
        effectiveAt: toUtcIso(supportTierForm.effectiveAt, "support-tier action time"),
        idempotencyKey: supportTierForm.idempotencyKey,
      });
    } catch (error) {
      supportTierSubmitting.current = false;
      setSupportTierError(error instanceof Error ? error.message : "Support-tier validation failed.");
    }
  };
  const [remoteCommandForm, setRemoteCommandForm] = useState<RemoteCommandFormState | null>(null);
  const [remoteCommandError, setRemoteCommandError] = useState<string | null>(null);
  const [remoteCommandNotice, setRemoteCommandNotice] = useState<string | null>(null);
  const remoteCommandSubmitting = useRef(false);
  const remoteCommandMut = useMutation({
    mutationFn: (input: DeviceRemoteCommandInput) => telematicsService.requestDeviceRemoteCommand(device.id, input),
    retry: false,
    onSuccess: async (result) => {
      setRemoteCommandForm(null); setRemoteCommandError(null); setRemoteCommandNotice(result.note);
      await queryClient.invalidateQueries({ queryKey: ["telematics", "device"] });
    },
    onError: (error) => setRemoteCommandError(apiErrorMessage(error, "The command request was not recorded.")),
    onSettled: () => { remoteCommandSubmitting.current = false; },
  });
  const submitRemoteCommand = (event: FormEvent) => {
    event.preventDefault();
    if (!canRequestRemoteCommand || !remoteCommandForm || remoteCommandSubmitting.current || remoteCommandMut.isPending) return;
    setRemoteCommandError(null);
    const delaySeconds = Number(remoteCommandForm.delaySeconds);
    if (remoteCommandForm.commandType === "RestartDevice" &&
        (!Number.isInteger(delaySeconds) || delaySeconds < 0 || delaySeconds > 300)) {
      setRemoteCommandError("Restart delay must be a whole number from 0 to 300 seconds.");
      return;
    }
    remoteCommandSubmitting.current = true;
    remoteCommandMut.mutate({
      commandType: remoteCommandForm.commandType,
      payload: remoteCommandForm.commandType === "RestartDevice" ? { delaySeconds } : {},
      purpose: remoteCommandForm.purpose,
      sourceReference: remoteCommandForm.sourceReference,
      safetyConfirmation: remoteCommandForm.safetyConfirmation,
      idempotencyKey: remoteCommandForm.idempotencyKey,
    });
  };
  const selectedRemoteCommandCapability = remoteCommandForm
    ? detail.remoteCommandCapabilities.find(capability => capability.commandType === remoteCommandForm.commandType) ?? null
    : null;
  // Guard every [0] access — these live sub-feeds are frequently empty. `telemetry`
  // is a single live position point (or none), and `diagnostics` are active fault codes.
  const latestTelemetry = detail.telemetry[0] ?? null;
  const latestDiagnostic = detail.diagnostics[0] ?? null;
  const latestSensor = detail.sensorReadings[0] ?? null;
  const latestConnectivityObservation = detail.connectivityObservations[0] ?? null;
  // Values from the live position are already normalized to "—" upstream when null,
  // so a value is meaningful only when it is a non-empty, non-"—" string.
  const cell = (value: unknown): string => {
    const text = value == null ? "" : String(value).trim();
    return text && text !== "—" ? text : "—";
  };

  return (
    <>
      <p className="section-title text-teal-300">Device Detail</p>
      <div className="mt-3 flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 className="text-2xl font-bold text-white">{device.deviceName}</h2>
          <p className="mt-1 text-sm text-slate-400">{device.deviceCategory} · {device.deviceType} · {device.provider} · {device.serialNumber || device.identifier}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <StatusBadge status={device.connectionStatus} />
          <StatusBadge status={device.lifecycleStatus} />
          <RiskBadge risk={device.signalStrength} />
          <StatusBadge status={device.installStatus} />
        </div>
      </div>

      <div className="mt-2 flex flex-wrap gap-2">
        {actionContracts.filter((contract) => contract.visible).map((contract) => (
          <ActionContractBadge key={`contract-${contract.key}`} contract={contract} />
        ))}
      </div>
      <div className="mt-6 flex flex-wrap gap-3">
        {actionContracts.filter((contract) => contract.visible).map((contract) => (
          <ActionButton
            key={contract.key}
            label={contract.label}
            icon={contract.icon}
            allowed={contract.state === "ready"}
            onClick={() => contract.onClick()}
            unavailableReason={contract.reason}
            contractState={contract.state}
          />
        ))}
      </div>
      {lifecycleError ? (
        <div role="alert" className="mt-4 flex items-start justify-between gap-4 rounded-xl border border-red-300/30 bg-red-500/10 p-4 text-sm text-red-100">
          <span>
            <strong className="block">{lifecycleFailureHeading(lifecycleError)}</strong>
            {lifecycleError instanceof Error ? lifecycleError.message : "The installation change was not completed. Reload the device and try again."}
          </span>
          <button className="icon-btn" aria-label="Dismiss lifecycle error" onClick={onDismissLifecycleError}><X className="h-4 w-4" /></button>
        </div>
      ) : null}

      <div className="mt-6 grid gap-4 lg:grid-cols-3">
        <InfoBlock title="Overview" items={[
          ["Device name", device.deviceName],
          ["Hardware category", device.deviceCategory],
          ["Type", device.deviceType],
          ["Provider", device.provider],
          ["Serial", device.serialNumber],
          ["IMEI", device.imei],
          ["Tenant", device.tenantName],
          ["Lifecycle", device.lifecycleStatus],
          ["Device state", device.deviceState || "Unavailable"],
          ["Archived at", device.archivedAt ? new Date(device.archivedAt).toLocaleString() : "Not archived"],
        ]} />
        <InfoBlock title="Assignment" items={[
          ["Vehicle", device.assignedVehicleCode || "Unassigned"],
          ["Driver", device.assignedDriverName || "Unassigned"],
          ["Shipment", device.linkedShipmentId || "No active shipment"],
          ["Vehicle status", device.linkedVehicleStatus],
          ["Vehicle location", device.linkedVehicleLocation],
          ["Assignment active", boolText(Boolean(device.assignedVehicleCode))],
        ]} />
        <InfoBlock title="Readiness" items={[
          ["Connection", device.connectionStatus],
          ["Power", device.powerStatus],
          ["Signal", device.signalStrength],
          ["Data health", device.dataHealthAvailable ? `${device.dataHealthScore}%` : "Unknown — no health evidence yet"],
          ["Install status", device.installStatus],
          ["Compliance", device.complianceSummary],
        ]} />
      </div>

      <div className="mt-6">
        <PanelSection title="Provider / Integration Audit">
          {detail.providers.length ? (
            <div className="space-y-3">
              {detail.providers.map((provider) => {
                const connectedTo = Array.isArray(provider.connectedTo) && provider.connectedTo.length ? provider.connectedTo.join(", ") : "—";
                const matchConfidence = provider.matchConfidence ?? "none";
                return (
                  <div key={String(provider.id)} className="rounded-xl border border-white/[0.07] bg-black/10 p-3">
                    <MiniGrid rows={[
                      ["Connector", String(provider.name)],
                      ["Integration status", String(provider.integrationStatus)],
                      ["Match confidence", String(matchConfidence)],
                      ["Device match", provider.isMatchedToDevice ? "Matched to this device" : "No direct match"],
                      ["Scoped devices", measuredCount(provider.deviceCount)],
                      ["Connected devices", connectedTo],
                      ["Data visibility", String(provider.visibilitySource ?? "unmatched")],
                      ["Needs follow-up", measuredCount(provider.pendingDevices)],
                    ]} />
                    <p className="mt-3 rounded-lg border border-white/[0.08] bg-black/10 p-2 text-xs text-slate-300">
                      {String(provider.auditMessage || "No integration audit note available.")}
                    </p>
                  </div>
                );
              })}
            </div>
          ) : (
            <p className="text-sm text-slate-400">No provider integration evidence is currently available for this device.</p>
          )}
        </PanelSection>
      </div>

      <div className="mt-6 grid gap-4 xl:grid-cols-2">
        <PanelSection title="Latest Telemetry Summary">
          {latestTelemetry ? (
            <MiniGrid rows={[
              [
                "Last GPS point",
                cell(latestTelemetry.latitude) === "—" || cell(latestTelemetry.longitude) === "—"
                  ? "—"
                  : `${latestTelemetry.latitude}, ${latestTelemetry.longitude}`,
              ],
              ["Speed", cell(latestTelemetry.speedMph)],
              ["Heading", cell(latestTelemetry.heading)],
              ["Last check-in", cell(device.lastCheckIn)],
            ]} />
          ) : (
            <p className="text-sm text-slate-400">No telemetry received yet. This device has no live position snapshot.</p>
          )}
        </PanelSection>
        <PanelSection title="Engine / OBD / J1939">
          {latestTelemetry ? (
            <MiniGrid rows={[
              ["Engine status", cell(latestTelemetry.engineStatus)],
              ["Odometer", cell(latestTelemetry.odometer)],
              ["Fuel level", cell(latestTelemetry.fuelLevel)],
              ["Geofence", cell(latestTelemetry.geofenceStatus)],
            ]} />
          ) : (
            <p className="text-sm text-slate-400">No engine or OBD/J1939 data received from this device.</p>
          )}
        </PanelSection>
        <PanelSection title="Latest Sensor Readings">
          {latestSensor ? (
            <MiniGrid rows={[
              ["Temperature", cell(latestSensor.temperature)],
              ["Humidity", cell(latestSensor.humidity)],
              ["Door status", cell(latestSensor.doorStatus)],
              [
                "Tire / fuel",
                cell(latestSensor.tirePressure) === "—" && cell(latestSensor.fuelLevel) === "—"
                  ? "—"
                  : `${cell(latestSensor.tirePressure)} · ${cell(latestSensor.fuelLevel)}`,
              ],
            ]} />
          ) : (
            <p className="text-sm text-slate-400">No sensor channels reporting for this device.</p>
          )}
        </PanelSection>
        <PanelSection title="Diagnostics">
          {latestDiagnostic ? (
            <MiniGrid rows={[
              ["Latest result", cell(latestDiagnostic.result)],
              ["Fault code", cell(latestDiagnostic.faultCode)],
              ["Battery voltage", cell(latestDiagnostic.batteryVoltage)],
              ["Modem status", cell(latestDiagnostic.modemStatus)],
              ["GNSS status", cell(latestDiagnostic.gnssStatus)],
            ]} />
          ) : (
            <p className="text-sm text-slate-400">No active fault codes for this device.</p>
          )}
        </PanelSection>
      </div>

      <div className="mt-6 grid gap-4 xl:grid-cols-2">
        {detail.retirementRecord ? (
          <PanelSection title="Retirement Receipt">
            <MiniGrid rows={[
              ["Receipt", detail.retirementRecord.id],
              ["Effective", new Date(detail.retirementRecord.effectiveAt).toLocaleString()],
              ["Reason", detail.retirementRecord.retirementReason],
              ["Disposition plan", detail.retirementRecord.dispositionPlan.replace(/([a-z])([A-Z])/g, "$1 $2")],
              ["Source", detail.retirementRecord.sourceReference],
              ["Credentials", detail.retirementRecord.credentialsRevoked ? "Revoked" : "Unverified"],
              ["Physical disposition", detail.retirementRecord.physicalDispositionStatus],
              ["Certification", detail.retirementRecord.certificationClaim ? "Claimed" : "Not claimed"],
            ]} />
            <p className="mt-3 text-xs text-amber-200">The disposition is an operator plan. This receipt does not prove return, recycling, storage, destruction, or hardware certification.</p>
          </PanelSection>
        ) : null}
        <PanelSection title="Device Lifecycle History">
          <TimelineList rows={detail.lifecycleHistory.map((row) => ({
            id: row.id,
            title: `${row.fromState ?? "Initial"} → ${row.toState}`,
            subtitle: row.reason || row.reasonCode,
            meta: row.occurredAt,
          }))} emptyText="No lifecycle history is available for this device." />
        </PanelSection>
        <PanelSection title="Health Timeline">
          {/* Live: real telemetry alerts for this device (empty when none exist). */}
          <TimelineList rows={detail.healthEvents.map((row) => ({
            title: cell(row.status),
            subtitle: `${cell(row.summary)} · ${cell(row.score)}%`,
            meta: cell(row.eventAt) === "—" ? "" : String(row.eventAt),
          }))} emptyText="No health events or alerts recorded." />
        </PanelSection>
        <PanelSection title="Connectivity History">
          {/* Derived from the single live position snapshot (one point, or none). */}
          <TimelineList rows={detail.telemetry.map((row) => ({
            title: cell(row.geofenceStatus) === "—" ? "Connectivity event" : String(row.geofenceStatus),
            subtitle: `${cell(row.speedMph)} mph · ${cell(row.engineStatus)}`,
            meta: cell(row.eventAt) === "—" ? "" : String(row.eventAt),
          }))} emptyText="No connectivity history recorded." />
        </PanelSection>
        <PanelSection title="Audit / Activity Log">
          {/* No device audit-log endpoint exists — always empty until one is added. */}
          <TimelineList rows={detail.auditLog.map((row) => ({
            title: cell(row.action) === "—" ? "Activity" : String(row.action),
            subtitle: cell(row.notes) === "—" ? "" : String(row.notes),
            meta: cell(row.eventAt) === "—" ? "" : String(row.eventAt),
          }))} emptyText="No device activity recorded." />
        </PanelSection>
      </div>

      <div className="mt-6 grid gap-4 xl:grid-cols-2">
        <PanelSection title="SIM / eSIM inventory">
          <div className="flex items-start justify-between gap-4">
            <div>
              <p className="text-sm font-semibold text-white">
                {detail.currentConnectivityProfile
                  ? `${detail.currentConnectivityProfile.profileKind} · ${detail.currentConnectivityProfile.carrierName}`
                  : "No current connectivity profile"}
              </p>
              <p className="mt-1 text-xs text-slate-400">Inventory assignment only. Network attachment and live telemetry require separate observed evidence.</p>
            </div>
            {canManageConnectivity ? (
              <button type="button" className="btn-secondary shrink-0" onClick={openConnectivityForm} disabled={connectivityMut.isPending}>
                {detail.currentConnectivityProfile ? "Change profile" : "Record profile"}
              </button>
            ) : null}
          </div>
          {connectivityNotice ? <p role="status" className="mt-3 rounded-lg border border-emerald-300/30 bg-emerald-500/10 p-3 text-sm text-emerald-100">{connectivityNotice}</p> : null}
          {detail.currentConnectivityProfile ? (
            <div className="mt-4">
              <MiniGrid rows={[
                ["ICCID", `•••• ${detail.currentConnectivityProfile.iccidLast4}`],
                ["MSISDN", detail.currentConnectivityProfile.msisdnLast4 ? `•••• ${detail.currentConnectivityProfile.msisdnLast4}` : "—"],
                ["APN", detail.currentConnectivityProfile.apnConfigured ? "Configured · protected" : "Not recorded"],
                ["Effective", cell(detail.currentConnectivityProfile.effectiveFrom)],
                ["Source", cell(detail.currentConnectivityProfile.sourceReference)],
                ["Assignment", detail.currentConnectivityProfile.assignmentStatus],
              ]} />
            </div>
          ) : (
            <p className="mt-4 text-sm text-slate-400">No operator-recorded SIM or eSIM assignment exists for this device.</p>
          )}
          <div className="mt-4 rounded-xl border border-white/[0.08] bg-black/10 p-4">
            <p className="text-xs font-bold uppercase tracking-[0.14em] text-slate-400">Latest carrier/provider observation</p>
            {latestConnectivityObservation?.softwareObservationAvailable ? (
              <>
                <div className="mt-3">
                  <MiniGrid rows={[
                    ["Source", latestConnectivityObservation.sourceProvider],
                    ["Source authentication", latestConnectivityObservation.sourceAuthenticationStatus],
                    ["ICCID", `•••• ${latestConnectivityObservation.profileIccidLast4}`],
                    ["Reported subscription", latestConnectivityObservation.subscriptionStatus],
                    ["Reported network", latestConnectivityObservation.networkRegistrationStatus],
                    ["Reported data session", latestConnectivityObservation.dataSessionStatus],
                    ["Reported roaming", latestConnectivityObservation.roaming == null ? "Unknown" : latestConnectivityObservation.roaming ? "Yes" : "No"],
                    ["Reported usage", latestConnectivityObservation.usageBytes == null ? "Unknown" : `${latestConnectivityObservation.usageBytes.toLocaleString()} bytes`],
                    ["Observed", new Date(latestConnectivityObservation.observedAt).toLocaleString()],
                  ]} />
                </div>
                <p className="mt-3 text-xs text-amber-200">Provider-reported software status only. It does not prove radio attachment, telemetry delivery, physical operation, or certification.</p>
              </>
            ) : (
              <p className="mt-3 text-sm text-slate-400">No exact-profile observation from an authenticated carrier/provider adapter is available.</p>
            )}
          </div>
          {connectivityOpen && canManageConnectivity ? (
            <form className="mt-4 space-y-3 rounded-xl border border-white/[0.08] bg-black/10 p-4" onSubmit={submitConnectivityProfile} autoComplete="off">
              <p className="text-xs text-slate-400">ICCID, MSISDN and APN are encrypted and will only be shown here as masked/configured values after submission.</p>
              <div className="grid gap-3 md:grid-cols-2">
                <label className="field-label">Profile type
                  <select className="field mt-1 w-full" value={connectivityForm.profileKind} onChange={(event) => setConnectivityForm((form) => ({ ...form, profileKind: event.target.value as DeviceConnectivityProfileInput["profileKind"] }))} disabled={connectivityMut.isPending}>
                    <option value="PhysicalSIM">Physical SIM</option>
                    <option value="eSIM">eSIM</option>
                  </select>
                </label>
                <label className="field-label">Carrier
                  <input className="field mt-1 w-full" value={connectivityForm.carrierName} onChange={(event) => setConnectivityForm((form) => ({ ...form, carrierName: event.target.value }))} minLength={2} maxLength={120} required disabled={connectivityMut.isPending} />
                </label>
                <label className="field-label">ICCID
                  <input className="field mt-1 w-full" inputMode="numeric" pattern="[0-9]{18,22}" value={connectivityForm.iccid} onChange={(event) => setConnectivityForm((form) => ({ ...form, iccid: event.target.value }))} required disabled={connectivityMut.isPending} />
                </label>
                <label className="field-label">MSISDN · optional E.164
                  <input className="field mt-1 w-full" inputMode="tel" placeholder="+14165550123" value={connectivityForm.msisdn} onChange={(event) => setConnectivityForm((form) => ({ ...form, msisdn: event.target.value }))} disabled={connectivityMut.isPending} />
                </label>
                <label className="field-label">APN · optional
                  <input className="field mt-1 w-full" value={connectivityForm.apn} onChange={(event) => setConnectivityForm((form) => ({ ...form, apn: event.target.value }))} maxLength={253} disabled={connectivityMut.isPending} />
                </label>
                <label className="field-label">Effective time
                  <input className="field mt-1 w-full" type="datetime-local" max={currentLocalMinute()} value={connectivityForm.effectiveAt} onChange={(event) => setConnectivityForm((form) => ({ ...form, effectiveAt: event.target.value }))} required disabled={connectivityMut.isPending} />
                </label>
                <label className="field-label md:col-span-2">Change reason
                  <input className="field mt-1 w-full" value={connectivityForm.changeReason} onChange={(event) => setConnectivityForm((form) => ({ ...form, changeReason: event.target.value }))} minLength={5} maxLength={500} placeholder="Initial assignment, carrier change, or SIM replacement" required disabled={connectivityMut.isPending} />
                </label>
                <label className="field-label md:col-span-2">Source reference
                  <input className="field mt-1 w-full" value={connectivityForm.sourceReference} onChange={(event) => setConnectivityForm((form) => ({ ...form, sourceReference: event.target.value }))} minLength={3} maxLength={240} placeholder="Carrier portal, purchase order, or installer record" required disabled={connectivityMut.isPending} />
                </label>
              </div>
              {connectivityError ? <p role="alert" className="text-sm text-red-300">{connectivityError}</p> : null}
              <div className="flex justify-end gap-2">
                <button type="button" className="btn-ghost" disabled={connectivityMut.isPending} onClick={() => { setConnectivityForm(newConnectivityProfileForm()); setConnectivityOpen(false); setConnectivityError(null); }}>Cancel</button>
                <button type="submit" className="btn-primary" disabled={connectivityMut.isPending}>{connectivityMut.isPending ? "Recording…" : "Record inventory change"}</button>
              </div>
            </form>
          ) : null}
          {detail.connectivityProfiles.length > 1 ? (
            <div className="mt-4">
              <p className="mb-2 text-xs font-bold uppercase tracking-[0.14em] text-slate-400">Assignment history</p>
              <TimelineList rows={detail.connectivityProfiles.map((profile) => ({
                id: profile.id,
                title: `${profile.profileKind} · ${profile.carrierName} · •••• ${profile.iccidLast4}`,
                subtitle: `${profile.assignmentStatus} · ${profile.sourceReference}${profile.endReason ? ` · ${profile.endReason}` : ""}`,
                meta: `${profile.effectiveFrom}${profile.effectiveTo ? ` → ${profile.effectiveTo}` : " → current"}`,
              }))} emptyText="No connectivity profile history recorded." />
            </div>
          ) : null}
        </PanelSection>
        <PanelSection title="Hardware compatibility truth">
          <div className="flex items-center justify-between gap-3">
            <div>
              <p className="text-xs font-bold uppercase tracking-[0.16em] text-amber-300">Certification status</p>
              <p className="mt-1 text-lg font-semibold text-white">External hold</p>
            </div>
            <StatusBadge status={detail.compatibility.registryStatus} />
          </div>
          <div className="mt-4">
            <MiniGrid rows={[
              ["Manufacturer", cell(detail.compatibility.manufacturer)],
              ["Exact model", cell(detail.compatibility.deviceModel)],
              ["Hardware revision", cell(detail.compatibility.hardwareRevision)],
              ["Reported firmware", cell(detail.compatibility.firmwareVersion)],
              ["Frozen software candidate", detail.compatibility.candidateSha ? detail.compatibility.candidateSha.slice(0, 12) : "—"],
              ["Maximum certified tier", detail.compatibility.maximumTier],
              ["Capability declaration", detail.compatibility.capabilityDeclarationStatus === "EngineeringDeclaredUnverified" ? "Engineering-declared / unverified" : "Not recorded"],
              ["Protocols", detail.compatibility.protocols.length > 0 ? detail.compatibility.protocols.join(", ") : "Not recorded"],
              ["Supported fields", detail.compatibility.supportedFields.length > 0 ? detail.compatibility.supportedFields.join(", ") : "Not recorded"],
              ["Supported events", detail.compatibility.supportedEvents.length > 0 ? detail.compatibility.supportedEvents.join(", ") : "Not recorded"],
              ["Supported commands", detail.compatibility.supportedCommands.length > 0 ? detail.compatibility.supportedCommands.join(", ") : "None declared"],
              ["Catalog support tier", detail.compatibility.catalogSupportTier],
              ["Certification reference", detail.compatibility.certificationReference ?? "Not issued"],
              ["Certification date", detail.compatibility.certificationDate ?? "Not issued"],
            ]} />
          </div>
          <p className="mt-3 text-sm text-slate-200"><span className="font-semibold text-white">Known limitations:</span> {detail.compatibility.knownLimitations}</p>
          {detail.compatibility.declarationSourceReference ? (
            <p className="mt-2 text-xs text-slate-400">Declaration source: {detail.compatibility.declarationSourceReference}{detail.compatibility.declaredAt ? ` · ${detail.compatibility.declaredAt}` : ""}</p>
          ) : null}
          <p className="mt-3 text-sm text-amber-100">{detail.compatibility.externalHoldReason}</p>
          {detail.compatibility.missingIdentityFields.length > 0 ? (
            <p className="mt-2 text-xs text-slate-400">Missing exact identity: {detail.compatibility.missingIdentityFields.join(", ")}.</p>
          ) : null}
          <p className="mt-2 text-xs text-slate-400">Engineering-declared capabilities describe intended software behavior only. Registration, installation, commissioning, or live data never certifies hardware. Physical bench, route, recovery, soak, security, provider, and independent acceptance evidence is still required.</p>
        </PanelSection>
        <PanelSection title="Firmware campaign planning">
          <div className="flex items-start justify-between gap-4">
            <div>
              <p className="text-xs font-bold uppercase tracking-[0.16em] text-slate-400">Current reported version</p>
              <p className="mt-1 text-lg font-semibold text-white">{cell(device.firmwareVersion)}</p>
            </div>
            {canPlanFirmware ? (
              <button type="button" className="btn-secondary" onClick={openFirmwareForm} disabled={firmwareMut.isPending}>
                Plan campaign
              </button>
            ) : null}
          </div>
          <p className="mt-3 rounded-xl border border-amber-300/25 bg-amber-500/10 p-3 text-sm text-amber-100">
            Planning record only. OpsTrax does not dispatch an OTA command from this workflow. Provider capability and physical upgrade, recovery, rollback, and soak evidence remain on external hold.
          </p>
          {firmwareNotice ? <p role="status" className="mt-3 rounded-lg border border-emerald-400/25 bg-emerald-500/10 p-3 text-sm text-emerald-100">{firmwareNotice}</p> : null}
          {firmwareOpen && canPlanFirmware ? (
            <form className="mt-4 space-y-3 rounded-xl border border-white/[0.08] bg-black/10 p-4" onSubmit={submitFirmwareCampaign}>
              <div className="grid gap-3 md:grid-cols-2">
                <FormField label="Campaign name"><input className="field w-full" required minLength={3} maxLength={160} value={firmwareForm.campaignName} onChange={(event) => setFirmwareForm((form) => ({ ...form, campaignName: event.target.value }))} disabled={firmwareMut.isPending} /></FormField>
                <FormField label="Rollout strategy">
                  <select className="field w-full" value={firmwareForm.rolloutStrategy} onChange={(event) => setFirmwareForm((form) => ({ ...form, rolloutStrategy: event.target.value as DeviceFirmwareCampaignInput["rolloutStrategy"] }))} disabled={firmwareMut.isPending}>
                    <option value="Canary">Canary</option><option value="Staged">Staged</option><option value="Manual">Manual</option>
                  </select>
                </FormField>
                <FormField label="Target firmware"><input className="field w-full" required maxLength={120} placeholder="e.g. v2.4.1" value={firmwareForm.targetFirmwareVersion} onChange={(event) => setFirmwareForm((form) => ({ ...form, targetFirmwareVersion: event.target.value }))} disabled={firmwareMut.isPending} /></FormField>
                <FormField label="Rollback version (optional)"><input className="field w-full" maxLength={120} value={firmwareForm.rollbackFirmwareVersion} onChange={(event) => setFirmwareForm((form) => ({ ...form, rollbackFirmwareVersion: event.target.value }))} disabled={firmwareMut.isPending} /></FormField>
                <FormField label="Planned window start"><input className="field w-full" type="datetime-local" required value={firmwareForm.scheduledFor} onChange={(event) => setFirmwareForm((form) => ({ ...form, scheduledFor: event.target.value }))} disabled={firmwareMut.isPending} /></FormField>
                <FormField label="Window minutes"><input className="field w-full" type="number" min={15} max={720} required value={firmwareForm.maintenanceWindowMinutes} onChange={(event) => setFirmwareForm((form) => ({ ...form, maintenanceWindowMinutes: event.target.value }))} disabled={firmwareMut.isPending} /></FormField>
                <FormField label="Source reference"><input className="field w-full" required minLength={3} maxLength={240} placeholder="Change ticket or vendor release" value={firmwareForm.sourceReference} onChange={(event) => setFirmwareForm((form) => ({ ...form, sourceReference: event.target.value }))} disabled={firmwareMut.isPending} /></FormField>
                <FormField label="Planning reason"><input className="field w-full" required minLength={5} maxLength={500} value={firmwareForm.changeReason} onChange={(event) => setFirmwareForm((form) => ({ ...form, changeReason: event.target.value }))} disabled={firmwareMut.isPending} /></FormField>
              </div>
              {firmwareError ? <p role="alert" className="text-sm text-red-300">{firmwareError}</p> : null}
              <div className="flex justify-end gap-2">
                <button type="button" className="btn-ghost" disabled={firmwareMut.isPending} onClick={() => { setFirmwareForm(newFirmwareCampaignForm()); setFirmwareOpen(false); setFirmwareError(null); }}>Cancel</button>
                <button type="submit" className="btn-primary" disabled={firmwareMut.isPending}>{firmwareMut.isPending ? "Recording…" : "Record firmware plan"}</button>
              </div>
            </form>
          ) : null}
          <div className="mt-4 space-y-3">
            {detail.firmwareCampaigns.length === 0 ? <p className="text-sm text-slate-400">No firmware campaign plans recorded for this device.</p> : detail.firmwareCampaigns.map((plan) => (
              <div key={`${plan.campaignId}-${plan.targetId}`} className="rounded-xl border border-white/[0.08] bg-white/[0.03] p-4">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div><p className="font-semibold text-white">{plan.campaignName}</p><p className="mt-1 text-xs text-slate-400">Target {cell(plan.targetFirmwareVersion)} · Batch {plan.rolloutBatch || "—"} · {plan.rolloutStrategy}</p></div>
                  <StatusBadge status={plan.deliveryStatus} />
                </div>
                <MiniGrid rows={[
                  ["Reported at planning", cell(plan.reportedFirmwareVersion)],
                  ["Target", cell(plan.targetFirmwareVersion)],
                  ["Rollback", cell(plan.rollbackFirmwareVersion)],
                  ["Planning eligibility", plan.planningStatus],
                  ["Provider capability", plan.providerCapabilityStatus],
                  ["Scheduled", cell(plan.scheduledFor)],
                  ["Window", plan.maintenanceWindowMinutes ? `${plan.maintenanceWindowMinutes} minutes` : "—"],
                  ["Source", cell(plan.sourceReference)],
                ]} />
                <p className="mt-3 text-sm text-slate-300">{plan.planningReason}</p>
                <p className="mt-2 text-xs text-amber-200">{plan.externalHoldReason}</p>
              </div>
            ))}
          </div>
        </PanelSection>
        <PanelSection title="Capability-governed remote commands">
          <p className="rounded-xl border border-amber-300/25 bg-amber-500/10 p-3 text-sm text-amber-100">
            A command can be recorded only when current provider or device evidence verifies that exact command for this exact hardware, firmware, provider, and serial. Recording does not prove provider dispatch, device acknowledgement, application, or any physical outcome.
          </p>
          {remoteCommandNotice ? <p role="status" className="mt-3 rounded-lg border border-emerald-400/25 bg-emerald-500/10 p-3 text-sm text-emerald-100">{remoteCommandNotice}</p> : null}
          {remoteCommandError ? <p role="alert" className="mt-3 text-sm text-red-300">{remoteCommandError}</p> : null}
          <div className="mt-4 grid gap-3 lg:grid-cols-3">
            {detail.remoteCommandCapabilities.map(capability => (
              <div key={capability.commandType} className="rounded-xl border border-white/[0.08] bg-white/[0.03] p-4">
                <div className="flex items-start justify-between gap-2"><div><p className="font-semibold text-white">{capability.displayName}</p><p className="mt-1 text-xs text-slate-400">{capability.commandClass}</p></div><StatusBadge status={capability.requestAdmissionAvailable ? "Evidence verified" : "External hold"} /></div>
                <MiniGrid rows={[["Capability", capability.capabilityStatus], ["Evidence", cell(capability.evidenceReference)], ["Observed", cell(capability.observedAt)], ["Expires", cell(capability.expiresAt)]]} />
                {capability.externalHoldReason ? <p className="mt-2 text-xs text-amber-200">{capability.externalHoldReason}</p> : null}
                {canRequestRemoteCommand ? <button type="button" className="btn-ghost mt-3" disabled={!capability.requestAdmissionAvailable || remoteCommandMut.isPending} title={!capability.requestAdmissionAvailable ? capability.externalHoldReason ?? "Verified capability evidence is required." : `Request ${capability.displayName}`} onClick={() => { setRemoteCommandForm(newRemoteCommandForm(capability.commandType)); setRemoteCommandError(null); setRemoteCommandNotice(null); }}>Request command</button> : null}
              </div>
            ))}
          </div>
          {remoteCommandForm && selectedRemoteCommandCapability?.requestAdmissionAvailable && canRequestRemoteCommand ? (
            <form className="mt-4 space-y-3 rounded-xl border border-white/[0.08] bg-black/10 p-4" onSubmit={submitRemoteCommand}>
              <div><p className="font-semibold text-white">{selectedRemoteCommandCapability.displayName}</p><p className="mt-1 text-xs text-slate-400">Type the exact safety phrase shown below. The phrase is hashed before storage.</p></div>
              {remoteCommandForm.commandType === "RestartDevice" ? <FormField label="Delay seconds"><input className="field w-full" type="number" min={0} max={300} required value={remoteCommandForm.delaySeconds} onChange={event => setRemoteCommandForm(form => form ? { ...form, delaySeconds: event.target.value } : form)} disabled={remoteCommandMut.isPending} /></FormField> : null}
              <FormField label="Operational purpose"><textarea className="field h-20 w-full resize-none" required minLength={10} maxLength={500} value={remoteCommandForm.purpose} onChange={event => setRemoteCommandForm(form => form ? { ...form, purpose: event.target.value } : form)} disabled={remoteCommandMut.isPending} /></FormField>
              <FormField label="Source reference"><input className="field w-full" required minLength={3} maxLength={240} placeholder="Approved ticket or work order" value={remoteCommandForm.sourceReference} onChange={event => setRemoteCommandForm(form => form ? { ...form, sourceReference: event.target.value } : form)} disabled={remoteCommandMut.isPending} /></FormField>
              <FormField label={`Safety confirmation — ${selectedRemoteCommandCapability.confirmationText}`}><input className="field w-full font-mono" required autoComplete="off" value={remoteCommandForm.safetyConfirmation} onChange={event => setRemoteCommandForm(form => form ? { ...form, safetyConfirmation: event.target.value } : form)} disabled={remoteCommandMut.isPending} /></FormField>
              <div className="flex justify-end gap-2"><button type="button" className="btn-ghost" disabled={remoteCommandMut.isPending} onClick={() => { setRemoteCommandForm(null); setRemoteCommandError(null); }}>Cancel</button><button type="submit" className="btn-primary" disabled={remoteCommandMut.isPending}>{remoteCommandMut.isPending ? "Recording…" : "Record command request"}</button></div>
            </form>
          ) : null}
          <div className="mt-4 space-y-3">
            {detail.remoteCommandHistory.length === 0 ? <p className="text-sm text-slate-400">No governed remote-command requests recorded for this device.</p> : detail.remoteCommandHistory.map(command => (
              <div key={command.id} className="rounded-xl border border-white/[0.08] bg-white/[0.03] p-4">
                <div className="flex items-start justify-between gap-3"><div><p className="font-semibold text-white">{command.commandType}</p><p className="mt-1 text-sm text-slate-300">{command.purpose || "Legacy command purpose unavailable"}</p></div><StatusBadge status={command.status} /></div>
                <MiniGrid rows={[["Governance", command.governanceStatus], ["Source", cell(command.sourceReference)], ["Recorded", cell(command.createdAt)], ["Dispatched", cell(command.dispatchedAt)], ["Acknowledged", cell(command.acknowledgedAt)], ["Applied", cell(command.appliedAt)]]} />
                <p className="mt-2 text-xs text-slate-400">Provider delivery claim: No · Physical outcome claim: No</p>
              </div>
            ))}
          </div>
        </PanelSection>
        <PanelSection title="Device support tier">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div>
              <p className="text-xs font-bold uppercase tracking-[0.16em] text-slate-400">Operator-recorded routing plan</p>
              <p className="mt-1 text-lg font-semibold text-white">{activeSupportTier?.tierCode ?? "Not assigned"}</p>
            </div>
            {canManageRma && !supportTierForm ? (
              <div className="flex flex-wrap gap-2">
                {!activeSupportTier ? <button type="button" className="btn-secondary" disabled={supportTierMut.isPending} onClick={() => { setSupportTierForm(newSupportTierForm("Assign")); setSupportTierError(null); setSupportTierNotice(null); }}>Assign tier</button> : null}
                {activeSupportTier ? <><button type="button" className="btn-secondary" disabled={supportTierMut.isPending} onClick={() => { setSupportTierForm(newSupportTierForm("Change", activeSupportTier)); setSupportTierError(null); setSupportTierNotice(null); }}>Change tier</button><button type="button" className="btn-ghost" disabled={supportTierMut.isPending} onClick={() => { setSupportTierForm(newSupportTierForm("End", activeSupportTier)); setSupportTierError(null); setSupportTierNotice(null); }}>End coverage</button></> : null}
              </div>
            ) : null}
          </div>
          <p className="mt-3 rounded-xl border border-amber-300/25 bg-amber-500/10 p-3 text-sm text-amber-100">
            This is a service-routing target recorded by an operator. It does not verify a commercial entitlement, provider support, hardware supportability, or certification.
          </p>
          {supportTierNotice ? <p role="status" className="mt-3 rounded-lg border border-emerald-400/25 bg-emerald-500/10 p-3 text-sm text-emerald-100">{supportTierNotice}</p> : null}
          {supportTierError ? <p role="alert" className="mt-3 text-sm text-red-300">{supportTierError}</p> : null}
          {activeSupportTier ? <div className="mt-4"><MiniGrid rows={[["Tier", activeSupportTier.tierCode], ["Coverage window", activeSupportTier.coverageWindow], ["Routing response target", `${activeSupportTier.routingResponseTargetMinutes} minutes`], ["Escalation policy", activeSupportTier.escalationPolicyReference], ["Commercial reference", activeSupportTier.commercialReference], ["Assurance", activeSupportTier.recordStatus]]} /></div> : null}
          {supportTierForm && canManageRma ? (
            <form className="mt-4 space-y-3 rounded-xl border border-white/[0.08] bg-black/10 p-4" onSubmit={submitSupportTier}>
              <p className="font-semibold text-white">{supportTierForm.actionType === "Assign" ? "Assign support tier" : supportTierForm.actionType === "Change" ? "Change support tier" : "End support coverage"}</p>
              {supportTierForm.actionType !== "End" ? <div className="grid gap-3 md:grid-cols-2">
                <FormField label="Tier"><select className="field w-full" value={supportTierForm.tierCode} onChange={event => setSupportTierForm(form => form ? ({ ...form, tierCode: event.target.value as DeviceSupportTierEventRecord["tierCode"] }) : form)} disabled={supportTierMut.isPending}><option value="Standard">Standard</option><option value="Priority">Priority</option><option value="CriticalOps">Critical operations</option><option value="Custom">Custom</option></select></FormField>
                <FormField label="Coverage window"><select className="field w-full" value={supportTierForm.coverageWindow} onChange={event => setSupportTierForm(form => form ? ({ ...form, coverageWindow: event.target.value as DeviceSupportTierEventRecord["coverageWindow"] }) : form)} disabled={supportTierMut.isPending}><option value="BusinessHours">Business hours</option><option value="ExtendedHours">Extended hours</option><option value="AlwaysOn">Always on</option><option value="Custom">Custom</option></select></FormField>
                <FormField label="Routing response target (minutes)"><input className="field w-full" type="number" min={15} max={10080} required value={supportTierForm.routingResponseTargetMinutes} onChange={event => setSupportTierForm(form => form ? ({ ...form, routingResponseTargetMinutes: Number(event.target.value) }) : form)} disabled={supportTierMut.isPending} /></FormField>
                <FormField label="Escalation policy reference"><input className="field w-full" required minLength={3} maxLength={240} value={supportTierForm.escalationPolicyReference} onChange={event => setSupportTierForm(form => form ? ({ ...form, escalationPolicyReference: event.target.value }) : form)} disabled={supportTierMut.isPending} /></FormField>
                <FormField label="Commercial reference"><input className="field w-full" required minLength={3} maxLength={240} placeholder="Contract, order, or approved plan reference" value={supportTierForm.commercialReference} onChange={event => setSupportTierForm(form => form ? ({ ...form, commercialReference: event.target.value }) : form)} disabled={supportTierMut.isPending} /></FormField>
                <FormField label="Effective at"><input className="field w-full" type="datetime-local" required max={currentLocalMinute()} value={supportTierForm.effectiveAt} onChange={event => setSupportTierForm(form => form ? ({ ...form, effectiveAt: event.target.value }) : form)} disabled={supportTierMut.isPending} /></FormField>
              </div> : <FormField label="Effective at"><input className="field w-full" type="datetime-local" required max={currentLocalMinute()} value={supportTierForm.effectiveAt} onChange={event => setSupportTierForm(form => form ? ({ ...form, effectiveAt: event.target.value }) : form)} disabled={supportTierMut.isPending} /></FormField>}
              <FormField label="Source reference"><input className="field w-full" required minLength={3} maxLength={240} value={supportTierForm.sourceReference} onChange={event => setSupportTierForm(form => form ? ({ ...form, sourceReference: event.target.value }) : form)} disabled={supportTierMut.isPending} /></FormField>
              <FormField label="Action reason"><textarea className="field h-20 w-full resize-none" required minLength={5} maxLength={500} value={supportTierForm.actionReason} onChange={event => setSupportTierForm(form => form ? ({ ...form, actionReason: event.target.value }) : form)} disabled={supportTierMut.isPending} /></FormField>
              <div className="flex justify-end gap-2"><button type="button" className="btn-ghost" disabled={supportTierMut.isPending} onClick={() => setSupportTierForm(null)}>Cancel</button><button type="submit" className="btn-primary" disabled={supportTierMut.isPending}>{supportTierMut.isPending ? "Recording…" : "Record support-tier action"}</button></div>
            </form>
          ) : null}
          {detail.supportTierEvents.length > 0 ? <div className="mt-4"><TimelineList rows={detail.supportTierEvents.map(item => ({ id: `support-tier-${item.id}`, title: `${item.actionType} · ${item.tierCode}`, subtitle: `${item.coverageWindow} · ${item.routingResponseTargetMinutes} min target · ${item.actionReason} · ${item.recordStatus}`, meta: item.effectiveAt }))} emptyText="No support-tier history recorded." /></div> : null}
        </PanelSection>
        <PanelSection title="RMA, custody & replacement">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div>
              <p className="text-xs font-bold uppercase tracking-[0.16em] text-slate-400">Support record</p>
              <p className="mt-1 text-lg font-semibold text-white">{detail.rmaCases.filter(rmaCase => rmaCase.currentStatus !== "Resolved").length} open case(s)</p>
            </div>
            {canManageRma ? <button type="button" className="btn-secondary" disabled={rmaBusy} onClick={() => { setRmaCaseForm(newRmaCaseForm()); setRmaCaseOpen(true); setRmaError(null); setRmaNotice(null); }}>Open RMA case</button> : null}
          </div>
          <p className="mt-3 rounded-xl border border-amber-300/25 bg-amber-500/10 p-3 text-sm text-amber-100">
            OpsTrax records the operator-supplied support, custody, warranty, and replacement references. It does not verify physical receipt, vendor warranty acceptance, replacement installation, or device readiness.
          </p>
          {rmaNotice ? <p role="status" className="mt-3 rounded-lg border border-emerald-400/25 bg-emerald-500/10 p-3 text-sm text-emerald-100">{rmaNotice}</p> : null}
          {rmaError ? <p role="alert" className="mt-3 text-sm text-red-300">{rmaError}</p> : null}
          {rmaCaseOpen && canManageRma ? (
            <form className="mt-4 space-y-3 rounded-xl border border-white/[0.08] bg-black/10 p-4" onSubmit={submitRmaCase}>
              <p className="font-semibold text-white">New support case</p>
              <div className="grid gap-3 md:grid-cols-2">
                <FormField label="Severity"><select className="field w-full" value={rmaCaseForm.severity} onChange={event => setRmaCaseForm(form => ({ ...form, severity: event.target.value as DeviceRmaCaseInput["severity"] }))} disabled={rmaBusy}><option>P0</option><option>P1</option><option>P2</option><option>P3</option></select></FormField>
                <FormField label="Failure category"><select className="field w-full" value={rmaCaseForm.failureCategory} onChange={event => setRmaCaseForm(form => ({ ...form, failureCategory: event.target.value as DeviceRmaCaseInput["failureCategory"] }))} disabled={rmaBusy}>{["Power","Connectivity","GNSS","CAN","Camera","Firmware","PhysicalDamage","Intermittent","Other"].map(value => <option key={value}>{value}</option>)}</select></FormField>
                <FormField label="Observed at"><input className="field w-full" type="datetime-local" required value={rmaCaseForm.observedAt} onChange={event => setRmaCaseForm(form => ({ ...form, observedAt: event.target.value }))} disabled={rmaBusy} /></FormField>
                <FormField label="Response due"><input className="field w-full" type="datetime-local" required value={rmaCaseForm.responseDueAt} onChange={event => setRmaCaseForm(form => ({ ...form, responseDueAt: event.target.value }))} disabled={rmaBusy} /></FormField>
                <FormField label="Warranty posture"><select className="field w-full" value={rmaCaseForm.warrantyPosture} onChange={event => setRmaCaseForm(form => ({ ...form, warrantyPosture: event.target.value as DeviceRmaCaseInput["warrantyPosture"] }))} disabled={rmaBusy}><option value="Unknown">Unknown</option><option value="ClaimedInWarranty">Claimed in warranty</option><option value="ClaimedOutOfWarranty">Claimed out of warranty</option><option value="NotApplicable">Not applicable</option></select></FormField>
                <FormField label="Warranty reference"><input className="field w-full" maxLength={240} required={rmaCaseForm.warrantyPosture === "ClaimedInWarranty" || rmaCaseForm.warrantyPosture === "ClaimedOutOfWarranty"} value={rmaCaseForm.warrantyReference} onChange={event => setRmaCaseForm(form => ({ ...form, warrantyReference: event.target.value }))} placeholder="Vendor policy or claim reference" disabled={rmaBusy} /></FormField>
                <FormField label="Support SLA reference"><input className="field w-full" required minLength={3} maxLength={240} value={rmaCaseForm.supportSlaReference} onChange={event => setRmaCaseForm(form => ({ ...form, supportSlaReference: event.target.value }))} placeholder="Contract / support tier" disabled={rmaBusy} /></FormField>
                <FormField label="Source reference"><input className="field w-full" required minLength={3} maxLength={240} value={rmaCaseForm.sourceReference} onChange={event => setRmaCaseForm(form => ({ ...form, sourceReference: event.target.value }))} placeholder="Support ticket or inspection" disabled={rmaBusy} /></FormField>
              </div>
              <FormField label="Failure description"><textarea className="field h-24 w-full resize-none" required minLength={10} maxLength={1000} value={rmaCaseForm.failureDescription} onChange={event => setRmaCaseForm(form => ({ ...form, failureDescription: event.target.value }))} disabled={rmaBusy} /></FormField>
              <div className="flex justify-end gap-2"><button type="button" className="btn-ghost" disabled={rmaBusy} onClick={() => { setRmaCaseOpen(false); setRmaError(null); }}>Cancel</button><button type="submit" className="btn-primary" disabled={rmaBusy}>{rmaCaseMut.isPending ? "Recording…" : "Record RMA case"}</button></div>
            </form>
          ) : null}
          <div className="mt-4 space-y-4">
            {detail.rmaCases.length === 0 ? <p className="text-sm text-slate-400">No RMA cases recorded for this device.</p> : detail.rmaCases.map(rmaCase => (
              <div key={rmaCase.id} className="rounded-xl border border-white/[0.08] bg-white/[0.03] p-4">
                <div className="flex flex-wrap items-start justify-between gap-3"><div><p className="font-semibold text-white">RMA #{rmaCase.id} · {rmaCase.failureCategory}</p><p className="mt-1 text-sm text-slate-300">{rmaCase.failureDescription}</p></div><div className="flex gap-2"><RiskBadge risk={rmaCase.severity} /><StatusBadge status={rmaCase.currentStatus} /></div></div>
                <div className="mt-3"><MiniGrid rows={[["Observed", rmaCase.observedAt], ["Response due", rmaCase.responseDueAt], ["SLA", rmaCase.supportSlaReference], ["Current owner", rmaCase.supportActions[0]?.ownerNameSnapshot ?? "Unassigned"], ["Support queue", rmaCase.supportActions[0]?.supportQueue ?? "Unassigned"], ["Latest escalation", rmaCase.supportActions.find(action => action.actionType === "Escalated")?.escalationSeverity ?? "None"], ["Warranty posture", rmaCase.warrantyPosture], ["Warranty evidence", rmaCase.warrantyEvidenceStatus], ["Source", rmaCase.sourceReference]]} /></div>
                {rmaCase.replacement ? (
                  <p className="mt-3 rounded-lg border border-sky-400/20 bg-sky-500/10 p-3 text-sm text-sky-100">Replacement planned: {rmaCase.replacement.replacementDeviceSerial} · physical swap {rmaCase.replacement.physicalSwapStatus}. No installation is claimed.</p>
                ) : null}
                {canManageRma && rmaCase.currentStatus !== "Resolved" ? (
                  <div className="mt-3 flex flex-wrap gap-2">
                    <button type="button" className="btn-ghost" disabled={rmaBusy} onClick={() => { setRmaEventForm(newRmaEventForm()); setRmaEventCaseId(rmaCase.id); setRmaReplacementCaseId(null); setRmaSupportCaseId(null); setRmaError(null); }}>Add custody event</button>
                    {!rmaCase.replacement ? <button type="button" className="btn-ghost" disabled={rmaBusy} onClick={() => { setRmaReplacementForm(newRmaReplacementForm()); setRmaReplacementCaseId(rmaCase.id); setRmaEventCaseId(null); setRmaSupportCaseId(null); setRmaError(null); }}>Plan replacement</button> : null}
                    <button type="button" className="btn-ghost" disabled={rmaBusy} onClick={() => { setRmaSupportForm(newRmaSupportForm("TakeOwnership", rmaCase.supportActions[0]?.supportQueue)); setRmaSupportCaseId(rmaCase.id); setRmaEventCaseId(null); setRmaReplacementCaseId(null); setRmaError(null); }}>Take ownership</button>
                    {rmaCase.supportActions.length > 0 ? <button type="button" className="btn-ghost" disabled={rmaBusy} onClick={() => { setRmaSupportForm(newRmaSupportForm("Escalate", rmaCase.supportActions[0]?.supportQueue)); setRmaSupportCaseId(rmaCase.id); setRmaEventCaseId(null); setRmaReplacementCaseId(null); setRmaError(null); }}>Escalate</button> : null}
                  </div>
                ) : null}
                {rmaEventCaseId === rmaCase.id && canManageRma ? (
                  <form className="mt-4 space-y-3 rounded-lg border border-white/[0.08] bg-black/10 p-3" onSubmit={submitRmaEvent}>
                    <div className="grid gap-3 md:grid-cols-2">
                      <FormField label="Event"><select className="field w-full" value={rmaEventForm.eventType} onChange={event => setRmaEventForm(form => ({ ...form, eventType: event.target.value as DeviceRmaEventInput["eventType"] }))} disabled={rmaBusy}><option value="ReturnAuthorized">Return authorized</option><option value="Shipped">Shipped</option><option value="Received">Received</option><option value="VendorDisposition">Vendor disposition</option><option value="CaseClosed">Close case</option></select></FormField>
                      <FormField label="Occurred at"><input className="field w-full" type="datetime-local" required value={rmaEventForm.occurredAt} onChange={event => setRmaEventForm(form => ({ ...form, occurredAt: event.target.value }))} disabled={rmaBusy} /></FormField>
                      <FormField label="Custody location"><input className="field w-full" maxLength={240} required={rmaEventForm.eventType === "Shipped" || rmaEventForm.eventType === "Received"} value={rmaEventForm.custodyLocation} onChange={event => setRmaEventForm(form => ({ ...form, custodyLocation: event.target.value }))} disabled={rmaBusy} /></FormField>
                      <FormField label="Tracking reference"><input className="field w-full" maxLength={240} value={rmaEventForm.trackingReference} onChange={event => setRmaEventForm(form => ({ ...form, trackingReference: event.target.value }))} disabled={rmaBusy} /></FormField>
                      <FormField label="Evidence reference"><input className="field w-full" required minLength={3} maxLength={240} value={rmaEventForm.evidenceReference} onChange={event => setRmaEventForm(form => ({ ...form, evidenceReference: event.target.value }))} disabled={rmaBusy} /></FormField>
                      <FormField label="Notes"><input className="field w-full" required minLength={5} maxLength={1000} value={rmaEventForm.notes} onChange={event => setRmaEventForm(form => ({ ...form, notes: event.target.value }))} disabled={rmaBusy} /></FormField>
                    </div>
                    <div className="flex justify-end gap-2"><button type="button" className="btn-ghost" disabled={rmaBusy} onClick={() => setRmaEventCaseId(null)}>Cancel</button><button type="submit" className="btn-primary" disabled={rmaBusy}>{rmaEventMut.isPending ? "Recording…" : "Record referenced event"}</button></div>
                  </form>
                ) : null}
                {rmaReplacementCaseId === rmaCase.id && canManageRma ? (
                  <form className="mt-4 space-y-3 rounded-lg border border-white/[0.08] bg-black/10 p-3" onSubmit={submitRmaReplacement}>
                    <p className="text-sm text-amber-100">Enter the exact serial of an existing, visible inventory device. This links a plan only.</p>
                    <div className="grid gap-3 md:grid-cols-2">
                      <FormField label="Replacement device serial"><input className="field w-full" required minLength={3} maxLength={120} value={rmaReplacementForm.replacementDeviceSerial} onChange={event => setRmaReplacementForm(form => ({ ...form, replacementDeviceSerial: event.target.value }))} disabled={rmaBusy} /></FormField>
                      <FormField label="Source reference"><input className="field w-full" required minLength={3} maxLength={240} value={rmaReplacementForm.sourceReference} onChange={event => setRmaReplacementForm(form => ({ ...form, sourceReference: event.target.value }))} disabled={rmaBusy} /></FormField>
                    </div>
                    <FormField label="Replacement reason"><input className="field w-full" required minLength={5} maxLength={500} value={rmaReplacementForm.changeReason} onChange={event => setRmaReplacementForm(form => ({ ...form, changeReason: event.target.value }))} disabled={rmaBusy} /></FormField>
                    <div className="flex justify-end gap-2"><button type="button" className="btn-ghost" disabled={rmaBusy} onClick={() => setRmaReplacementCaseId(null)}>Cancel</button><button type="submit" className="btn-primary" disabled={rmaBusy}>{rmaReplacementMut.isPending ? "Recording…" : "Record replacement plan"}</button></div>
                  </form>
                ) : null}
                {rmaSupportCaseId === rmaCase.id && canManageRma ? (
                  <form className="mt-4 space-y-3 rounded-lg border border-white/[0.08] bg-black/10 p-3" onSubmit={submitRmaSupport}>
                    <p className="text-sm font-semibold text-white">{rmaSupportForm.actionType === "Escalate" ? "Escalate support case" : "Take support ownership"}</p>
                    <p className="text-xs text-amber-200">This records operator routing only. It does not claim a support response, warranty acceptance, or physical outcome.</p>
                    <div className="grid gap-3 md:grid-cols-2">
                      <FormField label="Support queue"><input className="field w-full" required minLength={3} maxLength={120} value={rmaSupportForm.supportQueue} onChange={event => setRmaSupportForm(form => ({ ...form, supportQueue: event.target.value }))} disabled={rmaBusy} /></FormField>
                      {rmaSupportForm.actionType === "Escalate" ? <FormField label="Escalation severity"><select className="field w-full" value={rmaSupportForm.escalationSeverity} onChange={event => setRmaSupportForm(form => ({ ...form, escalationSeverity: event.target.value as DeviceRmaSupportActionInput["escalationSeverity"] }))} disabled={rmaBusy}><option>P0</option><option>P1</option><option>P2</option><option>P3</option></select></FormField> : null}
                      <FormField label="Effective at"><input className="field w-full" type="datetime-local" required max={currentLocalMinute()} value={rmaSupportForm.effectiveAt} onChange={event => setRmaSupportForm(form => ({ ...form, effectiveAt: event.target.value }))} disabled={rmaBusy} /></FormField>
                      <FormField label="Source reference"><input className="field w-full" required minLength={3} maxLength={240} placeholder="Support ticket or incident" value={rmaSupportForm.sourceReference} onChange={event => setRmaSupportForm(form => ({ ...form, sourceReference: event.target.value }))} disabled={rmaBusy} /></FormField>
                    </div>
                    <FormField label="Action reason"><textarea className="field h-20 w-full resize-none" required minLength={5} maxLength={500} value={rmaSupportForm.actionReason} onChange={event => setRmaSupportForm(form => ({ ...form, actionReason: event.target.value }))} disabled={rmaBusy} /></FormField>
                    <div className="flex justify-end gap-2"><button type="button" className="btn-ghost" disabled={rmaBusy} onClick={() => setRmaSupportCaseId(null)}>Cancel</button><button type="submit" className="btn-primary" disabled={rmaBusy}>{rmaSupportMut.isPending ? "Recording…" : rmaSupportForm.actionType === "Escalate" ? "Record escalation" : "Take ownership"}</button></div>
                  </form>
                ) : null}
                {rmaCase.supportActions.length > 0 ? <div className="mt-4"><TimelineList rows={rmaCase.supportActions.map(action => ({ id: `support-${action.id}`, title: `${action.actionType.replace(/([a-z])([A-Z])/g, "$1 $2")} · ${action.ownerNameSnapshot}`, subtitle: `${action.supportQueue}${action.escalationSeverity ? ` · ${action.escalationSeverity}` : ""} · ${action.actionReason} · ${action.supportActionStatus}`, meta: action.effectiveAt }))} emptyText="No support ownership history recorded." /></div> : null}
                <div className="mt-4"><TimelineList rows={rmaCase.events.map(rmaEvent => ({ id: rmaEvent.id, title: `${rmaEvent.sequenceNumber}. ${rmaEvent.eventType}`, subtitle: `${rmaEvent.caseStatusAfter} · Evidence ${rmaEvent.evidenceStatus} · ${rmaEvent.evidenceReference}${rmaEvent.custodyLocation ? ` · ${rmaEvent.custodyLocation}` : ""}`, meta: rmaEvent.occurredAt }))} emptyText="No RMA events recorded." /></div>
              </div>
            ))}
          </div>
        </PanelSection>
        <PanelSection title="Spare-device pool planning">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div>
              <p className="text-xs font-bold uppercase tracking-[0.16em] text-slate-400">Software inventory plan</p>
              <p className="mt-1 text-lg font-semibold text-white">{detail.sparePool?.currentState ?? "Not in a spare pool"}</p>
            </div>
            {canManageRma && !sparePoolForm ? (
              <div className="flex flex-wrap gap-2">
                {!detail.sparePool && !detail.currentInstallation ? <button type="button" className="btn-secondary" disabled={sparePoolMut.isPending} onClick={() => { setSparePoolForm(newSparePoolForm("Add")); setSparePoolError(null); setSparePoolNotice(null); }}>Add to pool</button> : null}
                {detail.sparePool?.currentState === "Available" ? <><button type="button" className="btn-secondary" disabled={sparePoolMut.isPending} onClick={() => { setSparePoolForm(newSparePoolForm("Reserve", detail.sparePool!.poolName)); setSparePoolError(null); setSparePoolNotice(null); }}>Reserve for RMA</button><button type="button" className="btn-ghost" disabled={sparePoolMut.isPending} onClick={() => { setSparePoolForm(newSparePoolForm("Remove", detail.sparePool!.poolName)); setSparePoolError(null); setSparePoolNotice(null); }}>Remove from plan</button></> : null}
                {detail.sparePool?.currentState === "Reserved" ? <button type="button" className="btn-secondary" disabled={sparePoolMut.isPending} onClick={() => { setSparePoolForm(newSparePoolForm("Release", detail.sparePool!.poolName)); setSparePoolError(null); setSparePoolNotice(null); }}>Release reservation</button> : null}
              </div>
            ) : null}
          </div>
          <p className="mt-3 rounded-xl border border-amber-300/25 bg-amber-500/10 p-3 text-sm text-amber-100">
            Pool availability and reservation are operator-recorded planning facts. They do not prove physical possession, device condition, compatibility, installation, or certification.
          </p>
          {!detail.sparePool && detail.currentInstallation ? <p className="mt-3 text-sm text-slate-400">Record removal from the current installation before adding this device to a spare-pool plan.</p> : null}
          {sparePoolNotice ? <p role="status" className="mt-3 rounded-lg border border-emerald-400/25 bg-emerald-500/10 p-3 text-sm text-emerald-100">{sparePoolNotice}</p> : null}
          {sparePoolError ? <p role="alert" className="mt-3 text-sm text-red-300">{sparePoolError}</p> : null}
          {detail.sparePool ? <div className="mt-4"><MiniGrid rows={[["Pool", detail.sparePool.poolName], ["Current state", detail.sparePool.currentState], ["Inventory assurance", detail.sparePool.inventoryAssuranceStatus], ["Physical possession", "Unverified"], ["Condition", "Unverified"], ["Certification", "Not claimed"], ["Entry source", detail.sparePool.sourceReference]]} /></div> : null}
          {sparePoolForm && canManageRma ? (
            <form className="mt-4 space-y-3 rounded-xl border border-white/[0.08] bg-black/10 p-4" onSubmit={submitSparePool}>
              <p className="font-semibold text-white">{sparePoolForm.actionType === "Add" ? "Add device to spare-pool plan" : sparePoolForm.actionType === "Reserve" ? "Reserve spare for an RMA case" : sparePoolForm.actionType === "Release" ? "Release spare reservation" : "Remove device from spare-pool plan"}</p>
              <div className="grid gap-3 md:grid-cols-2">
                {sparePoolForm.actionType === "Add" ? <FormField label="Pool name"><input className="field w-full" required minLength={3} maxLength={120} value={sparePoolForm.poolName} onChange={event => setSparePoolForm(form => form ? ({ ...form, poolName: event.target.value }) : form)} disabled={sparePoolMut.isPending} /></FormField> : null}
                {sparePoolForm.actionType === "Reserve" ? <FormField label="RMA case number"><input className="field w-full" required inputMode="numeric" pattern="[0-9]+" value={sparePoolForm.rmaCaseId} onChange={event => setSparePoolForm(form => form ? ({ ...form, rmaCaseId: event.target.value }) : form)} placeholder="Case number shown as RMA #" disabled={sparePoolMut.isPending} /></FormField> : null}
                <FormField label="Effective at"><input className="field w-full" type="datetime-local" required max={currentLocalMinute()} value={sparePoolForm.effectiveAt} onChange={event => setSparePoolForm(form => form ? ({ ...form, effectiveAt: event.target.value }) : form)} disabled={sparePoolMut.isPending} /></FormField>
                <FormField label="Source reference"><input className="field w-full" required minLength={3} maxLength={240} value={sparePoolForm.sourceReference} onChange={event => setSparePoolForm(form => form ? ({ ...form, sourceReference: event.target.value }) : form)} placeholder="Inventory ticket or RMA reference" disabled={sparePoolMut.isPending} /></FormField>
              </div>
              <FormField label="Action reason"><textarea className="field h-20 w-full resize-none" required minLength={5} maxLength={500} value={sparePoolForm.actionReason} onChange={event => setSparePoolForm(form => form ? ({ ...form, actionReason: event.target.value }) : form)} disabled={sparePoolMut.isPending} /></FormField>
              <div className="flex justify-end gap-2"><button type="button" className="btn-ghost" disabled={sparePoolMut.isPending} onClick={() => setSparePoolForm(null)}>Cancel</button><button type="submit" className="btn-primary" disabled={sparePoolMut.isPending}>{sparePoolMut.isPending ? "Recording…" : "Record planning action"}</button></div>
            </form>
          ) : null}
          {detail.sparePool ? <div className="mt-4"><TimelineList rows={detail.sparePool.events.map(poolEvent => ({ id: `spare-${poolEvent.id}`, title: `${poolEvent.actionType} · ${poolEvent.stateAfter}`, subtitle: `${poolEvent.actionReason} · ${poolEvent.sourceReference}${poolEvent.rmaCaseId ? ` · RMA #${poolEvent.rmaCaseId}` : ""} · ${poolEvent.eventStatus}`, meta: poolEvent.effectiveAt }))} emptyText="No spare-pool history recorded." /></div> : null}
        </PanelSection>
        <PanelSection title="Installer work packages">
          <div className="flex items-start justify-between gap-4">
            <div>
              <p className="text-sm font-semibold text-white">Appointment, checklist, and artifact references</p>
              <p className="mt-1 text-xs text-slate-400">Every result is an operator-recorded assertion. Attendance, artifact content, physical work, and certification remain unverified until independent evidence review.</p>
            </div>
            {canManageConnectivity ? (
              <button type="button" className="btn-secondary shrink-0" disabled={installationEvidenceBusy} onClick={() => {
                setInstallationEvidenceError(null); setInstallationEvidenceNotice(null);
                setWorkPackageForm(newInstallationWorkPackageForm(detail.currentInstallation?.vehicleId ?? ""));
                setWorkPackageOpen(true);
              }}>Schedule work</button>
            ) : null}
          </div>
          {installationEvidenceNotice ? <p role="status" className="mt-3 rounded-lg border border-emerald-300/30 bg-emerald-500/10 p-3 text-sm text-emerald-100">{installationEvidenceNotice}</p> : null}
          {installationEvidenceError ? <p role="alert" className="mt-3 rounded-lg border border-red-300/30 bg-red-500/10 p-3 text-sm text-red-100">{installationEvidenceError}</p> : null}

          {workPackageOpen && canManageConnectivity ? (
            <form className="mt-4 space-y-3 rounded-xl border border-white/[0.08] bg-black/10 p-4" onSubmit={submitWorkPackage}>
              <p className="text-xs text-amber-200">The signed-in operator is recorded as the assigned installer. Scheduling does not claim that the appointment occurred.</p>
              <div className="grid gap-3 md:grid-cols-2">
                <FormField label="Vehicle">
                  <select className="field w-full" required value={workPackageForm.vehicleId} onChange={event => setWorkPackageForm(form => ({ ...form, vehicleId: event.target.value }))} disabled={installationEvidenceBusy}>
                    <option value="">Select a vehicle</option>
                    {vehicleOptions.map(vehicle => <option key={String(vehicle.id ?? vehicle.vehicleId)} value={String(vehicle.id ?? vehicle.vehicleId)}>{String(vehicle.vehicleCode ?? vehicle.vehicleId)}</option>)}
                  </select>
                </FormField>
                <FormField label="Work-order reference"><input className="field w-full" required minLength={2} maxLength={120} value={workPackageForm.workOrderReference} onChange={event => setWorkPackageForm(form => ({ ...form, workOrderReference: event.target.value }))} disabled={installationEvidenceBusy} /></FormField>
                <FormField label="Appointment start"><input className="field w-full" required type="datetime-local" value={workPackageForm.appointmentStart} onChange={event => setWorkPackageForm(form => ({ ...form, appointmentStart: event.target.value }))} disabled={installationEvidenceBusy} /></FormField>
                <FormField label="Appointment end"><input className="field w-full" required type="datetime-local" value={workPackageForm.appointmentEnd} onChange={event => setWorkPackageForm(form => ({ ...form, appointmentEnd: event.target.value }))} disabled={installationEvidenceBusy} /></FormField>
                <FormField label="Service location"><input className="field w-full" required minLength={2} maxLength={160} value={workPackageForm.serviceLocation} onChange={event => setWorkPackageForm(form => ({ ...form, serviceLocation: event.target.value }))} disabled={installationEvidenceBusy} /></FormField>
                <FormField label="Work scope"><input className="field w-full" required minLength={5} maxLength={1000} value={workPackageForm.workScope} onChange={event => setWorkPackageForm(form => ({ ...form, workScope: event.target.value }))} disabled={installationEvidenceBusy} /></FormField>
              </div>
              <div className="flex justify-end gap-2"><button type="button" className="btn-ghost" onClick={() => setWorkPackageOpen(false)} disabled={installationEvidenceBusy}>Cancel</button><button type="submit" className="btn-primary" disabled={installationEvidenceBusy}>{workPackageMut.isPending ? "Recording…" : "Record appointment plan"}</button></div>
            </form>
          ) : null}

          <div className="mt-4 space-y-4">
            {detail.installationWorkPackages.length === 0 ? <p className="text-sm text-slate-400">No installer work package has been recorded for this device.</p> : null}
            {detail.installationWorkPackages.map(workPackage => {
              const observed = new Map(workPackage.latestChecklist.map(row => [row.checklistItem, row]));
              const matchingInstallation = detail.currentInstallation &&
                String(detail.currentInstallation.vehicleId ?? "") === workPackage.vehicleId
                ? detail.currentInstallation
                : detail.installations.find(installation =>
                  String(installation.vehicleId ?? "") === workPackage.vehicleId) ?? null;
              return (
                <div key={workPackage.id} className="rounded-xl border border-white/[0.08] bg-black/10 p-4">
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div><p className="font-semibold text-white">{workPackage.workOrderReference}</p><p className="mt-1 text-xs text-slate-400">{workPackage.vehicleCode || `Vehicle ${workPackage.vehicleId}`} · Installer {workPackage.installerName || `User ${workPackage.assignedInstallerUserId}`}</p></div>
                    <StatusBadge status={workPackage.readinessStatus.replace(/([a-z])([A-Z])/g, "$1 $2")} />
                  </div>
                  <div className="mt-3"><MiniGrid rows={[
                    ["Appointment plan", `${new Date(workPackage.appointmentStart).toLocaleString()} → ${new Date(workPackage.appointmentEnd).toLocaleString()}`],
                    ["Service location", workPackage.serviceLocation], ["Scope", workPackage.workScope],
                    ["Physical attendance", "Unverified"], ["Physical work", "Unverified"], ["Certification", "Not claimed"],
                  ]} /></div>
                  <div className="mt-4 grid gap-2 sm:grid-cols-2">
                    {workPackage.requiredChecklistItems.map(item => {
                      const observation = observed.get(item);
                      return <div key={item} className="rounded-lg border border-white/[0.07] bg-black/10 p-2 text-xs"><div className="flex items-center justify-between gap-2"><span className="font-medium text-slate-200">{item.replace(/([a-z])([A-Z])/g, "$1 $2")}</span><span className="text-slate-400">{observation?.observedResult ?? "Not recorded"}</span></div>{observation ? <p className="mt-1 text-slate-500">{observation.evidenceReference} · {observation.assuranceStatus}</p> : null}</div>;
                    })}
                  </div>
                  {workPackage.artifactReferences.length ? <div className="mt-4"><TimelineList rows={workPackage.artifactReferences.map(artifact => ({ id: artifact.id, title: artifact.artifactType.replace(/([a-z])([A-Z])/g, "$1 $2"), subtitle: `${artifact.objectKey} · SHA-256 ${artifact.sha256.slice(0, 12)}… · ${artifact.contentVerificationStatus}`, meta: artifact.capturedAt }))} emptyText="No artifact references recorded." /></div> : null}
                  {workPackage.linkedInstallationId ? (
                    <p className="mt-3 rounded-lg border border-sky-400/20 bg-sky-500/10 p-3 text-xs text-sky-100">Linked to persisted installation #{workPackage.linkedInstallationId} ({workPackage.linkedInstallationStatus}) · {workPackage.linkAssuranceStatus}. This link does not verify physical work.</p>
                  ) : matchingInstallation && workPackage.readinessStatus === "RecordedAwaitingIndependentVerification" ? (
                    <div className="mt-3 rounded-lg border border-amber-400/20 bg-amber-500/10 p-3 text-xs text-amber-100">
                      <p>Matching installation #{matchingInstallation.id} is recorded for this vehicle. Linking records traceability only; physical work and certification remain unverified.</p>
                      {canManageConnectivity ? <button type="button" className="btn-secondary mt-2" disabled={installationEvidenceBusy} onClick={() => {
                        if (installationEvidenceSubmitting.current) return;
                        installationEvidenceSubmitting.current = true; setInstallationEvidenceError(null);
                        linkWorkPackageMut.mutate({ workPackageId: workPackage.id, installationId: matchingInstallation.id });
                      }}>{linkWorkPackageMut.isPending ? "Linking…" : "Link recorded installation"}</button> : null}
                    </div>
                  ) : null}
                  {canManageConnectivity ? <div className="mt-4 flex flex-wrap gap-2"><button type="button" className="btn-secondary" disabled={installationEvidenceBusy} onClick={() => { setInstallationEvidenceError(null); setChecklistForm(newInstallationChecklistForm(workPackage.id)); setArtifactForm(null); }}>Record checklist observation</button><button type="button" className="btn-secondary" disabled={installationEvidenceBusy} onClick={() => { setInstallationEvidenceError(null); setArtifactForm(newInstallationArtifactForm(workPackage.id)); setChecklistForm(null); }}>Record artifact reference</button></div> : null}

                  {checklistForm?.workPackageId === workPackage.id ? (
                    <form className="mt-4 space-y-3 rounded-lg border border-white/[0.08] bg-black/10 p-3" onSubmit={submitChecklistObservation}>
                      <p className="text-xs text-amber-200">Choose only the result actually observed. This record stays unverified and cannot certify the installation.</p>
                      <div className="grid gap-3 md:grid-cols-2">
                        <FormField label="Checklist item"><select className="field w-full" value={checklistForm.checklistItem} onChange={event => setChecklistForm(form => form ? ({ ...form, checklistItem: event.target.value as DeviceInstallationChecklistObservationInput["checklistItem"] }) : form)} disabled={installationEvidenceBusy}>{["DeviceIdentity","VehicleIdentity","Mounting","PrimaryPower","Ground","Ignition","GNSSAntenna","CellularAntenna","Harness","CANBus","CameraAlignment","SensorPlacement"].map(item => <option key={item} value={item}>{item.replace(/([a-z])([A-Z])/g, "$1 $2")}</option>)}</select></FormField>
                        <FormField label="Observed result"><select className="field w-full" value={checklistForm.observedResult} onChange={event => setChecklistForm(form => form ? ({ ...form, observedResult: event.target.value as DeviceInstallationChecklistObservationInput["observedResult"] }) : form)} disabled={installationEvidenceBusy}><option value="NotObserved">Not observed</option><option value="Fail">Fail</option><option value="Pass">Pass</option><option value="NotApplicable">Not applicable</option></select></FormField>
                        <FormField label="Observation time"><input className="field w-full" required type="datetime-local" max={currentLocalMinute()} value={checklistForm.observedAt} onChange={event => setChecklistForm(form => form ? ({ ...form, observedAt: event.target.value }) : form)} disabled={installationEvidenceBusy} /></FormField>
                        <FormField label="Evidence reference"><input className="field w-full" required minLength={3} maxLength={240} placeholder="Work note, meter reading, or artifact key" value={checklistForm.evidenceReference} onChange={event => setChecklistForm(form => form ? ({ ...form, evidenceReference: event.target.value }) : form)} disabled={installationEvidenceBusy} /></FormField>
                        <FormField label="Observation notes"><input className="field w-full" required minLength={3} maxLength={1000} value={checklistForm.observationNotes} onChange={event => setChecklistForm(form => form ? ({ ...form, observationNotes: event.target.value }) : form)} disabled={installationEvidenceBusy} /></FormField>
                      </div>
                      <div className="flex justify-end gap-2"><button type="button" className="btn-ghost" onClick={() => setChecklistForm(null)} disabled={installationEvidenceBusy}>Cancel</button><button type="submit" className="btn-primary" disabled={installationEvidenceBusy}>{checklistMut.isPending ? "Recording…" : "Record unverified observation"}</button></div>
                    </form>
                  ) : null}

                  {artifactForm?.workPackageId === workPackage.id ? (
                    <form className="mt-4 space-y-3 rounded-lg border border-white/[0.08] bg-black/10 p-3" onSubmit={submitArtifactReference}>
                      <p className="text-xs text-amber-200">Record a key from governed storage and its independently calculated SHA-256. OpsTrax stores the reference, not an upload or verification claim.</p>
                      <div className="grid gap-3 md:grid-cols-2">
                        <FormField label="Artifact type"><select className="field w-full" value={artifactForm.artifactType} onChange={event => setArtifactForm(form => form ? ({ ...form, artifactType: event.target.value as DeviceInstallationArtifactReferenceInput["artifactType"] }) : form)} disabled={installationEvidenceBusy}>{["InstallationPhoto","SerialLabel","WiringPhoto","PowerReading","TechnicianChecklist","CommissioningReport","RemovalPhoto","OtherDocument"].map(type => <option key={type} value={type}>{type.replace(/([a-z])([A-Z])/g, "$1 $2")}</option>)}</select></FormField>
                        <FormField label="Capture time"><input className="field w-full" required type="datetime-local" max={currentLocalMinute()} value={artifactForm.capturedAt} onChange={event => setArtifactForm(form => form ? ({ ...form, capturedAt: event.target.value }) : form)} disabled={installationEvidenceBusy} /></FormField>
                        <FormField label="Governed-storage object key"><input className="field w-full" required maxLength={1024} placeholder="installations/work-order/photo.jpg" value={artifactForm.objectKey} onChange={event => setArtifactForm(form => form ? ({ ...form, objectKey: event.target.value }) : form)} disabled={installationEvidenceBusy} /></FormField>
                        <FormField label="SHA-256"><input className="field w-full font-mono" required minLength={64} maxLength={64} pattern="[0-9a-fA-F]{64}" value={artifactForm.sha256} onChange={event => setArtifactForm(form => form ? ({ ...form, sha256: event.target.value }) : form)} disabled={installationEvidenceBusy} /></FormField>
                      </div>
                      <div className="flex justify-end gap-2"><button type="button" className="btn-ghost" onClick={() => setArtifactForm(null)} disabled={installationEvidenceBusy}>Cancel</button><button type="submit" className="btn-primary" disabled={installationEvidenceBusy}>{artifactMut.isPending ? "Recording…" : "Record unverified reference"}</button></div>
                    </form>
                  ) : null}
                </div>
              );
            })}
          </div>
        </PanelSection>
        <PanelSection title="Installation History">
          {detail.currentInstallation ? (
            <div className="mb-4 rounded-xl border border-emerald-400/20 bg-emerald-500/10 p-3">
              <div className="flex items-center justify-between gap-3">
                <div>
                  <p className="text-xs font-bold uppercase tracking-[0.16em] text-emerald-300">Current installation</p>
                  <p className="mt-1 font-medium text-white">{detail.currentInstallation.vehicleCode || `Vehicle ${detail.currentInstallation.vehicleId || "—"}`}</p>
                </div>
                <StatusBadge status={detail.currentInstallation.installStatus} />
              </div>
              <div className="mt-3 grid gap-2 text-xs text-emerald-50 md:grid-cols-2">
                <span>Role: {detail.currentInstallation.deviceRole || "—"} · {detail.currentInstallation.isPrimary ? "Primary" : "Secondary"}</span>
                <span>Location: {detail.currentInstallation.installationLocation || "—"}</span>
                <span>Odometer: {detail.currentInstallation.odometerAtInstallation || "—"}</span>
                <span>Method: {detail.currentInstallation.commissioningMethod || "—"}</span>
                <span>Result: {detail.currentInstallation.commissioningResult || "Pending"}</span>
                <span>Evidence: {detail.currentInstallation.verificationReference || "—"}</span>
              </div>
            </div>
          ) : null}
          <TimelineList rows={detail.installations.map((installation) => ({
            title: installation.vehicleCode || `Vehicle ${installation.vehicleId || "—"}`,
            subtitle: [
              installation.installStatus,
              installation.deviceRole,
              installation.isPrimary ? "Primary" : "Secondary",
              installation.installationLocation ? `Location: ${installation.installationLocation}` : null,
              installation.odometerAtInstallation ? `Odometer: ${installation.odometerAtInstallation}` : null,
              installation.commissioningMethod ? `Method: ${installation.commissioningMethod}` : null,
              installation.assignmentReason,
              installation.removalReason,
            ].filter(Boolean).join(" · "),
            meta: installation.installedAt
              ? `${installation.installedAt}${installation.removedAt ? ` → ${installation.removedAt}` : " → current"}`
              : "",
          }))} emptyText="No installation history is recorded for this device." />
        </PanelSection>
      </div>
    </>
  );
}

// ── STEP 1: Register connection ─────────────────────────────────────────────
// Minimal, honest form. Serial and hardware category are required by the
// backend provisioning contract. IMEI is optional but is what a hardware
// GPS tracker is resolved by at ingest, so it is collected for GT06/PT40-class units.
// Provider and model/name are optional. Installation is a separate governed step. SIM/firmware/power/
// compliance remain out of the connection handshake.
function ConnectDeviceDialog({
  form,
  onChange,
  onClose,
  onSubmit,
  busy,
  error,
}: {
  form: ConnectFormState;
  onChange: (form: ConnectFormState) => void;
  onClose: () => void;
  onSubmit: () => void;
  busy: boolean;
  error?: string | null;
}) {
  const serialValid = form.serialNumber.trim().length > 0 && form.deviceCategory.trim().length > 0;
  return (
    <div className="fixed inset-0 z-[60] grid place-items-center bg-black/60 p-4" role="dialog" aria-modal="true" aria-labelledby="connect-device-title">
      <form
        className="panel max-h-[90vh] w-full max-w-2xl overflow-y-auto p-6"
        onSubmit={(event) => { event.preventDefault(); if (serialValid && !busy) onSubmit(); }}
      >
        <div className="flex items-start justify-between gap-4">
          <div className="flex items-start gap-3">
            <div className="grid h-11 w-11 place-items-center rounded-2xl border border-teal-200 bg-teal-50 text-teal-600 shadow-inner">
              <PlugZap className="h-5 w-5" />
            </div>
            <div>
              <h2 id="connect-device-title" className="text-2xl font-semibold text-slate-900">Connect a device</h2>
              <p className="mt-1 text-sm text-slate-500">Register the device serial to mint its live streaming credentials. Nothing streams until the device authenticates with the key you get next.</p>
            </div>
          </div>
          <button type="button" className="icon-btn" aria-label="Close" onClick={onClose}><X className="h-4 w-4" /></button>
        </div>

        <div className="mt-6 grid gap-4 md:grid-cols-2">
          <FormField label="Device Serial (required)">
            <input
              className="field w-full font-mono"
              value={form.serialNumber}
              onChange={(event) => onChange({ ...form, serialNumber: event.target.value })}
              placeholder="e.g. GT06-8891-2245"
              autoFocus
              required
              aria-required="true"
              aria-label="Device serial number, required"
            />
          </FormField>
          <FormField label="IMEI (GPS trackers, optional)">
            <input
              className="field w-full font-mono"
              value={form.imei}
              onChange={(event) => onChange({ ...form, imei: event.target.value })}
              placeholder="e.g. 862464068456321"
              inputMode="numeric"
              aria-label="Device IMEI, optional, for hardware GPS trackers"
            />
          </FormField>
          <FormField label="Hardware category (required)">
            <select
              className="field w-full"
              value={form.deviceCategory}
              onChange={(event) => onChange({ ...form, deviceCategory: event.target.value })}
              aria-label="Governed hardware category, required"
              required
            >
              <option value="">Select the hardware category</option>
              {INSTALLATION_ROLES.map((role) => <option key={role} value={role}>{role}</option>)}
            </select>
          </FormField>
          <FormField label="Provider">
            <input
              className="field w-full"
              value={form.provider}
              onChange={(event) => onChange({ ...form, provider: event.target.value })}
              placeholder="e.g. Motive, Geotab, Samsara"
              list="connect-provider-options"
              aria-label="Telematics provider"
            />
            <datalist id="connect-provider-options">
              <option value="Motive" />
              <option value="Geotab" />
              <option value="Samsara" />
              <option value="Verizon Connect" />
            </datalist>
          </FormField>
          <FormField label="Device Model / Name (optional)">
            <input
              className="field w-full"
              value={form.deviceModel}
              onChange={(event) => onChange({ ...form, deviceModel: event.target.value })}
              placeholder="e.g. OBD-II Gateway"
              aria-label="Device model or friendly name"
            />
          </FormField>
          <FormField label="Manufacturer (optional)">
            <input
              className="field w-full"
              value={form.manufacturer}
              onChange={(event) => onChange({ ...form, manufacturer: event.target.value })}
              placeholder="Exact label from the device"
              maxLength={120}
              aria-label="Exact device manufacturer"
            />
          </FormField>
          <FormField label="Hardware revision (optional)">
            <input
              className="field w-full"
              value={form.hardwareRevision}
              onChange={(event) => onChange({ ...form, hardwareRevision: event.target.value })}
              placeholder="Exact revision from the device"
              maxLength={120}
              aria-label="Exact hardware revision"
            />
          </FormField>
          <FormField label="Reported firmware version (optional)">
            <input
              className="field w-full"
              value={form.firmwareVersion}
              onChange={(event) => onChange({ ...form, firmwareVersion: event.target.value })}
              placeholder="Exact version reported by the device"
              maxLength={120}
              aria-label="Reported firmware version"
            />
          </FormField>
          <p className="text-xs text-slate-500">
            The device is registered uninstalled. After copying its one-time credentials,
            use Install on vehicle to create the governed effective-dated installation.
          </p>
          <p className="text-xs text-slate-500 md:col-span-2">
            Hardware identity fields are operator-recorded candidate data. They do not prove physical identity, compatibility, or certification.
          </p>
        </div>

        <div className="mt-5 flex items-start gap-2 rounded-2xl border border-slate-200 bg-slate-50 p-3 text-xs text-slate-500">
          <KeyRound className="mt-0.5 h-4 w-4 flex-shrink-0 text-slate-400" />
          <span>On connect, we generate a one-time API key and HMAC secret. Copy them on the next screen — they are shown only once.</span>
        </div>

        {error ? (
          <div className="mt-4 flex items-start gap-2 rounded-2xl border border-red-200 bg-red-50 p-3 text-sm text-red-700" role="alert">
            <AlertTriangle className="mt-0.5 h-4 w-4 flex-shrink-0" />
            <span>{error}</span>
          </div>
        ) : null}

        <div className="mt-6 flex justify-end gap-3">
          <button type="button" className="btn-ghost" onClick={onClose}>Cancel</button>
          <button type="submit" className="btn-primary" disabled={!serialValid || busy}>
            {busy ? "Connecting..." : (<><PlugZap className="h-4 w-4" /> Register Connection</>)}
          </button>
        </div>
      </form>
    </div>
  );
}

// ── STEP 2: Credentials and historical check-in record ───────────────────────
// Displays the one-time credentials and a periodically refreshed historical
// check-in record. Stored timestamps are not evidence of a live connection.
function DeviceCredentialsDialog({
  result,
  onDone,
}: {
  result: DeviceProvisionResult;
  onDone: () => void;
}) {
  const { credentials, ingestUrl, device } = result;

  const connectionQ = useQuery({
    queryKey: ["telematics", "device-connection", credentials.deviceId],
    queryFn: () => telematicsService.getDeviceConnectionState(credentials.deviceId),
    // Keep observing errors and lifecycle changes for as long as the dialog is open.
    refetchInterval: 5000,
    refetchOnWindowFocus: false,
  });
  // React Query retains old data after a failed refresh; it must not remain a
  // successful observation while the current record cannot be read.
  const record = connectionQ.isError ? undefined : connectionQ.data;
  const recordMessage = connectionQ.isError
    ? "Unable to check device record"
    : !record
      ? "Checking device record…"
      : record.lifecycleBlocked
        ? "Device lifecycle is restricted"
        : record.status === "Unknown"
          ? "Device lifecycle status is unknown"
          : record.status.toLowerCase() !== "active"
            ? `Device lifecycle status: ${record.status}`
            : record.deviceState === "Unknown"
              ? "Device lifecycle state is unknown"
              : record.deviceState.toLowerCase() !== "registered"
                ? `Device lifecycle state: ${record.deviceState}`
                : record.hasRecordedCheckIn
                  ? "Recorded check-in found"
                  : "No valid recorded check-in";

  return (
    <div className="fixed inset-0 z-[60] grid place-items-center bg-black/60 p-4" role="dialog" aria-modal="true" aria-labelledby="device-credentials-title">
      <div className="panel max-h-[90vh] w-full max-w-2xl overflow-y-auto p-6">
        <div className="flex items-start gap-3">
          <div className="grid h-11 w-11 place-items-center rounded-2xl border border-emerald-200 bg-emerald-50 text-emerald-600 shadow-inner">
            <CheckCircle2 className="h-5 w-5" />
          </div>
          <div>
            <h2 id="device-credentials-title" className="text-2xl font-semibold text-slate-900">
              Device credentials created
            </h2>
            <p className="mt-1 text-sm text-slate-500">
              {device.deviceName} · {device.serialNumber || credentials.deviceSerial}. Configure your device with the credentials below.
            </p>
          </div>
        </div>

        {/* One-time warning banner */}
        <div className="mt-5 flex items-start gap-2 rounded-2xl border border-amber-300 bg-amber-50 p-3 text-sm text-amber-800" role="alert">
          <AlertTriangle className="mt-0.5 h-4 w-4 flex-shrink-0" />
          <span>{credentials.note || "Store these credentials securely — they will not be shown again."}</span>
        </div>

        {/* Credentials — neumorphic inset wells */}
        <div className="mt-5 space-y-4">
          <CopyField label="API Key" value={credentials.apiKey} secret />
          <CopyField label="HMAC Secret" value={credentials.hmacSecret} secret />
        </div>

        {/* Ingest endpoint */}
        <div className="mt-5">
          <CopyField label="Ingest Endpoint" value={ingestUrl} />
        </div>

        {/* How to connect */}
        <div className="mt-5 rounded-2xl border border-slate-200 bg-slate-50 p-4">
          <div className="flex items-center gap-2 text-sm font-semibold text-slate-800">
            <Terminal className="h-4 w-4 text-slate-500" /> How to connect
          </div>
          <ol className="mt-3 space-y-1.5 text-sm text-slate-600">
            <li>1. Point the device (or your gateway) at the ingest endpoint above.</li>
            <li>2. Authenticate every telemetry POST with header <code className="rounded bg-white px-1.5 py-0.5 font-mono text-xs text-slate-800 shadow-inner">X-Device-Key: {"<API Key>"}</code>.</li>
            <li>3. Sign the request body with the HMAC secret. Review the recorded check-in below separately from device verification.</li>
          </ol>
        </div>

        {/* Historical record only; there is no live connection indicator. */}
        <div className="clay-card mt-5 flex items-center justify-between gap-4 p-4">
          <div className="flex items-center gap-3">
            <span className="inline-block h-[7px] w-[7px] flex-shrink-0 rounded-full bg-slate-400" aria-hidden />
            <div>
              <p className="text-sm font-semibold text-slate-800" role="status">{recordMessage}</p>
              <p className="mt-0.5 text-xs text-slate-500">
                {connectionQ.isError
                  ? "Retrying automatically."
                  : record?.lastSeenAt
                    ? `Last recorded check-in ${record.lastSeenAt}`
                    : "Checking the saved device record every 5 seconds."}
              </p>
              <p className="mt-1 text-xs text-slate-500">A recorded check-in does not establish current connectivity or device verification.</p>
            </div>
          </div>
          <RadioTower className="h-5 w-5 text-slate-400" />
        </div>

        <div className="mt-6 flex justify-end gap-3">
          <button type="button" className="btn-primary" onClick={onDone}>
            Close
          </button>
        </div>
      </div>
    </div>
  );
}

function RotatedCredentialsDialog({
  credentials,
  onDone,
}: {
  credentials: DeviceCredentialRotationResult;
  onDone: () => void;
}) {
  return (
    <div className="fixed inset-0 z-[70] grid place-items-center bg-black/60 p-4" role="dialog" aria-modal="true" aria-labelledby="rotated-credentials-title">
      <div className="panel max-h-[90vh] w-full max-w-2xl overflow-y-auto p-6">
        <div className="flex items-start gap-3">
          <div className="grid h-11 w-11 place-items-center rounded-2xl border border-amber-200 bg-amber-50 text-amber-700 shadow-inner"><KeyRound className="h-5 w-5" /></div>
          <div>
            <h2 id="rotated-credentials-title" className="text-2xl font-semibold text-slate-900">Replacement credentials</h2>
            <p className="mt-1 text-sm text-slate-500">Device {credentials.deviceId}. These values exist only in this dialog and are cleared when you close it.</p>
          </div>
        </div>
        <div className="mt-5 rounded-2xl border border-amber-300 bg-amber-50 p-3 text-sm text-amber-800" role="alert">
          {credentials.note || "Store the replacement credentials securely; they will not be shown again."}
          {credentials.previousCredentialsValidUntil ? ` Previous credentials expire at ${new Date(credentials.previousCredentialsValidUntil).toLocaleString()}.` : " Previous credentials are no longer valid."}
        </div>
        <div className="mt-5 space-y-4">
          <CopyField label="Replacement API Key" value={credentials.apiKey} secret />
          <CopyField label="Replacement HMAC Secret" value={credentials.hmacSecret} secret />
        </div>
        <div className="mt-6 flex justify-end">
          <button type="button" className="btn-primary" onClick={onDone}>I stored these credentials</button>
        </div>
      </div>
    </div>
  );
}

// Neumorphic inset well with a monospace value and an accessible Copy button.
// `secret` values render in a subtly stronger inset to read as sensitive tokens.
function CopyField({ label, value, secret = false }: { label: string; value: string; secret?: boolean }) {
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    if (!copied) return;
    const timer = window.setTimeout(() => setCopied(false), 1600);
    return () => window.clearTimeout(timer);
  }, [copied]);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
    } catch {
      setCopied(false);
    }
  };

  return (
    <div>
      <span className="mb-2 block text-xs font-bold uppercase tracking-[0.16em] text-slate-500">{label}</span>
      <div
        className="flex items-center gap-2 rounded-xl border border-slate-200 bg-slate-100 px-3 py-2"
        style={{ boxShadow: secret ? "inset 0 2px 5px rgba(15,23,42,.12)" : "inset 0 1px 3px rgba(15,23,42,.08)" }}
      >
        <code className="min-w-0 flex-1 overflow-x-auto whitespace-nowrap font-mono text-sm text-slate-800">
          {value || "—"}
        </code>
        <button
          type="button"
          className="btn-ghost h-8 flex-shrink-0 px-3"
          onClick={() => void copy()}
          disabled={!value}
          aria-label={copied ? `${label} copied to clipboard` : `Copy ${label} to clipboard`}
          title={copied ? "Copied" : `Copy ${label}`}
        >
          {copied ? (<><CheckCircle2 className="h-3.5 w-3.5 text-emerald-500" /> Copied</>) : (<><Copy className="h-3.5 w-3.5" /> Copy</>)}
        </button>
      </div>
    </div>
  );
}

function ActionContractBadge({ contract }: { contract: DeviceActionContract }) {
  return (
    <span
      className={`inline-flex rounded-full border px-2 py-1 text-xs font-semibold ${actionContractTone(contract.state)}`}
      title={contract.reason}
    >
      {contract.label}
      <span className="ml-1 rounded-full border border-current/40 px-1.5 py-0.5 text-[10px] text-current">
        {actionContractLabel(contract.state)}
      </span>
    </span>
  );
}

function ActionButton({
  label,
  icon,
  allowed,
  onClick,
  unavailableReason,
  contractState,
}: {
  label: string;
  icon: ReactNode;
  allowed: boolean;
  onClick: () => void;
  unavailableReason?: string;
  contractState?: ActionContractState;
}) {
  const state = contractState ?? (allowed ? "ready" : "permission-blocked");
  return (
    <button
      className={allowed ? "btn-ghost" : "btn-ghost opacity-60"}
      disabled={!allowed}
      title={
        allowed
          ? unavailableReason
            ? `${label} · ${unavailableReason}`
            : label
          : unavailableReason
            ? `${actionContractLabel(state)}: ${unavailableReason}`
            : "You do not have permission to perform this action."
      }
      onClick={() => allowed && onClick()}
    >
      {icon} {label}
    </button>
  );
}

function InfoBlock({ title, items }: { title: string; items: Array<[string, string]> }) {
  return (
    <div className="rounded-2xl border border-white/[0.07] bg-white/[0.02] p-4">
      <p className="text-sm font-semibold text-white">{title}</p>
      <div className="mt-4 space-y-2">
        {items.map(([label, value]) => (
          <div key={label} className="flex items-start justify-between gap-3 rounded-xl border border-white/[0.05] bg-black/10 px-3 py-2">
            <span className="text-[11px] font-bold uppercase tracking-[0.16em] text-slate-500">{label}</span>
            <span className="text-right text-sm text-slate-200">{value || "—"}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

function ModalForm({
  title,
  children,
  onClose,
  onSubmit,
  submitLabel,
  busy,
  error,
}: {
  title: string;
  children: ReactNode;
  onClose: () => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
  submitLabel: string;
  busy: boolean;
  error?: string | null;
}) {
  const titleId = useId();
  const dialogRef = useRef<HTMLFormElement>(null);
  const closeButton = useRef<HTMLButtonElement>(null);
  const onCloseRef = useRef(onClose);
  onCloseRef.current = onClose;
  useEffect(() => {
    const previouslyFocused = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    closeButton.current?.focus();
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") onCloseRef.current();
      if (event.key === "Tab") {
        const focusable = Array.from(dialogRef.current?.querySelectorAll<HTMLElement>(
          'button:not([disabled]), input:not([disabled]), textarea:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])',
        ) ?? []);
        if (!focusable.length) return;
        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => { window.removeEventListener("keydown", onKeyDown); previouslyFocused?.focus(); };
  }, []);
  return (
    <div className="fixed inset-0 z-[60] grid place-items-center bg-black/60 p-4" role="presentation">
      <form ref={dialogRef} role="dialog" aria-modal="true" aria-labelledby={titleId} className="panel max-h-[90vh] w-full max-w-4xl overflow-y-auto p-6" onSubmit={onSubmit}>
        <div className="flex items-center justify-between">
          <h2 id={titleId} className="text-2xl font-semibold text-slate-900">{title}</h2>
          <button ref={closeButton} type="button" className="icon-btn" aria-label={`Close ${title}`} onClick={onClose}><X className="h-4 w-4" /></button>
        </div>
        <div className="mt-6">{children}</div>
        {error ? <div role="alert" className="mt-4 rounded-xl border border-red-200 bg-red-50 p-3 text-sm text-red-700">{error}</div> : null}
        <div className="mt-6 flex justify-end gap-3">
          <button type="button" className="btn-ghost" onClick={onClose}>Cancel</button>
          <button type="submit" className="btn-primary" disabled={busy}>{busy ? "Saving..." : submitLabel}</button>
        </div>
      </form>
    </div>
  );
}

function FormField({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label>
      <span className="mb-2 block text-xs font-bold uppercase tracking-[0.16em] text-slate-500">{label}</span>
      {children}
    </label>
  );
}

function PanelSection({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="rounded-2xl border border-white/[0.07] bg-white/[0.02] p-5">
      <p className="text-sm font-semibold text-white">{title}</p>
      <div className="mt-4">{children}</div>
    </div>
  );
}

function MiniGrid({ rows }: { rows: Array<[string, string]> }) {
  return (
    <div className="space-y-2">
      {rows.map(([label, value]) => (
        <div key={label} className="flex items-start justify-between gap-3 rounded-xl border border-white/[0.05] bg-black/10 px-3 py-2">
          <span className="text-[11px] font-bold uppercase tracking-[0.16em] text-slate-500">{label}</span>
          <span className="text-right text-sm text-slate-200">{value}</span>
        </div>
      ))}
    </div>
  );
}

function TimelineList({ rows, emptyText }: { rows: Array<{ id?: string; title: string; subtitle: string; meta: string }>; emptyText: string }) {
  if (!rows.length) {
    return <p className="text-sm text-slate-400">{emptyText}</p>;
  }

  return (
    <div className="flex h-full flex-col gap-3 overflow-y-auto">
      {rows.map((row) => (
        <div key={row.id || `${row.title}-${row.meta}`} className="rounded-xl border border-white/[0.06] bg-black/10 p-3">
          <div className="flex items-start justify-between gap-3">
            <div>
              <p className="font-medium text-white">{row.title}</p>
              <p className="mt-1 text-sm text-slate-400">{row.subtitle}</p>
            </div>
            <span className="text-xs text-slate-500">{row.meta}</span>
          </div>
        </div>
      ))}
    </div>
  );
}

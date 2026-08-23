import { FormEvent, useEffect, useId, useMemo, useRef, useState } from "react";
import type { ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Activity,
  AlertTriangle,
  ArrowRightLeft,
  CheckCircle2,
  ChevronDown,
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
import { PERMISSIONS } from "@/auth/rbacConfig";
import { useHasPermission } from "@/hooks/usePermission";
import { vehiclesApi } from "@/services/vehiclesApi";
import {
  telematicsService,
  type DeviceCommandRecord,
  type DeviceCommissioningInput,
  type DeviceCredentialRotationResult,
  type DeviceDetailRecord,
  type DeviceIdentityQuarantineRecord,
  type DeviceInstallationInput,
  type DeviceInstallationRemovalInput,
  type DeviceProvisionResult,
} from "@/services/telematicsService";
import type { AnyRecord } from "@/types";

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

type CommissioningFormState = {
  result: "" | DeviceCommissioningInput["result"];
  verificationReference: string;
};

// DEF-023: destructive/lifecycle actions confirm through the in-app accessible
// ConfirmDialog (native window.confirm cannot be completed by automation and is
// invisible to assistive technology).
type ConfirmableLifecycleAction = "archive" | "suspend" | "rotate-credentials";

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
  archive: {
    title: "Archive device",
    confirmLabel: "Archive Device",
    variant: "danger",
    message: (deviceName) => `Archive ${deviceName}? The device is revoked and new ingestion is blocked; it remains visible in the Archived view with its lifecycle history.`,
  },
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
};

const defaultConnectForm: ConnectFormState = {
  serialNumber: "",
  imei: "",
  deviceCategory: "",
  provider: "",
  deviceModel: "",
};

const DEVICE_TABS: Array<{ key: DeviceTab; label: string }> = [
  { key: "all", label: "All Devices" },
  { key: "archived", label: "Archived" },
  { key: "unassigned", label: "Unassigned" },
  { key: "offline", label: "Offline" },
  { key: "attention", label: "Needs Attention" },
  { key: "firmware", label: "Firmware (read-only)" },
  { key: "provisioning", label: "Provisioning" },
  { key: "diagnostics", label: "Diagnostics" },
  { key: "quarantine", label: "Identity Quarantine" },
  { key: "installations", label: "Installations" },
  { key: "data-health", label: "Data Health" },
  { key: "providers", label: "Provider Connections" },
];

function downloadCsv(filename: string, body: string) {
  const anchor = document.createElement("a");
  anchor.href = URL.createObjectURL(new Blob([body], { type: "text/csv" }));
  anchor.download = filename;
  anchor.click();
}

function emptyStateForTab(tab: DeviceTab) {
  if (tab === "archived") return { title: "No archived devices", subtitle: "Revoked and retired devices remain visible here with their lifecycle history." };
  if (tab === "offline") return { title: "No offline devices", subtitle: "Every scoped device is checking in within the current monitoring window." };
  if (tab === "firmware") return { title: "Current version only", subtitle: "OTA scheduling and firmware history are not connected in this pilot. Current versions appear when devices report them." };
  if (tab === "providers") return { title: "No providers found", subtitle: "Integrations are pulled from your connected provider catalog." };
  if (tab === "quarantine") return { title: "No unresolved identity conflicts", subtitle: "Every device and installation identity in this fleet is currently unambiguous." };
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
  if (tab === "firmware") return true; // read-only current-version listing; no OTA/target diff exists in this pilot
  if (tab === "provisioning") return /provision|awaiting/i.test(row.connectionStatus) || /awaiting|warning/i.test(row.installStatus);
  if (tab === "diagnostics") return true;
  if (tab === "installations") return true;
  if (tab === "data-health") return true;
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
  state: ActionContractState;
  reason: string;
  onClick: () => void;
};

function buildActionContracts(
  device: DeviceCommandRecord,
  {
    canUpdate,
    canDelete,
    canAssign,
    canManageLifecycle,
    canRecover,
    onAssign,
    onUnassign,
    onArchive,
    onMarkInstalled,
    onRefresh,
    onFlagAttention,
    onResolve,
    onSuspend,
    onActivate,
    onRotateSecret,
  }: {
    canUpdate: boolean;
    canDelete: boolean;
    canAssign: boolean;
    canManageLifecycle: boolean;
    canRecover: boolean;
    onAssign: () => void;
    onUnassign: () => void;
    onArchive: () => void;
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
      state: canUpdate ? "unsupported" : "permission-blocked",
      reason: canUpdate ? "No metadata PATCH contract is available; inventory fields are read-only." : "Requires TELEMATICS_DEVICES_UPDATE.",
      onClick: () => undefined,
    },
    {
      key: "assign",
      label: hasCurrentVehicle ? "Transfer" : "Install",
      icon: <ArrowRightLeft className="h-4 w-4" />,
      state: archived ? "state-blocked" : canAssign ? "ready" : "permission-blocked",
      reason: archived ? "Archived devices cannot be installed or transferred." : canAssign ? "Governed installation permission is available." : "Requires device assignment plus TELEMETRY_DEVICES_MANAGE.",
      onClick: onAssign,
    },
    {
      key: "unassign",
      label: "Remove installation",
      icon: <Truck className="h-4 w-4" />,
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
      state: "ready",
      reason: "Read endpoint available for a fresh status read.",
      onClick: onRefresh,
    },
    {
      key: "device-state",
      label: /suspended/.test(eldStatus) ? "Activate device" : "Suspend device",
      icon: <ShieldCheck className="h-4 w-4" />,
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
      key: "archive",
      label: "Archive",
      icon: <Trash2 className="h-4 w-4" />,
      state: archived ? "state-blocked" : canDelete ? "ready" : "permission-blocked",
      reason: archived ? "This device is already archived." : canDelete ? "Permission and revoke route available." : "Requires TELEMATICS_DEVICES_DELETE.",
      onClick: onArchive,
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

  const canCreate = hasPermission(PERMISSIONS.TELEMATICS_DEVICES_CREATE);
  const canUpdate = hasPermission(PERMISSIONS.TELEMATICS_DEVICES_UPDATE);
  const canDelete = hasPermission(PERMISSIONS.TELEMATICS_DEVICES_DELETE);
  const canAssign = hasPermission(PERMISSIONS.TELEMATICS_DEVICES_ASSIGN);
  const canDiagnostics = hasPermission(PERMISSIONS.TELEMATICS_DEVICES_DIAGNOSTICS);
  const canManageDeviceLifecycle = hasPermission(PERMISSIONS.TELEMETRY_DEVICES_MANAGE);
  const canGovernInstallations = canAssign && canManageDeviceLifecycle;
  // Recovery (mark/resolve malfunction) is gated server-side on
  // compliance:update | compliance:manage | telematics:manage — NOT maintenance:manage,
  // which the broader DIAGNOSTICS alias set includes. Gate the UI on the compliance set so
  // a maintenance-manager never sees an enabled recovery button that then 403s. (A pure
  // telematics:manage admin without any compliance grant sees a disabled button rather than
  // a 403 — the safe failure direction.)
  const canRecover = hasPermission(PERMISSIONS.COMPLIANCE_UPDATE);
  const canExport = hasPermission(PERMISSIONS.TELEMATICS_DEVICES_EXPORT);
  const canManageProviders = hasPermission(PERMISSIONS.TELEMATICS_PROVIDERS_MANAGE);

  const [tab, setTab] = useState<DeviceTab>("all");
  const [search, setSearch] = useState("");
  const [selectedId, setSelectedId] = useState<string | number | null>(null);
  // Step 1 of the connect flow — the minimal register-connection form.
  const [connectOpen, setConnectOpen] = useState(false);
  const [connectForm, setConnectForm] = useState<ConnectFormState>(defaultConnectForm);
  // Step 2 of the connect flow — the one-time credentials + live pairing panel.
  // Populated ONLY by a real provisionDevice() response.
  const [provisionResult, setProvisionResult] = useState<DeviceProvisionResult | null>(null);
  const [assignTarget, setAssignTarget] = useState<DeviceCommandRecord | null>(null);
  const [installationForm, setInstallationForm] = useState<InstallationFormState>(defaultInstallationForm);
  const [removalTarget, setRemovalTarget] = useState<DeviceCommandRecord | null>(null);
  const [removalForm, setRemovalForm] = useState<RemovalFormState>(defaultRemovalForm);
  const [commissionTarget, setCommissionTarget] = useState<DeviceCommandRecord | null>(null);
  const [commissioningForm, setCommissioningForm] = useState<CommissioningFormState>(defaultCommissioningForm);
  const [rotatedCredentials, setRotatedCredentials] = useState<DeviceCredentialRotationResult | null>(null);
  const [attentionTarget, setAttentionTarget] = useState<DeviceCommandRecord | null>(null);
  const [attentionNotes, setAttentionNotes] = useState("");
  const [quarantineTarget, setQuarantineTarget] = useState<DeviceIdentityQuarantineRecord | null>(null);
  const [quarantineResolution, setQuarantineResolution] = useState({ resolutionNotes: "", correctedDeviceSerial: "", correctedImei: "" });
  const [openMenuId, setOpenMenuId] = useState<string | number | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  // DEF-022: client-side validation failures must be visible, never swallowed.
  const [formError, setFormError] = useState<string | null>(null);
  // DEF-023: pending in-app confirmation for a destructive lifecycle action.
  const [confirmTarget, setConfirmTarget] = useState<ConfirmActionTarget | null>(null);

  const devicesQ = useQuery({ queryKey: ["telematics", "devices"], queryFn: telematicsService.getDevices, staleTime: 20_000 });
  const providersQ = useQuery({ queryKey: ["telematics", "providers"], queryFn: telematicsService.getProviders, staleTime: 20_000 });
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

  // Provisioning INITIATES A REAL CONNECTION: the backend mints an apiKey + HMAC
  // secret (returned once) that the physical device uses to authenticate its
  // telemetry stream. We keep the result in state to render the credentials +
  // live pairing panel; we do NOT close the flow or claim "registered" here.
  const provisionMut = useMutation({
    mutationFn: (payload: ConnectFormState) =>
      telematicsService.provisionDevice({
        serialNumber: payload.serialNumber.trim(),
        imei: payload.imei.trim(),
        deviceCategory: payload.deviceCategory,
        provider: payload.provider.trim(),
        deviceName: payload.deviceModel.trim() || payload.serialNumber.trim(),
        deviceType: payload.deviceModel.trim() || "Device",
      }),
    onSuccess: async (result) => {
      setProvisionResult(result);
      setConnectOpen(false);
      setConnectForm(defaultConnectForm);
      // Surface the new (not-yet-streaming) device in the list immediately.
      await refreshAll();
    },
  });
  const archiveMut = useMutation({
    mutationFn: (id: string | number) => telematicsService.archiveDevice(id),
    onSuccess: async () => {
      setConfirmTarget(null);
      setSelectedId(null);
      setNotice("Device archived successfully.");
      await refreshAll();
    },
  });
  const assignMut = useMutation({
    mutationFn: ({ deviceId, input }: { deviceId: string | number; input: DeviceInstallationInput }) => telematicsService.assignDeviceToVehicle(deviceId, input),
    onSuccess: async () => {
      setAssignTarget(null);
      setInstallationForm(defaultInstallationForm); setFormError(null);
      setNotice("Device installation updated successfully.");
      await refreshAll();
    },
  });
  const unassignMut = useMutation({
    mutationFn: ({ deviceId, input }: { deviceId: string | number; input: DeviceInstallationRemovalInput }) => telematicsService.unassignDevice(deviceId, input),
    onSuccess: async () => {
      setRemovalTarget(null);
      setRemovalForm(defaultRemovalForm); setFormError(null);
      setNotice("Device installation removed successfully.");
      await refreshAll();
    },
  });
  const installMut = useMutation({
    mutationFn: ({ deviceId, input }: { deviceId: string | number; input: DeviceCommissioningInput }) => telematicsService.markInstalled(deviceId, input),
    onSuccess: async () => {
      setCommissionTarget(null);
      setCommissioningForm(defaultCommissioningForm); setFormError(null);
      setNotice("Commissioning result recorded successfully.");
      await refreshAll();
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
  const suspendMut = useMutation({
    mutationFn: (deviceId: string | number) => telematicsService.suspendDevice(deviceId),
    onSuccess: async () => { setConfirmTarget(null); setNotice("Device suspended. New ingestion is blocked until activation."); await refreshAll(); },
  });
  const activateMut = useMutation({
    mutationFn: (deviceId: string | number) => telematicsService.activateDevice(deviceId),
    onSuccess: async () => { setNotice("Device activated."); await refreshAll(); },
  });
  const rotateSecretMut = useMutation({
    mutationFn: (deviceId: string | number) => telematicsService.rotateDeviceSecret(deviceId),
    onSuccess: (result) => { setConfirmTarget(null); setRotatedCredentials(result); },
  });
  const lifecycleError = assignMut.error ?? unassignMut.error ?? installMut.error ?? suspendMut.error ?? activateMut.error ?? rotateSecretMut.error;
  const clearLifecycleError = () => {
    assignMut.reset();
    unassignMut.reset();
    installMut.reset();
    suspendMut.reset();
    activateMut.reset();
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
    setConfirmTarget({
      ...target,
      opener: document.activeElement instanceof HTMLElement ? document.activeElement : null,
    });
  };

  // DEF-023: run/cancel the confirmed lifecycle action through the existing mutations.
  const runConfirmedAction = () => {
    if (!confirmTarget) return;
    if (confirmTarget.action === "archive") archiveMut.mutate(confirmTarget.device.id);
    else if (confirmTarget.action === "suspend") suspendMut.mutate(confirmTarget.device.id);
    else rotateSecretMut.mutate(confirmTarget.device.id);
  };
  const cancelConfirmedAction = () => {
    if (!confirmTarget) return;
    if (confirmTarget.action === "archive") archiveMut.reset();
    else if (confirmTarget.action === "suspend") suspendMut.reset();
    else rotateSecretMut.reset();
    setConfirmTarget(null);
  };
  const confirmBusy =
    confirmTarget?.action === "archive" ? archiveMut.isPending
    : confirmTarget?.action === "suspend" ? suspendMut.isPending
    : confirmTarget?.action === "rotate-credentials" ? rotateSecretMut.isPending
    : false;
  const confirmActionError =
    confirmTarget?.action === "archive" ? archiveMut.error
    : confirmTarget?.action === "suspend" ? suspendMut.error
    : confirmTarget?.action === "rotate-credentials" ? rotateSecretMut.error
    : null;

  // DEF-022: any field-level change clears the visible validation error.
  //
  // The dialogs render `formError ?? mutation.error`, so clearing ONLY formError
  // un-masks a stale server error from an earlier submit: the user fixes a
  // client-side complaint and the previous 400 reappears in the same role="alert",
  // announced as though the field they just corrected caused it. Reset the
  // matching mutation alongside the form error.
  const updateInstallationForm = (updater: (form: InstallationFormState) => InstallationFormState) => {
    setFormError(null);
    assignMut.reset();
    setInstallationForm(updater);
  };
  const updateRemovalForm = (updater: (form: RemovalFormState) => RemovalFormState) => {
    setFormError(null);
    unassignMut.reset();
    setRemovalForm(updater);
  };
  const updateCommissioningForm = (updater: (form: CommissioningFormState) => CommissioningFormState) => {
    setFormError(null);
    installMut.reset();
    setCommissioningForm(updater);
  };

  const deviceRows = useMemo(() => {
    const query = search.trim().toLowerCase();
    return (devicesQ.data ?? [])
      .filter((row) => activeTabCount(tab, row))
      .filter((row) => {
        const haystack = [
          row.deviceName,
          row.deviceType,
          row.deviceCategory,
          row.provider,
          row.serialNumber,
          row.identifier,
          row.imei,
          row.assignedVehicleCode,
          row.assignedDriverName,
          row.tenantName,
          row.linkedShipmentId,
          row.connectionStatus,
          row.installStatus,
        ].join(" ").toLowerCase();
        return !query || haystack.includes(query);
      });
  }, [devicesQ.data, search, tab]);
  const vehicleOptions = (vehiclesQ.data ?? []) as AnyRecord[];

  const selectedRecord = detailQ.data?.device ?? deviceRows.find((row) => String(row.id) === String(selectedId)) ?? null;

  const activeDevices = (devicesQ.data ?? []).filter((row) => activeTabCount("all", row));
  const archivedCount = (devicesQ.data ?? []).filter((row) => activeTabCount("archived", row)).length;
  const offlineCount = activeDevices.filter((row) => /offline/i.test(row.connectionStatus)).length;
  const attentionCount = activeDevices.filter((row) => /attention|offline/i.test(row.connectionStatus) || row.openAlertCount > 0).length;
  const measuredHealth = activeDevices.filter((row) => row.dataHealthAvailable);
  const managedCount = activeDevices.length;
  const avgHealth = measuredHealth.length
    ? Math.round(measuredHealth.reduce((sum, row) => sum + Number(row.dataHealthScore), 0) / measuredHealth.length)
    : null;

  if (devicesQ.isLoading) return <DeviceLoadingState />;
  if (devicesQ.isError) return <ErrorState message="Unable to load the device command center right now." />;

  const openConnect = () => {
    setConnectForm(defaultConnectForm);
    setConnectOpen(true);
  };

  const openInstallation = (device: DeviceCommandRecord) => {
    setAssignTarget(device);
    setInstallationForm(defaultInstallationForm); setFormError(null);
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
    const csv = await telematicsService.exportDevicesCsv();
    downloadCsv("opstrax-device-command-center.csv", csv);
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
            <button
              className="btn-ghost"
              disabled={!canExport}
              title={actionTitle(canExport, "Export the current device inventory to CSV.")}
              onClick={() => canExport && void exportCurrent()}
            >
              <Download className="h-4 w-4" /> Export Devices CSV
            </button>
            <button
              className="btn-primary"
              disabled={!canCreate}
              title={actionTitle(canCreate, "Connect a new device and generate its live credentials.")}
              onClick={() => canCreate && openConnect()}
            >
              <PlugZap className="h-4 w-4" /> Connect Device
            </button>
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
            <strong className="block">Device lifecycle action failed</strong>
            {lifecycleError instanceof Error ? lifecycleError.message : "The installation change was not completed. Reload the device and try again."}
          </span>
          <button className="icon-btn" aria-label="Dismiss lifecycle error" onClick={clearLifecycleError}><X className="h-4 w-4" /></button>
        </div>
      ) : null}

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <KpiCard label="Active Managed Devices" value={managedCount} status={managedCount ? "Active" : "Pending"} icon={<RadioTower className="h-4 w-4" />} />
        <KpiCard label="Offline" value={offlineCount} status={!managedCount ? "Pending" : offlineCount ? "Critical" : "Healthy"} icon={<WifiOff className="h-4 w-4" />} />
        <KpiCard label="Needs Attention" value={attentionCount} status={!managedCount ? "Pending" : attentionCount ? "Watch" : "Healthy"} icon={<Activity className="h-4 w-4" />} />
        <KpiCard label="Average Data Health" value={avgHealth == null ? "Unknown" : `${avgHealth}%`} status={avgHealth == null ? "Pending" : avgHealth >= 85 ? "Healthy" : avgHealth >= 70 ? "Watch" : "Critical"} icon={<Cpu className="h-4 w-4" />} />
      </div>
      <p className="text-xs text-slate-500">Data health is a derived signal score for active devices: stale check-in, malfunction state, open telemetry alerts, and active faults reduce the score. Devices without evidence remain Unknown. <button type="button" className="font-semibold text-teal-700 hover:underline" onClick={() => setTab("archived")}>{archivedCount} archived</button> devices are retained separately.</p>

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
          <div className="flex flex-wrap gap-2" role="tablist" aria-label="Device workspace views">
            {DEVICE_TABS.map((item) => (
              <button key={item.key} role="tab" aria-selected={tab === item.key} className={tab === item.key ? "btn-primary py-2 text-xs" : "btn-ghost py-2 text-xs"} onClick={() => setTab(item.key)}>
                {item.label}
              </button>
            ))}
          </div>
        </div>

        {tab === "quarantine" ? (
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
                        <button
                          className="btn-primary py-2 text-xs"
                          disabled={!canGovernInstallations}
                          title={actionTitle(canGovernInstallations, "Review evidence and resolve this quarantined identity.")}
                          onClick={() => {
                            setQuarantineTarget(row);
                            setQuarantineResolution({ resolutionNotes: "", correctedDeviceSerial: row.deviceSerial ?? "", correctedImei: row.imei ?? "" });
                          }}
                        >Resolve conflict</button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )
        ) : tab === "providers" ? (
          providersQ.isLoading ? (
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
                  <div className="mt-4 flex flex-wrap gap-2">
                    <button
                      className="btn-ghost"
                      disabled={!canManageProviders}
                      title={actionTitle(canManageProviders, "Open provider management settings.")}
                      onClick={() => canManageProviders && navigate("/integrations")}
                    >
                      <Settings2 className="h-4 w-4" /> Manage Provider
                    </button>
                    <button
                      className="btn-ghost"
                      disabled={!canManageProviders || providerSyncMut.isPending}
                      title={actionTitle(canManageProviders, "Run provider sync for the selected integration scope.")}
                      onClick={() => canManageProviders && providerSyncMut.mutate(String(provider.id))}
                    >
                      <PlugZap className="h-4 w-4" /> Sync Provider
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )
        ) : !deviceRows.length ? (
          <EmptyState title={emptyState.title} subtitle={emptyState.subtitle} />
        ) : (
          <div className="overflow-x-auto" onClick={(e) => { if (!(e.target as HTMLElement).closest(".relative")) setOpenMenuId(null); }}>
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200">
                  {["Device", "Provider", "Identifier", "Vehicle", "Driver", "Firmware", "Check-in", "Connection", "Lifecycle", "Power", "Signal", "Health", "Install", "Compliance", "Support", "Actions"].map((header) => (
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
                      <div>{row.warrantyStatus}</div>
                      <div className="text-xs text-slate-500">{row.supportStatus}</div>
                    </td>
                    <td className="px-4 py-3">
                      <div className="relative">
                        <button
                          type="button"
                          className="btn-ghost h-8 px-3 flex items-center gap-1"
                          onClick={() => setOpenMenuId(openMenuId === row.id ? null : row.id)}
                        >
                          Manage <ChevronDown className="h-3 w-3" />
                        </button>
                        {openMenuId === row.id && (
                          <div className="absolute right-0 z-50 mt-1 w-48 rounded-xl border border-slate-200 bg-white py-1 shadow-lg">
                            <button type="button" className="flex w-full items-center gap-2 px-4 py-2 text-sm text-slate-700 hover:bg-slate-50" onClick={() => { setSelectedId(row.id); setOpenMenuId(null); }}>View Details</button>
                            <button type="button" className="flex w-full items-center gap-2 px-4 py-2 text-sm text-slate-400" disabled title="Device metadata is read-only until a PATCH contract is available.">Metadata read-only</button>
                            {row.lifecycleStatus !== "Archived" && canGovernInstallations && <button type="button" className="flex w-full items-center gap-2 px-4 py-2 text-sm text-slate-700 hover:bg-slate-50" onClick={() => { openInstallation(row); setOpenMenuId(null); }}>{row.assignedVehicleId ? "Transfer installation" : "Install on vehicle"}</button>}
                            {canDiagnostics && <button type="button" className="flex w-full items-center gap-2 px-4 py-2 text-sm text-slate-700 hover:bg-slate-50" onClick={() => { navigate("/obd-j1939"); setOpenMenuId(null); }}>View Diagnostics</button>}
                            <button type="button" className="flex w-full items-center gap-2 px-4 py-2 text-sm text-slate-700 hover:bg-slate-50" onClick={() => { refreshMut.mutate(row.id); setOpenMenuId(null); }}>Refresh Status</button>
                            {row.lifecycleStatus !== "Archived" && canDelete && <button type="button" className="flex w-full items-center gap-2 px-4 py-2 text-sm text-red-600 hover:bg-red-50" onClick={() => { openConfirm({ action: "archive", device: row }); setOpenMenuId(null); }}>Archive Device</button>}
                          </div>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {selectedId ? (
        <div className="fixed inset-0 z-50 flex justify-end bg-black/55 backdrop-blur-sm" onClick={() => setSelectedId(null)}>
          <aside className="h-full w-full max-w-5xl overflow-y-auto border-l border-white/[0.09] bg-slate-950 p-6 shadow-2xl" onClick={(event) => event.stopPropagation()}>
            <button className="float-right icon-btn" aria-label="Close device details" onClick={() => setSelectedId(null)}><X className="h-4 w-4" /></button>
            {detailQ.isLoading ? (
              <LoadingState />
            ) : detailQ.isError || !detailQ.data ? (
              <ErrorState message="Unable to load this device." />
	            ) : (
	              <DeviceDetailDrawer
	                detail={detailQ.data}
	                lifecycleError={lifecycleError}
	                onDismissLifecycleError={clearLifecycleError}
	                actionContracts={
	                  selectedRecord
	                    ? buildActionContracts(selectedRecord, {
	                      canUpdate,
	                      canDelete,
	                      canAssign: canGovernInstallations,
	                      canManageLifecycle: canManageDeviceLifecycle,
	                      canRecover,
	                      onAssign: () => openInstallation(selectedRecord),
	                      onUnassign: () => {
	                        if (!canGovernInstallations) return;
	                        setRemovalTarget(selectedRecord);
	                        setRemovalForm(defaultRemovalForm); setFormError(null);
	                      },
	                      onArchive: () => canDelete && openConfirm({ action: "archive", device: selectedRecord }),
	                      onMarkInstalled: () => {
	                        if (!canGovernInstallations) return;
	                        setCommissionTarget(selectedRecord);
	                        setCommissioningForm(defaultCommissioningForm); setFormError(null);
	                      },
	                      onRefresh: () => selectedRecord && void refreshMut.mutate(selectedRecord.id),
	                      onFlagAttention: () => canRecover && setAttentionTarget(selectedRecord),
	                      onResolve: () =>
	                        canRecover && resolveMut.mutate({ id: selectedRecord.id, rowVersion: selectedRecord.rowVersion }),
	                      onSuspend: () => canManageDeviceLifecycle && openConfirm({ action: "suspend", device: selectedRecord }),
	                      onActivate: () => canManageDeviceLifecycle && activateMut.mutate(selectedRecord.id),
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
      {connectOpen ? (
        <ConnectDeviceDialog
          form={connectForm}
          onChange={setConnectForm}
          onClose={() => { setConnectOpen(false); provisionMut.reset(); }}
          onSubmit={() => provisionMut.mutate(connectForm)}
          busy={provisionMut.isPending}
          error={provisionMut.isError ? (provisionMut.error as Error)?.message : null}
        />
      ) : null}

      {/* STEP 2 — Connection established. One-time credentials + live pairing indicator. */}
      {provisionResult ? (
        <DeviceCredentialsDialog
          result={provisionResult}
          onDone={() => void finishConnect()}
        />
      ) : null}

      {assignTarget ? (
        <ModalForm
          title={`${assignTarget.assignedVehicleId ? "Transfer" : "Install"} ${assignTarget.deviceName}`}
          onClose={() => { setAssignTarget(null); setInstallationForm(defaultInstallationForm); setFormError(null); assignMut.reset(); }}
          onSubmit={(event) => {
            event.preventDefault();
            // DEF-022: all field conversion/validation happens BEFORE mutate is called.
            // A throw inside the mutate argument is swallowed silently (zero network
            // I/O, no visible error), so failures must land in formError instead.
            setFormError(null);
            let effectiveAtIso = "";
            let odometer: number | null = null;
            try {
              const odometerText = installationForm.odometerAtInstallation.trim();
              odometer = odometerText ? Number(odometerText) : null;
              if (odometerText && (!Number.isFinite(odometer) || Number(odometer) < 0)) {
                throw new Error("Enter a valid odometer at installation (a number of 0 or greater).");
              }
              effectiveAtIso = toUtcIso(installationForm.effectiveAt, assignTarget.assignedVehicleId ? "transfer effective time" : "installation effective time");
            } catch (validationError) {
              setFormError(validationError instanceof Error ? validationError.message : "Enter valid installation details before submitting.");
              return;
            }
            assignMut.mutate({
              deviceId: assignTarget.id,
              input: {
                vehicleId: installationForm.vehicleId,
                deviceRole: installationForm.deviceRole,
                isPrimary: installationForm.primaryDesignation === "primary",
                effectiveAt: effectiveAtIso,
                installationLocation: installationForm.installationLocation,
                odometerAtInstallation: odometer,
                commissioningMethod: installationForm.commissioningMethod,
                assignmentReason: installationForm.assignmentReason,
                removalReason: assignTarget.assignedVehicleId ? installationForm.removalReason : undefined,
              },
            });
          }}
          submitLabel={assignTarget.assignedVehicleId ? "Transfer Device" : "Install Device"}
          busy={assignMut.isPending}
          error={formError ?? (assignMut.error instanceof Error ? assignMut.error.message : null)}
        >
          <p className="mb-4 rounded-xl border border-sky-200 bg-sky-50 p-3 text-sm text-sky-800">
            This creates effective-dated installation history. Enter the observed facts; OpsTrax will not infer the role, primary designation, time, location, odometer, method, or reasons.
          </p>
          <div className="grid gap-4 md:grid-cols-2">
            <FormField label="Vehicle (required)">
              <select className="field w-full" value={installationForm.vehicleId} onChange={(event) => updateInstallationForm((form) => ({ ...form, vehicleId: event.target.value }))} required>
                <option value="">Select a vehicle</option>
                {vehicleOptions.map((vehicle) => (
                  <option key={String(vehicle.id ?? vehicle.vehicleId)} value={String(vehicle.id ?? vehicle.vehicleId)} disabled={String(vehicle.id ?? vehicle.vehicleId) === assignTarget.assignedVehicleId}>
                    {String(vehicle.vehicleCode ?? vehicle.vehicleId)} · {String(vehicle.status ?? "Fleet asset")}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Device role (required)">
              <select className="field w-full" value={installationForm.deviceRole} onChange={(event) => updateInstallationForm((form) => ({ ...form, deviceRole: event.target.value }))} required>
                <option value="">Select the installed role</option>
                {INSTALLATION_ROLES.map((role) => <option key={role} value={role}>{role}</option>)}
              </select>
            </FormField>
            <FormField label="Vehicle role designation (required)">
              <select className="field w-full" value={installationForm.primaryDesignation} onChange={(event) => updateInstallationForm((form) => ({ ...form, primaryDesignation: event.target.value as InstallationFormState["primaryDesignation"] }))} required>
                <option value="">Select primary or secondary</option>
                <option value="primary">Primary for this role</option>
                <option value="secondary">Secondary for this role</option>
              </select>
            </FormField>
            <FormField label={`${assignTarget.assignedVehicleId ? "Transfer" : "Installation"} effective time (required)`}>
              <input className="field w-full" type="datetime-local" value={installationForm.effectiveAt} onChange={(event) => updateInstallationForm((form) => ({ ...form, effectiveAt: event.target.value }))} required />
            </FormField>
            <FormField label="Installation location (required)">
              <input className="field w-full" maxLength={160} value={installationForm.installationLocation} onChange={(event) => updateInstallationForm((form) => ({ ...form, installationLocation: event.target.value }))} placeholder="Bay, depot, or service location" required />
            </FormField>
            <FormField label="Odometer at installation (required)">
              <input className="field w-full" type="number" min="0" step="0.1" value={installationForm.odometerAtInstallation} onChange={(event) => updateInstallationForm((form) => ({ ...form, odometerAtInstallation: event.target.value }))} required />
            </FormField>
            <FormField label="Commissioning method (required)">
              <input className="field w-full" maxLength={80} value={installationForm.commissioningMethod} onChange={(event) => updateInstallationForm((form) => ({ ...form, commissioningMethod: event.target.value }))} placeholder="e.g. authenticated heartbeat + GNSS fix" required />
            </FormField>
            <FormField label="Assignment reason (required)">
              <input className="field w-full" minLength={4} maxLength={500} value={installationForm.assignmentReason} onChange={(event) => updateInstallationForm((form) => ({ ...form, assignmentReason: event.target.value }))} required />
            </FormField>
            {assignTarget.assignedVehicleId ? (
              <FormField label="Prior installation removal reason (required)">
                <input className="field w-full" minLength={4} maxLength={500} value={installationForm.removalReason} onChange={(event) => updateInstallationForm((form) => ({ ...form, removalReason: event.target.value }))} required />
              </FormField>
            ) : null}
          </div>
        </ModalForm>
      ) : null}

      {removalTarget ? (
        <ModalForm
          title={`Remove ${removalTarget.deviceName} installation`}
          onClose={() => { setRemovalTarget(null); setRemovalForm(defaultRemovalForm); setFormError(null); unassignMut.reset(); }}
          onSubmit={(event) => {
            event.preventDefault();
            // DEF-022 class: convert/validate before mutate so failures are visible.
            setFormError(null);
            let effectiveToIso = "";
            try {
              effectiveToIso = toUtcIso(removalForm.effectiveTo, "removal effective time");
            } catch (validationError) {
              setFormError(validationError instanceof Error ? validationError.message : "Enter a valid removal effective time.");
              return;
            }
            unassignMut.mutate({
              deviceId: removalTarget.id,
              input: {
                effectiveTo: effectiveToIso,
                removalReason: removalForm.removalReason,
              },
            });
          }}
          submitLabel="Record Removal"
          busy={unassignMut.isPending}
          error={formError ?? (unassignMut.error instanceof Error ? unassignMut.error.message : null)}
        >
          <div className="grid gap-4 md:grid-cols-2">
            <FormField label="Removal effective time (required)"><input className="field w-full" type="datetime-local" value={removalForm.effectiveTo} onChange={(event) => updateRemovalForm((form) => ({ ...form, effectiveTo: event.target.value }))} required /></FormField>
            <FormField label="Removal reason (required)"><input className="field w-full" minLength={4} maxLength={500} value={removalForm.removalReason} onChange={(event) => updateRemovalForm((form) => ({ ...form, removalReason: event.target.value }))} required /></FormField>
          </div>
        </ModalForm>
      ) : null}

      {commissionTarget ? (
        <ModalForm
          title={`Commission ${commissionTarget.deviceName}`}
          onClose={() => { setCommissionTarget(null); setCommissioningForm(defaultCommissioningForm); setFormError(null); installMut.reset(); }}
          onSubmit={(event) => {
            event.preventDefault();
            // DEF-022 class: a bare return on validation gives no feedback — surface it.
            setFormError(null);
            if (!commissioningForm.result) {
              setFormError("Select the observed commissioning result (Passed or Failed).");
              return;
            }
            installMut.mutate({
              deviceId: commissionTarget.id,
              input: { result: commissioningForm.result, verificationReference: commissioningForm.verificationReference },
            });
          }}
          submitLabel="Record Commissioning Result"
          busy={installMut.isPending}
          error={formError ?? (installMut.error instanceof Error ? installMut.error.message : null)}
        >
          <p className="mb-4 rounded-xl border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
            Select the result actually observed. Passed requires an authenticated device heartbeat; no result is selected automatically.
          </p>
          <div className="grid gap-4 md:grid-cols-2">
            <FormField label="Observed result (required)">
              <select className="field w-full" value={commissioningForm.result} onChange={(event) => updateCommissioningForm((form) => ({ ...form, result: event.target.value as DeviceCommissioningInput["result"] }))} required>
                <option value="">Select Passed or Failed</option>
                <option value="Failed">Failed</option>
                <option value="Passed">Passed</option>
              </select>
            </FormField>
            <FormField label="Evidence / failure reference (required)"><input className="field w-full" maxLength={500} value={commissioningForm.verificationReference} onChange={(event) => updateCommissioningForm((form) => ({ ...form, verificationReference: event.target.value }))} placeholder="Work order, test report, or failure details" required /></FormField>
          </div>
        </ModalForm>
      ) : null}

      {rotatedCredentials ? (
        <RotatedCredentialsDialog credentials={rotatedCredentials} onDone={() => { setRotatedCredentials(null); rotateSecretMut.reset(); }} />
      ) : null}

      {attentionTarget ? (
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

      {quarantineTarget ? (
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

      {confirmTarget ? (
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
  actionContracts,
  lifecycleError,
  onDismissLifecycleError,
}: {
  detail: DeviceDetailRecord;
  actionContracts: DeviceActionContract[];
  lifecycleError?: unknown;
  onDismissLifecycleError: () => void;
}) {
  const { device } = detail;
  // Guard every [0] access — these live sub-feeds are frequently empty. `telemetry`
  // is a single live position point (or none), and `diagnostics` are active fault codes.
  const latestTelemetry = detail.telemetry[0] ?? null;
  const latestDiagnostic = detail.diagnostics[0] ?? null;
  const latestSensor = detail.sensorReadings[0] ?? null;
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
        {actionContracts.map((contract) => (
          <ActionContractBadge key={`contract-${contract.key}`} contract={contract} />
        ))}
      </div>
      <div className="mt-6 flex flex-wrap gap-3">
        {actionContracts.map((contract) => (
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
            <strong className="block">Device lifecycle action failed</strong>
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
        <PanelSection title="Assignment History">
          <TimelineList rows={detail.assignmentHistory.map((row) => ({
            id: String(row.id ?? ""),
            title: `${cell(row.fromState)} → ${cell(row.toState)}`,
            subtitle: cell(row.reason) !== "—" ? cell(row.reason) : cell(row.reasonCode),
            meta: cell(row.occurredAt) === "—" ? "" : String(row.occurredAt),
          }))} emptyText="No assignment history available for this device." />
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
        <PanelSection title="Reported Firmware (read-only)">
          <MiniGrid rows={[["Current reported version", cell(device.firmwareVersion)]]} />
          <p className="mt-3 text-sm text-slate-400">OTA scheduling and firmware history are not connected. No firmware operation is available from this page.</p>
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
          <p className="text-xs text-slate-500">
            The device is registered uninstalled. After copying its one-time credentials,
            use Install on vehicle to create the governed effective-dated installation.
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

// ── STEP 2: Connection established — credentials + live pairing ──────────────
// Displays the one-time apiKey + hmacSecret (neumorphic inset wells with Copy),
// the ingest endpoint + a "how to connect" block, and a live pairing indicator
// that polls getDeviceConnectionState until the device streams its first
// heartbeat. NEVER fabricates "connected" — it flips only on connected:true.
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
    // Poll for the first heartbeat every 5s; stop once the device is connected.
    refetchInterval: (query) => (query.state.data?.connected ? false : 5000),
    refetchOnWindowFocus: false,
  });
  const connected = Boolean(connectionQ.data?.connected);

  return (
    <div className="fixed inset-0 z-[60] grid place-items-center bg-black/60 p-4" role="dialog" aria-modal="true" aria-labelledby="device-credentials-title">
      <div className="panel max-h-[90vh] w-full max-w-2xl overflow-y-auto p-6">
        <div className="flex items-start gap-3">
          <div className="grid h-11 w-11 place-items-center rounded-2xl border border-emerald-200 bg-emerald-50 text-emerald-600 shadow-inner">
            <CheckCircle2 className="h-5 w-5" />
          </div>
          <div>
            <h2 id="device-credentials-title" className="text-2xl font-semibold text-slate-900">
              {connected ? "Connection established" : "Device credentials created"}
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
            <li>3. Sign the request body with the HMAC secret; the first accepted POST completes pairing.</li>
          </ol>
        </div>

        {/* Live pairing indicator — real state only */}
        <div className="clay-card mt-5 flex items-center justify-between gap-4 p-4">
          <div className="flex items-center gap-3">
            {connected ? (
              <span className="live-dot" aria-hidden />
            ) : (
              <span className="inline-block h-[7px] w-[7px] flex-shrink-0 animate-pulse rounded-full bg-amber-400" aria-hidden />
            )}
            <div>
              <p className={connected ? "text-sm font-semibold text-emerald-700" : "text-sm font-semibold text-slate-800"}>
                {connectionQ.isError
                  ? "Unable to check connection status"
                  : connected
                    ? "Connected — device is streaming"
                    : "Waiting for first heartbeat…"}
              </p>
              <p className="mt-0.5 text-xs text-slate-500">
                {connected && connectionQ.data?.lastSeenAt
                  ? `Last seen ${connectionQ.data.lastSeenAt}`
                  : connectionQ.isError
                    ? "Retrying automatically."
                    : "Polling the device for its first authenticated telemetry POST."}
              </p>
            </div>
          </div>
          <RadioTower className={connected ? "h-5 w-5 text-emerald-500" : "h-5 w-5 text-slate-300"} />
        </div>

        <div className="mt-6 flex justify-end gap-3">
          <button type="button" className="btn-primary" onClick={onDone}>
            {connected ? "Done" : "Close and finish later"}
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

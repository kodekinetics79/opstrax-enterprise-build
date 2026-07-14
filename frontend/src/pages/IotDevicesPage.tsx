import { FormEvent, useEffect, useMemo, useState } from "react";
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
  Edit3,
  FileUp,
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
  Wrench,
  X,
} from "lucide-react";
import { useNavigate } from "react-router-dom";
import { EmptyState, ErrorState, KpiCard, LoadingState, PageHeader, RiskBadge, StatusBadge } from "@/components/ui";
import { PERMISSIONS } from "@/auth/rbacConfig";
import { useHasPermission } from "@/hooks/usePermission";
import { vehiclesApi } from "@/services/vehiclesApi";
import {
  telematicsService,
  type DeviceCommandRecord,
  type DeviceDetailRecord,
  type DeviceProvisionResult,
} from "@/services/telematicsService";
import type { AnyRecord } from "@/types";

type DeviceTab =
  | "all"
  | "unassigned"
  | "offline"
  | "attention"
  | "firmware"
  | "provisioning"
  | "diagnostics"
  | "installations"
  | "data-health"
  | "providers";

type DeviceFormState = {
  deviceName: string;
  deviceType: string;
  provider: string;
  serialNumber: string;
  identifier: string;
  imei: string;
  simNumber: string;
  assignedVehicleCode: string;
  firmwareVersion: string;
  powerStatus: string;
  supportStatus: string;
  complianceStatus: string;
};

type FirmwareFormState = {
  targetVersion: string;
  scheduledFor: string;
};

// Minimal, honest inputs for INITIATING A CONNECTION (the Render/Vercel model).
// The device serial is the real key the backend provisions credentials against.
// IMEI is collected too (optional): hardware GPS trackers (GT06/Concox/PT40-class)
// are resolved by IMEI at the trusted-gateway ingest, so onboarding one end-to-end
// needs it. SIM/firmware/power/compliance remain out of the connection handshake.
type ConnectFormState = {
  serialNumber: string;
  imei: string;
  provider: string;
  deviceModel: string;
  assignedVehicleId: string;
};

const defaultConnectForm: ConnectFormState = {
  serialNumber: "",
  imei: "",
  provider: "",
  deviceModel: "",
  assignedVehicleId: "",
};

const DEVICE_TABS: Array<{ key: DeviceTab; label: string }> = [
  { key: "all", label: "All Devices" },
  { key: "unassigned", label: "Unassigned" },
  { key: "offline", label: "Offline" },
  { key: "attention", label: "Needs Attention" },
  { key: "firmware", label: "Firmware Updates" },
  { key: "provisioning", label: "Provisioning" },
  { key: "diagnostics", label: "Diagnostics" },
  { key: "installations", label: "Installations" },
  { key: "data-health", label: "Data Health" },
  { key: "providers", label: "Provider Integrations" },
];

const defaultForm: DeviceFormState = {
  deviceName: "",
  deviceType: "ELD device",
  provider: "Motive",
  serialNumber: "",
  identifier: "",
  imei: "",
  simNumber: "",
  assignedVehicleCode: "",
  firmwareVersion: "1.0.0",
  powerStatus: "Vehicle power",
  supportStatus: "Enterprise",
  complianceStatus: "Pending review",
};

function toForm(row?: DeviceCommandRecord | null): DeviceFormState {
  if (!row) return defaultForm;
  return {
    deviceName: row.deviceName,
    deviceType: row.deviceType,
    provider: row.provider,
    serialNumber: row.serialNumber,
    identifier: row.identifier,
    imei: row.imei,
    simNumber: row.simNumber,
    assignedVehicleCode: row.assignedVehicleCode,
    firmwareVersion: row.firmwareVersion,
    powerStatus: row.powerStatus,
    supportStatus: row.supportStatus,
    complianceStatus: row.complianceStatus,
  };
}

function downloadCsv(filename: string, body: string) {
  const anchor = document.createElement("a");
  anchor.href = URL.createObjectURL(new Blob([body], { type: "text/csv" }));
  anchor.download = filename;
  anchor.click();
}

function emptyStateForTab(tab: DeviceTab) {
  if (tab === "offline") return { title: "No offline devices", subtitle: "Every scoped device is checking in within the current monitoring window." };
  if (tab === "firmware") return { title: "No firmware updates pending", subtitle: "Every visible device is already on its approved firmware channel." };
  if (tab === "providers") return { title: "Device integration required", subtitle: "Connect a telematics provider to activate live GPS, engine, and ELD data." };
  return { title: "No devices found", subtitle: "Refine the search, switch tabs, or register a device for this fleet." };
}

function activeTabCount(tab: DeviceTab, row: DeviceCommandRecord) {
  if (tab === "all") return true;
  if (tab === "unassigned") return !row.assignedVehicleCode;
  if (tab === "offline") return /offline/i.test(row.connectionStatus);
  if (tab === "attention") return /attention|offline/i.test(row.connectionStatus) || row.openAlertCount > 0;
  if (tab === "firmware") return row.firmwareVersion !== row.targetFirmwareVersion;
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
  const canFirmware = hasPermission(PERMISSIONS.TELEMATICS_DEVICES_FIRMWARE);
  const canExport = hasPermission(PERMISSIONS.TELEMATICS_DEVICES_EXPORT);
  const canManageProviders = hasPermission(PERMISSIONS.TELEMATICS_PROVIDERS_MANAGE);

  const [tab, setTab] = useState<DeviceTab>("all");
  const [search, setSearch] = useState("");
  const [selectedId, setSelectedId] = useState<string | number | null>(null);
  // "create" now opens the connection flow (register → credentials → pairing);
  // "edit" keeps the full field editor for an already-provisioned device.
  const [deviceModal, setDeviceModal] = useState<{ mode: "edit"; id: string | number } | null>(null);
  const [deviceForm, setDeviceForm] = useState<DeviceFormState>(defaultForm);
  // Step 1 of the connect flow — the minimal register-connection form.
  const [connectOpen, setConnectOpen] = useState(false);
  const [connectForm, setConnectForm] = useState<ConnectFormState>(defaultConnectForm);
  // Step 2 of the connect flow — the one-time credentials + live pairing panel.
  // Populated ONLY by a real provisionDevice() response.
  const [provisionResult, setProvisionResult] = useState<DeviceProvisionResult | null>(null);
  const [assignTarget, setAssignTarget] = useState<DeviceCommandRecord | null>(null);
  const [assignVehicleCode, setAssignVehicleCode] = useState("");
  const [firmwareTarget, setFirmwareTarget] = useState<DeviceCommandRecord | null>(null);
  const [firmwareForm, setFirmwareForm] = useState<FirmwareFormState>({ targetVersion: "", scheduledFor: "" });
  const [attentionTarget, setAttentionTarget] = useState<DeviceCommandRecord | null>(null);
  const [attentionNotes, setAttentionNotes] = useState("");
  const [openMenuId, setOpenMenuId] = useState<string | number | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const devicesQ = useQuery({ queryKey: ["telematics", "devices"], queryFn: telematicsService.getDevices, staleTime: 20_000 });
  const providersQ = useQuery({ queryKey: ["telematics", "providers"], queryFn: telematicsService.getProviders, staleTime: 20_000 });
  const vehiclesQ = useQuery({ queryKey: ["vehicles", "list"], queryFn: vehiclesApi.list, staleTime: 20_000 });
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
        provider: payload.provider.trim(),
        deviceName: payload.deviceModel.trim() || payload.serialNumber.trim(),
        deviceType: payload.deviceModel.trim() || "Device",
        assignedVehicleId: payload.assignedVehicleId || null,
      }),
    onSuccess: async (result) => {
      setProvisionResult(result);
      setConnectOpen(false);
      setConnectForm(defaultConnectForm);
      // Surface the new (not-yet-streaming) device in the list immediately.
      await refreshAll();
    },
  });
  const updateMut = useMutation({
    mutationFn: ({ id, payload }: { id: string | number; payload: DeviceFormState }) => telematicsService.updateDevice(id, payload),
    onSuccess: async () => {
      setNotice("Device updated successfully.");
      setDeviceModal(null);
      await refreshAll();
    },
  });
  const archiveMut = useMutation({
    mutationFn: (id: string | number) => telematicsService.archiveDevice(id),
    onSuccess: async () => {
      setSelectedId(null);
      setNotice("Device archived successfully.");
      await refreshAll();
    },
  });
  const assignMut = useMutation({
    mutationFn: ({ deviceId, vehicleId }: { deviceId: string | number; vehicleId: string }) => telematicsService.assignDeviceToVehicle(deviceId, vehicleId),
    onSuccess: async () => {
      setAssignTarget(null);
      setAssignVehicleCode("");
      setNotice("Device assigned successfully.");
      await refreshAll();
    },
  });
  // Some device operations have no persistence endpoint in the current backend, so
  // the service returns { success:false, reason }. We surface that HONESTLY rather
  // than claiming a success the backend never performed. When (and only when) a real
  // endpoint is added and the service starts returning success, the same handler
  // shows the confirmation. `okNotice`/`failNotice` are the truthful messages.
  const noticeFromResult = (
    result: unknown,
    okNotice: string,
    failNotice: string,
  ) => {
    const ok = !(result && typeof result === "object" && "success" in result && (result as { success?: boolean }).success === false);
    if (ok) {
      setNotice(okNotice);
    } else {
      const reason = (result as { reason?: string }).reason;
      setNotice(reason ? `${failNotice} (${reason})` : failNotice);
    }
  };
  const unassignMut = useMutation({
    mutationFn: (deviceId: string | number) => telematicsService.unassignDevice(deviceId),
    onSuccess: async (result) => {
      noticeFromResult(result, "Device unassigned successfully.", "Unassign is not available for this device yet.");
      await refreshAll();
    },
  });
  const installMut = useMutation({
    mutationFn: (deviceId: string | number) => telematicsService.markInstalled(deviceId),
    onSuccess: async (result) => {
      noticeFromResult(result, "Installation checklist completed.", "Marking installed is not available for this device yet.");
      await refreshAll();
    },
  });
  const diagnosticsMut = useMutation({
    mutationFn: (deviceId: string | number) => telematicsService.runDeviceDiagnostics(deviceId),
    onSuccess: async (result) => {
      noticeFromResult(result, "Diagnostics completed.", "On-demand diagnostics are not available for this device yet.");
      await refreshAll();
    },
  });
  const refreshMut = useMutation({
    mutationFn: (deviceId: string | number) => telematicsService.refreshDeviceStatus(deviceId),
    onSuccess: async (result) => {
      noticeFromResult(result, "Device status refreshed.", "Manual status refresh is not available for this device yet.");
      await refreshAll();
    },
  });
  const firmwareMut = useMutation({
    mutationFn: ({ id, payload }: { id: string | number; payload: FirmwareFormState }) => telematicsService.scheduleFirmwareUpdate(id, payload),
    onSuccess: async (result) => {
      setFirmwareTarget(null);
      setFirmwareForm({ targetVersion: "", scheduledFor: "" });
      noticeFromResult(result, "Firmware update scheduled.", "Firmware scheduling is not available for this device yet.");
      await refreshAll();
    },
  });
  const providerSyncMut = useMutation({
    mutationFn: (providerId: string | number) => telematicsService.syncProvider(providerId),
    onSuccess: async (result) => {
      noticeFromResult(result, "Provider sync completed.", "Provider sync is not available yet.");
      await refreshAll();
    },
  });
  const attentionMut = useMutation({
    mutationFn: ({ id, notes }: { id: string | number; notes: string }) => telematicsService.markDeviceAttention(id, notes),
    onSuccess: async () => {
      setAttentionTarget(null);
      setAttentionNotes("");
      setNotice("Recovery workflow opened.");
      await refreshAll();
    },
  });
  const resolveMut = useMutation({
    mutationFn: (id: string | number) => telematicsService.resolveDeviceAttention(id),
    onSuccess: async () => {
      setNotice("Recovery cleared and device returned to service.");
      await refreshAll();
    },
  });

  const deviceRows = useMemo(() => {
    const query = search.trim().toLowerCase();
    return (devicesQ.data ?? [])
      .filter((row) => row.lifecycleStatus !== "Archived")
      .filter((row) => activeTabCount(tab, row))
      .filter((row) => {
        const haystack = [
          row.deviceName,
          row.deviceType,
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

  const selectedRecord = deviceRows.find((row) => String(row.id) === String(selectedId)) ?? detailQ.data?.device ?? null;

  const offlineCount = (devicesQ.data ?? []).filter((row) => /offline/i.test(row.connectionStatus)).length;
  const attentionCount = (devicesQ.data ?? []).filter((row) => /attention|offline/i.test(row.connectionStatus) || row.openAlertCount > 0).length;
  const avgHealth = Math.round((devicesQ.data ?? []).reduce((sum, row) => sum + Number(row.dataHealthScore), 0) / Math.max((devicesQ.data ?? []).length, 1));

  if (devicesQ.isLoading) return <DeviceLoadingState />;
  if (devicesQ.isError) return <ErrorState message="Unable to load the device command center right now." />;

  const openConnect = () => {
    setConnectForm(defaultConnectForm);
    setConnectOpen(true);
  };

  const openEdit = (row: DeviceCommandRecord) => {
    setDeviceForm(toForm(row));
    setDeviceModal({ mode: "edit", id: row.id });
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
        description="GPS trackers, ELD units, OBD gateways, sensors — connection, firmware and diagnostics per device."
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

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <KpiCard label="Managed Devices" value={(devicesQ.data ?? []).length} status="Active" icon={<RadioTower className="h-4 w-4" />} />
        <KpiCard label="Offline" value={offlineCount} status={offlineCount ? "Critical" : "Healthy"} icon={<WifiOff className="h-4 w-4" />} />
        <KpiCard label="Needs Attention" value={attentionCount} status={attentionCount ? "Watch" : "Healthy"} icon={<Activity className="h-4 w-4" />} />
        <KpiCard label="Average Data Health" value={`${Number.isFinite(avgHealth) ? avgHealth : 0}%`} status={avgHealth >= 85 ? "Healthy" : avgHealth >= 70 ? "Watch" : "Critical"} icon={<Cpu className="h-4 w-4" />} />
      </div>

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
            {DEVICE_TABS.map((item) => (
              <button key={item.key} className={tab === item.key ? "btn-primary py-2 text-xs" : "btn-ghost py-2 text-xs"} onClick={() => setTab(item.key)}>
                {item.label}
              </button>
            ))}
          </div>
        </div>

        {tab === "providers" ? (
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
                    <div className="flex justify-between"><span>Scoped devices</span><span>{String(provider.deviceCount)}</span></div>
                    <div className="flex justify-between"><span>Needs follow-up</span><span>{String((provider as AnyRecord).pendingDevices ?? 0)}</span></div>
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
                      title={actionTitle(canManageProviders, "Run a provider sync for scoped device inventory.")}
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
                  {["Device", "Provider", "Identifier", "Vehicle", "Driver", "Firmware", "Check-in", "Connection", "Power", "Signal", "Health", "Install", "Compliance", "Support", "Actions"].map((header) => (
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
                        <p className="text-xs text-slate-400">{row.deviceType} · {row.serialNumber || row.identifier}</p>
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
                    <td className="px-4 py-3 text-slate-700">{row.powerStatus}</td>
                    <td className="px-4 py-3"><RiskBadge risk={row.signalStrength} /></td>
                    <td className="px-4 py-3 text-slate-700">{row.dataHealthScore}%</td>
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
                            {canUpdate && <button type="button" className="flex w-full items-center gap-2 px-4 py-2 text-sm text-slate-700 hover:bg-slate-50" onClick={() => { openEdit(row); setOpenMenuId(null); }}>Edit Device</button>}
                            {canAssign && <button type="button" className="flex w-full items-center gap-2 px-4 py-2 text-sm text-slate-700 hover:bg-slate-50" onClick={() => { setAssignTarget(row); setAssignVehicleCode(row.assignedVehicleCode || ""); setOpenMenuId(null); }}>Assign to Vehicle</button>}
                            {canDiagnostics && <button type="button" className="flex w-full items-center gap-2 px-4 py-2 text-sm text-slate-700 hover:bg-slate-50 disabled:opacity-50" disabled={diagnosticsMut.isPending} onClick={() => { diagnosticsMut.mutate(row.id); setOpenMenuId(null); }}>Run Diagnostics</button>}
                            <button type="button" className="flex w-full items-center gap-2 px-4 py-2 text-sm text-slate-700 hover:bg-slate-50" onClick={() => { refreshMut.mutate(row.id); setOpenMenuId(null); }}>Refresh Status</button>
                            {canFirmware && <button type="button" className="flex w-full items-center gap-2 px-4 py-2 text-sm text-slate-700 hover:bg-slate-50" onClick={() => { setFirmwareTarget(row); setFirmwareForm({ targetVersion: row.targetFirmwareVersion, scheduledFor: "" }); setOpenMenuId(null); }}>Schedule Firmware</button>}
                            {canDelete && <button type="button" className="flex w-full items-center gap-2 px-4 py-2 text-sm text-red-600 hover:bg-red-50" onClick={() => { if (window.confirm(`Archive ${row.deviceName}?`)) { archiveMut.mutate(row.id); } setOpenMenuId(null); }}>Archive Device</button>}
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
            <button className="float-right icon-btn" onClick={() => setSelectedId(null)}><X className="h-4 w-4" /></button>
            {detailQ.isLoading ? (
              <LoadingState />
            ) : detailQ.isError || !detailQ.data ? (
              <ErrorState message="Unable to load this device." />
            ) : (
              <DeviceDetailDrawer
                detail={detailQ.data}
                onEdit={() => selectedRecord && openEdit(selectedRecord)}
                onAssign={() => selectedRecord && (setAssignTarget(selectedRecord), setAssignVehicleCode(selectedRecord.assignedVehicleCode || ""))}
                onUnassign={() => selectedRecord && canAssign && unassignMut.mutate(selectedRecord.id)}
                onArchive={() => selectedRecord && canDelete && window.confirm(`Archive ${selectedRecord.deviceName}?`) && archiveMut.mutate(selectedRecord.id)}
                onMarkInstalled={() => selectedRecord && canUpdate && installMut.mutate(selectedRecord.id)}
                onRunDiagnostics={() => selectedRecord && canDiagnostics && diagnosticsMut.mutate(selectedRecord.id)}
                onRefresh={() => selectedRecord && refreshMut.mutate(selectedRecord.id)}
                onScheduleFirmware={() => selectedRecord && (setFirmwareTarget(selectedRecord), setFirmwareForm({ targetVersion: selectedRecord.targetFirmwareVersion, scheduledFor: "" }))}
                onFlagAttention={() => selectedRecord && setAttentionTarget(selectedRecord)}
                onResolve={() => selectedRecord && canDiagnostics && resolveMut.mutate(selectedRecord.id)}
                canUpdate={canUpdate}
                canDelete={canDelete}
                canAssign={canAssign}
                canDiagnostics={canDiagnostics}
                canFirmware={canFirmware}
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
          vehicleOptions={vehicleOptions}
        />
      ) : null}

      {/* STEP 2 — Connection established. One-time credentials + live pairing indicator. */}
      {provisionResult ? (
        <DeviceCredentialsDialog
          result={provisionResult}
          onDone={() => void finishConnect()}
        />
      ) : null}

      {deviceModal ? (
        <ModalForm
          title="Edit Device"
          onClose={() => setDeviceModal(null)}
          onSubmit={(event) => {
            event.preventDefault();
            updateMut.mutate({ id: deviceModal.id, payload: deviceForm });
          }}
          submitLabel="Save Device"
          busy={updateMut.isPending}
        >
          <div className="grid gap-4 md:grid-cols-2">
            <FormField label="Device Name"><input className="field w-full" value={deviceForm.deviceName} onChange={(event) => setDeviceForm((form) => ({ ...form, deviceName: event.target.value }))} required /></FormField>
            <FormField label="Device Type"><input className="field w-full" value={deviceForm.deviceType} onChange={(event) => setDeviceForm((form) => ({ ...form, deviceType: event.target.value }))} required /></FormField>
            <FormField label="Provider"><input className="field w-full" value={deviceForm.provider} onChange={(event) => setDeviceForm((form) => ({ ...form, provider: event.target.value }))} required /></FormField>
            <FormField label="Serial Number"><input className="field w-full" value={deviceForm.serialNumber} onChange={(event) => setDeviceForm((form) => ({ ...form, serialNumber: event.target.value }))} required /></FormField>
            <FormField label="Device Identifier"><input className="field w-full" value={deviceForm.identifier} onChange={(event) => setDeviceForm((form) => ({ ...form, identifier: event.target.value }))} required /></FormField>
            <FormField label="IMEI"><input className="field w-full" value={deviceForm.imei} onChange={(event) => setDeviceForm((form) => ({ ...form, imei: event.target.value }))} required /></FormField>
            <FormField label="SIM Number"><input className="field w-full" value={deviceForm.simNumber} onChange={(event) => setDeviceForm((form) => ({ ...form, simNumber: event.target.value }))} /></FormField>
            <FormField label="Assigned Vehicle"><input className="field w-full" value={deviceForm.assignedVehicleCode} onChange={(event) => setDeviceForm((form) => ({ ...form, assignedVehicleCode: event.target.value }))} aria-label="Assigned vehicle code or installation queue" /></FormField>
            <FormField label="Firmware Version"><input className="field w-full" value={deviceForm.firmwareVersion} onChange={(event) => setDeviceForm((form) => ({ ...form, firmwareVersion: event.target.value }))} /></FormField>
            <FormField label="Power Status"><input className="field w-full" value={deviceForm.powerStatus} onChange={(event) => setDeviceForm((form) => ({ ...form, powerStatus: event.target.value }))} /></FormField>
            <FormField label="Support Status"><input className="field w-full" value={deviceForm.supportStatus} onChange={(event) => setDeviceForm((form) => ({ ...form, supportStatus: event.target.value }))} /></FormField>
            <FormField label="Compliance Status"><input className="field w-full" value={deviceForm.complianceStatus} onChange={(event) => setDeviceForm((form) => ({ ...form, complianceStatus: event.target.value }))} /></FormField>
          </div>
        </ModalForm>
      ) : null}

      {assignTarget ? (
        <ModalForm
          title={`Assign ${assignTarget.deviceName}`}
          onClose={() => setAssignTarget(null)}
          onSubmit={(event) => {
            event.preventDefault();
            assignMut.mutate({ deviceId: assignTarget.id, vehicleId: assignVehicleCode });
          }}
          submitLabel="Assign Device"
          busy={assignMut.isPending}
        >
          <FormField label="Vehicle">
            <select className="field w-full" value={assignVehicleCode} onChange={(event) => setAssignVehicleCode(event.target.value)} required>
              <option value="">Select a vehicle</option>
              {vehicleOptions.map((vehicle) => (
                <option key={String(vehicle.id ?? vehicle.vehicleId)} value={String(vehicle.vehicleCode ?? vehicle.vehicleId)}>
                  {String(vehicle.vehicleCode ?? vehicle.vehicleId)} · {String(vehicle.status ?? "Fleet asset")}
                </option>
              ))}
            </select>
          </FormField>
        </ModalForm>
      ) : null}

      {firmwareTarget ? (
        <ModalForm
          title={`Schedule firmware for ${firmwareTarget.deviceName}`}
          onClose={() => setFirmwareTarget(null)}
          onSubmit={(event) => {
            event.preventDefault();
            firmwareMut.mutate({ id: firmwareTarget.id, payload: firmwareForm });
          }}
          submitLabel="Schedule Update"
          busy={firmwareMut.isPending}
        >
          <div className="grid gap-4 md:grid-cols-2">
            <FormField label="Target Version"><input className="field w-full" value={firmwareForm.targetVersion} onChange={(event) => setFirmwareForm((form) => ({ ...form, targetVersion: event.target.value }))} required /></FormField>
            <FormField label="Scheduled For"><input className="field w-full" type="datetime-local" value={firmwareForm.scheduledFor} onChange={(event) => setFirmwareForm((form) => ({ ...form, scheduledFor: event.target.value }))} required /></FormField>
          </div>
        </ModalForm>
      ) : null}

      {attentionTarget ? (
        <ModalForm
          title={`Open recovery for ${attentionTarget.deviceName}`}
          onClose={() => setAttentionTarget(null)}
          onSubmit={(event) => {
            event.preventDefault();
            attentionMut.mutate({ id: attentionTarget.id, notes: attentionNotes });
          }}
          submitLabel="Open Recovery"
          busy={attentionMut.isPending}
        >
          <FormField label="Recovery Notes">
            <textarea className="field h-24 w-full resize-none" value={attentionNotes} onChange={(event) => setAttentionNotes(event.target.value)} required />
          </FormField>
        </ModalForm>
      ) : null}
    </div>
  );
}

function DeviceDetailDrawer({
  detail,
  onEdit,
  onAssign,
  onUnassign,
  onArchive,
  onMarkInstalled,
  onRunDiagnostics,
  onRefresh,
  onScheduleFirmware,
  onFlagAttention,
  onResolve,
  canUpdate,
  canDelete,
  canAssign,
  canDiagnostics,
  canFirmware,
}: {
  detail: DeviceDetailRecord;
  onEdit: () => void;
  onAssign: () => void;
  onUnassign: () => void;
  onArchive: () => void;
  onMarkInstalled: () => void;
  onRunDiagnostics: () => void;
  onRefresh: () => void;
  onScheduleFirmware: () => void;
  onFlagAttention: () => void;
  onResolve: () => void;
  canUpdate: boolean;
  canDelete: boolean;
  canAssign: boolean;
  canDiagnostics: boolean;
  canFirmware: boolean;
}) {
  const { device } = detail;
  // Guard every [0] access — these live sub-feeds are frequently empty. `telemetry`
  // is a single live position point (or none), `diagnostics` are active fault codes
  // (or none), `sensorReadings`/`installations` have no verified source and are always [].
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
          <p className="mt-1 text-sm text-slate-400">{device.deviceType} · {device.provider} · {device.serialNumber || device.identifier}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <StatusBadge status={device.connectionStatus} />
          <RiskBadge risk={device.signalStrength} />
          <StatusBadge status={device.installStatus} />
        </div>
      </div>

      <div className="mt-6 flex flex-wrap gap-3">
        <ActionButton label="Edit Device" icon={<Edit3 className="h-4 w-4" />} allowed={canUpdate} onClick={onEdit} />
        <ActionButton label="Assign" icon={<ArrowRightLeft className="h-4 w-4" />} allowed={canAssign} onClick={onAssign} />
        <ActionButton label="Unassign" icon={<Truck className="h-4 w-4" />} allowed={canAssign && Boolean(device.assignedVehicleCode)} onClick={onUnassign} />
        <ActionButton label="Mark Installed" icon={<CheckCircle2 className="h-4 w-4" />} allowed={canUpdate} onClick={onMarkInstalled} />
        <ActionButton label="Run Diagnostics" icon={<Wrench className="h-4 w-4" />} allowed={canDiagnostics} onClick={onRunDiagnostics} />
        <ActionButton label="Refresh Status" icon={<RefreshCw className="h-4 w-4" />} allowed={true} onClick={onRefresh} />
        <ActionButton label="Schedule Firmware" icon={<FileUp className="h-4 w-4" />} allowed={canFirmware} onClick={onScheduleFirmware} />
        {/offline|attention/i.test(device.connectionStatus) ? (
          <ActionButton label="Resolve" icon={<ShieldCheck className="h-4 w-4" />} allowed={canDiagnostics} onClick={onResolve} />
        ) : (
          <ActionButton label="Needs Attention" icon={<Activity className="h-4 w-4" />} allowed={canDiagnostics} onClick={onFlagAttention} />
        )}
        <ActionButton label="Archive" icon={<Trash2 className="h-4 w-4" />} allowed={canDelete} onClick={onArchive} />
      </div>

      <div className="mt-6 grid gap-4 lg:grid-cols-3">
        <InfoBlock title="Overview" items={[
          ["Device name", device.deviceName],
          ["Type", device.deviceType],
          ["Provider", device.provider],
          ["Serial", device.serialNumber],
          ["IMEI", device.imei],
          ["Tenant", device.tenantName],
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
          ["Data health", `${device.dataHealthScore}%`],
          ["Install status", device.installStatus],
          ["Compliance", device.complianceSummary],
        ]} />
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
          {/* No assignment-history endpoint exists — always empty until one is added. */}
          <TimelineList rows={detail.assignmentHistory.map((row) => ({
            title: cell(row.status) === "—" ? "Assignment" : String(row.status),
            subtitle: `${cell(row.vehicleCode)} · ${cell(row.driverName)}`,
            meta: cell(row.assignedAt) === "—" ? "" : String(row.assignedAt),
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
        <PanelSection title="Firmware History">
          {/* No OTA/firmware-schedule endpoint exists — always empty until one is added. */}
          <TimelineList rows={detail.firmwareUpdates.map((row) => ({
            title: `${cell(row.currentVersion)} → ${cell(row.targetVersion)}`,
            subtitle: cell(row.status),
            meta: cell(row.scheduledFor) === "—" ? "" : String(row.scheduledFor),
          }))} emptyText="No firmware history available." />
        </PanelSection>
        <PanelSection title="Installation Checklist">
          {/* No installation-records endpoint exists — always empty until one is added. */}
          {(detail.installations[0]?.checklist ?? []).length ? (
            <div className="space-y-3">
              {(detail.installations[0]?.checklist ?? []).map((item: AnyRecord) => (
                <div key={String(item.item)} className="rounded-xl border border-white/[0.06] bg-black/10 p-3">
                  <div className="flex items-center justify-between gap-3">
                    <p className="font-medium text-white">{String(item.item)}</p>
                    <StatusBadge status={String(item.status)} />
                  </div>
                </div>
              ))}
            </div>
          ) : (
            <p className="text-sm text-slate-400">No installation checklist recorded for this device.</p>
          )}
        </PanelSection>
      </div>
    </>
  );
}

// ── STEP 1: Register connection ─────────────────────────────────────────────
// Minimal, honest form. Serial is the ONLY required field (the real key the
// backend provisions credentials against). IMEI is optional but is what a hardware
// GPS tracker is resolved by at ingest, so it is collected for GT06/PT40-class units.
// Provider, model/name and assigned vehicle are optional. SIM/firmware/power/
// compliance remain out of the connection handshake.
function ConnectDeviceDialog({
  form,
  onChange,
  onClose,
  onSubmit,
  busy,
  error,
  vehicleOptions,
}: {
  form: ConnectFormState;
  onChange: (form: ConnectFormState) => void;
  onClose: () => void;
  onSubmit: () => void;
  busy: boolean;
  error?: string | null;
  vehicleOptions: AnyRecord[];
}) {
  const serialValid = form.serialNumber.trim().length > 0;
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
          <FormField label="Assign to Vehicle (optional)">
            <select
              className="field w-full"
              value={form.assignedVehicleId}
              onChange={(event) => onChange({ ...form, assignedVehicleId: event.target.value })}
              aria-label="Assign device to a vehicle"
            >
              <option value="">Leave in installation queue</option>
              {vehicleOptions.map((vehicle) => (
                <option key={String(vehicle.id ?? vehicle.vehicleId)} value={String(vehicle.id ?? vehicle.vehicleId)}>
                  {String(vehicle.vehicleCode ?? vehicle.vehicleId ?? vehicle.id)} · {String(vehicle.status ?? "Fleet asset")}
                </option>
              ))}
            </select>
          </FormField>
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
            <h2 id="device-credentials-title" className="text-2xl font-semibold text-slate-900">Connection established</h2>
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

function ActionButton({ label, icon, allowed, onClick }: { label: string; icon: ReactNode; allowed: boolean; onClick: () => void }) {
  return (
    <button
      className={allowed ? "btn-ghost" : "btn-ghost opacity-60"}
      disabled={!allowed}
      title={actionTitle(allowed, label)}
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
}: {
  title: string;
  children: ReactNode;
  onClose: () => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
  submitLabel: string;
  busy: boolean;
}) {
  return (
    <div className="fixed inset-0 z-[60] grid place-items-center bg-black/60 p-4">
      <form className="panel max-h-[90vh] w-full max-w-4xl overflow-y-auto p-6" onSubmit={onSubmit}>
        <div className="flex items-center justify-between">
          <h2 className="text-2xl font-semibold text-slate-900">{title}</h2>
          <button type="button" className="icon-btn" onClick={onClose}><X className="h-4 w-4" /></button>
        </div>
        <div className="mt-6">{children}</div>
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

function TimelineList({ rows, emptyText }: { rows: Array<{ title: string; subtitle: string; meta: string }>; emptyText: string }) {
  if (!rows.length) {
    return <p className="text-sm text-slate-400">{emptyText}</p>;
  }

  return (
    <div className="flex h-full flex-col gap-3 overflow-y-auto">
      {rows.map((row) => (
        <div key={`${row.title}-${row.meta}`} className="rounded-xl border border-white/[0.06] bg-black/10 p-3">
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

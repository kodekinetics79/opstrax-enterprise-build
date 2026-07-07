import { FormEvent, useMemo, useState } from "react";
import type { ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Activity,
  ArrowRightLeft,
  CheckCircle2,
  ChevronDown,
  Cpu,
  Download,
  Edit3,
  FileUp,
  PlugZap,
  Plus,
  RadioTower,
  RefreshCw,
  Search,
  Settings2,
  ShieldCheck,
  Sparkles,
  Trash2,
  Truck,
  WifiOff,
  Wrench,
  X,
} from "lucide-react";
import { useNavigate } from "react-router-dom";
import { EmptyState, ErrorState, KpiCard, LoadingState, RiskBadge, Select, StatusBadge } from "@/components/ui";
import { PERMISSIONS } from "@/auth/rbacConfig";
import { useHasPermission } from "@/hooks/usePermission";
import { developmentFleetSeedData } from "@/data/developmentFleetSeedData";
import { telematicsService, type DeviceCommandRecord, type DeviceDetailRecord } from "@/services/telematicsService";
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
  const [deviceModal, setDeviceModal] = useState<{ mode: "create" | "edit"; id?: string | number } | null>(null);
  const [deviceForm, setDeviceForm] = useState<DeviceFormState>(defaultForm);
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

  const createMut = useMutation({
    mutationFn: (payload: DeviceFormState) => telematicsService.createDevice(payload),
    onSuccess: async () => {
      setNotice("Device registered successfully.");
      setDeviceModal(null);
      setDeviceForm(defaultForm);
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
  const unassignMut = useMutation({
    mutationFn: (deviceId: string | number) => telematicsService.unassignDevice(deviceId),
    onSuccess: async () => {
      setNotice("Device unassigned successfully.");
      await refreshAll();
    },
  });
  const installMut = useMutation({
    mutationFn: (deviceId: string | number) => telematicsService.markInstalled(deviceId),
    onSuccess: async () => {
      setNotice("Installation checklist completed.");
      await refreshAll();
    },
  });
  const diagnosticsMut = useMutation({
    mutationFn: (deviceId: string | number) => telematicsService.runDeviceDiagnostics(deviceId),
    onSuccess: async () => {
      setNotice("Diagnostics completed.");
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
  const firmwareMut = useMutation({
    mutationFn: ({ id, payload }: { id: string | number; payload: FirmwareFormState }) => telematicsService.scheduleFirmwareUpdate(id, payload),
    onSuccess: async () => {
      setFirmwareTarget(null);
      setFirmwareForm({ targetVersion: "", scheduledFor: "" });
      setNotice("Firmware update scheduled.");
      await refreshAll();
    },
  });
  const providerSyncMut = useMutation({
    mutationFn: (providerId: string | number) => telematicsService.syncProvider(providerId),
    onSuccess: async () => {
      setNotice("Provider sync completed.");
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

  const selectedRecord = deviceRows.find((row) => String(row.id) === String(selectedId)) ?? detailQ.data?.device ?? null;

  const offlineCount = (devicesQ.data ?? []).filter((row) => /offline/i.test(row.connectionStatus)).length;
  const attentionCount = (devicesQ.data ?? []).filter((row) => /attention|offline/i.test(row.connectionStatus) || row.openAlertCount > 0).length;
  const avgHealth = Math.round((devicesQ.data ?? []).reduce((sum, row) => sum + Number(row.dataHealthScore), 0) / Math.max((devicesQ.data ?? []).length, 1));

  if (devicesQ.isLoading) return <DeviceLoadingState />;
  if (devicesQ.isError) return <ErrorState message="Unable to load the device command center right now." />;

  const openCreate = () => {
    setDeviceForm(defaultForm);
    setDeviceModal({ mode: "create" });
  };

  const openEdit = (row: DeviceCommandRecord) => {
    setDeviceForm(toForm(row));
    setDeviceModal({ mode: "edit", id: row.id });
  };

  const exportCurrent = async () => {
    const csv = await telematicsService.exportDevicesCsv();
    downloadCsv("opstrax-device-command-center.csv", csv);
  };

  const emptyState = emptyStateForTab(tab);

  return (
    <div className="space-y-6 pb-10">
      {/* ── fh-hero header ─────────────────────────────────────── */}
      <header className="fh-hero relative">
        <span className="fh-hero-bar" />
        <span className="fh-hero-glow-1" />
        <span className="fh-hero-glow-2" />
        <div className="relative px-7 py-6">
          <div className="flex flex-wrap items-start justify-between gap-6">
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-3 mb-3">
                <span className="inline-flex items-center gap-1.5 rounded-lg bg-white/90 px-3 py-1 text-[10px] font-bold uppercase tracking-[0.2em] text-teal-700 ring-1 ring-teal-200/50 shadow-sm">
                  <RadioTower className="h-3 w-3" /> Telematics &amp; IoT
                </span>
                <span className="text-[11px] font-semibold text-slate-500">GPS trackers, ELD units, OBD gateways, dashcams, sensor packs, firmware posture, and diagnostics</span>
              </div>
              <h1 className="text-[32px] font-black tracking-tight leading-none cc-gradient-text sm:text-[36px]">
                Device Health
              </h1>
              <p className="mt-1 text-[13px] font-medium text-slate-400 tracking-wide">
                Operational control for IoT device inventory, assignments, telemetry, diagnostics, and installation readiness.
              </p>
            </div>
            <div className="flex items-center gap-2">
              <button
                className="fh-btn-ghost cursor-pointer"
                disabled={!canExport}
                title={actionTitle(canExport, "Export the current device inventory to CSV.")}
                onClick={() => canExport && void exportCurrent()}
              >
                <Download className="h-4 w-4" /> Export CSV
              </button>
              <button
                className="fh-btn-primary cursor-pointer"
                disabled={!canCreate}
                title={actionTitle(canCreate, "Register a new device into the tenant inventory.")}
                onClick={() => canCreate && openCreate()}
              >
                <Plus className="h-4 w-4" /> Add Device
              </button>
            </div>
          </div>
        </div>
      </header>

      {/* ── Ops intelligence bar ─────────────────────────────── */}
      <div className="anim-fade-up relative flex flex-col gap-4 overflow-hidden rounded-2xl border border-slate-700/20 bg-gradient-to-br from-slate-900 via-slate-800 to-slate-900 p-5 text-white shadow-xl sm:flex-row sm:items-center sm:justify-between">
        <div className="absolute -right-8 -top-8 h-32 w-32 rounded-full bg-teal-500/10 blur-2xl" />
        <div className="absolute -bottom-6 left-1/3 h-24 w-24 rounded-full bg-indigo-500/8 blur-2xl" />
        <div className="relative flex items-center gap-4">
          <span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-teal-400/20 to-teal-600/10 ring-1 ring-teal-400/20">
            <Sparkles className="h-5 w-5 text-teal-300" />
          </span>
          <div>
            <p className="text-[10px] font-bold uppercase tracking-[0.22em] text-teal-300/80">Live operations signal</p>
            <p className="mt-1 text-sm font-medium leading-relaxed text-slate-600">
              {offlineCount + attentionCount === 0
                ? "All devices are online and healthy — no recovery workflows or firmware updates pending."
                : `${offlineCount} offline device${offlineCount !== 1 ? "s" : ""}${attentionCount > 0 ? ` · ${attentionCount} need attention` : ""} require review`}
            </p>
          </div>
        </div>
        <div className="relative flex items-center gap-6 text-xs">
          <div className="flex items-center gap-2">
            <span className="h-1.5 w-1.5 rounded-full bg-emerald-400 animate-pulse" />
            <span className="text-slate-300">{deviceRows.length} devices visible</span>
          </div>
          <div className="flex items-center gap-2">
            <Cpu className="h-3.5 w-3.5 text-teal-400" />
            <span className="text-slate-300">RBAC Enforced</span>
          </div>
        </div>
      </div>

      {notice ? (
        <div className="anim-slide-right panel flex items-center justify-between gap-4 border-l-4 border-emerald-400 bg-white/95 p-4 text-sm text-slate-700 backdrop-blur">
          <span className="font-medium">{notice}</span>
          <button className="icon-btn cursor-pointer" onClick={() => setNotice(null)}><X className="h-4 w-4" /></button>
        </div>
      ) : null}

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <KpiCard label="Managed Devices" value={(devicesQ.data ?? []).length} status="Active" icon={<RadioTower className="h-4 w-4" />} />
        <KpiCard label="Offline" value={offlineCount} status={offlineCount ? "Critical" : "Healthy"} icon={<WifiOff className="h-4 w-4" />} />
        <KpiCard label="Needs Attention" value={attentionCount} status={attentionCount ? "Watch" : "Healthy"} icon={<Activity className="h-4 w-4" />} />
        <KpiCard label="Average Data Health" value={`${Number.isFinite(avgHealth) ? avgHealth : 0}%`} status={avgHealth >= 85 ? "Healthy" : avgHealth >= 70 ? "Watch" : "Critical"} icon={<Cpu className="h-4 w-4" />} />
      </div>

      {/* ── Search bar ─────────────────────────────────────── */}
      <div className="panel p-4">
        <div className="relative xl:min-w-[360px]">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-slate-500" />
          <input
            className="field h-10 w-full pl-9"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            aria-label="Search devices by provider, serial, IMEI, vehicle, driver, or tenant"
          />
        </div>
      </div>

      {/* ── Tab bar ──────────────────────────────────────────── */}
      <div className="panel p-2">
        <div className="flex flex-wrap gap-2">
          {DEVICE_TABS.map((item) => (
            <button key={item.key} type="button" className={`rounded-xl px-4 py-2 text-sm font-semibold transition cursor-pointer ${tab === item.key ? "bg-teal-50 text-teal-700 shadow-sm ring-1 ring-teal-200/60" : "text-slate-500 hover:bg-slate-50 hover:text-slate-700"}`} onClick={() => setTab(item.key)}>
              {item.label}
            </button>
          ))}
        </div>
      </div>

      {/* ── Content panel ────────────────────────────────────── */}
      <div className="panel p-5">

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
                      className="fh-btn-ghost cursor-pointer"
                      disabled={!canManageProviders}
                      title={actionTitle(canManageProviders, "Open provider management settings.")}
                      onClick={() => canManageProviders && navigate("/integrations")}
                    >
                      <Settings2 className="h-4 w-4" /> Manage Provider
                    </button>
                    <button
                      className="fh-btn-ghost cursor-pointer"
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
          <div className="overflow-x-auto rounded-xl border border-slate-200" onClick={(e) => { if (!(e.target as HTMLElement).closest(".relative")) setOpenMenuId(null); }}>
            <table className="w-full min-w-[620px] text-left text-sm">
              <thead className="bg-slate-50 text-xs font-semibold uppercase tracking-wide text-slate-500">
                <tr>
                  {["Device", "Provider", "Identifier", "Vehicle", "Driver", "Firmware", "Check-in", "Connection", "Power", "Signal", "Health", "Install", "Compliance", "Support", "Actions"].map((header) => (
                    <th key={header} className="px-4 py-2.5">{header}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {deviceRows.map((row) => (
                  <tr key={String(row.id)} className="hover:bg-slate-50 cursor-pointer transition-colors">
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
                          className="fh-btn-ghost h-8 px-3 flex items-center gap-1 cursor-pointer"
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
        <div className="fixed inset-0 z-50 flex justify-end bg-slate-900/40 backdrop-blur-sm anim-fade-in" onClick={() => setSelectedId(null)}>
          <aside className="anim-slide-right flex h-full w-full max-w-5xl flex-col overflow-y-auto border-l border-slate-200 bg-white shadow-2xl" onClick={(event) => event.stopPropagation()}>
            <div className="sticky top-0 z-10 flex items-center justify-between border-b border-slate-200 bg-white/95 px-6 py-5 backdrop-blur">
              <p className="section-title text-teal-600">Device Detail</p>
              <button className="icon-btn cursor-pointer" onClick={() => setSelectedId(null)}><X className="h-4 w-4" /></button>
            </div>
            <div className="flex-1 overflow-y-auto p-6">
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
            </div>
          </aside>
        </div>
      ) : null}

      {deviceModal ? (
        <ModalForm
          title={deviceModal.mode === "create" ? "Add Device" : "Edit Device"}
          onClose={() => setDeviceModal(null)}
          onSubmit={(event) => {
            event.preventDefault();
            if (deviceModal.mode === "create") {
              createMut.mutate(deviceForm);
            } else if (deviceModal.id != null) {
              updateMut.mutate({ id: deviceModal.id, payload: deviceForm });
            }
          }}
          submitLabel={deviceModal.mode === "create" ? "Create Device" : "Save Device"}
          busy={createMut.isPending || updateMut.isPending}
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
            <Select className="w-full" value={assignVehicleCode} onChange={(event) => setAssignVehicleCode(event.target.value)} required>
              <option value="">Select a vehicle</option>
              {developmentFleetSeedData.vehicles.map((vehicle) => (
                <option key={String(vehicle.id ?? vehicle.vehicleId)} value={String(vehicle.vehicleCode ?? vehicle.vehicleId)}>
                  {String(vehicle.vehicleCode ?? vehicle.vehicleId)} · {String(vehicle.status ?? "Fleet asset")}
                </option>
              ))}
            </Select>
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
  const latestTelemetry = detail.telemetry[0];
  const latestDiagnostic = detail.diagnostics[0];
  const latestSensor = detail.sensorReadings[0];

  return (
    <>
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 className="text-2xl font-bold text-slate-900">{device.deviceName}</h2>
          <p className="mt-1 text-sm text-slate-500">{device.deviceType} · {device.provider} · {device.serialNumber || device.identifier}</p>
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
          <MiniGrid rows={[
            ["Last GPS point", latestTelemetry ? `${latestTelemetry.latitude}, ${latestTelemetry.longitude}` : "No GPS fix recorded"],
            ["Speed", latestTelemetry ? String(latestTelemetry.speedMph ?? "0") : "No speed reading"],
            ["Heading", latestTelemetry ? String(latestTelemetry.heading ?? "Stationary") : "Awaiting heading"],
            ["Last check-in", device.lastCheckIn],
          ]} />
        </PanelSection>
        <PanelSection title="Engine / OBD / J1939">
          <MiniGrid rows={[
            ["Engine status", latestTelemetry ? String(latestTelemetry.engineStatus ?? "No engine state") : "No engine state"],
            ["Odometer", latestTelemetry ? String(latestTelemetry.odometer ?? "No odometer reading") : "No odometer reading"],
            ["Fuel level", latestTelemetry ? String(latestTelemetry.fuelLevel ?? "No fuel reading") : "No fuel reading"],
            ["Geofence", latestTelemetry ? String(latestTelemetry.geofenceStatus ?? "In corridor") : "In corridor"],
          ]} />
        </PanelSection>
        <PanelSection title="Latest Sensor Readings">
          <MiniGrid rows={[
            ["Temperature", latestSensor ? String(latestSensor.temperature ?? "Ambient") : "Ambient"],
            ["Humidity", latestSensor ? String(latestSensor.humidity ?? "No humidity channel") : "No humidity channel"],
            ["Door status", latestSensor ? String(latestSensor.doorStatus ?? "No door channel") : "No door channel"],
            ["Tire / fuel", latestSensor ? `${String(latestSensor.tirePressure ?? "No tire channel")} · ${String(latestSensor.fuelLevel ?? "No fuel channel")}` : "No sensor channel"],
          ]} />
        </PanelSection>
        <PanelSection title="Diagnostics">
          <MiniGrid rows={[
            ["Latest result", latestDiagnostic ? String(latestDiagnostic.result) : "Diagnostics not run"],
            ["Battery voltage", latestDiagnostic ? String(latestDiagnostic.batteryVoltage) : "No voltage reading"],
            ["Modem status", latestDiagnostic ? String(latestDiagnostic.modemStatus) : "No modem reading"],
            ["GNSS status", latestDiagnostic ? String(latestDiagnostic.gnssStatus) : "No GNSS reading"],
          ]} />
        </PanelSection>
      </div>

      <div className="mt-6 grid gap-4 xl:grid-cols-2">
        <PanelSection title="Assignment History">
          <TimelineList rows={detail.assignmentHistory.map((row) => ({
            title: String(row.status ?? "Assignment"),
            subtitle: `${String(row.vehicleCode ?? "No vehicle")} · ${String(row.driverName ?? "No driver")}`,
            meta: String(row.assignedAt ?? ""),
          }))} emptyText="No assignment changes recorded." />
        </PanelSection>
        <PanelSection title="Health Timeline">
          <TimelineList rows={detail.healthEvents.map((row) => ({
            title: String(row.status),
            subtitle: `${String(row.summary)} · ${String(row.score)}%`,
            meta: String(row.eventAt),
          }))} emptyText="No health events recorded." />
        </PanelSection>
        <PanelSection title="Connectivity History">
          <TimelineList rows={detail.telemetry.map((row) => ({
            title: String(row.geofenceStatus ?? "Connectivity event"),
            subtitle: `${String(row.speedMph ?? "0")} mph · ${String(row.engineStatus ?? "Signal update")}`,
            meta: String(row.eventAt ?? ""),
          }))} emptyText="No connectivity history recorded." />
        </PanelSection>
        <PanelSection title="Audit / Activity Log">
          <TimelineList rows={detail.auditLog.map((row) => ({
            title: String(row.action ?? "Activity"),
            subtitle: String(row.notes ?? ""),
            meta: String(row.eventAt ?? ""),
          }))} emptyText="No device activity recorded." />
        </PanelSection>
      </div>

      <div className="mt-6 panel p-5">
        <p className="section-title">Installation Checklist</p>
        <div className="mt-4 grid gap-3 md:grid-cols-2">
          {(detail.installations[0]?.checklist ?? []).map((item: AnyRecord) => (
            <div key={String(item.item)} className="rounded-xl border border-slate-200 bg-white p-4">
              <div className="flex items-center justify-between gap-3">
                <p className="font-semibold text-slate-900">{String(item.item)}</p>
                <StatusBadge status={String(item.status)} />
              </div>
            </div>
          ))}
        </div>
      </div>
    </>
  );
}

function ActionButton({ label, icon, allowed, onClick }: { label: string; icon: ReactNode; allowed: boolean; onClick: () => void }) {
  return (
    <button
      className={allowed ? "fh-btn-ghost cursor-pointer" : "fh-btn-ghost opacity-60 cursor-not-allowed"}
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
    <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
      <p className="text-sm font-semibold text-slate-900">{title}</p>
      <div className="mt-4 space-y-2">
        {items.map(([label, value]) => (
          <div key={label} className="flex items-start justify-between gap-3 rounded-xl border border-slate-200 bg-white px-3 py-2">
            <span className="text-[11px] font-bold uppercase tracking-[0.16em] text-slate-500">{label}</span>
            <span className="text-right text-sm text-slate-700">{value || "—"}</span>
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
    <div className="fixed inset-0 z-[60] grid place-items-center bg-slate-900/50 p-4 backdrop-blur-sm anim-fade-in">
      <form className="panel max-h-[90vh] w-full max-w-4xl overflow-y-auto p-6 shadow-2xl" onSubmit={onSubmit}>
        <div className="flex items-center justify-between border-b border-slate-200 pb-4">
          <h2 className="text-2xl font-semibold text-slate-900">{title}</h2>
          <button type="button" className="icon-btn cursor-pointer" onClick={onClose}><X className="h-4 w-4" /></button>
        </div>
        <div className="mt-6">{children}</div>
        <div className="mt-6 flex justify-end gap-3 border-t border-slate-200 pt-4">
          <button type="button" className="fh-btn-ghost cursor-pointer" onClick={onClose}>Cancel</button>
          <button type="submit" className="fh-btn-primary cursor-pointer" disabled={busy}>{busy ? "Saving..." : submitLabel}</button>
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
    <div className="rounded-2xl border border-slate-200 bg-slate-50 p-5">
      <p className="text-sm font-semibold text-slate-900">{title}</p>
      <div className="mt-4">{children}</div>
    </div>
  );
}

function MiniGrid({ rows }: { rows: Array<[string, string]> }) {
  return (
    <div className="space-y-2">
      {rows.map(([label, value]) => (
        <div key={label} className="flex items-start justify-between gap-3 rounded-xl border border-slate-200 bg-white px-3 py-2">
          <span className="text-[11px] font-bold uppercase tracking-[0.16em] text-slate-500">{label}</span>
          <span className="text-right text-sm text-slate-700">{value}</span>
        </div>
      ))}
    </div>
  );
}

function TimelineList({ rows, emptyText }: { rows: Array<{ title: string; subtitle: string; meta: string }>; emptyText: string }) {
  if (!rows.length) {
    return <p className="text-sm text-slate-500">{emptyText}</p>;
  }

  return (
    <div className="space-y-3">
      {rows.map((row) => (
        <div key={`${row.title}-${row.meta}`} className="rounded-xl border border-slate-200 bg-white p-3">
          <div className="flex items-start justify-between gap-3">
            <div>
              <p className="font-medium text-slate-900">{row.title}</p>
              <p className="mt-1 text-sm text-slate-500">{row.subtitle}</p>
            </div>
            <span className="text-xs text-slate-500">{row.meta}</span>
          </div>
        </div>
      ))}
    </div>
  );
}

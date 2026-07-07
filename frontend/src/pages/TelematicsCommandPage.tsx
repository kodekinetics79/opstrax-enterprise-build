import { useMemo, useState } from "react";
import type { ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  AlertTriangle,
  BatteryCharging,
  CheckCircle2,
  Download,
  Gauge,
  MapPinned,
  RadioTower,
  RefreshCw,
  Search,
  ShieldCheck,
  Sparkles,
  Thermometer,
  Truck,
  Wrench,
  X,
} from "lucide-react";
import { useNavigate } from "react-router-dom";
import { EmptyState, ErrorState, KpiCard, LoadingState, RiskBadge, StatusBadge } from "@/components/ui";
import { PERMISSIONS } from "@/auth/rbacConfig";
import { useHasPermission } from "@/hooks/usePermission";
import { maintenanceApi } from "@/services/maintenanceApi";
import { telematicsService, type DeviceDetailRecord, type TelematicsClusterRecord } from "@/services/telematicsService";

type TelematicsKind = "gps-tracking" | "obd-j1939" | "sensor-health" | "cold-chain";

type ClusterConfig = {
  eyebrow: string;
  title: string;
  description: string;
  columns: string[];
  emptyTitle: string;
  emptySubtitle: string;
  searchPlaceholder: string;
  query: () => Promise<TelematicsClusterRecord[]>;
  requiredExportPermission: string;
  requiredUpdatePermission: string;
  filterTabs: string[];
};

const configs: Record<TelematicsKind, ClusterConfig> = {
  "gps-tracking": {
    eyebrow: "Telematics & IoT",
    title: "GPS Tracking",
    description: "Fleet location intelligence with geofence posture, stale GPS detection, route linkage, and dispatch-ready visibility.",
    columns: ["vehicleCode", "deviceName", "driverName", "locationLabel", "lastPingAt", "speedMph", "heading", "geofenceStatus", "staleGps", "dataFreshnessStatus"],
    emptyTitle: "No GPS records found",
    emptySubtitle: "No vehicles match the current GPS filters for this tenant.",
    searchPlaceholder: "Search vehicle, driver, location, route, status, or device health...",
    query: () => telematicsService.getGpsTrackingRecords(),
    requiredExportPermission: PERMISSIONS.TELEMATICS_GPS_EXPORT,
    requiredUpdatePermission: PERMISSIONS.TELEMATICS_GPS_VIEW,
    filterTabs: ["All", "Online", "Stale GPS", "Offline", "Critical"],
  },
  "obd-j1939": {
    eyebrow: "Telematics & IoT",
    title: "OBD / J1939",
    description: "Engine diagnostics, protocol coverage, odometer and fuel telemetry, battery health, trouble-code readiness, and maintenance escalation in one workflow.",
    columns: ["vehicleCode", "deviceName", "protocolType", "engineStatus", "engineHours", "odometer", "fuelLevel", "batteryVoltage", "lastEngineDataAt", "dataFreshnessStatus"],
    emptyTitle: "No diagnostics records found",
    emptySubtitle: "No engine or bus diagnostics are visible for the current filters.",
    searchPlaceholder: "Search vehicle, protocol, driver, fault code, freshness, or provider...",
    query: () => telematicsService.getDiagnosticsRecords(),
    requiredExportPermission: PERMISSIONS.TELEMATICS_DIAGNOSTICS_EXPORT,
    requiredUpdatePermission: PERMISSIONS.TELEMATICS_DIAGNOSTICS_UPDATE,
    filterTabs: ["All", "Fresh", "Watch", "Stale", "Issues"],
  },
  "sensor-health": {
    eyebrow: "Telematics & IoT",
    title: "Sensor Health",
    description: "Temperature, reefer, power, fuel, door, tire, and asset sensor health with reading quality, calibration state, and field follow-up.",
    columns: ["vehicleCode", "deviceName", "sensorType", "latestReading", "expectedRange", "sensorStatus", "signalStrength", "powerStatus", "calibrationStatus", "alertStatus"],
    emptyTitle: "No sensor records found",
    emptySubtitle: "No scoped sensors match the active filters.",
    searchPlaceholder: "Search sensor type, vehicle, alert status, reading, calibration, or signal...",
    query: () => telematicsService.getSensorHealthRecords(),
    requiredExportPermission: PERMISSIONS.TELEMATICS_SENSORS_EXPORT,
    requiredUpdatePermission: PERMISSIONS.TELEMATICS_SENSORS_UPDATE,
    filterTabs: ["All", "Nominal", "Watch", "Alerting", "Offline"],
  },
  "cold-chain": {
    eyebrow: "Telematics & IoT",
    title: "Cold Chain Telemetry",
    description: "Cold-chain sensor posture with reefer readings, humidity, door status, and shipment protection decisions.",
    columns: ["vehicleCode", "deviceName", "sensorType", "latestReading", "expectedRange", "sensorStatus", "signalStrength", "powerStatus", "calibrationStatus", "alertStatus"],
    emptyTitle: "No cold-chain telemetry found",
    emptySubtitle: "No cold-chain sensors are visible for this tenant and filter set.",
    searchPlaceholder: "Search reefer unit, route, reading, shipment, or alert status...",
    query: () => telematicsService.getSensorHealthRecords(),
    requiredExportPermission: PERMISSIONS.TELEMATICS_SENSORS_EXPORT,
    requiredUpdatePermission: PERMISSIONS.TELEMATICS_SENSORS_UPDATE,
    filterTabs: ["All", "Nominal", "Watch", "Alerting", "Offline"],
  },
};

function downloadCsv(filename: string, body: string) {
  const anchor = document.createElement("a");
  anchor.href = URL.createObjectURL(new Blob([body], { type: "text/csv" }));
  anchor.download = filename;
  anchor.click();
}

function filterRecord(kind: TelematicsKind, record: TelematicsClusterRecord, tab: string) {
  if (tab === "All") return true;
  if (kind === "gps-tracking") {
    if (tab === "Online") return !record.offlineWarning && record.dataFreshnessStatus === "Fresh";
    if (tab === "Stale GPS") return /h ago|Stale/i.test(record.staleGps) || record.dataFreshnessStatus === "Stale";
    if (tab === "Offline") return record.offlineWarning;
    return record.deviceHealth < 70 || record.alertStatus === "Open";
  }
  if (kind === "obd-j1939") {
    if (tab === "Fresh") return record.dataFreshnessStatus === "Fresh";
    if (tab === "Watch") return record.dataFreshnessStatus === "Watch";
    if (tab === "Stale") return record.dataFreshnessStatus === "Stale";
    return record.troubleCodes.length > 0 || /inspection/i.test(record.emissionsStatus);
  }
  if (kind === "sensor-health" || kind === "cold-chain") {
    if (tab === "Nominal") return record.sensorStatus === "Nominal";
    if (tab === "Watch") return record.sensorStatus === "Watch";
    if (tab === "Alerting") return record.sensorStatus === "Alerting" || record.alertStatus === "Open";
    return record.offlineWarning;
  }
  return true;
}

function renderCell(column: string, row: TelematicsClusterRecord) {
  if (column === "deviceName") {
    return (
      <div>
        <p className="font-semibold text-slate-900">{row.deviceName}</p>
        <p className="text-xs text-slate-400">{row.provider} · {row.deviceType}</p>
      </div>
    );
  }
  if (column === "sensorStatus" || column === "dataFreshnessStatus" || column === "alertStatus") {
    return <StatusBadge status={String(row[column as keyof TelematicsClusterRecord])} />;
  }
  if (column === "signalStrength" || column === "geofenceStatus") {
    return <RiskBadge risk={String(row[column as keyof TelematicsClusterRecord])} />;
  }
  if (column === "latestReading" && String(row.latestReading).includes("Ambient")) {
    return "Ambient";
  }
  return String(row[column as keyof TelematicsClusterRecord] ?? "—");
}

function permissionTitle(allowed: boolean, message: string) {
  return allowed ? message : "You do not have permission to perform this action.";
}

export function TelematicsCommandPage({ kind }: { kind: TelematicsKind }) {
  const config = configs[kind];
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const hasPermission = useHasPermission();
  const kpiIcon = kind === "gps-tracking"
    ? <MapPinned className="h-4 w-4" />
    : kind === "obd-j1939"
      ? <Gauge className="h-4 w-4" />
      : <Thermometer className="h-4 w-4" />;

  const canExport = hasPermission(config.requiredExportPermission);
  const canUpdate = hasPermission(config.requiredUpdatePermission);
  const [tab, setTab] = useState(config.filterTabs[0]);
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<TelematicsClusterRecord | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const recordsQ = useQuery({ queryKey: ["telematics-cluster", kind], queryFn: config.query, staleTime: 20_000 });
  const detailQ = useQuery({
    queryKey: ["telematics-cluster-detail", kind, selected?.deviceId],
    queryFn: () => telematicsService.getDeviceById(String(selected?.deviceId)),
    enabled: Boolean(selected?.deviceId),
    staleTime: 20_000,
  });

  const refreshMut = useMutation({
    mutationFn: (deviceId: string | number) => telematicsService.refreshDeviceStatus(deviceId),
    onSuccess: async () => {
      setNotice(kind === "gps-tracking" ? "GPS stream refreshed." : kind === "obd-j1939" ? "Diagnostics stream refreshed." : "Sensor readings refreshed.");
      await queryClient.invalidateQueries({ queryKey: ["telematics-cluster", kind] });
      await queryClient.invalidateQueries({ queryKey: ["telematics-cluster-detail"] });
    },
  });
  const acknowledgeMut = useMutation({
    mutationFn: ({ deviceId, note }: { deviceId: string | number; note: string }) => telematicsService.acknowledgeTelematicsIssue(deviceId, note),
    onSuccess: async () => {
      setNotice("Telematics issue acknowledged.");
      await queryClient.invalidateQueries({ queryKey: ["telematics-cluster", kind] });
    },
  });
  const resolveMut = useMutation({
    mutationFn: (deviceId: string | number) => telematicsService.resolveDeviceAttention(deviceId),
    onSuccess: async () => {
      setNotice("Telematics exception resolved.");
      await queryClient.invalidateQueries({ queryKey: ["telematics-cluster", kind] });
      await queryClient.invalidateQueries({ queryKey: ["telematics-cluster-detail"] });
    },
  });
  const maintenanceMut = useMutation({
    mutationFn: async (record: TelematicsClusterRecord) => {
      const maintenance = await telematicsService.createMaintenanceTask(record.deviceId, `Created from ${config.title} for ${record.vehicleCode}`);
      await maintenanceApi.create({
        vehicleCode: record.vehicleCode,
        title: maintenance.title,
        priority: record.deviceHealth < 70 ? "High" : "Medium",
        status: "Scheduled",
        estimatedCost: 0,
        notes: maintenance.note,
      });
      return maintenance;
    },
    onSuccess: () => {
      setNotice("Maintenance follow-up created.");
    },
  });

  const rows = useMemo(() => {
    const query = search.trim().toLowerCase();
    return (recordsQ.data ?? []).filter((record) => {
      const haystack = [
        record.vehicleCode,
        record.deviceName,
        record.driverName,
        record.locationLabel,
        record.routeAssociation,
        record.protocolType,
        record.sensorType,
        record.latestReading,
        record.signalStrength,
        record.alertStatus,
        record.dataFreshnessStatus,
        record.troubleCodes.join(" "),
      ].join(" ").toLowerCase();
      return (!query || haystack.includes(query)) && filterRecord(kind, record, tab);
    });
  }, [recordsQ.data, search, kind, tab]);

  const selectedRecord = rows.find((row) => row.id === selected?.id) ?? selected;
  const offlineCount = rows.filter((row) => row.offlineWarning).length;
  const issueCount = rows.filter((row) => row.alertStatus === "Open" || row.troubleCodes.length > 0 || row.deviceHealth < 70).length;
  const avgHealth = rows.length ? Math.round(rows.reduce((sum, row) => sum + row.deviceHealth, 0) / rows.length) : 0;

  if (recordsQ.isLoading) return <LoadingState />;
  if (recordsQ.isError) return <ErrorState message={`Unable to load ${config.title.toLowerCase()} right now.`} />;

  const exportCurrent = () => {
    const csv = telematicsService.exportClusterCsv(rows, config.columns);
    downloadCsv(`opstrax-${kind}.csv`, csv);
  };

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
                  {kind === "gps-tracking" ? <MapPinned className="h-3 w-3" /> : kind === "obd-j1939" ? <Gauge className="h-3 w-3" /> : <Thermometer className="h-3 w-3" />}
                  {config.eyebrow}
                </span>
                <span className="text-[11px] font-semibold text-slate-500">{config.description}</span>
              </div>
              <h1 className="text-[32px] font-black tracking-tight leading-none cc-gradient-text sm:text-[36px]">
                {config.title}
              </h1>
              <p className="mt-1 text-[13px] font-medium text-slate-400 tracking-wide">
                {kind === "gps-tracking" ? "Fleet location intelligence with geofence posture and route linkage" : kind === "obd-j1939" ? "Engine diagnostics and power telemetry in one workflow" : "Sensor health with reading quality and calibration state"}
              </p>
            </div>
            <div className="flex items-center gap-2">
              <button
                className="fh-btn-ghost cursor-pointer"
                disabled={!canExport}
                title={permissionTitle(canExport, "Export the current telematics view.")}
                onClick={() => canExport && exportCurrent()}
              >
                <Download className="h-4 w-4" /> Export CSV
              </button>
              <button className="fh-btn-primary cursor-pointer" onClick={() => navigate("/iot-devices")}>
                <Truck className="h-4 w-4" /> Open Device Command
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
              {offlineCount + issueCount === 0
                ? `All ${config.title.toLowerCase()} units are online and healthy — no action items pending.`
                : `${offlineCount} offline/stale${issueCount > 0 ? ` · ${issueCount} need action` : ""} require review`}
            </p>
          </div>
        </div>
        <div className="relative flex items-center gap-6 text-xs">
          <div className="flex items-center gap-2">
            <span className="h-1.5 w-1.5 rounded-full bg-emerald-400 animate-pulse" />
            <span className="text-slate-300">{rows.length} units visible</span>
          </div>
          <div className="flex items-center gap-2">
            <RadioTower className="h-3.5 w-3.5 text-teal-400" />
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
        <KpiCard label="Visible Units" value={rows.length} status="Active" icon={kpiIcon} />
        <KpiCard label="Offline / Stale" value={offlineCount} status={offlineCount ? "Critical" : "Healthy"} icon={<AlertTriangle className="h-4 w-4" />} />
        <KpiCard label="Needs Action" value={issueCount} status={issueCount ? "Watch" : "Healthy"} icon={<RadioTower className="h-4 w-4" />} />
        <KpiCard label="Average Health" value={`${avgHealth}%`} status={avgHealth >= 85 ? "Healthy" : avgHealth >= 70 ? "Watch" : "Critical"} icon={<BatteryCharging className="h-4 w-4" />} />
      </div>

      {kind === "gps-tracking" ? (
        <div className="grid gap-4 xl:grid-cols-3">
          {rows.slice(0, 6).map((row) => (
            <button type="button" key={row.id} className="panel rounded-2xl p-4 text-left transition hover:border-teal-300 hover:bg-slate-50 cursor-pointer" onClick={() => setSelected(row)}>
              <div className="flex items-start justify-between gap-3">
                <div>
                  <p className="font-semibold text-slate-900">{row.vehicleCode}</p>
                  <p className="mt-1 text-xs text-slate-400">{row.locationLabel} · {row.routeAssociation}</p>
                </div>
                <RiskBadge risk={row.geofenceStatus} />
              </div>
              <div className="mt-4 grid gap-2 text-sm text-slate-700">
                <div className="flex justify-between"><span>GPS ping</span><span>{row.staleGps}</span></div>
                <div className="flex justify-between"><span>Coordinates</span><span>{row.latitude}, {row.longitude}</span></div>
                <div className="flex justify-between"><span>Speed / heading</span><span>{row.speedMph} mph · {row.heading}</span></div>
              </div>
            </button>
          ))}
        </div>
      ) : null}

      {/* ── Search bar ─────────────────────────────────────── */}
      <div className="panel p-4">
        <input
          className="field h-10 xl:min-w-[360px]"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          placeholder={config.searchPlaceholder}
        />
      </div>

      {/* ── Tab bar ──────────────────────────────────────────── */}
      <div className="panel p-2">
        <div className="flex flex-wrap gap-2">
          {config.filterTabs.map((item) => (
            <button key={item} type="button" className={`rounded-xl px-4 py-2 text-sm font-semibold transition cursor-pointer ${tab === item ? "bg-teal-50 text-teal-700 shadow-sm ring-1 ring-teal-200/60" : "text-slate-500 hover:bg-slate-50 hover:text-slate-700"}`} onClick={() => setTab(item)}>
              {item}
            </button>
          ))}
        </div>
      </div>

      {/* ── Content panel ────────────────────────────────────── */}
      <div className="panel p-5">

        {!rows.length ? (
          <EmptyState title={config.emptyTitle} subtitle={config.emptySubtitle} />
        ) : (
          <div className="overflow-x-auto rounded-xl border border-slate-200">
            <table className="w-full min-w-[620px] text-left text-sm">
              <thead className="bg-slate-50 text-xs font-semibold uppercase tracking-wide text-slate-500">
                <tr>
                  {config.columns.map((column) => (
                    <th key={column} className="px-4 py-2.5">{column}</th>
                  ))}
                  <th className="px-4 py-2.5">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((row) => (
                  <tr key={row.id} className="hover:bg-slate-50 cursor-pointer transition-colors">
                    {config.columns.map((column) => (
                      <td key={column} className="px-4 py-3 text-slate-700">
                        {renderCell(column, row)}
                      </td>
                    ))}
                    <td className="px-4 py-3">
                      <div className="flex flex-wrap gap-2">
                        <button className="fh-btn-ghost h-8 px-3 cursor-pointer" onClick={() => setSelected(row)}>
                          {kind === "gps-tracking" ? "View on map" : kind === "obd-j1939" ? "View diagnostics" : "View sensor"}
                        </button>
                        <button className="fh-btn-ghost h-8 px-3 cursor-pointer" onClick={() => navigate("/iot-devices")}>View device</button>
                        <button className="fh-btn-ghost h-8 px-3 cursor-pointer" onClick={() => navigate("/vehicles")}>View vehicle</button>
                        {row.shipmentId !== "No active shipment" ? (
                          <button className="fh-btn-ghost h-8 px-3 cursor-pointer" onClick={() => navigate("/jobs")}>Open trip</button>
                        ) : null}
                        <button
                          className="fh-btn-ghost h-8 px-3 cursor-pointer"
                          disabled={!canUpdate || refreshMut.isPending}
                          title={permissionTitle(canUpdate, kind === "gps-tracking" ? "Refresh GPS visibility." : "Refresh telematics stream.")}
                          onClick={() => canUpdate && refreshMut.mutate(row.deviceId)}
                        >
                          Refresh
                        </button>
                        {kind === "obd-j1939" ? (
                          <>
                            <button
                              className="fh-btn-ghost h-8 px-3 cursor-pointer"
                              disabled={!canUpdate}
                              title={permissionTitle(canUpdate, "Acknowledge this diagnostic issue.")}
                              onClick={() => canUpdate && acknowledgeMut.mutate({ deviceId: row.deviceId, note: `Acknowledged ${row.vehicleCode} diagnostics review.` })}
                            >
                              Acknowledge
                            </button>
                            <button
                              className="fh-btn-primary h-8 px-3 cursor-pointer"
                              disabled={!canUpdate || maintenanceMut.isPending}
                              title={permissionTitle(canUpdate, "Create a maintenance follow-up.")}
                              onClick={() => canUpdate && maintenanceMut.mutate(row)}
                            >
                              Create Maintenance
                            </button>
                          </>
                        ) : null}
                        {kind === "sensor-health" ? (
                          <>
                            <button
                              className="fh-btn-ghost h-8 px-3 cursor-pointer"
                              disabled={!canUpdate}
                              title={permissionTitle(canUpdate, "Acknowledge this sensor alert.")}
                              onClick={() => canUpdate && acknowledgeMut.mutate({ deviceId: row.deviceId, note: `Acknowledged ${row.sensorType} alert on ${row.vehicleCode}.` })}
                            >
                              Acknowledge
                            </button>
                            <button
                              className="fh-btn-primary h-8 px-3 cursor-pointer"
                              disabled={!canUpdate || maintenanceMut.isPending}
                              title={permissionTitle(canUpdate, "Create a maintenance task for this sensor.")}
                              onClick={() => canUpdate && maintenanceMut.mutate(row)}
                            >
                              Create Task
                            </button>
                          </>
                        ) : null}
                        {kind === "gps-tracking" && (row.offlineWarning || row.dataFreshnessStatus === "Stale") ? (
                          <button
                            className="fh-btn-primary h-8 px-3 cursor-pointer"
                            disabled={!canUpdate || resolveMut.isPending}
                            title={permissionTitle(canUpdate, "Resolve the stale or offline GPS state.")}
                            onClick={() => canUpdate && resolveMut.mutate(row.deviceId)}
                          >
                            Resolve
                          </button>
                        ) : null}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {selectedRecord ? (
        <div className="fixed inset-0 z-50 flex justify-end bg-slate-900/40 backdrop-blur-sm anim-fade-in" onClick={() => setSelected(null)}>
          <aside className="anim-slide-right flex h-full w-full max-w-5xl flex-col overflow-y-auto border-l border-slate-200 bg-white shadow-2xl" onClick={(event) => event.stopPropagation()}>
            <div className="sticky top-0 z-10 flex items-center justify-between border-b border-slate-200 bg-white/95 px-6 py-5 backdrop-blur">
              <p className="section-title text-teal-600">{kind === "gps-tracking" ? "GPS Detail" : kind === "obd-j1939" ? "Diagnostics Detail" : "Sensor Detail"}</p>
              <button className="icon-btn cursor-pointer" onClick={() => setSelected(null)}><X className="h-4 w-4" /></button>
            </div>
            <div className="flex-1 overflow-y-auto p-6">
            {detailQ.isLoading ? (
              <LoadingState />
            ) : detailQ.isError || !detailQ.data ? (
              <ErrorState message="Unable to load telematics detail." />
            ) : (
              <TelematicsDetailDrawer
                kind={kind}
                row={selectedRecord}
                detail={detailQ.data}
                canUpdate={canUpdate}
                onRefresh={() => canUpdate && refreshMut.mutate(selectedRecord.deviceId)}
                onAcknowledge={() => canUpdate && acknowledgeMut.mutate({ deviceId: selectedRecord.deviceId, note: `Acknowledged ${selectedRecord.vehicleCode} telematics alert.` })}
                onResolve={() => canUpdate && resolveMut.mutate(selectedRecord.deviceId)}
                onMaintenance={() => canUpdate && maintenanceMut.mutate(selectedRecord)}
              />
            )}
            </div>
          </aside>
        </div>
      ) : null}
    </div>
  );
}

function TelematicsDetailDrawer({
  kind,
  row,
  detail,
  canUpdate,
  onRefresh,
  onAcknowledge,
  onResolve,
  onMaintenance,
}: {
  kind: TelematicsKind;
  row: TelematicsClusterRecord;
  detail: DeviceDetailRecord;
  canUpdate: boolean;
  onRefresh: () => void;
  onAcknowledge: () => void;
  onResolve: () => void;
  onMaintenance: () => void;
}) {
  const latestDiagnostic = detail.diagnostics[0];
  const latestSensor = detail.sensorReadings[0];
  const latestHealth = detail.healthEvents[0];
  return (
    <>
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 className="text-2xl font-bold text-slate-900">{row.vehicleCode}</h2>
          <p className="mt-1 text-sm text-slate-500">{row.deviceName} · {row.driverName} · {row.routeAssociation}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <StatusBadge status={row.dataFreshnessStatus} />
          <RiskBadge risk={row.signalStrength} />
        </div>
      </div>

      <div className="mt-6 flex flex-wrap gap-3">
        <button className="fh-btn-ghost cursor-pointer" onClick={() => window.location.assign(`/map-view`)}><MapPinned className="h-4 w-4" /> View on map</button>
        <button className="fh-btn-ghost cursor-pointer" onClick={() => window.location.assign(`/iot-devices`)}><Truck className="h-4 w-4" /> View device</button>
        <button className="fh-btn-ghost cursor-pointer" onClick={() => window.location.assign(`/vehicles`)}><Truck className="h-4 w-4" /> View vehicle</button>
        {row.shipmentId !== "No active shipment" ? <button className="fh-btn-ghost cursor-pointer" onClick={() => window.location.assign(`/jobs`)}><Truck className="h-4 w-4" /> Open trip</button> : null}
        <button className="fh-btn-ghost cursor-pointer" disabled={!canUpdate} title={permissionTitle(canUpdate, "Refresh telematics data.")} onClick={onRefresh}><RefreshCw className="h-4 w-4" /> Refresh</button>
        {kind !== "gps-tracking" ? <button className="fh-btn-ghost cursor-pointer" disabled={!canUpdate} title={permissionTitle(canUpdate, "Acknowledge this issue.")} onClick={onAcknowledge}><ShieldCheck className="h-4 w-4" /> Acknowledge</button> : null}
        {kind !== "gps-tracking" ? <button className="fh-btn-primary cursor-pointer" disabled={!canUpdate} title={permissionTitle(canUpdate, "Create a maintenance follow-up.")} onClick={onMaintenance}><Wrench className="h-4 w-4" /> Create maintenance</button> : null}
        {row.offlineWarning || row.alertStatus === "Open" ? <button className="fh-btn-primary cursor-pointer" disabled={!canUpdate} title={permissionTitle(canUpdate, "Resolve this telematics exception.")} onClick={onResolve}><CheckCircle2 className="h-4 w-4" /> Resolve</button> : null}
      </div>

      <div className="mt-6 grid gap-4 lg:grid-cols-3">
        <InfoPanel title="Location / Route" items={[
          ["Location", row.locationLabel],
          ["GPS ping", row.lastPingAt],
          ["Stale warning", row.staleGps],
          ["Route", row.routeAssociation],
          ["Shipment", row.shipmentId],
          ["Geofence", row.geofenceStatus],
        ]} />
        <InfoPanel title="Vehicle / Device" items={[
          ["Device", row.deviceName],
          ["Provider", row.provider],
          ["Vehicle", row.vehicleCode],
          ["Driver", row.driverName],
          ["Signal", row.signalStrength],
          ["Health", `${row.deviceHealth}%`],
        ]} />
        <InfoPanel title={kind === "obd-j1939" ? "Diagnostics" : "Sensors"} items={[
          ["Protocol", row.protocolType],
          ["Trouble codes", row.troubleCodes.join(", ") || "None"],
          ["Battery", row.batteryVoltage],
          ["Latest reading", row.latestReading],
          ["Expected range", row.expectedRange],
          ["Alert status", row.alertStatus],
        ]} />
      </div>

      <div className="mt-6 grid gap-4 xl:grid-cols-2">
        <InfoPanel title="Engine / Powertrain" items={[
          ["Engine status", row.engineStatus],
          ["Engine hours", row.engineHours],
          ["Odometer", row.odometer],
          ["Fuel level", row.fuelLevel],
          ["Emissions", row.emissionsStatus],
          ["Last engine data", row.lastEngineDataAt],
        ]} />
        <InfoPanel title="Sensor / Health" items={[
          ["Sensor type", row.sensorType],
          ["Sensor status", row.sensorStatus],
          ["Calibration", row.calibrationStatus],
          ["Power", row.powerStatus],
          ["Signal strength", row.signalStrength],
          ["Health event", latestHealth ? `${latestHealth.status} · ${latestHealth.score}%` : "Healthy"],
        ]} />
      </div>

      <div className="mt-6 panel p-5">
        <p className="section-title">Field Notes</p>
        <div className="mt-4 grid gap-3 md:grid-cols-3">
          <ContextCard title="Latest diagnostic" body={latestDiagnostic ? `${latestDiagnostic.result} · ${latestDiagnostic.faultCode}` : "No diagnostics captured for this unit yet."} />
          <ContextCard title="Latest sensor reading" body={latestSensor ? `${latestSensor.temperature ?? latestSensor.tirePressure ?? latestSensor.fuelLevel ?? "No reading"} · ${latestSensor.recordedAt}` : "No sensor reading captured for this unit yet."} />
          <ContextCard title="Recommended action" body={row.recommendedAction} />
        </div>
      </div>
    </>
  );
}

function InfoPanel({ title, items }: { title: string; items: Array<[string, string]> }) {
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

function ContextCard({ title, body }: { title: string; body: string }) {
  return (
    <div className="rounded-xl border border-slate-200 bg-white p-4">
      <p className="font-semibold text-slate-900">{title}</p>
      <p className="mt-2 text-sm text-slate-500">{body}</p>
    </div>
  );
}

import { useEffect, useMemo, useState } from "react";
import type { ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  AlertTriangle,
  BatteryCharging,
  Download,
  Gauge,
  MapPinned,
  RadioTower,
  RefreshCw,
  Search,
  Thermometer,
  Truck,
  Wrench,
  X,
} from "lucide-react";
import { useNavigate } from "react-router";
import { EmptyState, ErrorState, KpiCard, LoadingState, PageHeader, RiskBadge, StatusBadge } from "@/components/ui";
import { PERMISSIONS } from "@/auth/rbacConfig";
import { useHasDirectPermission, useHasPermission } from "@/hooks/usePermission";
import { maintenanceApi } from "@/services/maintenanceApi";
import { telematicsService, type DeviceDetailRecord, type TelematicsClusterRecord, type TelemetryClusterPageResult } from "@/services/telematicsService";
import { resolveTelemetryEmptyState } from "@/utils/telemetryEmptyState";
import { apiErrorMessage } from "@/utils/apiErrorMessage";

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
  requiredViewPermission: string;
  requiredExportPermission: string;
  requiredUpdatePermission: string;
  filterTabs: string[];
};

const configs: Record<TelematicsKind, ClusterConfig> = {
  "gps-tracking": {
    eyebrow: "Telematics & IoT",
    title: "GPS Tracking",
    description: "Current and last-known positions with fix time, gateway receipt, provenance, and explicit blockers. Route linkage is not inferred.",
    columns: ["serialNumber", "vehicleCode", "locationLabel", "positionSource", "positionAccuracy", "deviceFixAt", "gatewayReceivedAt", "dataFreshnessStatus", "routingReadiness"],
    emptyTitle: "No GPS records found",
    emptySubtitle: "No vehicles match the current GPS filters for this tenant.",
    searchPlaceholder: "Search serial, IMEI, model, category, provider, vehicle, driver, or location...",
    query: () => telematicsService.getGpsTrackingRecords(),
    requiredViewPermission: PERMISSIONS.TELEMATICS_GPS_VIEW,
    requiredExportPermission: PERMISSIONS.TELEMATICS_GPS_EXPORT,
    requiredUpdatePermission: PERMISSIONS.TELEMATICS_GPS_VIEW,
    filterTabs: ["All", "Online", "Delayed / Watch", "Stale GPS", "Offline", "Critical"],
  },
  "obd-j1939": {
    eyebrow: "Telematics & IoT",
    title: "OBD / J1939",
    description: "Received OBD/J1939/CAN evidence with explicit protocol identity, active DTCs, freshness, and maintenance escalation. DTCs are not assumed to be emissions faults.",
    columns: ["serialNumber", "vehicleCode", "deviceName", "protocolType", "troubleCodes", "engineStatus", "odometer", "fuelLevel", "batteryVoltage", "lastEngineDataAt", "dataFreshnessStatus"],
    emptyTitle: "No diagnostics records found",
    emptySubtitle: "No engine or bus diagnostics are visible for the current filters.",
    searchPlaceholder: "Search vehicle, protocol, driver, fault code, freshness, or provider...",
    query: () => telematicsService.getDiagnosticsRecords(),
    requiredViewPermission: PERMISSIONS.TELEMATICS_DIAGNOSTICS_VIEW,
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
    requiredViewPermission: PERMISSIONS.TELEMATICS_SENSORS_VIEW,
    requiredExportPermission: PERMISSIONS.TELEMATICS_SENSORS_EXPORT,
    requiredUpdatePermission: PERMISSIONS.TELEMATICS_SENSORS_UPDATE,
    filterTabs: ["All", "Nominal", "Watch", "Alerting", "Offline"],
  },
  "cold-chain": {
    eyebrow: "Telematics & IoT",
    title: "Cold Chain Telemetry",
    description: "Reported cold-chain readings, configured zone thresholds, battery, shipment linkage, freshness, and breach posture. Freshness is based on the last reported timestamp.",
    columns: ["vehicleCode", "deviceName", "sensorType", "latestReading", "expectedRange", "sensorStatus", "signalStrength", "powerStatus", "calibrationStatus", "alertStatus"],
    emptyTitle: "No cold-chain telemetry found",
    emptySubtitle: "No cold-chain sensors are visible for this tenant and filter set.",
    searchPlaceholder: "Search reefer unit, route, reading, shipment, or alert status...",
    query: () => telematicsService.getColdChainRecords(),
    requiredViewPermission: PERMISSIONS.TELEMATICS_SENSORS_VIEW,
    requiredExportPermission: PERMISSIONS.TELEMATICS_SENSORS_EXPORT,
    requiredUpdatePermission: "fleet:manage",
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
    if (tab === "Watch") return record.dataFreshnessStatus === "Watch";
    if (tab === "Stale GPS") return /h ago|Stale/i.test(record.staleGps) || record.dataFreshnessStatus === "Stale";
    if (tab === "Offline") return record.offlineWarning;
    return (record.deviceHealthAvailable && record.deviceHealth < 70) || record.alertStatus === "Open";
  }
  if (kind === "obd-j1939") {
    if (tab === "Fresh") return record.dataFreshnessStatus === "Fresh";
    if (tab === "Watch") return record.dataFreshnessStatus === "Watch";
    if (tab === "Stale") return record.dataFreshnessStatus === "Stale";
    return record.troubleCodes.length > 0;
  }
  if (kind === "sensor-health" || kind === "cold-chain") {
    if (tab === "Nominal") return record.sensorStatus === "Nominal";
    if (tab === "Watch") return record.sensorStatus === "Watch";
    if (tab === "Alerting") return record.sensorStatus === "Alerting" || record.alertStatus === "Open";
    return record.offlineWarning;
  }
  return true;
}

function isServerPaged(kind: TelematicsKind): kind is "gps-tracking" | "obd-j1939" {
  return kind === "gps-tracking" || kind === "obd-j1939";
}

function serverView(kind: "gps-tracking" | "obd-j1939", tab: string) {
  if (kind === "gps-tracking") {
    if (tab === "Online") return "online";
    if (tab === "Delayed / Watch") return "delayed-gps";
    if (tab === "Stale GPS") return "stale-gps";
    if (tab === "Offline") return "offline";
    if (tab === "Critical") return "attention";
    return "all";
  }
  if (tab === "Fresh") return "fresh";
  if (tab === "Watch") return "watch";
  if (tab === "Stale") return "stale";
  if (tab === "Issues") return "issues";
  return "all";
}

const columnLabels: Record<string, string> = {
  serialNumber: "Device serial",
  vehicleCode: "Vehicle",
  deviceName: "Device",
  locationLabel: "Location",
  positionSource: "Source",
  positionAccuracy: "Accuracy",
  deviceFixAt: "Device fix",
  gatewayReceivedAt: "Gateway receipt",
  dataFreshnessStatus: "Freshness",
  routingReadiness: "Operational use",
  protocolType: "Protocol",
  troubleCodes: "Trouble codes",
  engineStatus: "Engine status",
  odometer: "Odometer",
  fuelLevel: "Fuel level",
  batteryVoltage: "Battery voltage",
  lastEngineDataAt: "Last engine data",
};

// Treat the service's honest empty markers ("—", "", "No ...") as "no value" so we
// never join them into a half-real string like "—, —" or "— mph · —".
function hasValue(value: string | number | null | undefined) {
  if (value == null) return false;
  const text = String(value).trim();
  return text !== "" && text !== "—";
}

function formatCoordinates(lat: string, lng: string) {
  return hasValue(lat) && hasValue(lng) ? `${lat}, ${lng}` : "No fix";
}

function formatSpeedHeading(speed: string, heading: string) {
  const speedPart = hasValue(speed) ? `${speed} mph` : null;
  const headingPart = hasValue(heading) ? heading : null;
  return [speedPart, headingPart].filter(Boolean).join(" · ") || "—";
}

function renderCell(column: string, row: TelematicsClusterRecord) {
  if (column === "serialNumber") {
    return (
      <div>
        <p className="font-semibold text-slate-900">{row.serialNumber}</p>
        <p className="text-xs text-slate-400">{row.deviceName} · {row.provider}</p>
      </div>
    );
  }
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
  if (column === "troubleCodes") return row.troubleCodes.join(", ") || "None reported";
  return String(row[column as keyof TelematicsClusterRecord] ?? "—");
}

function permissionTitle(allowed: boolean, message: string) {
  return allowed ? message : "You do not have permission to perform this action.";
}

function isForbidden(error: unknown) {
  return (error as { response?: { status?: unknown } } | null)?.response?.status === 403;
}

export function TelematicsCommandPage({ kind }: { kind: TelematicsKind }) {
  const config = configs[kind];
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const hasPermission = useHasPermission();
  const hasDirectPermission = useHasDirectPermission();
  const kpiIcon = kind === "gps-tracking"
    ? <MapPinned className="h-4 w-4" />
    : kind === "obd-j1939"
      ? <Gauge className="h-4 w-4" />
      : <Thermometer className="h-4 w-4" />;

  const canExport = hasPermission(config.requiredExportPermission);
  const canUpdate = hasPermission(config.requiredUpdatePermission);
  const canCreateMaintenance = canUpdate && hasPermission(PERMISSIONS.MAINTENANCE_CREATE);
  // Read destinations use the same semantic permission aliases as the route
  // guard and backend RequirePermission policy. Direct-only checks are reserved
  // for security-sensitive mutations such as governed imports and assignments.
  const canView = hasPermission(config.requiredViewPermission);
  const canViewGeofences = hasPermission("map:view");
  const canViewDevices = hasPermission(PERMISSIONS.TELEMATICS_DEVICES_VIEW);
  const canViewVehicles = hasPermission(PERMISSIONS.VEHICLES_VIEW);
  const canViewJobs = hasDirectPermission(PERMISSIONS.SHIPMENTS_VIEW);
  const canViewMap = hasPermission(PERMISSIONS.TELEMETRY_LIVE_STATE_READ);
  const [tab, setTab] = useState(config.filterTabs[0]);
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [serverSort, setServerSort] = useState<"risk" | "freshness" | "lastFix" | "vehicle" | "serial" | "provider">("risk");
  const [selected, setSelected] = useState<TelematicsClusterRecord | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      setSearch(searchInput);
      setPage(1);
    }, 300);
    return () => window.clearTimeout(timer);
  }, [searchInput]);

  const paged = isServerPaged(kind);
  const pageSize = 50;
  const recordsQ = useQuery({
    queryKey: ["telematics-cluster", kind, paged ? page : 1, paged ? search : "", paged ? tab : "", paged ? serverSort : ""],
    queryFn: async (): Promise<TelemetryClusterPageResult> => {
      if (paged) {
        return telematicsService.getTelemetryClusterPage(kind, {
          page,
          pageSize,
          search,
          view: serverView(kind, tab),
          sort: serverSort,
          direction: serverSort === "vehicle" || serverSort === "serial" || serverSort === "provider" ? "asc" : "desc",
        });
      }
      const items = await config.query();
      return { items, total: items.length, page: 1, pageSize: Math.max(1, items.length), summary: { active: items.length, offline: 0, attention: 0, online: 0, delayed: 0, stale: 0, noPosition: 0 } };
    },
    enabled: canView,
    staleTime: 20_000,
  });
  const detailQ = useQuery({
    queryKey: ["telematics-cluster-detail", kind, selected?.deviceId],
    queryFn: () => telematicsService.getDeviceById(String(selected?.deviceId)),
    // GPS/diagnostics rows already came from the dedicated, permission-gated
    // cluster projection. Do not over-fetch the generic device/location/fault
    // detail feeds, whose broader permissions are intentionally different.
    enabled: canView && Boolean(selected?.deviceId) && !paged,
    staleTime: 20_000,
  });

  // Honest result reporting: the service returns { success:false, reason } for
  // operations that have no backend persistence endpoint yet. We surface that
  // truthfully instead of claiming a success the backend never performed. The live
  // data is always re-fetched so the view reflects the real server state.
  const okOrReason = (result: unknown, okNotice: string, failNotice: string) => {
    const failed = result && typeof result === "object" && "success" in result && (result as { success?: boolean }).success === false;
    if (!failed) { setNotice(okNotice); return; }
    const reason = (result as { reason?: string }).reason;
    setNotice(reason ? `${failNotice} (${reason})` : failNotice);
  };
  const refreshMut = useMutation({
    mutationFn: (deviceId: string | number) => telematicsService.refreshDeviceStatus(deviceId),
    onSuccess: async (result) => {
      // Re-fetch first so the operator always sees the freshest live snapshot,
      // then report honestly whether a manual refresh was actually performed.
      await queryClient.invalidateQueries({ queryKey: ["telematics-cluster", kind] });
      await queryClient.invalidateQueries({ queryKey: ["telematics-cluster-detail"] });
      okOrReason(
        result,
        kind === "gps-tracking" ? "GPS stream refreshed." : kind === "obd-j1939" ? "Diagnostics stream refreshed." : "Sensor readings refreshed.",
        "Live data re-fetched. Manual device refresh is not available yet.",
      );
    },
  });
  const maintenanceMut = useMutation({
    mutationFn: async (record: TelematicsClusterRecord) => {
      const maintenance = await telematicsService.createMaintenanceTask(record.deviceId, config.title);
      const vehicleId = Number(maintenance.vehicleId);
      if (!Number.isSafeInteger(vehicleId) || vehicleId <= 0) {
        throw new Error("A current vehicle assignment is required before creating a maintenance follow-up.");
      }
      return maintenanceApi.createWorkOrder({
        vehicleId,
        title: maintenance.title,
        serviceType: kind === "obd-j1939" ? "Telematics diagnostic review" : "Telematics sensor review",
        description: maintenance.note,
        priority: record.deviceHealthAvailable && record.deviceHealth < 70 ? "High" : "Medium",
        estimatedCost: 0,
        scheduledAt: new Date().toISOString().slice(0, 10),
      });
    },
    onMutate: () => setNotice(null),
    onSuccess: () => {
      setNotice("Maintenance follow-up created.");
    },
    onError: () => setNotice(null),
  });

  const rows = useMemo(() => {
    const query = search.trim().toLowerCase();
    const records = recordsQ.data?.items ?? [];
    if (paged) return records;
    return records.filter((record) => {
      const haystack = [
        record.vehicleCode,
        record.serialNumber,
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
  }, [recordsQ.data?.items, search, kind, tab, paged]);

  const exportMut = useMutation({
    mutationFn: async () => paged
      ? telematicsService.exportTelemetryClusterCsv(kind, {
          search,
          view: serverView(kind, tab),
          sort: serverSort,
          direction: serverSort === "vehicle" || serverSort === "serial" || serverSort === "provider" ? "asc" : "desc",
        }, config.columns)
      : telematicsService.exportClusterCsv(rows, config.columns),
    onSuccess: (csv) => downloadCsv(`opstrax-${kind}.csv`, csv),
    onError: (error) => setNotice(apiErrorMessage(error, "The complete authorized export could not be created. Retry the export.")),
  });

  const selectedRecord = rows.find((row) => row.id === selected?.id) ?? selected;
  const offlineCount = paged ? recordsQ.data?.summary.offline ?? 0 : rows.filter((row) => row.offlineWarning).length;
  const issueCount = paged ? recordsQ.data?.summary.attention ?? 0 : rows.filter((row) => row.alertStatus === "Open" || (row.troubleCodes?.length ?? 0) > 0 || (row.deviceHealthAvailable && row.deviceHealth < 70)).length;

  // Average health only counts rows that carry a real numeric health signal. With an
  // empty (or all-signal-less) fleet there is nothing to average, so we surface "—"
  // rather than a fabricated 0% / NaN%.
  const healthValues = rows.filter((row) => row.deviceHealthAvailable).map((row) => Number(row.deviceHealth)).filter((value) => Number.isFinite(value));
  const avgHealth = healthValues.length
    ? Math.round(healthValues.reduce((sum, value) => sum + value, 0) / healthValues.length)
    : null;

  // Distinguish "this tenant has no live telemetry at all" from "the current search /
  // filter simply matched nothing" — the two empty states carry different meaning.
  const hasAnyLiveData = (recordsQ.data?.total ?? 0) > 0;
  const total = recordsQ.data?.total ?? 0;
  const fleetUnits = paged ? recordsQ.data?.summary.active ?? 0 : total;
  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const emptyState = resolveTelemetryEmptyState({
    rowCount: rows.length,
    searchInput,
    appliedSearch: search,
    tab,
  });

  if (!canView || (recordsQ.isError && isForbidden(recordsQ.error))) {
    return (
      <div className="panel p-8 text-center" role="status">
        <p className="text-lg font-semibold text-slate-900">{config.title} access restricted</p>
        <p className="mx-auto mt-2 max-w-xl text-sm leading-6 text-slate-600">
          This evidence is not available for the current role. Ask a tenant administrator to grant the appropriate access if it is required for your work.
        </p>
      </div>
    );
  }
  if (recordsQ.isLoading) return <LoadingState />;
  if (recordsQ.isError) {
    const fallback = kind === "obd-j1939"
      ? "OBD / J1939 evidence could not be loaded. Retry, or ask a tenant administrator to confirm diagnostics access for this role."
      : `Unable to load ${config.title.toLowerCase()} right now.`;
    return (
      <ErrorState
        message={apiErrorMessage(recordsQ.error, fallback)}
        onRetry={() => void recordsQ.refetch()}
      />
    );
  }

  return (
    <div className="fleet-console flex h-full flex-col gap-3 overflow-y-auto">
      <PageHeader
        eyebrow={config.eyebrow}
        title={config.title}
        description={config.description}
        actions={
          <>
            <button
              className="btn-ghost"
              disabled={!canExport || exportMut.isPending}
              title={permissionTitle(canExport, "Export every authorized row matching the current search and filter.")}
              onClick={() => canExport && exportMut.mutate()}
            >
              <Download className="h-4 w-4" /> {exportMut.isPending ? "Preparing export…" : "Export CSV"}
            </button>
            <button className="btn-primary" onClick={() => navigate("/iot-devices")}>
              <Truck className="h-4 w-4" /> Open Device Command
            </button>
            {kind === "gps-tracking" ? (
              <button
                className="btn-ghost"
                disabled={!canViewGeofences}
                title={permissionTitle(canViewGeofences, "Open geofence setup and alert boundaries.")}
                onClick={() => canViewGeofences && navigate("/geofences")}
              >
                <MapPinned className="h-4 w-4" /> Manage Geofences
              </button>
            ) : null}
            {kind === "cold-chain" ? <button className="btn-ghost" onClick={() => navigate("/fleet-cold-chain")}><Thermometer className="h-4 w-4" /> Cold Chain Monitor</button> : null}
          </>
        }
      />

      {notice ? (
        <div className="panel flex items-center justify-between gap-4 border border-emerald-400/20 bg-emerald-500/10 p-4 text-sm text-emerald-100">
          <span>{notice}</span>
          <button className="icon-btn" aria-label="Dismiss action message" onClick={() => setNotice(null)}><X className="h-4 w-4" /></button>
        </div>
      ) : null}

      {refreshMut.isError || maintenanceMut.isError ? (
        <div role="alert" className="panel border border-rose-300 bg-rose-50 p-4 text-sm text-rose-800">
          {refreshMut.isError
            ? apiErrorMessage(refreshMut.error, "The telemetry refresh was not completed.")
            : apiErrorMessage(maintenanceMut.error, "The maintenance follow-up was not created.")}
        </div>
      ) : null}

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <KpiCard label="Fleet managed units" value={fleetUnits} status="Active" icon={kpiIcon} />
        <KpiCard label="Fleet offline / stale" value={offlineCount} status={offlineCount ? "Critical" : "Healthy"} icon={<AlertTriangle className="h-4 w-4" />} />
        <KpiCard label="Fleet needs action" value={issueCount} status={issueCount ? "Watch" : "Healthy"} icon={<RadioTower className="h-4 w-4" />} />
        <KpiCard
          label="Current page health"
          value={avgHealth == null ? "—" : `${avgHealth}%`}
          status={avgHealth == null ? "Unknown" : avgHealth >= 85 ? "Healthy" : avgHealth >= 70 ? "Watch" : "Critical"}
          icon={<BatteryCharging className="h-4 w-4" />}
        />
      </div>
      {paged ? <p className="text-xs text-slate-500">Fleet cards cover every authorized unit. Health is averaged only across evidence-bearing rows on the current page.</p> : null}

      {kind === "gps-tracking" ? (
        <div className="grid gap-4 xl:grid-cols-3">
          {rows.slice(0, 6).map((row) => (
            <button type="button" key={row.id} className="panel rounded-2xl p-4 text-left transition hover:border-teal-300 hover:bg-slate-50" onClick={() => setSelected(row)}>
              <div className="flex items-start justify-between gap-3">
                <div>
                  <p className="font-semibold text-slate-900">{row.vehicleCode}</p>
                  <p className="mt-1 text-xs text-slate-400">{row.locationLabel} · {row.positionSource}</p>
                </div>
                <RiskBadge risk={row.geofenceStatus} />
              </div>
              <div className="mt-4 grid gap-2 text-sm text-slate-700">
                <div className="flex justify-between"><span>GPS ping</span><span>{row.staleGps || "—"}</span></div>
                <div className="flex justify-between"><span>Coordinates</span><span>{formatCoordinates(row.latitude, row.longitude)}</span></div>
                <div className="flex justify-between"><span>Speed / heading</span><span>{formatSpeedHeading(row.speedMph, row.heading)}</span></div>
                <div className="flex justify-between gap-3"><span>Operational use</span><span className="text-right">{row.routingReadiness}</span></div>
              </div>
            </button>
          ))}
        </div>
      ) : null}

      <div className="panel space-y-4 p-4">
        <div className="flex flex-col gap-3 xl:flex-row xl:items-center xl:justify-between">
          <input
            className="field xl:min-w-[360px]"
            value={searchInput}
            onChange={(event) => setSearchInput(event.target.value)}
            placeholder={config.searchPlaceholder}
          />
          {paged ? <label className="min-w-48">
            <span className="sr-only">Sort telemetry records</span>
            <select className="field" value={serverSort} onChange={(event) => { setServerSort(event.target.value as typeof serverSort); setPage(1); }}>
              <option value="risk">Highest risk first</option>
              <option value="freshness">Freshness risk</option>
              <option value="lastFix">Latest fix first</option>
              <option value="vehicle">Vehicle</option>
              <option value="serial">Device serial</option>
              <option value="provider">Provider</option>
            </select>
          </label> : null}
          <div className="flex flex-wrap gap-2">
            {config.filterTabs.map((item) => (
              <button key={item} className={tab === item ? "btn-primary py-2 text-xs" : "btn-ghost py-2 text-xs"} onClick={() => { setTab(item); setPage(1); }}>
                {item}
              </button>
            ))}
          </div>
        </div>

        {emptyState !== "rows" ? (
          emptyState === "filtered-empty" || hasAnyLiveData ? (
            // An active search/filter produced no matches. Never describe this as
            // an empty tenant or tell the operator to provision existing devices.
            <EmptyState title={config.emptyTitle} subtitle={config.emptySubtitle} />
          ) : (
            // No devices reported any live telemetry for this tenant yet.
            <EmptyState
              title={kind === "cold-chain" ? "No reported cold-chain readings yet" : "No live telemetry yet"}
              subtitle={kind === "cold-chain"
                ? "No cold-chain device with a reported timestamp is visible for this tenant. Open Cold Chain Monitor to register devices or enter a clearly marked manual observation."
                : `No ${config.title.toLowerCase()} is streaming for this tenant. Provision or activate a device to see live data here.`}
            />
          )
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-[1280px] text-sm">
              <thead>
                <tr className="border-b border-slate-200">
                  {config.columns.map((column) => (
                    <th key={column} className={`px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500 ${column === config.columns[0] ? "sticky left-0 z-10 bg-white" : ""}`}>{columnLabels[column] ?? column}</th>
                  ))}
                  <th className="sticky right-0 z-10 bg-white px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((row) => (
                  <tr key={row.id} className="transition hover:bg-slate-50">
                    {config.columns.map((column) => (
                      <td key={column} className={`px-4 py-3 text-slate-700 ${column === config.columns[0] ? "sticky left-0 z-[1] bg-white" : ""}`}>
                        {renderCell(column, row)}
                      </td>
                    ))}
                    <td className="sticky right-0 z-[1] bg-white px-4 py-3">
                      <div className="flex flex-wrap gap-2">
                        <button className="btn-ghost h-8 px-3" onClick={() => setSelected(row)}>
                          {kind === "gps-tracking" ? "Inspect position" : kind === "obd-j1939" ? "View diagnostics" : "View sensor"}
                        </button>
                        {canViewDevices ? <button className="btn-ghost h-8 px-3" onClick={() => navigate("/iot-devices")}>View device</button> : null}
                        {canViewVehicles ? <button className="btn-ghost h-8 px-3" onClick={() => navigate("/vehicles")}>View vehicle</button> : null}
                        {canViewJobs && row.shipmentId !== "No active shipment" ? (
                          <button className="btn-ghost h-8 px-3" onClick={() => navigate("/jobs")}>Open trip</button>
                        ) : null}
                        <button
                          className="btn-ghost h-8 px-3"
                          disabled={!canUpdate || refreshMut.isPending}
                          title={permissionTitle(canUpdate, kind === "gps-tracking" ? "Refresh GPS visibility." : "Refresh telematics stream.")}
                          onClick={() => canUpdate && refreshMut.mutate(row.deviceId)}
                        >
                          Reload snapshot
                        </button>
                        {kind === "obd-j1939" ? (
                          <>
                            <button
                              className="btn-primary h-8 px-3"
                              disabled={!canCreateMaintenance || maintenanceMut.isPending}
                              title={permissionTitle(canCreateMaintenance, "Create a maintenance follow-up.")}
                              onClick={() => canCreateMaintenance && maintenanceMut.mutate(row)}
                            >
                              Create Maintenance
                            </button>
                          </>
                        ) : null}
                        {kind === "sensor-health" ? (
                          <>
                            <button
                              className="btn-primary h-8 px-3"
                              disabled={!canCreateMaintenance || maintenanceMut.isPending}
                              title={permissionTitle(canCreateMaintenance, "Create a maintenance task for this sensor.")}
                              onClick={() => canCreateMaintenance && maintenanceMut.mutate(row)}
                            >
                              Create Task
                            </button>
                          </>
                        ) : null}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {paged && total > 0 ? (
          <nav className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-200 pt-4" aria-label={`${config.title} pagination`}>
            <p className="text-sm text-slate-600">
              Showing {(page - 1) * pageSize + 1}–{Math.min(page * pageSize, total)} of {total}
            </p>
            <div className="flex items-center gap-2">
              <button className="btn-ghost py-2 text-xs" disabled={page <= 1 || recordsQ.isFetching} onClick={() => setPage((current) => Math.max(1, current - 1))}>Previous</button>
              <span className="text-sm text-slate-600">Page {page} of {totalPages}</span>
              <button className="btn-ghost py-2 text-xs" disabled={page >= totalPages || recordsQ.isFetching} onClick={() => setPage((current) => Math.min(totalPages, current + 1))}>Next</button>
            </div>
          </nav>
        ) : null}
      </div>

      {selectedRecord ? (
        <div className="fixed inset-0 z-50 flex justify-end bg-black/55 backdrop-blur-sm" onClick={() => setSelected(null)}>
          <aside className="h-full w-full max-w-5xl overflow-y-auto border-l border-white/[0.09] bg-slate-950 p-6 shadow-2xl" onClick={(event) => event.stopPropagation()}>
            <button className="float-right icon-btn" aria-label="Close telematics details" onClick={() => setSelected(null)}><X className="h-4 w-4" /></button>
            {!paged && detailQ.isLoading ? (
              <LoadingState />
            ) : !paged && (detailQ.isError || !detailQ.data) ? (
              <ErrorState message="Unable to load telematics detail." />
            ) : (
              <TelematicsDetailDrawer
                kind={kind}
                row={selectedRecord}
                detail={detailQ.data}
                canUpdate={canUpdate}
                canCreateMaintenance={canCreateMaintenance}
                isMaintenancePending={maintenanceMut.isPending}
                canViewDevices={canViewDevices}
                canViewVehicles={canViewVehicles}
                canViewJobs={canViewJobs}
                canViewMap={canViewMap}
                onRefresh={() => canUpdate && refreshMut.mutate(selectedRecord.deviceId)}
                onMaintenance={() => canCreateMaintenance && maintenanceMut.mutate(selectedRecord)}
              />
            )}
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
  canCreateMaintenance,
  isMaintenancePending,
  canViewDevices,
  canViewVehicles,
  canViewJobs,
  canViewMap,
  onRefresh,
  onMaintenance,
}: {
  kind: TelematicsKind;
  row: TelematicsClusterRecord;
  detail?: DeviceDetailRecord;
  canUpdate: boolean;
  canCreateMaintenance: boolean;
  isMaintenancePending: boolean;
  canViewDevices: boolean;
  canViewVehicles: boolean;
  canViewJobs: boolean;
  canViewMap: boolean;
  onRefresh: () => void;
  onMaintenance: () => void;
}) {
  const latestDiagnostic = detail?.diagnostics[0];
  const latestSensor = detail?.sensorReadings[0];
  const latestHealth = detail?.healthEvents[0];
  return (
    <>
      <p className="section-title text-teal-300">{kind === "gps-tracking" ? "GPS Detail" : kind === "obd-j1939" ? "Diagnostics Detail" : "Sensor Detail"}</p>
      <div className="mt-3 flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 className="text-2xl font-bold text-white">{row.vehicleCode}</h2>
          <p className="mt-1 font-mono text-sm font-semibold text-teal-200">{row.serialNumber}</p>
          <p className="mt-1 text-sm text-slate-400">{row.deviceName} · {row.driverName} · {row.routeAssociation}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <StatusBadge status={row.dataFreshnessStatus} />
          <RiskBadge risk={row.signalStrength} />
        </div>
      </div>

      <div className="mt-6 flex flex-wrap gap-3">
        {canViewMap ? <button className="btn-ghost" disabled={!row.positionAvailable} title={row.positionAvailable ? "Open the reported position on the fleet map and verify its freshness before operational use." : "Map unavailable because this record has no valid position."} onClick={() => row.positionAvailable && window.location.assign(`/map-view`)}><MapPinned className="h-4 w-4" /> {row.positionAvailable ? "View on map" : "No valid map fix"}</button> : null}
        {canViewDevices ? <button className="btn-ghost" onClick={() => window.location.assign(`/iot-devices`)}><Truck className="h-4 w-4" /> View device</button> : null}
        {canViewVehicles ? <button className="btn-ghost" onClick={() => window.location.assign(`/vehicles`)}><Truck className="h-4 w-4" /> View vehicle</button> : null}
        {canViewJobs && row.shipmentId !== "No active shipment" ? <button className="btn-ghost" onClick={() => window.location.assign(`/jobs`)}><Truck className="h-4 w-4" /> Open trip</button> : null}
        <button className="btn-ghost" disabled={!canUpdate} title={permissionTitle(canUpdate, "Reload the latest server snapshot.")} onClick={onRefresh}><RefreshCw className="h-4 w-4" /> Reload snapshot</button>
        {kind !== "gps-tracking" ? <button className="btn-primary" disabled={!canCreateMaintenance || isMaintenancePending} title={permissionTitle(canCreateMaintenance, "Create a maintenance follow-up.")} onClick={onMaintenance}><Wrench className="h-4 w-4" /> Create maintenance</button> : null}
      </div>

      <div className="mt-6 grid gap-4 lg:grid-cols-3">
        <InfoPanel title="Position evidence" items={[
          ["Location", row.locationLabel],
          ["Source", row.positionSource],
          ["Source provider", row.positionProvider],
          ["Accuracy", row.positionAccuracy],
          ["Confidence", row.positionConfidence],
          ["Device fix time", row.deviceFixAt],
          ["Gateway receipt", row.gatewayReceivedAt],
          ["Freshness", row.dataFreshnessStatus],
          ["Operational use", row.routingReadiness],
        ]} />
        <InfoPanel title="Vehicle / Device" items={[
          ["Device serial", row.serialNumber],
          ["Device model", row.deviceName],
          ["Provider", row.provider],
          ["Vehicle", row.vehicleCode],
          ["Driver", row.driverName],
          ["Signal", row.signalStrength],
          ["Health", row.deviceHealthAvailable ? `${row.deviceHealth}%` : "Unknown — insufficient evidence"],
        ]} />
        <InfoPanel title={kind === "obd-j1939" ? "Diagnostics" : "Sensors"} items={[
          ["Protocol", row.protocolType],
          ["Trouble codes", row.troubleCodes.join(", ") || "None reported"],
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
          ["Emissions classification", row.emissionsStatus],
          ["Last engine data", row.lastEngineDataAt],
        ]} />
        <InfoPanel title="Sensor / Health" items={[
          ["Sensor type", row.sensorType],
          ["Sensor status", row.sensorStatus],
          ["Calibration", row.calibrationStatus],
          ["Power", row.powerStatus],
          ["Signal strength", row.signalStrength],
          ["Health event", latestHealth ? `${latestHealth.status} · ${latestHealth.score}%` : "No health event recorded"],
        ]} />
      </div>

      <div className="mt-6 panel p-5">
        <p className="section-title">Field Notes</p>
        <div className="mt-4 grid gap-3 md:grid-cols-3">
          <ContextCard
            title="Latest diagnostic"
            body={latestDiagnostic
              ? `${latestDiagnostic.result} · ${latestDiagnostic.faultCode}`
              : row.troubleCodes.length
                ? `Active fault codes: ${row.troubleCodes.join(", ")}`
                : "No diagnostics captured for this unit yet."}
          />
          <ContextCard title="Latest sensor reading" body={latestSensor ? `${latestSensor.temperature ?? latestSensor.tirePressure ?? latestSensor.fuelLevel ?? "No reading"} · ${latestSensor.recordedAt}` : "No sensor reading captured for this unit yet."} />
          <ContextCard title="Recommended action" body={row.recommendedAction} />
        </div>
      </div>
    </>
  );
}

function InfoPanel({ title, items }: { title: string; items: Array<[string, string]> }) {
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

function ContextCard({ title, body }: { title: string; body: string }) {
  return (
    <div className="rounded-xl border border-white/[0.06] bg-white/[0.02] p-4">
      <p className="font-semibold text-white">{title}</p>
      <p className="mt-2 text-sm text-slate-400">{body}</p>
    </div>
  );
}

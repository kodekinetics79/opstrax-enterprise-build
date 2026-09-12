import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  Activity, AlertTriangle, BellRing, ChevronRight, Clock, Gauge as GaugeIcon,
  MapPin, Package, Radio, Search, ShieldAlert, Truck, Wifi, WifiOff, Wrench, Zap,
} from "lucide-react";
import { useNavigate } from "react-router";
import { LoadingState, PageHeader } from "@/components/ui";
import { vehiclesApi } from "@/services/vehiclesApi";
import { alertsApi } from "@/services/alertsApi";
import { jobsApi } from "@/services/jobsApi";
import { useAuth } from "@/hooks/useAuth";
import { useHasDirectPermission, useHasPermission } from "@/hooks/usePermission";
import type { AnyRecord } from "@/types";
import { fleetQueryFingerprint, resolveFleetQueryPresentation } from "@/utils/fleetQueryPresentation";

// Vehicle movement status is derived from real vehicle + telemetry fields. We do NOT
// fabricate GPS speed/location — where live telemetry is absent we show honest blanks.
type VStatus = "Active" | "Idle" | "Available" | "Offline" | "OOS" | "Unknown";
type MStatus = "Healthy" | "Due Soon" | "Overdue" | "Critical" | "Unknown";
type Signal  = "Online" | "Degraded" | "Offline" | "Unknown";

const COMMAND_STATE_LABELS: Record<VStatus, string> = {
  Active: "Operational active",
  Idle: "Operational idle",
  Available: "Operational available",
  Offline: "Telemetry offline",
  OOS: "Dispatch restricted",
  Unknown: "State unknown",
};

interface FleetRow {
  id:          string;
  vehicleId:   string;
  type:        string;
  driver:      string | null;
  status:      VStatus;
  location:    string | null;
  maintenance: MStatus;
  signal:      Signal;
  riskScore:   number;
  readiness:   number;
  flag:        string | null;
}

const STATUS_TABS = ["All", "Active", "Idle", "Available", "OOS", "Offline", "Unknown"] as const;
type Tab = typeof STATUS_TABS[number];
const STATUS_TAB_LABELS: Record<Tab, string> = {
  All: "All command states",
  ...COMMAND_STATE_LABELS,
};

const STATUS_CFG: Record<VStatus, { badge: string; dot: string; label: string }> = {
  Active:    { badge: "badge badge-success",                                               dot: "bg-emerald-500 animate-pulse", label: COMMAND_STATE_LABELS.Active },
  Idle:      { badge: "badge badge-warning",                                               dot: "bg-amber-400",                 label: COMMAND_STATE_LABELS.Idle },
  Available: { badge: "inline-flex items-center rounded-md px-2 py-0.5 text-[10px] font-semibold bg-slate-100 text-slate-600 border border-slate-200", dot: "bg-slate-400", label: COMMAND_STATE_LABELS.Available },
  Offline:   { badge: "badge badge-danger",                                                dot: "bg-red-500",                   label: COMMAND_STATE_LABELS.Offline },
  OOS:       { badge: "inline-flex items-center rounded-md px-2 py-0.5 text-[10px] font-semibold bg-red-100 text-red-700 border border-red-200",      dot: "bg-red-500 animate-pulse", label: COMMAND_STATE_LABELS.OOS },
  Unknown:   { badge: "inline-flex items-center rounded-md px-2 py-0.5 text-[10px] font-semibold bg-slate-100 text-slate-600 border border-slate-200", dot: "bg-slate-400", label: COMMAND_STATE_LABELS.Unknown },
};

const MAINT_CFG: Record<MStatus, { cls: string }> = {
  Healthy:   { cls: "text-emerald-600 text-xs font-medium" },
  "Due Soon":{ cls: "text-amber-600 text-xs font-medium" },
  Overdue:   { cls: "text-red-600 text-xs font-semibold" },
  Critical:  { cls: "text-red-700 text-xs font-bold" },
  Unknown:   { cls: "text-slate-500 text-xs font-medium" },
};

const SIGNAL_ICON: Record<Signal, React.ReactNode> = {
  Online:   <Wifi className="h-3.5 w-3.5 text-emerald-500" />,
  Degraded: <Wifi className="h-3.5 w-3.5 text-amber-500" />,
  Offline:  <WifiOff className="h-3.5 w-3.5 text-red-400" />,
  Unknown:  <Radio className="h-3.5 w-3.5 text-slate-400" />,
};

function deriveStatus(v: AnyRecord): VStatus {
  if (v.outOfService === true || /out.?of.?service|oos/i.test(String(v.status ?? ""))) return "OOS";
  const s = String(v.status ?? "").toLowerCase();
  if (/maintenance|repair/.test(s)) return "OOS";
  if (/active|on route|driving|in.?transit|dispatched/.test(s)) return "Active";
  if (/idle|idling/.test(s)) return "Idle";
  if (/available|ready/.test(s)) return "Available";
  if (/offline|inactive/.test(s)) return "Offline";
  return "Unknown";
}

function deriveMaint(v: AnyRecord): MStatus {
  const s = String(v.status ?? "").toLowerCase();
  if (/critical/.test(s)) return "Critical";
  if (/maintenance|repair|overdue/.test(s)) return "Overdue";
  const readinessRaw = v.readinessScore ?? v.fleetReadinessScore;
  if (readinessRaw == null || !Number.isFinite(Number(readinessRaw))) return "Unknown";
  const readiness = Number(readinessRaw);
  if (readiness < 60) return "Overdue";
  if (readiness < 80) return "Due Soon";
  return "Healthy";
}

function deriveSignal(v: AnyRecord): Signal {
  const device = String(v.deviceStatus ?? "").toLowerCase();
  if (!device) return "Unknown";
  if (device === "online") return "Online";
  if (/degraded|weak|intermittent/.test(device)) return "Degraded";
  return "Offline";
}

function deriveFlag(v: AnyRecord): string | null {
  const risk = Number(v.riskScore ?? v.riskHeatScore ?? 0);
  if (String(v.cameraStatus ?? "").toLowerCase() === "offline") return "Camera offline";
  if (v.outOfService === true) return "Out of service — do not dispatch";
  if (/maintenance|repair/i.test(String(v.status ?? ""))) return "In maintenance";
  if (risk >= 60) return `Elevated risk score (${Math.round(risk)})`;
  return null;
}

function timeAgo(iso?: string): string | null {
  if (!iso) return null;
  const t = Date.parse(iso);
  if (Number.isNaN(t)) return null;
  const mins = Math.max(0, Math.round((Date.now() - t) / 60_000));
  if (mins < 60) return `${mins}m`;
  const hours = Math.round(mins / 60);
  if (hours < 48) return `${hours}h`;
  return `${Math.round(hours / 24)}d`;
}

const SEVERITY_RANK: Record<string, number> = { critical: 0, high: 1, warning: 2, info: 3 };
const SEVERITY_LED: Record<string, string> = {
  critical: "deck-led-red", high: "deck-led-amber", warning: "deck-led-amber", info: "deck-led-sky",
};
const SEVERITY_TEXT: Record<string, string> = {
  critical: "text-red-700", high: "text-orange-700", warning: "text-amber-700", info: "text-sky-700",
};

export function FleetOverviewPage() {
  const navigate = useNavigate();
  const { session } = useAuth();
  const hasPermission = useHasPermission();
  const hasDirectPermission = useHasDirectPermission();
  const [tab, setTab] = useState<Tab>("All");
  const [page, setPage] = useState(1);
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState("vehicle");
  const [sortOrder, setSortOrder] = useState<"asc" | "desc">("asc");
  const pageSize = 50;

  const canViewAlerts = hasDirectPermission("alerts:view");
  const canViewJobs = hasDirectPermission("shipments:view");
  const canViewVehicles = hasPermission("vehicles:view");
  const canViewDevices = hasPermission("telemetry.devices.read")
    && (session?.entitlementPolicyMode !== "package_allowlist" || session.entitlements?.telematics === true);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      setSearch(searchInput.trim());
      setPage(1);
    }, 300);
    return () => window.clearTimeout(timer);
  }, [searchInput]);

  const requestFingerprint = fleetQueryFingerprint({
    page, pageSize, search, status: tab, sort, order: sortOrder,
  });
  const vehiclesQ = useQuery({
    queryKey: ["fleet-overview-vehicles", requestFingerprint],
    queryFn: async (): Promise<AnyRecord & { queryFingerprint: string }> => {
      const response = await vehiclesApi.fleetOverview({ page, pageSize, search, status: tab, sort, order: sortOrder });
      return { ...response, queryFingerprint: requestFingerprint };
    },
    refetchInterval: 30_000,
    enabled: canViewVehicles,
  });
  const alertsQ   = useQuery({ queryKey: ["fleet-overview-alerts"],   queryFn: () => alertsApi.list(),  refetchInterval: 60_000, enabled: canViewAlerts });
  const jobsQ     = useQuery({ queryKey: ["fleet-overview-jobs"],     queryFn: () => jobsApi.summary(), refetchInterval: 60_000, enabled: canViewJobs });

  const fleetPresentation = resolveFleetQueryPresentation({
    rawSearch: searchInput,
    appliedSearch: search,
    requestFingerprint,
    responseFingerprint: vehiclesQ.data?.queryFingerprint as string | undefined,
    hasData: Boolean(vehiclesQ.data),
    isFetching: vehiclesQ.isFetching,
  });
  const isFleetSettling = fleetPresentation === "settling";
  const visibleFleetData = fleetPresentation === "rows" ? vehiclesQ.data : undefined;

  const fleet: FleetRow[] = useMemo(() => {
    const rows = ((visibleFleetData?.items ?? []) as AnyRecord[]);
    return rows.map((v) => ({
      id:          String(v.vehicleCode ?? v.id),
      vehicleId:   String(v.id),
      type:        String(v.type || [v.make, v.model].filter(Boolean).join(" ") || "Vehicle"),
      driver:      v.assignedDriver ? String(v.assignedDriver) : null,
      status:      (v.status as VStatus) ?? deriveStatus(v),
      location:    (v.lastKnownLocation as string) ?? null,
      maintenance: (v.maintenance as MStatus) ?? deriveMaint(v),
      signal:      (v.deviceStatus as Signal) ?? deriveSignal(v),
      riskScore:   Number(v.riskScore ?? v.riskHeatScore ?? 0),
      readiness:   Number(v.readiness ?? v.readinessScore ?? v.fleetReadinessScore ?? 0),
      flag:        v.flag ? String(v.flag) : deriveFlag(v),
    }));
  }, [visibleFleetData]);

  const summary = (vehiclesQ.data?.summary ?? {}) as AnyRecord;
  const counts = {
    Active: Number(summary.active ?? 0),
    Idle: Number(summary.idle ?? 0),
    Available: Number(summary.available ?? 0),
    Offline: Number(summary.offline ?? 0),
    OOS: Number(summary.oos ?? 0),
    Unknown: Number(summary.unknown ?? 0),
  };
  const filtered = fleet;
  const totalFleet = Number(summary.total ?? 0);
  const selectedTotal = Number(vehiclesQ.data?.total ?? 0);
  const pageCount = Number(vehiclesQ.data?.pageCount ?? 0);
  const displayPage = Number(vehiclesQ.data?.page ?? page);
  const flagged = Number(summary.flagged ?? 0);

  // Readiness instrumentation — only from vehicles that actually report a score.
  const readiness = useMemo(() => {
    const scoredCount = Number(summary.readinessScoredCount ?? 0);
    const average = Number(summary.readinessAverage);
    const lowestRaw = vehiclesQ.data?.lowestReadiness as AnyRecord | undefined;
    if (!scoredCount || !Number.isFinite(average) || !lowestRaw) return null;
    const lowest: FleetRow = {
      id: String(lowestRaw.vehicleCode ?? lowestRaw.id), vehicleId: String(lowestRaw.id), type: "Vehicle",
      driver: null, status: "Unknown", location: null, maintenance: "Unknown", signal: "Unknown",
      riskScore: 0, readiness: Number(lowestRaw.readiness ?? 0), flag: null,
    };
    return { avg: average, lowest, scoredCount };
  }, [summary.readinessAverage, summary.readinessScoredCount, vehiclesQ.data?.lowestReadiness]);

  const deviceCounts = {
    Online: Number(summary.deviceOnline ?? 0),
    Degraded: Number(summary.deviceDegraded ?? 0),
    Offline: Number(summary.deviceOffline ?? 0),
    Unknown: Number(summary.deviceUnknown ?? 0),
  };

  const alerts = useMemo(() => {
    if (!canViewAlerts) return [];
    const rows = (alertsQ.data ?? []) as AnyRecord[];
    return rows
      .map((r, i) => ({
        id:        String(r.id ?? i),
        title:     String(r.title ?? r.type ?? "Alert"),
        severity:  String(r.severity ?? "Info"),
        status:    String(r.status ?? "Open"),
        createdAt: r.createdAt != null ? String(r.createdAt) : undefined,
      }))
      .filter((a) => !/closed|resolved/i.test(a.status))
      .sort((a, b) =>
        (SEVERITY_RANK[a.severity.toLowerCase()] ?? 9) - (SEVERITY_RANK[b.severity.toLowerCase()] ?? 9)
        || (Date.parse(b.createdAt ?? "") || 0) - (Date.parse(a.createdAt ?? "") || 0))
      .slice(0, 8);
  }, [alertsQ.data, canViewAlerts]);

  const selectTab = (next: Tab) => { setTab(next); setPage(1); };
  const toggleTab = (next: Tab) => selectTab(tab === next ? "All" : next);

  if (!canViewVehicles) {
    return (
      <div className="ops-deck flex flex-col gap-3">
        <div className="deck-neumo mx-auto my-10 max-w-md p-6 text-center">
          <p className="text-sm font-bold text-slate-700">Fleet data is not available for this role</p>
          <p className="mt-1 text-xs text-slate-500">The current session does not include vehicle read access.</p>
        </div>
      </div>
    );
  }

  if (vehiclesQ.isLoading) return <LoadingState />;

  if (vehiclesQ.isError) {
    return (
      <div className="ops-deck flex flex-col gap-3">
        <div className="deck-neumo mx-auto my-10 max-w-md p-6 text-center">
          <AlertTriangle className="mx-auto h-8 w-8 text-red-500" />
          <p className="mt-3 text-sm font-bold text-slate-700">Unable to load fleet data</p>
          <p className="mt-1 text-xs text-slate-500">The vehicles service did not respond. Retry in a moment.</p>
          <button type="button" className="btn-ghost mt-4" onClick={() => void vehiclesQ.refetch()}>Retry fleet registry</button>
        </div>
      </div>
    );
  }

  return (
    <div className="ops-deck flex flex-col gap-3">

      <PageHeader
        eyebrow="Fleet Operations"
        title="Fleet Overview"
        description={`${totalFleet} vehicles evaluated · ${counts.Active} operationally active · ${flagged} dispatch flags`}
        actions={(
          <button type="button" className="btn-primary btn-compact min-h-11 sm:min-h-9" onClick={() => navigate("/vehicles")}>
            Full Fleet Registry
            <ChevronRight className="h-4 w-4" />
          </button>
        )}
      />

      {/* Command-state summary also acts as the single status filter surface. */}
      <div className="flex shrink-0 gap-2 overflow-x-auto pb-1" aria-label="Filter fleet by command state">
        <ClayKpi label={COMMAND_STATE_LABELS.Active}    count={counts.Active}    total={totalFleet} Icon={Truck}       icon="text-emerald-700" dot="bg-emerald-500 animate-pulse" active={tab === "Active"}    onClick={() => toggleTab("Active")} />
        <ClayKpi label={COMMAND_STATE_LABELS.Idle}      count={counts.Idle}      total={totalFleet} Icon={Zap}         icon="text-amber-700"   dot="bg-amber-400"             active={tab === "Idle"}      onClick={() => toggleTab("Idle")} />
        <ClayKpi label={COMMAND_STATE_LABELS.Available} count={counts.Available} total={totalFleet} Icon={Clock}       icon="text-sky-700"     dot="bg-sky-400"               active={tab === "Available"} onClick={() => toggleTab("Available")} />
        <ClayKpi label={COMMAND_STATE_LABELS.OOS}       count={counts.OOS}       total={totalFleet} Icon={ShieldAlert} icon="text-red-700"     dot="bg-red-500 animate-pulse" active={tab === "OOS"}       onClick={() => toggleTab("OOS")} />
        <ClayKpi label={COMMAND_STATE_LABELS.Offline}   count={counts.Offline}   total={totalFleet} Icon={WifiOff}     icon="text-slate-600"   dot="bg-slate-400"             active={tab === "Offline"}   onClick={() => toggleTab("Offline")} />
        <ClayKpi label={COMMAND_STATE_LABELS.Unknown}   count={counts.Unknown}   total={totalFleet} Icon={Radio}       icon="text-slate-600"   dot="bg-slate-400"             active={tab === "Unknown"}   onClick={() => toggleTab("Unknown")} />
      </div>

      <div role="note" className="shrink-0 px-1 text-[11px] font-medium text-slate-500">
        <p>Operational status does not prove device connectivity.</p>
        <details className="mt-0.5">
          <summary className="inline-flex min-h-11 cursor-pointer items-center font-bold text-teal-700 sm:min-h-8">How statuses are calculated</summary>
          <p className="max-w-5xl pb-1 leading-relaxed">
            Command-state buckets are mutually exclusive. Dispatch restricted covers maintenance or out-of-service units; when authorized telemetry is visible, offline or unknown telemetry takes precedence. Operational states do not prove connectivity, and Fleet Registry availability is a separate master-data summary.
          </p>
        </details>
      </div>

      {/* The fleet registry owns the full content width and sizes to its rows. */}
      <div className="min-w-0">

        {/* Roster console — neumorphic chassis with an inset bezel screen */}
        <section className="deck-neumo flex min-w-0 flex-col overflow-hidden">
          <div className="flex shrink-0 flex-wrap items-center gap-x-3 gap-y-2 px-3 py-2.5">
            <div className="flex min-h-11 items-center gap-2 rounded-lg border border-slate-200 bg-white px-3 text-xs font-semibold text-slate-600 sm:min-h-9" aria-live="polite">
              <span>Showing {STATUS_TAB_LABELS[tab]}</span>
              {tab !== "All" && (
                <button type="button" className="inline-flex min-h-11 items-center font-bold text-teal-700 hover:underline sm:min-h-8" onClick={() => selectTab("All")}>Clear filter</button>
              )}
            </div>
            <span className="ml-auto inline-flex items-center gap-1.5 text-[11px] font-semibold text-slate-500">
              <Activity className="h-3 w-3 text-teal-600" />
              Registry · refreshes every 30 s
            </span>
            <label className="relative min-w-[210px] flex-1 sm:max-w-[280px]">
              <span className="sr-only">Search fleet</span>
              <Search className="pointer-events-none absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-slate-400" />
              <input
                type="search"
                value={searchInput}
                onChange={(event) => setSearchInput(event.target.value)}
                placeholder="Search vehicle or driver"
                aria-controls="fleet-roster"
                aria-describedby={isFleetSettling ? "fleet-query-status" : undefined}
                className="min-h-11 w-full rounded-lg border border-slate-200 bg-white pl-9 pr-3 text-xs text-slate-700 outline-none focus:border-teal-500 focus:ring-2 focus:ring-teal-100 sm:min-h-9"
              />
            </label>
            <label className="inline-flex items-center gap-2 text-[11px] font-semibold text-slate-500">
              Sort
              <select
                value={sort}
                onChange={(event) => { setSort(event.target.value); setPage(1); }}
                className="min-h-11 rounded-lg border border-slate-200 bg-white px-2 text-xs font-semibold text-slate-700 sm:min-h-9"
                aria-label="Sort fleet"
              >
                <option value="vehicle">Vehicle</option>
                <option value="driver">Driver</option>
                <option value="status">Status</option>
                <option value="readiness">Readiness</option>
                <option value="device">Device</option>
              </select>
            </label>
            <button
              type="button"
              className="btn-ghost min-h-11 px-3 text-xs sm:min-h-9"
              onClick={() => { setSortOrder((current) => current === "asc" ? "desc" : "asc"); setPage(1); }}
              aria-label={`Sort ${sortOrder === "asc" ? "descending" : "ascending"}`}
            >
              {sortOrder === "asc" ? "Ascending" : "Descending"}
            </button>
          </div>

          <div className="deck-bezel mx-2.5 flex min-w-0 flex-col">
            <div
              id="fleet-roster"
              className="deck-screen max-h-[min(52dvh,560px)] overflow-auto"
              aria-busy={isFleetSettling || vehiclesQ.isFetching}
            >
              <table className="w-full min-w-[1040px] text-sm">
                <thead className="sticky top-0 z-10 bg-[#fcfdff]">
                  <tr className="border-b border-slate-200/80">
                    <th className="px-5 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-400">Vehicle</th>
                    <th className="px-3 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-400">Driver</th>
                    <th className="px-3 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-400">Command state</th>
                    <th className="px-3 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-400">Location</th>
                    <th className="px-3 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-400">Readiness</th>
                    <th className="px-3 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-400">Maint.</th>
                    <th className="px-3 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-400">Device</th>
                    <th className="px-3 py-3"><span className="sr-only">Actions</span></th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100/90">
                  {filtered.map((v) => {
                    const sc = STATUS_CFG[v.status];
                    const mc = MAINT_CFG[v.maintenance];
                    return (
                      <tr key={v.id} className={`group transition-colors hover:bg-sky-50/50 ${v.flag ? "bg-red-50/40" : ""}`}>
                        <td className="px-5 py-3">
                          <div className="flex items-center gap-2.5">
                            <span className={`mt-0.5 h-2 w-2 shrink-0 rounded-full ${sc.dot}`} />
                            <div>
                              <p className="font-semibold text-slate-900">{v.id}</p>
                              <p className="text-[11px] text-slate-400">{v.type}</p>
                            </div>
                          </div>
                        </td>
                        <td className="px-3 py-3">
                          {v.driver ? <p className="text-slate-700">{v.driver}</p> : <p className="text-slate-400 italic">Unassigned</p>}
                        </td>
                        <td className="px-3 py-3"><span className={sc.badge}>{sc.label}</span></td>
                        <td className="px-3 py-3">
                          {v.location ? (
                            <div className="flex items-center gap-1.5">
                              <MapPin className="h-3.5 w-3.5 shrink-0 text-slate-300" />
                              <span className="text-slate-600">{v.location}</span>
                            </div>
                          ) : (
                            <span className="text-slate-400 italic text-xs">No live GPS</span>
                          )}
                          {v.flag && (
                            <div className="mt-0.5 flex items-center gap-1">
                              <AlertTriangle className="h-3 w-3 shrink-0 text-amber-500" />
                              <span className="text-[11px] text-amber-700">{v.flag}</span>
                            </div>
                          )}
                        </td>
                        <td className="px-3 py-3">
                          <span className={`font-medium tabular-nums ${v.readiness >= 80 ? "text-emerald-600" : v.readiness >= 60 ? "text-amber-600" : "text-red-600"}`}>
                            {v.readiness > 0 ? `${Math.round(v.readiness)}%` : "—"}
                          </span>
                        </td>
                        <td className="px-3 py-3">
                          <div className="flex items-center gap-1">
                            <Wrench className="h-3.5 w-3.5 shrink-0 text-slate-300" />
                            <span className={mc.cls}>{v.maintenance}</span>
                          </div>
                        </td>
                        <td className="px-3 py-3">
                          <div className="flex items-center gap-1.5">
                            {SIGNAL_ICON[v.signal]}
                            <span className="text-[11px] text-slate-500">{v.signal}</span>
                          </div>
                        </td>
                        <td className="px-3 py-3">
                          <button type="button" className="btn-ghost btn-compact min-h-11 gap-1 px-3 text-xs sm:min-h-8" onClick={() => navigate(`/vehicles/${v.vehicleId}/live`)}>
                            Live detail
                            <ChevronRight className="h-3 w-3" />
                          </button>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>

              {isFleetSettling && (
                <div id="fleet-query-status" role="status" aria-live="polite" className="py-12 text-center">
                  <Activity className="mx-auto h-8 w-8 animate-pulse text-teal-500" />
                  <p className="mt-2 text-sm font-semibold text-slate-600">Updating fleet view…</p>
                  <p className="mt-1 text-xs text-slate-400">Applying search, filter, and sort changes.</p>
                </div>
              )}

              {!isFleetSettling && filtered.length === 0 && (
                <div className="py-12 text-center">
                  <Radio className="mx-auto h-8 w-8 text-slate-300" />
                  <p className="mt-2 text-sm font-semibold text-slate-500">
                    {totalFleet === 0 && !search ? "No vehicles in your fleet yet" : "No vehicles match this search or filter"}
                  </p>
                </div>
              )}
            </div>
          </div>

          {/* Instrument strip */}
          <div className="flex shrink-0 flex-wrap items-center gap-x-4 gap-y-2 px-4 py-2.5 text-[11.5px] font-semibold text-slate-500">
            <span className="tabular-nums">
              {isFleetSettling
                ? "Updating fleet view…"
                : `${selectedTotal > 0 ? `${(displayPage - 1) * pageSize + 1}–${Math.min(displayPage * pageSize, selectedTotal)}` : "0"} of ${selectedTotal} vehicles`}
            </span>
            <span className="inline-flex items-center gap-2">
              <span className="deck-led deck-led-emerald" />
              <span className="tabular-nums">{deviceCounts.Online} devices online</span>
            </span>
            <span className="inline-flex items-center gap-2">
              <span className={`deck-led ${flagged > 0 ? "deck-led-amber" : "deck-led-slate"}`} />
              <span className="tabular-nums">{flagged} flagged</span>
            </span>
            {canViewDevices && (
              <button type="button" className="ml-auto inline-flex items-center gap-1 font-bold text-teal-700 hover:underline" onClick={() => navigate("/iot-devices")}>
                Device Health
                <ChevronRight className="h-3 w-3" />
              </button>
            )}
            <nav className="ml-auto inline-flex items-center gap-2" aria-label="Fleet pages">
              <button
                type="button"
                className="btn-ghost btn-compact min-h-11 px-3 text-xs sm:min-h-8"
                disabled={displayPage <= 1 || vehiclesQ.isFetching || isFleetSettling}
                onClick={() => setPage(Math.max(1, displayPage - 1))}
                aria-label="Previous fleet page"
              >
                Previous
              </button>
              <span className="min-w-[76px] text-center tabular-nums" aria-live="polite">
                {isFleetSettling ? "Updating…" : `Page ${pageCount === 0 ? 0 : displayPage} of ${pageCount}`}
              </span>
              <button
                type="button"
                className="btn-ghost btn-compact min-h-11 px-3 text-xs sm:min-h-8"
                disabled={pageCount === 0 || displayPage >= pageCount || vehiclesQ.isFetching || isFleetSettling}
                onClick={() => setPage(Math.min(pageCount, displayPage + 1))}
                aria-label="Next fleet page"
              >
                Next
              </button>
            </nav>
          </div>
        </section>

      </div>

      <div className="grid items-start gap-3 md:grid-cols-2 xl:grid-cols-3">
        <ReadinessGauge readiness={readiness} flagged={flagged} />
        {canViewDevices && <SignalBay counts={deviceCounts} total={totalFleet} onOpen={() => navigate("/iot-devices")} />}
        <JobsPulse authorized={canViewJobs} query={jobsQ} onOpen={() => navigate("/jobs")} />
      </div>

      <AlertsFeed authorized={canViewAlerts} query={alertsQ} alerts={alerts} onOpen={() => navigate("/alerts")} />
    </div>
  );
}

/* ── Compact command-state filter ──────────────────────────────────────── */
function ClayKpi({
  label, count, total, Icon, icon, dot, active, onClick,
}: {
  label: string;
  count: number;
  total: number;
  Icon: React.ElementType;
  icon: string;
  dot: string;
  active: boolean;
  onClick: () => void;
}) {
  const pct = total > 0 ? Math.round((count / total) * 100) : 0;
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      aria-label={`${label}: ${count} of ${total}. ${active ? "Selected; activate to clear filter" : "Activate to filter"}`}
      className={`grid min-h-12 w-[170px] min-w-0 shrink-0 grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-2 rounded-xl border bg-white px-3 py-2 text-left shadow-sm transition hover:border-teal-300 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-teal-500/60 xl:w-auto xl:min-w-[150px] xl:flex-1 ${active ? "border-teal-400 ring-1 ring-teal-300" : "border-slate-200"}`}
    >
      <Icon className={`h-4 w-4 shrink-0 ${icon}`} aria-hidden />
      <p className="flex min-w-0 items-start gap-1.5 text-[10.5px] font-bold leading-tight text-slate-600">
        <span className={`mt-1 h-1.5 w-1.5 shrink-0 rounded-full ${dot}`} />
        <span className="line-clamp-2">{label}</span>
        <span className="shrink-0 text-[9.5px] font-medium text-slate-400">{total > 0 ? `${pct}%` : "—"}</span>
      </p>
      <p className="text-lg font-black leading-none tabular-nums text-slate-900">{count}</p>
    </button>
  );
}

function ReadinessGauge({
  readiness, flagged,
}: {
  readiness: { avg: number; lowest: FleetRow; scoredCount: number } | null;
  flagged: number;
}) {
  const value = readiness ? Math.round(readiness.avg) : 0;
  return (
    <div className="deck-neumo shrink-0 p-3">
      <div className="flex items-center justify-between">
        <span className="section-title inline-flex items-center gap-2">
          <GaugeIcon className="h-3.5 w-3.5 text-teal-700" />
          Fleet Readiness
        </span>
        <span className="text-[10px] font-bold uppercase tracking-wider text-slate-400 tabular-nums">
          {readiness ? `${readiness.scoredCount} metered` : "no telemetry"}
        </span>
      </div>

      <div className="mt-2 flex items-end justify-between gap-3">
        <p className="text-2xl font-black leading-none tabular-nums text-slate-900" aria-label={readiness ? `Average fleet readiness ${value} percent` : "Fleet readiness unavailable"}>
          {readiness ? `${value}%` : "—"}
        </p>
        <p className="text-right text-[10.5px] font-medium text-slate-500">
          {readiness ? "Average of metered units" : "Readiness evidence unavailable"}
        </p>
      </div>
      <div className="deck-track mt-2" aria-hidden>
        <div className="deck-fill deck-fill-teal" style={{ width: readiness ? `${Math.min(100, Math.max(0, value))}%` : 0 }} />
      </div>

      <div className="mt-2 grid grid-cols-2 gap-2">
        <div className="deck-inset rounded-xl px-3 py-2">
          <p className="text-[9.5px] font-bold uppercase tracking-wider text-slate-400">Lowest unit</p>
          <p className="mt-0.5 truncate text-[12px] font-bold text-slate-800 tabular-nums">
            {readiness ? `${readiness.lowest.id} · ${Math.round(readiness.lowest.readiness)}%` : "No readiness data"}
          </p>
        </div>
        <div className="deck-inset rounded-xl px-3 py-2">
          <p className="text-[9.5px] font-bold uppercase tracking-wider text-slate-400">Flagged units</p>
          <p className={`mt-0.5 text-[12px] font-bold tabular-nums ${flagged > 0 ? "text-amber-700" : "text-slate-800"}`}>
            {flagged} of fleet
          </p>
        </div>
      </div>
    </div>
  );
}

/* ── Device signal bay — LED board ─────────────────────────────────────── */
function SignalBay({
  counts, total, onOpen,
}: {
  counts: { Online: number; Degraded: number; Offline: number; Unknown: number };
  total: number;
  onOpen: () => void;
}) {
  const rows: Array<{ label: string; value: number; led: string; fill: string }> = [
    { label: "Online",   value: counts.Online,   led: "deck-led-emerald", fill: "deck-fill-emerald" },
    { label: "Degraded", value: counts.Degraded, led: "deck-led-amber",   fill: "deck-fill-amber" },
    { label: "Offline",  value: counts.Offline,  led: "deck-led-red",     fill: "deck-fill-red" },
    { label: "Unknown",  value: counts.Unknown,  led: "deck-led-slate",   fill: "deck-fill-slate" },
  ];
  return (
    <div className="deck-neumo shrink-0 p-3">
      <div className="flex items-center justify-between">
        <span className="section-title inline-flex items-center gap-2">
          <Radio className="h-3.5 w-3.5 text-teal-700" />
          Signal Bay
        </span>
        <button type="button" onClick={onOpen} className="text-[10.5px] font-bold text-teal-700 hover:underline">
          Devices →
        </button>
      </div>
      <div className="mt-2 space-y-2">
        {rows.map((r) => (
          <div key={r.label} className="flex items-center gap-2.5">
            <span className={`deck-led ${r.value > 0 ? r.led : "deck-led-slate"}`} />
            <span className="w-16 text-[11.5px] font-bold text-slate-600">{r.label}</span>
            <div className="deck-track flex-1">
              <div className={`deck-fill ${r.fill}`} style={{ width: total > 0 ? `${(r.value / total) * 100}%` : 0 }} />
            </div>
            <span className="w-6 text-right text-[12px] font-black tabular-nums text-slate-800">{r.value}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

/* ── Jobs pulse — today's pipeline from the live jobs summary ─────────── */
const PULSE_ROWS: Array<{ key: string; label: string; fill: string }> = [
  { key: "unassignedJobs", label: "Unassigned",  fill: "deck-fill-slate" },
  { key: "assignedJobs",   label: "Assigned",    fill: "deck-fill-teal" },
  { key: "enRoute",        label: "En route",    fill: "deck-fill-sky" },
  { key: "slaAtRisk",      label: "SLA at risk", fill: "deck-fill-red" },
  { key: "completed",      label: "Completed",   fill: "deck-fill-emerald" },
];

function JobsPulse({ authorized, query, onOpen }: { authorized: boolean; query: { data?: AnyRecord; isLoading: boolean; isError: boolean }; onOpen: () => void }) {
  if (!authorized) return <RoleUnavailablePanel icon={<Package className="h-3.5 w-3.5 text-slate-500" />} title="Jobs Pulse" />;
  const summary = (query.data ?? {}) as AnyRecord;
  const total = Number(summary.totalJobsToday ?? 0);
  return (
    <div className="deck-neumo shrink-0 p-3">
      <div className="flex items-center justify-between">
        <span className="section-title inline-flex items-center gap-2">
          <Package className="h-3.5 w-3.5 text-teal-700" />
          Jobs Pulse
        </span>
        <button type="button" onClick={onOpen} className="text-[10.5px] font-bold text-teal-700 hover:underline">
          {query.isError ? "Board →" : `${total} today →`}
        </button>
      </div>

      {query.isLoading && (
        <div className="mt-2 space-y-2">
          {[...Array(3)].map((_, i) => <div key={i} className="skeleton h-3.5 w-full" />)}
        </div>
      )}

      {query.isError && (
        <p className="mt-2 text-[11.5px] font-medium italic text-slate-400">Jobs service unreachable — pipeline hidden.</p>
      )}

      {!query.isLoading && !query.isError && (
        <div className="mt-2 space-y-2">
          {PULSE_ROWS.map((row) => {
            const value = Number(summary[row.key] ?? 0);
            return (
              <div key={row.key} className="flex items-center gap-2.5">
                <span className="w-[74px] text-[11px] font-bold text-slate-600">{row.label}</span>
                <div className="deck-track flex-1">
                  <div className={`deck-fill ${row.fill}`} style={{ width: total > 0 ? `${Math.min(100, (value / total) * 100)}%` : 0 }} />
                </div>
                <span className="w-6 text-right text-[12px] font-black tabular-nums text-slate-800">{value}</span>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}

/* ── Live alerts feed — raised chips on an inset tray ─────────────────── */
function AlertsFeed({
  authorized, query, alerts, onOpen,
}: {
  authorized: boolean;
  query: { isLoading: boolean; isError: boolean };
  alerts: Array<{ id: string; title: string; severity: string; status: string; createdAt?: string }>;
  onOpen: () => void;
}) {
  if (!authorized) return <RoleUnavailablePanel icon={<BellRing className="h-3.5 w-3.5 text-slate-500" />} title="Open Alerts" />;
  return (
    <section className="deck-neumo p-3">
      <div className="flex shrink-0 items-center justify-between">
        <span className="section-title inline-flex items-center gap-2">
          <BellRing className="h-3.5 w-3.5 text-teal-700" />
          Open Alerts
        </span>
        <button type="button" onClick={onOpen} className="text-[10.5px] font-bold text-teal-700 hover:underline">
          {alerts.length > 0 ? `${alerts.length} open →` : "Center →"}
        </button>
      </div>

      <div className="deck-inset mt-2 grid gap-2 rounded-xl p-2 md:grid-cols-2 xl:grid-cols-4">
        {query.isLoading && [...Array(3)].map((_, i) => <div key={i} className="skeleton h-11 w-full rounded-xl" />)}

        {query.isError && (
          <p className="px-2 py-3 text-[11.5px] font-medium italic text-slate-400 md:col-span-2 xl:col-span-4">Alerts service unreachable.</p>
        )}

        {!query.isLoading && !query.isError && alerts.length === 0 && (
          <div className="flex items-center gap-2.5 px-2 py-3 md:col-span-2 xl:col-span-4">
            <span className="deck-led deck-led-emerald" />
            <p className="text-[12px] font-semibold text-slate-500">No open alert records in the current result.</p>
          </div>
        )}

        {alerts.map((a) => {
          const sev = a.severity.toLowerCase();
          const age = timeAgo(a.createdAt);
          return (
            <button key={a.id} type="button" onClick={onOpen} className="deck-alert flex min-h-11 w-full items-center gap-2.5 px-3 py-2.5 text-left">
              <span className={`deck-led shrink-0 ${SEVERITY_LED[sev] ?? "deck-led-sky"}`} />
              <span className="min-w-0 flex-1">
                <span className="block truncate text-[12.5px] font-bold text-slate-800">{a.title}</span>
                <span className="mt-0.5 block text-[10.5px] font-semibold text-slate-400">
                  <span className={SEVERITY_TEXT[sev] ?? "text-sky-700"}>{a.severity}</span>
                  {age ? ` · ${age} ago` : ""} · {a.status}
                </span>
              </span>
              <ChevronRight className="h-3.5 w-3.5 shrink-0 text-slate-300" />
            </button>
          );
        })}
      </div>
    </section>
  );
}

function RoleUnavailablePanel({ icon, title }: { icon: React.ReactNode; title: string }) {
  return (
    <div className="deck-neumo shrink-0 p-3">
      <span className="section-title inline-flex items-center gap-2">{icon}{title}</span>
      <p className="mt-2 text-[11.5px] font-medium text-slate-500">Not available for this role.</p>
    </div>
  );
}

import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  Activity, AlertTriangle, BellRing, ChevronRight, Clock, Gauge as GaugeIcon,
  MapPin, Package, Radio, ShieldAlert, Truck, Wifi, WifiOff, Wrench, Zap,
} from "lucide-react";
import { useNavigate } from "react-router-dom";
import { LoadingState } from "@/components/ui";
import { vehiclesApi } from "@/services/vehiclesApi";
import { driversApi } from "@/services/driversApi";
import { alertsApi } from "@/services/alertsApi";
import { jobsApi } from "@/services/jobsApi";
import type { AnyRecord } from "@/types";

// Vehicle movement status is derived from real vehicle + telemetry fields. We do NOT
// fabricate GPS speed/location — where live telemetry is absent we show honest blanks.
type VStatus = "Active" | "Idle" | "Available" | "Offline" | "OOS";
type MStatus = "Healthy" | "Due Soon" | "Overdue" | "Critical";
type Signal  = "Online" | "Degraded" | "Offline";

interface FleetRow {
  id:          string;
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

const STATUS_TABS = ["All", "Active", "Idle", "Available", "OOS", "Offline"] as const;
type Tab = typeof STATUS_TABS[number];

const STATUS_CFG: Record<VStatus, { badge: string; dot: string; label: string }> = {
  Active:    { badge: "badge badge-success",                                               dot: "bg-emerald-500 animate-pulse", label: "Active" },
  Idle:      { badge: "badge badge-warning",                                               dot: "bg-amber-400",                 label: "Idle" },
  Available: { badge: "inline-flex items-center rounded-md px-2 py-0.5 text-[10px] font-semibold bg-slate-100 text-slate-600 border border-slate-200", dot: "bg-slate-400", label: "Available" },
  Offline:   { badge: "badge badge-danger",                                                dot: "bg-red-500",                   label: "Offline" },
  OOS:       { badge: "inline-flex items-center rounded-md px-2 py-0.5 text-[10px] font-semibold bg-red-100 text-red-700 border border-red-200",      dot: "bg-red-500 animate-pulse", label: "OOS" },
};

const MAINT_CFG: Record<MStatus, { cls: string }> = {
  Healthy:   { cls: "text-emerald-600 text-xs font-medium" },
  "Due Soon":{ cls: "text-amber-600 text-xs font-medium" },
  Overdue:   { cls: "text-red-600 text-xs font-semibold" },
  Critical:  { cls: "text-red-700 text-xs font-bold" },
};

const SIGNAL_ICON: Record<Signal, React.ReactNode> = {
  Online:   <Wifi className="h-3.5 w-3.5 text-emerald-500" />,
  Degraded: <Wifi className="h-3.5 w-3.5 text-amber-500" />,
  Offline:  <WifiOff className="h-3.5 w-3.5 text-red-400" />,
};

function deriveStatus(v: AnyRecord): VStatus {
  if (v.outOfService === true || /out.?of.?service|oos/i.test(String(v.status ?? ""))) return "OOS";
  const device = String(v.deviceStatus ?? "").toLowerCase();
  if (device && device !== "online") return "Offline";
  const s = String(v.status ?? "").toLowerCase();
  if (/maintenance|repair/.test(s)) return "OOS";
  if (/active|on route|driving|in.?transit|dispatched/.test(s)) return "Active";
  if (/idle|idling/.test(s)) return "Idle";
  return "Available";
}

function deriveMaint(v: AnyRecord): MStatus {
  const s = String(v.status ?? "").toLowerCase();
  if (/critical/.test(s)) return "Critical";
  if (/maintenance|repair|overdue/.test(s)) return "Overdue";
  const readiness = Number(v.readinessScore ?? v.fleetReadinessScore ?? 100);
  if (readiness < 60) return "Overdue";
  if (readiness < 80) return "Due Soon";
  return "Healthy";
}

function deriveSignal(v: AnyRecord): Signal {
  const device = String(v.deviceStatus ?? "").toLowerCase();
  if (!device) return "Offline";
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
  const [tab, setTab] = useState<Tab>("All");

  const vehiclesQ = useQuery({ queryKey: ["fleet-overview-vehicles"], queryFn: () => vehiclesApi.list(), refetchInterval: 30_000 });
  const driversQ  = useQuery({ queryKey: ["fleet-overview-drivers"],  queryFn: () => driversApi.list() });
  const alertsQ   = useQuery({ queryKey: ["fleet-overview-alerts"],   queryFn: () => alertsApi.list(),  refetchInterval: 60_000 });
  const jobsQ     = useQuery({ queryKey: ["fleet-overview-jobs"],     queryFn: () => jobsApi.summary(), refetchInterval: 60_000 });

  const driverById = useMemo(() => {
    const map = new Map<string, string>();
    for (const d of (driversQ.data ?? []) as AnyRecord[]) {
      map.set(String(d.id), String(d.fullName ?? d.driverCode ?? ""));
    }
    return map;
  }, [driversQ.data]);

  const fleet: FleetRow[] = useMemo(() => {
    const rows = (vehiclesQ.data ?? []) as AnyRecord[];
    return rows.map((v) => ({
      id:          String(v.vehicleCode ?? v.id),
      type:        String(v.type ?? [v.make, v.model].filter(Boolean).join(" ") ?? "Vehicle"),
      driver:      v.assignedDriverId != null ? (driverById.get(String(v.assignedDriverId)) || String(v.assignedDriver ?? "")) || null : null,
      status:      deriveStatus(v),
      location:    (v.lastKnownLocation as string) ?? null,
      maintenance: deriveMaint(v),
      signal:      deriveSignal(v),
      riskScore:   Number(v.riskScore ?? v.riskHeatScore ?? 0),
      readiness:   Number(v.readinessScore ?? v.fleetReadinessScore ?? 0),
      flag:        deriveFlag(v),
    }));
  }, [vehiclesQ.data, driverById]);

  const counts = useMemo(() => ({
    Active:    fleet.filter((v) => v.status === "Active").length,
    Idle:      fleet.filter((v) => v.status === "Idle").length,
    Available: fleet.filter((v) => v.status === "Available").length,
    Offline:   fleet.filter((v) => v.status === "Offline").length,
    OOS:       fleet.filter((v) => v.status === "OOS").length,
  }), [fleet]);

  const filtered = useMemo(
    () => tab === "All" ? fleet : fleet.filter((v) => v.status === tab),
    [tab, fleet],
  );

  const flagged = fleet.filter((v) => v.flag).length;

  // Readiness instrumentation — only from vehicles that actually report a score.
  const readiness = useMemo(() => {
    const scored = fleet.filter((v) => v.readiness > 0);
    if (scored.length === 0) return null;
    const avg = scored.reduce((s, v) => s + v.readiness, 0) / scored.length;
    const lowest = scored.reduce((min, v) => (v.readiness < min.readiness ? v : min), scored[0]);
    return { avg, lowest, scoredCount: scored.length };
  }, [fleet]);

  const deviceCounts = useMemo(() => ({
    Online:   fleet.filter((v) => v.signal === "Online").length,
    Degraded: fleet.filter((v) => v.signal === "Degraded").length,
    Offline:  fleet.filter((v) => v.signal === "Offline").length,
  }), [fleet]);

  const alerts = useMemo(() => {
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
  }, [alertsQ.data]);

  const toggleTab = (next: Tab) => setTab((cur) => (cur === next ? "All" : next));

  if (vehiclesQ.isLoading) return <LoadingState />;

  if (vehiclesQ.isError) {
    return (
      <div className="ops-deck flex h-full flex-col gap-3">
        <div className="deck-neumo m-auto max-w-md p-10 text-center">
          <AlertTriangle className="mx-auto h-8 w-8 text-red-500" />
          <p className="mt-3 text-sm font-bold text-slate-700">Unable to load fleet data</p>
          <p className="mt-1 text-xs text-slate-500">The vehicles service did not respond. Retry in a moment.</p>
        </div>
      </div>
    );
  }

  return (
    <div className="ops-deck flex h-full min-h-0 flex-col gap-3">

      {/* ── Console rail: title, live meta, clock, primary action ─────────── */}
      <header className="deck-rail relative shrink-0 px-5 py-3.5 pl-7 pr-7">
        <Screw className="left-2.5 top-2.5"   slot="18deg" />
        <Screw className="right-2.5 top-2.5"  slot="-42deg" />
        <Screw className="bottom-2.5 left-2.5" slot="66deg" />
        <Screw className="bottom-2.5 right-2.5" slot="-12deg" />
        <div className="flex flex-wrap items-center gap-x-6 gap-y-3">
          <div className="min-w-0">
            <span className="section-title inline-flex items-center gap-2">
              <span className="live-dot h-1.5 w-1.5" />
              Live Operations Deck
            </span>
            <h1 className="mt-1 text-[26px] font-black leading-none tracking-tight text-slate-950">Fleet Command</h1>
            <p className="mt-1.5 text-[12.5px] font-medium text-slate-500">
              {fleet.length} vehicles tracked · {counts.Active} active · {flagged} flagged for review
            </p>
          </div>
          <div className="ml-auto flex flex-wrap items-center gap-3">
            <DeckClock />
            <button type="button" className="btn-primary" onClick={() => navigate("/vehicles")}>
              Full Fleet Registry
              <ChevronRight className="h-4 w-4" />
            </button>
          </div>
        </div>
      </header>

      {/* ── Clay status tiles — puffy, pressable fleet filters ────────────── */}
      <div className="grid shrink-0 grid-cols-2 gap-3 sm:grid-cols-3 xl:grid-cols-5">
        <ClayKpi label="Active"         count={counts.Active}    total={fleet.length} Icon={Truck}       tone="deck-clay-emerald" fill="deck-fill-emerald" icon="text-emerald-700" dot="bg-emerald-500 animate-pulse" active={tab === "Active"}    onClick={() => toggleTab("Active")} />
        <ClayKpi label="Idle"           count={counts.Idle}      total={fleet.length} Icon={Zap}         tone="deck-clay-amber"   fill="deck-fill-amber"   icon="text-amber-700"   dot="bg-amber-400"                 active={tab === "Idle"}      onClick={() => toggleTab("Idle")} />
        <ClayKpi label="Available"      count={counts.Available} total={fleet.length} Icon={Clock}       tone="deck-clay-sky"     fill="deck-fill-sky"     icon="text-sky-700"     dot="bg-sky-400"                   active={tab === "Available"} onClick={() => toggleTab("Available")} />
        <ClayKpi label="Out of Service" count={counts.OOS}       total={fleet.length} Icon={ShieldAlert} tone="deck-clay-red"     fill="deck-fill-red"     icon="text-red-700"     dot="bg-red-500 animate-pulse"     active={tab === "OOS"}       onClick={() => toggleTab("OOS")} />
        <ClayKpi label="Offline"        count={counts.Offline}   total={fleet.length} Icon={WifiOff}     tone="deck-clay-slate"   fill="deck-fill-slate"   icon="text-slate-600"   dot="bg-slate-400"                 active={tab === "Offline"}   onClick={() => toggleTab("Offline")} />
      </div>

      {/* ── Main deck: roster console + instrument rail ───────────────────── */}
      <div className="grid min-h-0 flex-1 grid-cols-1 gap-3 xl:grid-cols-[minmax(0,1fr)_324px]">

        {/* Roster console — neumorphic chassis with an inset bezel screen */}
        <section className="deck-neumo flex min-h-[480px] flex-col overflow-hidden xl:min-h-0">
          <div className="flex shrink-0 flex-wrap items-center gap-x-3 gap-y-2 px-4 pb-3 pt-3.5">
            <div className="deck-seg flex flex-wrap items-center gap-1 p-1">
              {STATUS_TABS.map((t) => (
                <button
                  key={t}
                  type="button"
                  onClick={() => setTab(t)}
                  className={`deck-seg-btn ${tab === t ? "deck-seg-btn-active" : ""}`}
                >
                  {t}
                  {t !== "All" && (
                    <span className={`ml-1.5 rounded-full px-1.5 py-px text-[10px] font-bold tabular-nums ${
                      tab === t ? "bg-teal-100 text-teal-700" : "bg-slate-200/70 text-slate-500"
                    }`}>
                      {counts[t as Exclude<Tab, "All">] ?? 0}
                    </span>
                  )}
                </button>
              ))}
            </div>
            <span className="ml-auto inline-flex items-center gap-1.5 text-[11px] font-semibold text-slate-500">
              <Activity className="h-3 w-3 text-teal-600" />
              Live · refreshes every 30 s
            </span>
          </div>

          <div className="deck-bezel mx-3 flex min-h-0 flex-1 flex-col">
            <div className="deck-screen min-h-0 flex-1 overflow-auto">
              <table className="w-full text-sm">
                <thead className="sticky top-0 z-10 bg-[#fcfdff]">
                  <tr className="border-b border-slate-200/80">
                    <th className="px-5 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-400">Vehicle</th>
                    <th className="px-3 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-400">Driver</th>
                    <th className="px-3 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-400">Status</th>
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
                          <button type="button" className="btn-ghost invisible h-7 gap-1 px-3 text-xs group-hover:visible" onClick={() => navigate("/vehicles")}>
                            Detail
                            <ChevronRight className="h-3 w-3" />
                          </button>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>

              {filtered.length === 0 && (
                <div className="py-12 text-center">
                  <Radio className="mx-auto h-8 w-8 text-slate-300" />
                  <p className="mt-2 text-sm font-semibold text-slate-500">
                    {fleet.length === 0 ? "No vehicles in your fleet yet" : "No vehicles match this filter"}
                  </p>
                </div>
              )}
            </div>
          </div>

          {/* Instrument strip */}
          <div className="flex shrink-0 flex-wrap items-center gap-x-5 gap-y-2 px-5 py-3 text-[11.5px] font-semibold text-slate-500">
            <span className="tabular-nums">{filtered.length} of {fleet.length} vehicles shown</span>
            <span className="inline-flex items-center gap-2">
              <span className="deck-led deck-led-emerald" />
              <span className="tabular-nums">{deviceCounts.Online} devices online</span>
            </span>
            <span className="inline-flex items-center gap-2">
              <span className={`deck-led ${flagged > 0 ? "deck-led-amber" : "deck-led-slate"}`} />
              <span className="tabular-nums">{flagged} flagged</span>
            </span>
            <button type="button" className="ml-auto inline-flex items-center gap-1 font-bold text-teal-700 hover:underline" onClick={() => navigate("/iot-devices")}>
              Device Health
              <ChevronRight className="h-3 w-3" />
            </button>
          </div>
        </section>

        {/* Instrument rail — gauge, signal bay, jobs pulse, alert feed */}
        <aside className="flex min-h-0 flex-col gap-3 xl:overflow-y-auto">
          <ReadinessGauge readiness={readiness} flagged={flagged} />
          <SignalBay counts={deviceCounts} total={fleet.length} onOpen={() => navigate("/iot-devices")} />
          <JobsPulse query={jobsQ} onOpen={() => navigate("/jobs")} />
          <AlertsFeed query={alertsQ} alerts={alerts} onOpen={() => navigate("/alerts")} />
        </aside>
      </div>
    </div>
  );
}

/* ── Skeuomorphic corner screw ─────────────────────────────────────────── */
function Screw({ className, slot }: { className: string; slot: string }) {
  return <span aria-hidden className={`deck-screw absolute ${className}`} style={{ "--slot": slot } as React.CSSProperties} />;
}

/* ── LCD console clock ─────────────────────────────────────────────────── */
function DeckClock() {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const t = setInterval(() => setNow(new Date()), 1000);
    return () => clearInterval(t);
  }, []);
  const hh = String(now.getHours()).padStart(2, "0");
  const mm = String(now.getMinutes()).padStart(2, "0");
  const ss = String(now.getSeconds()).padStart(2, "0");
  return (
    <div className="deck-lcd px-3.5 py-2 text-right" role="timer" aria-label="Current time">
      <p className="font-mono text-[17px] font-bold leading-none tracking-[0.14em] tabular-nums">
        {hh}:{mm}<span className="opacity-60">:{ss}</span>
      </p>
      <p className="mt-1 text-[9px] font-semibold uppercase tracking-[0.22em] opacity-60">
        {now.toLocaleDateString(undefined, { weekday: "short", day: "2-digit", month: "short" })}
      </p>
    </div>
  );
}

/* ── Clay KPI tile — puffy pressable filter ────────────────────────────── */
function ClayKpi({
  label, count, total, Icon, tone, fill, icon, dot, active, onClick,
}: {
  label: string;
  count: number;
  total: number;
  Icon: React.ElementType;
  tone: string;
  fill: string;
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
      className={`deck-clay ${tone} ${active ? "deck-clay-pressed" : ""} flex flex-col gap-2.5 p-4 text-left`}
    >
      <div className="flex items-center gap-3">
        <span className="deck-blob">
          <Icon className={`h-4.5 w-4.5 ${icon}`} />
        </span>
        <div className="min-w-0">
          <p className="text-[22px] font-black leading-none tabular-nums text-slate-900">{count}</p>
          <p className="mt-1 flex items-center gap-1.5 text-[11px] font-bold text-slate-600">
            <span className={`h-1.5 w-1.5 shrink-0 rounded-full ${dot}`} />
            <span className="truncate">{label}</span>
          </p>
        </div>
      </div>
      <div className="flex items-center gap-2">
        <div className="deck-track flex-1">
          <div className={`deck-fill ${fill}`} style={{ width: `${pct}%` }} />
        </div>
        <span className="w-7 text-right text-[10px] font-bold tabular-nums text-slate-500">{total > 0 ? `${pct}%` : "—"}</span>
      </div>
    </button>
  );
}

/* ── Analog readiness gauge — real instrument, honest when unmetered ──── */
function polarPoint(cx: number, cy: number, r: number, fraction: number) {
  const theta = Math.PI * (1 - fraction);
  return { x: cx + r * Math.cos(theta), y: cy - r * Math.sin(theta) };
}

function arcPath(cx: number, cy: number, r: number, from: number, to: number) {
  const a = polarPoint(cx, cy, r, from);
  const b = polarPoint(cx, cy, r, to);
  return `M ${a.x.toFixed(2)} ${a.y.toFixed(2)} A ${r} ${r} 0 0 1 ${b.x.toFixed(2)} ${b.y.toFixed(2)}`;
}

function ReadinessGauge({
  readiness, flagged,
}: {
  readiness: { avg: number; lowest: FleetRow; scoredCount: number } | null;
  flagged: number;
}) {
  const value = readiness ? Math.round(readiness.avg) : 0;
  const needleDeg = (readiness ? Math.min(100, Math.max(0, readiness.avg)) : 0) * 1.8;
  const ticks = [0, 0.2, 0.4, 0.6, 0.8, 1];
  return (
    <div className="deck-neumo shrink-0 p-4">
      <div className="flex items-center justify-between">
        <span className="section-title inline-flex items-center gap-2">
          <GaugeIcon className="h-3.5 w-3.5 text-teal-700" />
          Fleet Readiness
        </span>
        <span className="text-[10px] font-bold uppercase tracking-wider text-slate-400 tabular-nums">
          {readiness ? `${readiness.scoredCount} metered` : "no telemetry"}
        </span>
      </div>

      <svg viewBox="0 0 200 118" className="mx-auto mt-2 block w-full max-w-[230px]" role="img"
        aria-label={readiness ? `Average fleet readiness ${value} percent` : "Fleet readiness unavailable"}>
        <defs>
          <linearGradient id="deckNeedle" x1="0" y1="0" x2="1" y2="0">
            <stop offset="0%" stopColor="#64748b" />
            <stop offset="100%" stopColor="#1e293b" />
          </linearGradient>
          <radialGradient id="deckCap" cx="35%" cy="30%" r="80%">
            <stop offset="0%" stopColor="#ffffff" />
            <stop offset="45%" stopColor="#cbd5e1" />
            <stop offset="100%" stopColor="#64748b" />
          </radialGradient>
        </defs>
        {/* recessed dial track */}
        <path d={arcPath(100, 100, 78, 0, 1)} fill="none" stroke="#d3dcea" strokeWidth="13" strokeLinecap="round" />
        {/* colored zones */}
        <path d={arcPath(100, 100, 78, 0, 0.6)}   fill="none" stroke="#f87171" strokeWidth="9" strokeLinecap="round" opacity=".85" />
        <path d={arcPath(100, 100, 78, 0.6, 0.8)} fill="none" stroke="#fbbf24" strokeWidth="9" opacity=".9" />
        <path d={arcPath(100, 100, 78, 0.8, 1)}   fill="none" stroke="#34d399" strokeWidth="9" strokeLinecap="round" opacity=".95" />
        {/* ticks */}
        {ticks.map((f) => {
          const o = polarPoint(100, 100, 66, f);
          const i = polarPoint(100, 100, 58, f);
          return <line key={f} x1={i.x} y1={i.y} x2={o.x} y2={o.y} stroke="#94a3b8" strokeWidth="1.6" strokeLinecap="round" />;
        })}
        {/* needle */}
        <g className="deck-needle" style={{ "--needle": `${needleDeg}deg` } as React.CSSProperties}>
          <line x1="100" y1="100" x2="48" y2="100" stroke="url(#deckNeedle)" strokeWidth="3.4" strokeLinecap="round" />
        </g>
        <circle cx="100" cy="100" r="7.5" fill="url(#deckCap)" stroke="#94a3b8" strokeWidth=".8" />
        <text x="100" y="88" textAnchor="middle" fill="#0f172a" fontSize="24" fontWeight="800" className="tabular-nums">
          {readiness ? `${value}%` : "—"}
        </text>
      </svg>

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
  counts: { Online: number; Degraded: number; Offline: number };
  total: number;
  onOpen: () => void;
}) {
  const rows: Array<{ label: string; value: number; led: string; fill: string }> = [
    { label: "Online",   value: counts.Online,   led: "deck-led-emerald", fill: "deck-fill-emerald" },
    { label: "Degraded", value: counts.Degraded, led: "deck-led-amber",   fill: "deck-fill-amber" },
    { label: "Offline",  value: counts.Offline,  led: "deck-led-red",     fill: "deck-fill-red" },
  ];
  return (
    <div className="deck-neumo shrink-0 p-4">
      <div className="flex items-center justify-between">
        <span className="section-title inline-flex items-center gap-2">
          <Radio className="h-3.5 w-3.5 text-teal-700" />
          Signal Bay
        </span>
        <button type="button" onClick={onOpen} className="text-[10.5px] font-bold text-teal-700 hover:underline">
          Devices →
        </button>
      </div>
      <div className="mt-3 space-y-2.5">
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

function JobsPulse({ query, onOpen }: { query: { data?: AnyRecord; isLoading: boolean; isError: boolean }; onOpen: () => void }) {
  const summary = (query.data ?? {}) as AnyRecord;
  const total = Number(summary.totalJobsToday ?? 0);
  return (
    <div className="deck-neumo shrink-0 p-4">
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
        <div className="mt-3 space-y-2.5">
          {[...Array(3)].map((_, i) => <div key={i} className="skeleton h-3.5 w-full" />)}
        </div>
      )}

      {query.isError && (
        <p className="mt-3 text-[11.5px] font-medium italic text-slate-400">Jobs service unreachable — pipeline hidden.</p>
      )}

      {!query.isLoading && !query.isError && (
        <div className="mt-3 space-y-2">
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
  query, alerts, onOpen,
}: {
  query: { isLoading: boolean; isError: boolean };
  alerts: Array<{ id: string; title: string; severity: string; status: string; createdAt?: string }>;
  onOpen: () => void;
}) {
  return (
    <div className="deck-neumo flex min-h-[240px] flex-1 flex-col overflow-hidden p-4 xl:min-h-[190px]">
      <div className="flex shrink-0 items-center justify-between">
        <span className="section-title inline-flex items-center gap-2">
          <BellRing className="h-3.5 w-3.5 text-teal-700" />
          Open Alerts
        </span>
        <button type="button" onClick={onOpen} className="text-[10.5px] font-bold text-teal-700 hover:underline">
          {alerts.length > 0 ? `${alerts.length} open →` : "Center →"}
        </button>
      </div>

      <div className="deck-inset mt-3 min-h-0 flex-1 space-y-2 overflow-y-auto rounded-2xl p-2.5">
        {query.isLoading && [...Array(3)].map((_, i) => <div key={i} className="skeleton h-11 w-full rounded-xl" />)}

        {query.isError && (
          <p className="px-2 py-3 text-[11.5px] font-medium italic text-slate-400">Alerts service unreachable.</p>
        )}

        {!query.isLoading && !query.isError && alerts.length === 0 && (
          <div className="flex items-center gap-2.5 px-2 py-3">
            <span className="deck-led deck-led-emerald" />
            <p className="text-[12px] font-semibold text-slate-500">No open alerts — all clear.</p>
          </div>
        )}

        {alerts.map((a) => {
          const sev = a.severity.toLowerCase();
          const age = timeAgo(a.createdAt);
          return (
            <button key={a.id} type="button" onClick={onOpen} className="deck-alert flex w-full items-center gap-2.5 px-3 py-2.5 text-left">
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
    </div>
  );
}

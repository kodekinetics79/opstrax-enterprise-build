import { useState, useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  Activity, AlertTriangle, MapPin, Radio, ShieldAlert,
  Truck, Wifi, WifiOff, Zap, Wrench, ChevronRight, Clock,
} from "lucide-react";
import { useNavigate } from "react-router-dom";
import { PageHeader, LoadingState } from "@/components/ui";
import { vehiclesApi } from "@/services/vehiclesApi";
import { driversApi } from "@/services/driversApi";
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

export function FleetOverviewPage() {
  const navigate = useNavigate();
  const [tab, setTab] = useState<Tab>("All");

  const vehiclesQ = useQuery({ queryKey: ["fleet-overview-vehicles"], queryFn: () => vehiclesApi.list(), refetchInterval: 30_000 });
  const driversQ  = useQuery({ queryKey: ["fleet-overview-drivers"],  queryFn: () => driversApi.list() });

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

  if (vehiclesQ.isLoading) return <LoadingState />;

  if (vehiclesQ.isError) {
    return (
      <div className="space-y-6">
        <PageHeader eyebrow="Fleet Overview" title="Live Vehicle Status" description="Real-time status across your fleet." />
        <div className="panel py-12 text-center">
          <AlertTriangle className="mx-auto h-8 w-8 text-red-400" />
          <p className="mt-2 text-sm font-semibold text-slate-600">Unable to load fleet data</p>
          <p className="mt-1 text-xs text-slate-400">The vehicles service did not respond. Retry in a moment.</p>
        </div>
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col gap-4">
      <div className="shrink-0">
        <PageHeader
          eyebrow="Fleet Overview"
          title="Live Vehicle Status"
          description={`${fleet.length} vehicles tracked · ${counts.Active} active · ${flagged} flagged for review`}
          actions={
            <button type="button" className="btn-primary" onClick={() => navigate("/vehicles")}>
              Full Fleet Registry
              <ChevronRight className="h-4 w-4" />
            </button>
          }
        />
      </div>

      {/* Status KPI strip — pinned, always visible */}
      <div className="grid shrink-0 grid-cols-2 gap-3 md:grid-cols-5">
        <KpiStrip label="Active"     count={counts.Active}    Icon={Truck}      accent="text-emerald-600" bg="bg-emerald-50" border="border-emerald-200" dot="bg-emerald-500 animate-pulse" onClick={() => setTab("Active")}    active={tab === "Active"} />
        <KpiStrip label="Idle"       count={counts.Idle}      Icon={Zap}        accent="text-amber-600"   bg="bg-amber-50"   border="border-amber-200"   dot="bg-amber-400"                 onClick={() => setTab("Idle")}      active={tab === "Idle"} />
        <KpiStrip label="Available"  count={counts.Available} Icon={Clock}      accent="text-slate-600"   bg="bg-slate-50"   border="border-slate-200"   dot="bg-slate-400"                 onClick={() => setTab("Available")} active={tab === "Available"} />
        <KpiStrip label="Out of Service" count={counts.OOS}   Icon={ShieldAlert} accent="text-red-600"    bg="bg-red-50"     border="border-red-200"     dot="bg-red-500 animate-pulse"     onClick={() => setTab("OOS")}       active={tab === "OOS"} />
        <KpiStrip label="Offline"    count={counts.Offline}   Icon={WifiOff}    accent="text-slate-500"   bg="bg-slate-50"   border="border-slate-200"   dot="bg-slate-300"                 onClick={() => setTab("Offline")}   active={tab === "Offline"} />
      </div>

      {/* Filter tabs + roster table — flexes to fill remaining height; table scrolls inside */}
      <div className="panel flex min-h-0 flex-1 flex-col overflow-hidden">
        <div className="flex shrink-0 items-center gap-1 border-b border-slate-100 px-4 pt-3 pb-0">
          {STATUS_TABS.map((t) => (
            <button
              key={t}
              type="button"
              onClick={() => setTab(t)}
              className={`relative px-3 py-2 text-sm font-medium transition-colors ${
                tab === t
                  ? "text-slate-950 after:absolute after:bottom-0 after:left-0 after:right-0 after:h-0.5 after:bg-teal-500"
                  : "text-slate-400 hover:text-slate-700"
              }`}
            >
              {t}
              {t !== "All" && (
                <span className={`ml-1.5 rounded-full px-1.5 py-px text-[10px] font-semibold ${
                  tab === t ? "bg-teal-100 text-teal-700" : "bg-slate-100 text-slate-500"
                }`}>
                  {counts[t as Exclude<Tab, "All">] ?? 0}
                </span>
              )}
            </button>
          ))}
          <span className="ml-auto text-xs text-slate-400 pb-2">
            <Activity className="inline h-3 w-3 mr-1 text-teal-500" />
            Live · refreshes every 30 s
          </span>
        </div>

        <div className="min-h-0 flex-1 overflow-auto">
          <table className="w-full text-sm">
            <thead className="sticky top-0 z-10 bg-white">
              <tr className="border-b border-slate-100">
                <th className="px-5 py-3 text-left text-[10px] font-semibold uppercase tracking-widest text-slate-400">Vehicle</th>
                <th className="px-3 py-3 text-left text-[10px] font-semibold uppercase tracking-widest text-slate-400">Driver</th>
                <th className="px-3 py-3 text-left text-[10px] font-semibold uppercase tracking-widest text-slate-400">Status</th>
                <th className="px-3 py-3 text-left text-[10px] font-semibold uppercase tracking-widest text-slate-400">Location</th>
                <th className="px-3 py-3 text-left text-[10px] font-semibold uppercase tracking-widest text-slate-400">Readiness</th>
                <th className="px-3 py-3 text-left text-[10px] font-semibold uppercase tracking-widest text-slate-400">Maint.</th>
                <th className="px-3 py-3 text-left text-[10px] font-semibold uppercase tracking-widest text-slate-400">Device</th>
                <th className="px-3 py-3" />
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-50">
              {filtered.map((v) => {
                const sc = STATUS_CFG[v.status];
                const mc = MAINT_CFG[v.maintenance];
                return (
                  <tr key={v.id} className={`group transition-colors hover:bg-slate-50/60 ${v.flag ? "bg-red-50/30" : ""}`}>
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
                      <span className={`font-medium ${v.readiness >= 80 ? "text-emerald-600" : v.readiness >= 60 ? "text-amber-600" : "text-red-600"}`}>
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

        <div className="flex shrink-0 items-center justify-between border-t border-slate-100 px-5 py-3 text-xs text-slate-400">
          <span>{filtered.length} of {fleet.length} vehicles shown</span>
          <div className="flex items-center gap-4">
            <span>
              <Wifi className="mr-1 inline h-3 w-3 text-emerald-500" />
              {fleet.filter((v) => v.signal === "Online").length} devices online
            </span>
            <span>
              <ShieldAlert className="mr-1 inline h-3 w-3 text-amber-500" />
              {flagged} flagged
            </span>
            <button type="button" className="text-teal-600 hover:underline" onClick={() => navigate("/iot-devices")}>
              Device Health →
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

function KpiStrip({
  label, count, Icon, accent, bg, border, dot, onClick, active,
}: {
  label: string;
  count: number;
  Icon: React.ElementType;
  accent: string;
  bg: string;
  border: string;
  dot: string;
  onClick: () => void;
  active: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`flex items-center gap-3 rounded-xl border p-4 text-left transition-all ${
        active ? `${bg} ${border} ring-2 ring-offset-1 ring-teal-400` : "bg-white border-slate-200 hover:border-slate-300"
      }`}
    >
      <div className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-xl ${active ? bg : "bg-slate-50"}`}>
        <Icon className={`h-4.5 w-4.5 ${active ? accent : "text-slate-400"}`} />
      </div>
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-1.5">
          <span className={`h-2 w-2 shrink-0 rounded-full ${dot}`} />
          <p className="text-2xl font-bold text-slate-900">{count}</p>
        </div>
        <p className={`truncate text-xs font-semibold ${active ? accent : "text-slate-500"}`}>{label}</p>
      </div>
    </button>
  );
}

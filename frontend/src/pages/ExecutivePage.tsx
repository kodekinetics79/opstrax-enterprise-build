import type { ReactNode } from "react";
import {
  Activity, AlertTriangle, Clock, Download, DollarSign, Shield,
  Truck, Users, Wrench,
} from "lucide-react";
import { useNavigate } from "react-router";
import { EmptyState, ErrorState, LoadingState, exportCsv } from "@/components/ui";
import { useAnalyticsExecutive } from "@/hooks/useReporting";
import type { AnyRecord } from "@/types";

const KPI_NAV_TILES = [
  { label: "Vehicles", accent: "text-sky-700", icon: "truck", route: "/vehicles" },
  { label: "Jobs", accent: "text-teal-700", icon: "activity", route: "/jobs" },
  { label: "Profitability", accent: "text-emerald-700", icon: "dollar", route: "/profitability" },
  { label: "Alerts", accent: "text-red-700", icon: "alert", route: "/alerts" },
  { label: "Fleet Utilization", accent: "text-amber-700", icon: "clock", route: "/fleet-utilization" },
  { label: "Driver Scorecards", accent: "text-violet-700", icon: "users", route: "/driver-scorecards" },
  { label: "SLA Records", accent: "text-teal-700", icon: "shield", route: "/sla-kpi" },
  { label: "Maintenance", accent: "text-amber-700", icon: "wrench", route: "/maintenance" },
] as const;

const ICON_MAP: Record<string, ReactNode> = {
  truck: <Truck className="h-5 w-5" />,
  activity: <Activity className="h-5 w-5" />,
  dollar: <DollarSign className="h-5 w-5" />,
  alert: <AlertTriangle className="h-5 w-5" />,
  clock: <Clock className="h-5 w-5" />,
  users: <Users className="h-5 w-5" />,
  shield: <Shield className="h-5 w-5" />,
  wrench: <Wrench className="h-5 w-5" />,
};

function optionalNumber(value: unknown): number | null {
  if (value == null || value === "") return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function MetricCard({ label, value, unit, detail, tone = "text-slate-900" }: {
  label: string;
  value: unknown;
  unit?: string;
  detail: string;
  tone?: string;
}) {
  const number = optionalNumber(value);
  return (
    <div className="panel p-4">
      <p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">{label}</p>
      <p className={`mt-1 text-2xl font-extrabold ${number == null ? "text-slate-400" : tone}`}>
        {number == null ? "—" : number.toLocaleString()}
        {number != null && unit ? <span className="ml-0.5 text-sm font-normal text-slate-400">{unit}</span> : null}
      </p>
      <p className="mt-1 text-xs text-slate-500">{number == null ? "Not enough data" : detail}</p>
    </div>
  );
}

export function ExecutivePage() {
  const summaryQ = useAnalyticsExecutive();
  const navigate = useNavigate();

  if (summaryQ.isLoading) return <LoadingState />;
  if (summaryQ.isError) {
    return (
      <ErrorState
        message="The current operational summary is unavailable for this account scope."
        onRetry={() => void summaryQ.refetch()}
      />
    );
  }

  const summary = (summaryQ.data ?? null) as AnyRecord | null;
  if (!summary) {
    return <EmptyState title="Operational summary unavailable" subtitle="No persisted aggregate was returned for this account scope." />;
  }

  const metrics = [
    { label: "Vehicles", value: summary.vehicleTotal, detail: "Recorded rows; known generated fixtures excluded", route: "/vehicles" },
    { label: "Operational Vehicles", value: summary.vehicleActive, detail: "Recorded operational status; known generated fixtures excluded", route: "/vehicles" },
    { label: "Drivers", value: summary.driverTotal, detail: "Recorded rows; known generated fixtures excluded", route: "/drivers" },
    { label: "Jobs Created", value: summary.jobsTotal, detail: "Recorded in the last 30 days; known generated fixtures excluded", route: "/jobs" },
    { label: "Jobs Completed", value: summary.jobsCompleted, detail: "Recorded complete or delivered in the last 30 days; known generated fixtures excluded", route: "/jobs" },
  ];
  const computedAt = summary.computedAt;

  function handleExport() {
    exportCsv("executive-operational-summary", [
      ...metrics.map((metric) => ({ metric: metric.label, value: optionalNumber(metric.value), evidence: metric.detail })),
      { metric: "Computed At", value: String(computedAt ?? "Unavailable"), evidence: "API response timestamp" },
    ]);
  }

  return (
    <div className="page-stack h-full overflow-y-auto">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="text-xl font-extrabold text-slate-900">Executive Operations</h1>
          <p className="mt-0.5 text-sm text-slate-500">Evidence-qualified record counts in the authorized tenant scope</p>
          <p className="mt-1 text-xs text-slate-400">
            {computedAt ? `Computed ${new Date(String(computedAt)).toLocaleString()}` : "Computation time unavailable"}
          </p>
        </div>
        <button type="button" className="btn-secondary flex items-center gap-2 text-sm" onClick={handleExport}>
          <Download className="h-4 w-4" />Export Current Summary
        </button>
      </div>

      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4 xl:grid-cols-8">
        {KPI_NAV_TILES.map((tile) => (
          <button key={tile.label} type="button" onClick={() => navigate(tile.route)}
            className="panel flex flex-col gap-1 p-3 text-left transition hover:border-slate-300 hover:shadow-sm">
            <div className={`flex h-8 w-8 items-center justify-center rounded-lg bg-slate-50 ${tile.accent}`}>
              {ICON_MAP[tile.icon]}
            </div>
            <span className="mt-1 text-[10px] font-semibold uppercase tracking-wide text-slate-500">{tile.label}</span>
            <span className="text-[10px] font-medium text-teal-600">View records →</span>
          </button>
        ))}
      </div>

      <section>
        <div className="mb-3">
          <h2 className="section-title">Current Record Counts</h2>
          <p className="mt-1 text-xs text-slate-500">Known generated fixtures are excluded. Derived score, SLA, safety, maintenance, exception and proof claims remain unavailable until their source provenance is recorded.</p>
          <p className="mt-1 text-xs text-slate-400">These operational counts do not certify provider, device, or regulatory evidence.</p>
        </div>
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {metrics.map((metric) => (
            <button key={metric.label} type="button" className="text-left" onClick={() => navigate(metric.route)}>
              <MetricCard label={metric.label} value={metric.value} detail={metric.detail} />
            </button>
          ))}
        </div>
      </section>

    </div>
  );
}

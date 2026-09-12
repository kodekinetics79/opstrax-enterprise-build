import { useState } from "react";
import {
  TrendingUp, TrendingDown, Minus, AlertTriangle, CheckCircle2,
  Truck, Users, ShieldCheck, Wrench, Package, BarChart3,
} from "lucide-react";
import { LoadingState, EmptyState } from "@/components/ui";
import {
  useAnalyticsExecutive,
  useAnalyticsOperations,
  useAnalyticsDispatch,
  useAnalyticsSafety,
  useAnalyticsMaintenance,
  useAnalyticsCustomer,
  useAnalyticsTrends,
  useAnalyticsInsights,
} from "@/hooks/useReporting";
import { useHasPermission } from "@/hooks/usePermission";

type Tab = "executive" | "operations" | "dispatch" | "safety" | "maintenance" | "customer" | "trends";

// ── Shared primitives ─────────────────────────────────────────────────────────

function KpiCard({
  label, value, unit, sub, target, color = "text-slate-800",
}: {
  label: string;
  value: string | number | null | undefined;
  unit?: string;
  sub?: string;
  target?: string;
  color?: string;
}) {
  const display = value == null ? "—" : value;
  return (
    <div className="panel p-4">
      <p className="text-[10px] font-bold uppercase tracking-widest text-slate-500 mb-1">{label}</p>
      <p className={`text-2xl font-extrabold leading-tight ${color}`}>
        {display}{unit && <span className="text-sm font-normal text-slate-400 ml-0.5">{unit}</span>}
      </p>
      {sub && <p className="mt-0.5 text-xs text-slate-500">{sub}</p>}
      {target && <p className="mt-0.5 text-[10px] text-slate-400">Target: {target}</p>}
    </div>
  );
}

function InsightBadge({ severity }: { severity: string }) {
  const map: Record<string, string> = {
    critical: "bg-red-50 text-red-700 border-red-200",
    warning:  "bg-amber-50 text-amber-700 border-amber-200",
    positive: "bg-emerald-50 text-emerald-700 border-emerald-200",
    info:     "bg-blue-50 text-blue-700 border-blue-200",
  };
  return (
    <span className={`inline-flex items-center rounded-full border px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide ${map[severity] ?? map.info}`}>
      {severity}
    </span>
  );
}

function TrendArrow({ value }: { value: string | undefined }) {
  if (value === "improving") return <TrendingUp className="h-4 w-4 text-emerald-500" />;
  if (value === "declining") return <TrendingDown className="h-4 w-4 text-red-500" />;
  return <Minus className="h-4 w-4 text-slate-400" />;
}

function metric(row: Record<string, unknown>, key: string): number | null {
  const value = row[key];
  if (value === null || value === undefined || value === "") return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function thresholdTone(value: number | null, target: number | null, below: string): string {
  if (value === null || target === null) return "text-slate-800";
  return value >= target ? "text-emerald-600" : below;
}

function positiveTone(value: number | null, tone: string): string {
  return value !== null && value > 0 ? tone : "text-slate-800";
}

function bandTone(value: number | null, high: number, middle: number): string {
  if (value === null) return "text-slate-800";
  return value >= high ? "text-emerald-600" : value >= middle ? "text-amber-600" : "text-red-600";
}

function metricText(value: number | null, prefix = "", suffix = ""): string | undefined {
  return value === null ? undefined : `${prefix}${value}${suffix}`;
}

function SectionLabel() {
  return (
    <p className="text-[10px] font-bold uppercase tracking-widest text-slate-400">
      System Analytics Insight · evidence status is shown by each data surface
    </p>
  );
}

function SimpleTable({ rows, cols }: { rows: Record<string, unknown>[]; cols: { key: string; label: string }[] }) {
  if (!rows || rows.length === 0) return <p className="text-xs text-slate-400 py-2">No data.</p>;
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-xs">
        <thead>
          <tr className="border-b border-slate-200">
            {cols.map((c) => <th key={c.key} className="px-3 py-2 text-left text-[10px] font-bold uppercase tracking-wide text-slate-500">{c.label}</th>)}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => (
            <tr key={i} className="border-b border-slate-100 hover:bg-slate-50">
              {cols.map((c) => <td key={c.key} className="px-3 py-2 text-slate-700">{String(row[c.key] ?? "")}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// ── Tab panels ────────────────────────────────────────────────────────────────

function ExecutivePanel() {
  const q = useAnalyticsExecutive();
  if (q.isLoading) return <LoadingState />;
  if (q.isError) return <EmptyState title="Unavailable" subtitle="Executive analytics require dashboard:view permission." />;
  const d = q.data ?? {};
  const n = (k: string) => metric(d, k);
  return (
    <div className="space-y-4">
      <SectionLabel />
      <p className="text-xs text-slate-500">Recorded tenant rows; known generated fixtures excluded. Derived rates remain unavailable until their inputs have provenance.</p>
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
        <KpiCard label="Recorded Vehicles" value={n("vehicleTotal")} />
        <KpiCard label="Operational Vehicles" value={n("vehicleActive")} />
        <KpiCard label="Recorded Drivers" value={n("driverTotal")} />
        <KpiCard label="Jobs Recorded (30d)" value={n("jobsTotal")} />
        <KpiCard label="Jobs Recorded Complete (30d)" value={n("jobsCompleted")} />
      </div>
    </div>
  );
}

function OperationsPanel() {
  const q = useAnalyticsOperations();
  if (q.isLoading) return <LoadingState />;
  if (q.isError) return <EmptyState title="Unavailable" subtitle="Operations analytics require dispatch:view permission." />;
  const d = q.data ?? {};
  const n = (k: string) => metric(d, k);
  const breakdown = (d["exceptionBreakdown"] as Record<string, unknown>[]) ?? [];
  return (
    <div className="space-y-4">
      <SectionLabel />
      <p className="text-xs text-slate-500">{String(d["evidenceStatus"] ?? "Operations metrics are unavailable until source provenance is recorded.")}</p>
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
        <KpiCard label="Qualified Active Trips" value={n("activeTrips")} />
        <KpiCard label="Qualified Trips Today" value={n("tripsToday")} />
        <KpiCard label="Qualified Route Compliance" value={n("routeComplianceAvg")} unit="%" />
        <KpiCard label="Qualified Open Exceptions" value={n("openExceptions")} />
        <KpiCard label="Qualified Active Assignments" value={n("activeAssignments")} />
      </div>
      {breakdown.length > 0 && (
        <div className="panel p-4">
          <p className="text-xs font-semibold text-slate-700 mb-2">Top Exception Types (30d)</p>
          <SimpleTable rows={breakdown} cols={[{ key: "exceptionType", label: "Type" }, { key: "cnt", label: "Count" }]} />
        </div>
      )}
    </div>
  );
}

function DispatchPanel() {
  const q = useAnalyticsDispatch();
  if (q.isLoading) return <LoadingState />;
  if (q.isError) return <EmptyState title="Unavailable" subtitle="Dispatch analytics require dispatch:view permission." />;
  const d = q.data ?? {};
  const n = (k: string) => metric(d, k);
  const dist = (d["statusDistribution"] as Record<string, unknown>[]) ?? [];
  return (
    <div className="space-y-4">
      <SectionLabel />
      <p className="text-xs text-slate-500">{String(d["evidenceStatus"] ?? "Dispatch metrics are unavailable until source provenance is recorded.")}</p>
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
        <KpiCard label="Qualified Assigned" value={n("currentlyAssigned")} />
        <KpiCard label="Qualified Accepted" value={n("accepted")} />
        <KpiCard label="Qualified In Transit" value={n("inTransit")} />
        <KpiCard label="Qualified Delivered (7d)" value={n("delivered")} />
        <KpiCard label="Qualified Exceptions Open" value={n("openExceptions")} />
        <KpiCard label="Qualified Proofs (7d)" value={n("proofsLast7d")} />
      </div>
      {dist.length > 0 && (
        <div className="panel p-4">
          <p className="text-xs font-semibold text-slate-700 mb-2">Status Distribution (30d)</p>
          <SimpleTable rows={dist} cols={[{ key: "status", label: "Status" }, { key: "cnt", label: "Count" }]} />
        </div>
      )}
    </div>
  );
}

function SafetyPanel() {
  const q = useAnalyticsSafety();
  if (q.isLoading) return <LoadingState />;
  if (q.isError) return <EmptyState title="Unavailable" subtitle="Safety analytics require safety:view permission." />;
  const d = q.data ?? {};
  const n = (k: string) => metric(d, k);
  const eventTypes = (d["eventTypeBreakdown"] as Record<string, unknown>[]) ?? [];
  const riskDrivers = (d["topRiskDrivers"] as Record<string, unknown>[]) ?? [];
  return (
    <div className="space-y-4">
      <SectionLabel />
      <p className="text-xs text-slate-500">{String(d["evidenceStatus"] ?? "Safety metrics are unavailable until source provenance is recorded.")}</p>
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
        <KpiCard label="Qualified Safety Events (30d)" value={n("safetyEventsLast30d")} />
        <KpiCard label="Qualified Critical Events" value={n("criticalEvents")} />
        <KpiCard label="Qualified Open Coaching Tasks" value={n("openCoachingTasks")} />
        <KpiCard label="Qualified Coaching Overdue" value={n("overdueCoachingTasks")} />
      </div>
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        {eventTypes.length > 0 && (
          <div className="panel p-4">
            <p className="text-xs font-semibold text-slate-700 mb-2">Event Types (30d)</p>
            <SimpleTable rows={eventTypes} cols={[{ key: "eventType", label: "Type" }, { key: "severity", label: "Severity" }, { key: "cnt", label: "Count" }]} />
          </div>
        )}
        {riskDrivers.length > 0 && (
          <div className="panel p-4">
            <p className="text-xs font-semibold text-slate-700 mb-2">Drivers by Qualified Event Count</p>
            <SimpleTable rows={riskDrivers} cols={[{ key: "driverName", label: "Driver" }, { key: "eventCount", label: "Events" }]} />
          </div>
        )}
      </div>
    </div>
  );
}

function MaintenancePanel() {
  const q = useAnalyticsMaintenance();
  if (q.isLoading) return <LoadingState />;
  if (q.isError) return <EmptyState title="Unavailable" subtitle="Maintenance analytics require maintenance:view permission." />;
  const d = q.data ?? {};
  const n = (k: string) => metric(d, k);
  const faults = (d["recurringFaultCodes"] as Record<string, unknown>[]) ?? [];
  const defects = (d["defectsByCategory"] as Record<string, unknown>[]) ?? [];
  return (
    <div className="space-y-4">
      <SectionLabel />
      <p className="text-xs text-slate-500">{String(d["evidenceStatus"] ?? "Maintenance metrics are unavailable until source provenance is recorded.")}</p>
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
        <KpiCard label="Qualified Vehicles OOS" value={n("vehiclesOutOfService")} />
        <KpiCard label="Qualified Critical Defects Open" value={n("criticalDefectsOpen")} />
        <KpiCard label="Qualified Open Work Orders" value={n("openWorkOrders")} />
        <KpiCard label="Qualified PM Overdue" value={n("pmOverdue")} />
        <KpiCard label="Qualified DVIRs (7d)" value={n("dvirLast7d")} />
      </div>
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        {faults.length > 0 && (
          <div className="panel p-4">
            <p className="text-xs font-semibold text-slate-700 mb-2">Recurring Fault Codes</p>
            <SimpleTable rows={faults} cols={[{ key: "code", label: "Code" }, { key: "component", label: "Component" }, { key: "total", label: "Total" }, { key: "maxRecurrences", label: "Max Recur." }]} />
          </div>
        )}
        {defects.length > 0 && (
          <div className="panel p-4">
            <p className="text-xs font-semibold text-slate-700 mb-2">Defects by Category (30d)</p>
            <SimpleTable rows={defects} cols={[{ key: "category", label: "Category" }, { key: "severity", label: "Severity" }, { key: "cnt", label: "Count" }]} />
          </div>
        )}
      </div>
    </div>
  );
}

function CustomerPanel() {
  const q = useAnalyticsCustomer();
  if (q.isLoading) return <LoadingState />;
  if (q.isError) return <EmptyState title="Unavailable" subtitle="Customer analytics require customer_portal:view permission." />;
  const d = q.data ?? {};
  const n = (k: string) => metric(d, k);
  const byType = (d["slaByType"] as Record<string, unknown>[]) ?? [];
  return (
    <div className="space-y-4">
      <SectionLabel />
      <p className="text-xs text-slate-500">Only evidence-qualified SLA measurements and breaches are included.</p>
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
        <KpiCard label="Verified SLA Met Rate" value={n("metRate")} unit="%" />
        <KpiCard label="Verified SLA Met" value={n("slaMet")} color={positiveTone(n("slaMet"), "text-emerald-600")} />
        <KpiCard label="Verified At Risk" value={n("slaAtRisk")} color={positiveTone(n("slaAtRisk"), "text-amber-600")} />
        <KpiCard label="Verified Breached" value={n("slaBreached")} color={positiveTone(n("slaBreached"), "text-red-600")} />
        <KpiCard label="Verified Open Breaches" value={n("openBreaches")} color={positiveTone(n("openBreaches"), "text-red-600")} />
        <KpiCard label="Verified SLA Records" value={n("slaTotal")} />
      </div>
      {byType.length > 0 && (
        <div className="panel p-4">
          <p className="text-xs font-semibold text-slate-700 mb-2">SLA by Type</p>
          <SimpleTable rows={byType} cols={[{ key: "slaType", label: "Type" }, { key: "total", label: "Total" }, { key: "met", label: "Met" }, { key: "breached", label: "Breached" }]} />
        </div>
      )}
    </div>
  );
}

function TrendsPanel() {
  const q = useAnalyticsTrends();
  if (q.isLoading) return <LoadingState />;
  if (q.isError) return <EmptyState title="Unavailable" subtitle="Trend analytics require reports:view permission." />;
  const d = q.data ?? {};
  const dispatch = (d["dispatchDailyTrend"] as Record<string, unknown>[]) ?? [];
  const safety   = (d["safetyDailyTrend"]   as Record<string, unknown>[]) ?? [];
  return (
    <div className="space-y-4">
      <SectionLabel />
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
        <KpiCard label="Verified OTD Last 30d" value={d["otdLast30d"] as number} unit="%" />
        <KpiCard label="Verified OTD Last 7d" value={d["otdLast7d"] as number} unit="%" />
        <div className="panel p-4 flex items-center gap-3">
          <TrendArrow value={d["otdTrend"] as string} />
          <div>
            <p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">Verified OTD Trend</p>
            <p className="text-sm font-semibold text-slate-700 capitalize">{String(d["otdTrend"] ?? "—")}</p>
          </div>
        </div>
      </div>
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        {dispatch.length > 0 && (
          <div className="panel p-4">
            <p className="text-xs font-semibold text-slate-700 mb-2">Daily Dispatch Activity (7d)</p>
            <SimpleTable rows={dispatch} cols={[{ key: "day", label: "Date" }, { key: "total", label: "Total" }, { key: "delivered", label: "Delivered" }, { key: "exceptions", label: "Exceptions" }]} />
          </div>
        )}
        {safety.length > 0 && (
          <div className="panel p-4">
            <p className="text-xs font-semibold text-slate-700 mb-2">Daily Safety Events (7d)</p>
            <SimpleTable rows={safety} cols={[{ key: "day", label: "Date" }, { key: "severity", label: "Severity" }, { key: "cnt", label: "Count" }]} />
          </div>
        )}
      </div>
    </div>
  );
}

// ── Insights sidebar ──────────────────────────────────────────────────────────

function InsightsSidebar() {
  const q = useAnalyticsInsights();
  if (q.isLoading) return null;
  if (q.isError) return <div className="panel p-4 text-xs text-slate-500">System insight records are unavailable.</div>;
  const insights = (q.data as Record<string, unknown>[]) ?? [];
  return (
    <div className="panel p-4 space-y-3">
      <p className="text-xs font-bold uppercase tracking-widest text-slate-500">System Analytics Insights</p>
      {insights.length === 0 && (
        <p className="text-xs leading-5 text-slate-500">No data-driven insight is available from the current operational records.</p>
      )}
      {insights.map((ins, i) => (
        <div key={i} className="flex gap-2">
          {(ins["severity"] as string) === "critical" && <AlertTriangle className="h-4 w-4 text-red-500 shrink-0 mt-0.5" />}
          {(ins["severity"] as string) === "warning"  && <AlertTriangle className="h-4 w-4 text-amber-500 shrink-0 mt-0.5" />}
          {(ins["severity"] as string) === "positive" && <CheckCircle2 className="h-4 w-4 text-emerald-500 shrink-0 mt-0.5" />}
          {(ins["severity"] as string) === "info"     && <BarChart3 className="h-4 w-4 text-blue-500 shrink-0 mt-0.5" />}
          <div className="min-w-0">
            <div className="flex items-center gap-2 mb-0.5 flex-wrap">
              <InsightBadge severity={String(ins["severity"] ?? "info")} />
            </div>
            <p className="text-xs font-semibold text-slate-700">{String(ins["title"] ?? "")}</p>
            <p className="text-[11px] text-slate-500 mt-0.5">{String(ins["detail"] ?? "")}</p>
          </div>
        </div>
      ))}
    </div>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────

const TABS: { key: Tab; label: string; icon: React.ReactNode }[] = [
  { key: "executive",   label: "Executive",   icon: <BarChart3 className="h-3.5 w-3.5" /> },
  { key: "operations",  label: "Operations",  icon: <Truck className="h-3.5 w-3.5" /> },
  { key: "dispatch",    label: "Dispatch",    icon: <Package className="h-3.5 w-3.5" /> },
  { key: "safety",      label: "Safety",      icon: <ShieldCheck className="h-3.5 w-3.5" /> },
  { key: "maintenance", label: "Maintenance", icon: <Wrench className="h-3.5 w-3.5" /> },
  { key: "customer",    label: "Customer",    icon: <Users className="h-3.5 w-3.5" /> },
  { key: "trends",      label: "Trends",      icon: <TrendingUp className="h-3.5 w-3.5" /> },
];

export function AnalyticsDashboardPage() {
  const [tab, setTab] = useState<Tab>("executive");

  return (
    <div className="page-stack min-w-0">
      <div>
        <h1 className="text-xl font-bold text-slate-800">Analytics Dashboard</h1>
        <p className="text-sm text-slate-500 mt-0.5">
          KPIs computed from current persisted operational records. Missing denominators and unmeasured scores remain unavailable.
        </p>
      </div>

      {/* Tab bar */}
      <div className="flex flex-wrap gap-1 border-b border-slate-200">
        {TABS.map((t) => (
          <button
            type="button"
            key={t.key}
            onClick={() => setTab(t.key)}
            className={`inline-flex items-center gap-1.5 px-3 py-2 text-sm font-medium border-b-2 transition-colors ${
              tab === t.key
                ? "border-blue-500 text-blue-600"
                : "border-transparent text-slate-500 hover:text-slate-700"
            }`}
          >
            {t.icon}{t.label}
          </button>
        ))}
      </div>

      {/* Content + insights */}
      <div className="grid grid-cols-1 items-start gap-3 xl:grid-cols-[minmax(0,1fr)_280px]">
        <div>
          {tab === "executive"   && <ExecutivePanel />}
          {tab === "operations"  && <OperationsPanel />}
          {tab === "dispatch"    && <DispatchPanel />}
          {tab === "safety"      && <SafetyPanel />}
          {tab === "maintenance" && <MaintenancePanel />}
          {tab === "customer"    && <CustomerPanel />}
          {tab === "trends"      && <TrendsPanel />}
        </div>
        <InsightsSidebar />
      </div>
    </div>
  );
}

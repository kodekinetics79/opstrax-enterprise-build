import { useMemo } from "react";
import { Link } from "react-router-dom";
import { useQueries } from "@tanstack/react-query";
import {
  Activity, AlertTriangle, ArrowUpRight, Camera, Cpu, Gauge,
  ClipboardCheck, Radio, ShieldAlert, Truck, Wrench, WifiOff,
} from "lucide-react";
import {
  ClayCard, EmptyState, ErrorState, KpiCard, PageHeader,
  RiskBadge, ScoreRing, SkeletonCard, StatusBadge,
} from "@/components/ui";
import { fleetHealthApi } from "@/services/fleetHealthApi";
import { vehiclesApi } from "@/services/vehiclesApi";
import { safetyApi } from "@/services/safetyApi";
import { maintenanceApi } from "@/services/maintenanceApi";
import { telemetryApi } from "@/services/telemetryApi";
import { incidentsApi } from "@/services/incidentsApi";
import { dvirApi } from "@/services/dvirApi";
import { useHasPermission } from "@/hooks/usePermission";
import type { AnyRecord } from "@/types";

// ── Value coercion helpers ──────────────────────────────────────────────────
// Backend serializes anonymous DTOs as camelCase and raw SQL rows as snake_case;
// read a set of candidate keys so the page survives either casing without ever
// fabricating a value.
function pick(row: AnyRecord | undefined, ...keys: string[]): unknown {
  if (!row) return undefined;
  for (const key of keys) {
    const v = row[key];
    if (v !== undefined && v !== null && v !== "") return v;
  }
  return undefined;
}
function num(v: unknown, fallback = 0): number {
  const n = Number(v);
  return Number.isFinite(n) ? n : fallback;
}
function bool(v: unknown): boolean {
  return v === true || v === "true" || v === 1 || v === "1";
}
function truthyCount(v: unknown): number {
  const n = Number(v);
  return Number.isFinite(n) && n > 0 ? n : 0;
}

// Module deep-links — every risk signal routes to the owning module so the
// intelligence view stays a router, not a dead-end dashboard.
const LINKS = {
  vehicles: "/vehicles",
  dvir: "/dvir-inspections",
  downtime: "/downtime",
  obd: "/obd-j1939",
  sensors: "/sensor-health",
  incidents: "/incidents",
  fleetHealth: "/fleet-health",
  safety: "/safety",
  maintenance: "/maintenance",
} as const;

function sevRing(sev: string): string {
  if (/critical/i.test(sev)) return "#dc2626";
  if (/high/i.test(sev)) return "#f97316";
  if (/medium/i.test(sev)) return "#f59e0b";
  return "#10b981";
}

// ── Signal chip: one linked entity-graph edge on a risk card ─────────────────
function SignalChip({
  to, icon, label, value, tone = "slate",
}: {
  to: string; icon: React.ReactNode; label: string; value: React.ReactNode; tone?: "red" | "amber" | "slate" | "sky";
}) {
  const toneCls =
    tone === "red" ? "border-red-200 bg-red-50 text-red-700"
    : tone === "amber" ? "border-amber-200 bg-amber-50 text-amber-700"
    : tone === "sky" ? "border-sky-200 bg-sky-50 text-sky-700"
    : "border-slate-200 bg-slate-50 text-slate-600";
  return (
    <Link
      to={to}
      className={`group inline-flex items-center gap-1.5 rounded-xl border px-2.5 py-1.5 text-[11px] font-bold shadow-sm transition hover:brightness-95 ${toneCls}`}
      title={`${label} — open module`}
    >
      <span className="opacity-80">{icon}</span>
      <span className="uppercase tracking-[0.08em]">{label}</span>
      <span className="tabular-nums">{value}</span>
      <ArrowUpRight className="h-3 w-3 opacity-0 transition group-hover:opacity-70" />
    </Link>
  );
}

// ── Vehicle risk card: the entity graph made visible for one unit ────────────
function VehicleRiskCard({ risk }: { risk: AnyRecord }) {
  const name = String(pick(risk, "displayName", "vehicle_code", "vehicleCode") ?? "Vehicle");
  const sev = String(pick(risk, "severity") ?? "low");
  const score = num(pick(risk, "priorityScore", "priority_score"));
  const status = String(pick(risk, "status") ?? "");
  const blocking = bool(pick(risk, "blockingDispatch", "blocking_dispatch"));
  const action = String(pick(risk, "recommendedAction", "recommended_action") ?? "");
  const reasons = Array.isArray(risk.reasons) ? (risk.reasons as unknown[]).map(String) : [];

  const m = (risk.metrics ?? {}) as AnyRecord;
  const critDef = truthyCount(pick(m, "criticalDefects", "critical_defects"));
  const openDef = truthyCount(pick(m, "openDefects", "open_defects"));
  const faults = truthyCount(pick(m, "activeFaultCodes", "active_fault_codes"));
  const openWo = truthyCount(pick(m, "openWorkOrders", "open_work_orders"));
  const overduePm = truthyCount(pick(m, "overduePm", "overdue_pm"));
  const offline = bool(pick(m, "deviceOffline", "device_offline"));
  const readiness = num(pick(m, "readinessScore", "readiness_score"), NaN);

  return (
    <ClayCard className="flex flex-col gap-3 p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <Truck className="h-4 w-4 shrink-0 text-slate-400" />
            <Link to={LINKS.vehicles} className="truncate text-[15px] font-black tracking-tight text-slate-900 hover:text-teal-700">
              {name}
            </Link>
          </div>
          <div className="mt-1.5 flex flex-wrap items-center gap-1.5">
            <RiskBadge risk={sev} />
            {status && <StatusBadge status={status} />}
            {blocking && (
              <span className="inline-flex items-center gap-1 rounded-full border border-red-300 bg-red-50 px-2 py-[3px] text-[10px] font-black uppercase tracking-[0.1em] text-red-700">
                <ShieldAlert className="h-3 w-3" /> Blocks Dispatch
              </span>
            )}
          </div>
        </div>
        <ScoreRing score={Math.round(score)} size={56} strokeWidth={5} color={sevRing(sev)} label="risk" />
      </div>

      {/* Linked signals — each edge deep-links into the owning module */}
      <div className="flex flex-wrap gap-1.5">
        <SignalChip to={LINKS.dvir} icon={<ClipboardCheck className="h-3.5 w-3.5" />} label="DVIR"
          value={critDef > 0 ? `${critDef} crit` : openDef > 0 ? `${openDef} open` : "OK"}
          tone={critDef > 0 ? "red" : openDef > 0 ? "amber" : "slate"} />
        <SignalChip to={LINKS.obd} icon={<Cpu className="h-3.5 w-3.5" />} label="OBD"
          value={faults > 0 ? `${faults} fault` : "clear"} tone={faults > 0 ? "red" : "slate"} />
        <SignalChip to={LINKS.downtime} icon={<Wrench className="h-3.5 w-3.5" />} label="WO"
          value={openWo > 0 ? `${openWo} open` : "none"} tone={openWo > 0 ? "amber" : "slate"} />
        <SignalChip to={LINKS.maintenance} icon={<Gauge className="h-3.5 w-3.5" />} label="PM"
          value={overduePm > 0 ? `${overduePm} due` : "current"} tone={overduePm > 0 ? "amber" : "slate"} />
        <SignalChip to={LINKS.sensors} icon={offline ? <WifiOff className="h-3.5 w-3.5" /> : <Radio className="h-3.5 w-3.5" />} label="Device"
          value={offline ? "offline" : "online"} tone={offline ? "red" : "sky"} />
        {Number.isFinite(readiness) && (
          <SignalChip to={LINKS.fleetHealth} icon={<Activity className="h-3.5 w-3.5" />} label="Ready"
            value={`${Math.round(readiness)}%`} tone={readiness < 60 ? "amber" : "slate"} />
        )}
      </div>

      {reasons.length > 0 && (
        <ul className="space-y-1 rounded-xl bg-slate-50 px-3 py-2.5 shadow-[inset_0_1px_3px_rgba(15,23,42,.08)]">
          {reasons.slice(0, 4).map((r, i) => (
            <li key={i} className="flex items-start gap-1.5 text-[12px] leading-5 text-slate-600">
              <span className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-slate-400" />
              {r}
            </li>
          ))}
        </ul>
      )}

      {action && (
        <div className="flex items-center justify-between gap-2 border-t border-slate-100 pt-2.5">
          <p className="min-w-0 truncate text-[12px] font-semibold text-slate-500">{action}</p>
          <Link to={LINKS.fleetHealth} className="btn-ghost shrink-0 px-2.5 py-1 text-[11px]">Resolve</Link>
        </div>
      )}
    </ClayCard>
  );
}

// ── Alert / recommendation row (right rail) ──────────────────────────────────
function AlertRow({ alert }: { alert: AnyRecord }) {
  const sev = String(pick(alert, "severity", "risk_level", "riskLevel") ?? "info");
  const type = String(pick(alert, "alert_type", "alertType", "recommendation_type", "recommendationType", "title") ?? "Alert");
  const message = String(pick(alert, "message", "summary", "body") ?? "");
  const vehicle = pick(alert, "vehicle_code", "vehicleCode", "displayName");
  const dot = /critical|high/i.test(sev) ? "bg-red-500" : /medium|warn/i.test(sev) ? "bg-amber-400" : "bg-sky-400";
  return (
    <div className="flex items-start gap-2.5 rounded-xl border border-slate-100 bg-white px-3 py-2.5 shadow-sm">
      <span className={`mt-1.5 h-2 w-2 shrink-0 rounded-full ${dot}`} />
      <div className="min-w-0 flex-1">
        <div className="flex items-center justify-between gap-2">
          <p className="truncate text-[12.5px] font-bold text-slate-800">{type.replace(/[_.]/g, " ")}</p>
          <RiskBadge risk={sev} />
        </div>
        {message && <p className="mt-0.5 line-clamp-2 text-[12px] leading-5 text-slate-500">{message}</p>}
        {vehicle != null && (
          <p className="mt-1 text-[11px] font-semibold uppercase tracking-[0.08em] text-slate-400">{String(vehicle)}</p>
        )}
      </div>
    </div>
  );
}

function SectionHeader({ icon, title, count, to, cta }: {
  icon: React.ReactNode; title: string; count?: number; to?: string; cta?: string;
}) {
  return (
    <div className="flex items-center justify-between gap-3">
      <div className="flex items-center gap-2">
        <span className="text-teal-600">{icon}</span>
        <h2 className="text-[15px] font-black tracking-tight text-slate-900">{title}</h2>
        {typeof count === "number" && (
          <span className="rounded-full border border-slate-200 bg-slate-50 px-2 py-0.5 text-[11px] font-bold text-slate-500">{count}</span>
        )}
      </div>
      {to && cta && <Link to={to} className="btn-ghost px-2.5 py-1 text-[11px]">{cta}</Link>}
    </div>
  );
}

// ── Page ─────────────────────────────────────────────────────────────────────
export function FleetIntelligencePage() {
  const hasPermission = useHasPermission();
  const canView = hasPermission("dashboard:view");

  const results = useQueries({
    queries: [
      { queryKey: ["fleet-intel", "health-summary"], queryFn: fleetHealthApi.summary, refetchInterval: 60_000, enabled: canView },
      { queryKey: ["fleet-intel", "health-risks"], queryFn: () => fleetHealthApi.risks(), refetchInterval: 60_000, enabled: canView },
      { queryKey: ["fleet-intel", "vehicles-summary"], queryFn: vehiclesApi.summary, refetchInterval: 120_000, enabled: canView },
      { queryKey: ["fleet-intel", "safety-scores"], queryFn: safetyApi.driverScores, refetchInterval: 120_000, enabled: canView },
      { queryKey: ["fleet-intel", "maint-summary"], queryFn: maintenanceApi.summary, refetchInterval: 120_000, enabled: canView },
      { queryKey: ["fleet-intel", "fault-codes"], queryFn: () => maintenanceApi.faultCodes("active"), refetchInterval: 120_000, enabled: canView },
      { queryKey: ["fleet-intel", "downtime"], queryFn: maintenanceApi.overdue, refetchInterval: 120_000, enabled: canView },
      { queryKey: ["fleet-intel", "telemetry-summary"], queryFn: telemetryApi.liveMapSummary, refetchInterval: 60_000, enabled: canView },
      { queryKey: ["fleet-intel", "telemetry-alerts"], queryFn: () => telemetryApi.alerts("Open"), refetchInterval: 60_000, enabled: canView },
      { queryKey: ["fleet-intel", "devices"], queryFn: telemetryApi.devices, refetchInterval: 120_000, enabled: canView },
      { queryKey: ["fleet-intel", "incidents-summary"], queryFn: incidentsApi.summary, refetchInterval: 120_000, enabled: canView },
      { queryKey: ["fleet-intel", "dvir-summary"], queryFn: dvirApi.summary, refetchInterval: 120_000, enabled: canView },
    ],
  });

  const [
    healthSummary, healthRisks, vehiclesSummary, safetyScores, maintSummary,
    faultCodes, downtime, telemetrySummary, telemetryAlerts, devices,
    incidentsSummary, dvirSummary,
  ] = results;

  const summary = (healthSummary.data ?? {}) as AnyRecord;
  const vehSum = (vehiclesSummary.data ?? {}) as AnyRecord;
  const maint = (maintSummary.data ?? {}) as AnyRecord;
  const teleKpis = ((telemetrySummary.data as AnyRecord)?.kpis ?? {}) as AnyRecord;
  const inc = (incidentsSummary.data ?? {}) as AnyRecord;

  // ── KPI band values — every figure is a live read, never a constant ────────
  const totalVehicles = num(pick(summary, "totalVehicles") ?? pick(vehSum, "total"));
  const dispatchReady = num(pick(summary, "dispatchReadyVehicles"));
  const availabilityPct = totalVehicles > 0 ? Math.round((dispatchReady / totalVehicles) * 100) : null;

  const openSafetyEvents = num(pick(summary, "openSafetyEventDrivers"));
  const belowSafety = num(pick(summary, "belowSafetyThreshold"));
  const activeFaults = Array.isArray(faultCodes.data) ? faultCodes.data.length : 0;
  const vehiclesDowntime = Array.isArray(downtime.data) ? downtime.data.length : 0;
  const overduePm = num(pick(summary, "overduePmVehicles"));
  const openIncidents = num(pick(inc, "open_incidents", "openIncidents"));
  const connectedDevices = num(
    pick(teleKpis, "registeredDevices", "connectedUnits") ??
    (Array.isArray(devices.data) ? devices.data.length : undefined)
  );

  const risks = useMemo(
    () => (Array.isArray(healthRisks.data) ? healthRisks.data : []),
    [healthRisks.data],
  );
  const vehicleRisks = useMemo(
    () => risks.filter((r) => String(pick(r as AnyRecord, "entityType", "entity_type")) === "vehicle"),
    [risks],
  );

  const alerts = useMemo(() => {
    const t = Array.isArray(telemetryAlerts.data) ? telemetryAlerts.data : [];
    return t as AnyRecord[];
  }, [telemetryAlerts.data]);

  const healthInsights = useMemo(() => {
    const raw = (summary.systemInsights ?? []) as AnyRecord[];
    return Array.isArray(raw) ? raw.filter((i) => String(pick(i, "type")) !== "all_clear") : [];
  }, [summary.systemInsights]);

  if (!canView) {
    return (
      <div className="space-y-4">
        <PageHeader eyebrow="Cross-Module Command" title="Fleet Intelligence"
          description="A unified command view that stitches every fleet-ops signal together." />
        <EmptyState title="Access restricted" subtitle="You need the dashboard:view permission to open Fleet Intelligence." />
      </div>
    );
  }

  const kpiLoading = healthSummary.isLoading || vehiclesSummary.isLoading;
  const anyCriticalError = healthSummary.isError && healthRisks.isError;

  return (
    <div className="space-y-5">
      <PageHeader
        eyebrow="Cross-Module Command"
        title="Fleet Intelligence"
        description="A single Samsara-style command surface that joins fleet health, safety, maintenance, telematics, DVIR and incident signals per vehicle — every figure pulled live, every risk linked back to its module."
        footer={
          <div className="flex flex-wrap items-center gap-2.5">
            {(["Fleet Health", "Vehicles", "Safety", "Maintenance", "Telematics", "DVIR", "Incidents"]).map((m) => (
              <span key={m} className="inline-flex items-center gap-1.5 rounded-full border border-slate-200 bg-white/80 px-2.5 py-1 text-[11px] font-bold text-slate-500">
                <span className="h-1.5 w-1.5 rounded-full bg-teal-400" />{m}
              </span>
            ))}
          </div>
        }
      />

      {/* ── KPI BAND ─────────────────────────────────────────────────────── */}
      {anyCriticalError ? (
        <ErrorState message="Unable to reach the intelligence services." onRetry={() => { healthSummary.refetch(); healthRisks.refetch(); }} />
      ) : kpiLoading ? (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4 xl:grid-cols-7">
          {Array.from({ length: 7 }).map((_, i) => <SkeletonCard key={i} />)}
        </div>
      ) : (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4 xl:grid-cols-7">
          <KpiCard label="Fleet Availability" value={availabilityPct === null ? "--" : `${availabilityPct}%`}
            trend={dispatchReady > 0 ? `${dispatchReady}/${totalVehicles} dispatch-ready` : undefined} icon={<Truck className="h-5 w-5" />} />
          <KpiCard label="Open Safety Events" value={openSafetyEvents} status={openSafetyEvents > 0 ? "risk" : undefined}
            trend={belowSafety > 0 ? `${belowSafety} drivers below threshold` : undefined} icon={<ShieldAlert className="h-5 w-5" />} />
          <KpiCard label="Active Fault Codes" value={activeFaults} status={activeFaults > 0 ? "critical" : undefined}
            trend="OBD / J1939 live" icon={<Cpu className="h-5 w-5" />} />
          <KpiCard label="Vehicles In Downtime" value={vehiclesDowntime} status={vehiclesDowntime > 0 ? "warning" : undefined}
            icon={<Wrench className="h-5 w-5" />} />
          <KpiCard label="Overdue PM" value={overduePm} status={overduePm > 0 ? "overdue" : undefined}
            trend={pick(maint, "pm_compliance") ? `${pick(maint, "pm_compliance")} compliant` : undefined} icon={<Gauge className="h-5 w-5" />} />
          <KpiCard label="Open Incidents" value={openIncidents} status={openIncidents > 0 ? "review" : undefined}
            icon={<AlertTriangle className="h-5 w-5" />} />
          <KpiCard label="Connected Devices" value={connectedDevices} status="connected"
            trend={pick(teleKpis, "connectivityCoverage") != null ? `${pick(teleKpis, "connectivityCoverage")}% coverage` : undefined}
            icon={<Radio className="h-5 w-5" />} />
        </div>
      )}

      {/* ── MAIN GRID: vehicle risk graph (left) + alerts rail (right) ────── */}
      <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_360px]">
        {/* Vehicle risk — the entity graph made visible */}
        <section className="space-y-3">
          <SectionHeader icon={<Truck className="h-4 w-4" />} title="Vehicle Risk — Entity Graph"
            count={vehicleRisks.length} to={LINKS.fleetHealth} cta="Full board" />
          {healthRisks.isLoading ? (
            <div className="grid gap-3 md:grid-cols-2">
              {Array.from({ length: 4 }).map((_, i) => <SkeletonCard key={i} />)}
            </div>
          ) : healthRisks.isError ? (
            <ErrorState message="Fleet-health risk feed is unavailable." onRetry={() => healthRisks.refetch()} />
          ) : vehicleRisks.length === 0 ? (
            <EmptyState title="No at-risk vehicles"
              subtitle="Every vehicle is within normal operating range — no critical defects, overdue PM, active fault codes or device drop-offs across the fleet." />
          ) : (
            <div className="grid gap-3 md:grid-cols-2">
              {vehicleRisks.map((r, i) => (
                <VehicleRiskCard key={String(pick(r as AnyRecord, "entityId", "entity_id") ?? i)} risk={r as AnyRecord} />
              ))}
            </div>
          )}
        </section>

        {/* Right rail: live alerts + fleet-health recommendations */}
        <aside className="space-y-5">
          <ClayCard className="p-4">
            <SectionHeader icon={<Radio className="h-4 w-4" />} title="Live Telematics Alerts"
              count={alerts.length} to="/alerts" cta="Center" />
            <div className="mt-3 space-y-2">
              {telemetryAlerts.isLoading ? (
                Array.from({ length: 3 }).map((_, i) => (
                  <div key={i} className="skeleton h-16 w-full rounded-xl" />
                ))
              ) : telemetryAlerts.isError ? (
                <ErrorState message="Telemetry alert feed unavailable." onRetry={() => telemetryAlerts.refetch()} />
              ) : alerts.length === 0 ? (
                <EmptyState title="No open alerts" subtitle="No telematics alerts are currently open across the fleet." />
              ) : (
                alerts.slice(0, 8).map((a, i) => <AlertRow key={String(a.id ?? i)} alert={a} />)
              )}
            </div>
          </ClayCard>

          <ClayCard className="p-4">
            <SectionHeader icon={<Activity className="h-4 w-4" />} title="Fleet-Health Recommendations"
              count={healthInsights.length} to={LINKS.fleetHealth} cta="Detail" />
            <div className="mt-3 space-y-2">
              {healthSummary.isLoading ? (
                Array.from({ length: 3 }).map((_, i) => (
                  <div key={i} className="skeleton h-16 w-full rounded-xl" />
                ))
              ) : healthSummary.isError ? (
                <ErrorState message="Fleet-health summary unavailable." onRetry={() => healthSummary.refetch()} />
              ) : healthInsights.length === 0 ? (
                <EmptyState title="All clear"
                  subtitle="Fleet health metrics are within normal operating range. No urgent actions required." />
              ) : (
                healthInsights.map((ins, i) => <AlertRow key={i} alert={ins} />)
              )}
            </div>
          </ClayCard>

          {/* Connectivity readout — neumorphic inset well */}
          <ClayCard className="p-4">
            <SectionHeader icon={<Camera className="h-4 w-4" />} title="Connectivity" to={LINKS.sensors} cta="Devices" />
            <div className="mt-3 grid grid-cols-2 gap-2.5">
              {[
                { label: "Live Units", value: pick(teleKpis, "liveUnits") ?? "--" },
                { label: "Device Offline", value: pick(teleKpis, "deviceOfflineUnits") ?? "--" },
                { label: "Camera Offline", value: pick(teleKpis, "cameraOfflineUnits") ?? "--" },
                { label: "DVIR Today", value: pick((dvirSummary.data ?? {}) as AnyRecord, "inspections_today") ?? "--" },
              ].map((cell) => (
                <div key={cell.label} className="rounded-xl bg-slate-50 px-3 py-2.5 shadow-[inset_0_1px_4px_rgba(15,23,42,.1)]">
                  <p className="text-[10px] font-black uppercase tracking-[0.14em] text-slate-400">{cell.label}</p>
                  <p className="mt-1 text-[20px] font-black tabular-nums text-slate-900">{String(cell.value)}</p>
                </div>
              ))}
            </div>
          </ClayCard>
        </aside>
      </div>
    </div>
  );
}

export default FleetIntelligencePage;

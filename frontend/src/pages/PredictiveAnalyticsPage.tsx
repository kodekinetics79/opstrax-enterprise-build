import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  AreaChart, Area, BarChart, Bar, XAxis, YAxis, CartesianGrid,
  Tooltip, ResponsiveContainer, ReferenceLine,
} from "recharts";
import {
  AlertTriangle, Bot, BrainCircuit, Download, Shield, Truck, TrendingDown,
  Wrench, Zap, Clock, DollarSign, Activity,
} from "lucide-react";
import { apiClient, unwrap } from "@/services/apiClient";
import { withFallback } from "@/services/fleetDomainApi";
import { exportCsv, LoadingState, PageHeader } from "@/components/ui";
import { vehicles as seedVehicles, drivers as seedDrivers } from "@/data/mockOperatingData";
import type { AnyRecord } from "@/types";

// ── Seed data ─────────────────────────────────────────────────────────────────

const VEHICLE_NAMES = ["BOX-104", "REF-209", "TRK-316", "VAN-512", "BOX-218", "REF-401", "TRK-107", "VAN-303"];
const DRIVER_NAMES  = ["Marcus J.", "Sofia R.", "Liam P.", "Aisha W.", "Ethan K.", "Priya S.", "Jordan M.", "Elena V."];
const FAILURE_TYPES = ["Engine Warning", "Brake Wear", "Tyre Pressure", "Battery Low", "Oil Level", "Coolant Leak", "Transmission", "Air Filter"];
const RISK_FACTORS  = ["Harsh braking trend ×3", "Speed events ×5", "Mobile use alert ×2", "Following distance ×4", "Lane departure ×1"];

function buildMaintenanceSeed(): AnyRecord[] {
  return VEHICLE_NAMES.map((code, i) => ({
    id: i + 1,
    vehicleCode:    code,
    vehicleType:    (["Box Truck","Reefer","Truck","Van"] as const)[i % 4],
    failureType:    FAILURE_TYPES[i],
    confidencePct:  [96, 91, 88, 84, 79, 74, 68, 61][i],
    daysUntilRisk:  [3, 5, 7, 9, 11, 14, 18, 22][i],
    estimatedCost:  [4200, 3100, 2800, 1900, 1500, 950, 750, 620][i],
    revenueAtRisk:  [8400, 6200, 5600, 3800, 3000, 1900, 1500, 1240][i],
    lastService:    `2026-05-${String(10 + i * 2).padStart(2, "0")}`,
    priority:       (["Critical","Critical","High","High","High","Medium","Medium","Low"] as const)[i],
    aiRecommendation: [
      "Schedule brake inspection immediately. Pattern matches pre-failure signature from 3 comparable vehicles.",
      "Coolant level dropping at 0.8L/week. Likely slow internal leak — pressure test before next long haul.",
      "Tyre tread depth below 3mm on rear axle. Replace before next reefer run to meet compliance threshold.",
      "Battery discharge events increasing. Cold-weather reefer cycles accelerating drain. Swap recommended.",
      "Oil sample shows metal particulate spike — potential bearing wear. Drain and inspect before week end.",
      "Air filter restriction above 90% threshold. Fuel efficiency dropping. Service during next scheduled stop.",
      "Transmission slip events detected on uphill segments. Fluid change and inspection recommended.",
      "Air filter service overdue by 4,200 km. Low urgency — schedule at next PM interval.",
    ][i],
  }));
}

function buildDriverRiskSeed(): AnyRecord[] {
  return DRIVER_NAMES.map((name, i) => ({
    id: i + 1,
    driverName:      name,
    driverCode:      `DRV-${String(i + 1).padStart(3, "0")}`,
    currentScore:    [94, 89, 83, 79, 74, 68, 63, 57][i],
    predictedScore:  [93, 86, 79, 72, 66, 58, 51, 43][i],
    scoreDelta:      [-1, -3, -4, -7, -8, -10, -12, -14][i],
    harshEvents:     [1, 3, 5, 7, 9, 12, 14, 18][i],
    riskFactor:      RISK_FACTORS[i % RISK_FACTORS.length],
    coachingStatus:  (["None Needed","Scheduled","Recommended","Urgent","Urgent","Critical","Critical","Critical"] as const)[i],
    revenueImpact:   [0, 0, 800, 1200, 2100, 3400, 4800, 6200][i],
    aiRecommendation: [
      "Driver is performing well. No coaching required this week.",
      "Minor harsh braking trend. Review with driver at next check-in.",
      "Speed events increasing. Schedule defensive driving module by Friday.",
      "Patterns suggest fatigue — review shift schedule and HOS logs.",
      "Mobile use events in restricted zones. Mandatory phone-policy coaching required.",
      "Safety score trajectory at -10pt/week. Immediate coaching and incident review.",
      "High incident correlation risk. Recommend temporary route reassignment pending coaching.",
      "Critical pattern — 3 near-miss events in 10 days. Suspend high-risk routes pending review.",
    ][i],
  }));
}

function buildSlaRiskSeed(): AnyRecord[] {
  return Array.from({ length: 6 }, (_, i) => ({
    id: i + 1,
    jobNumber:        `JOB-${String(2001 + i * 7).padStart(5, "0")}`,
    customerName:     ["Al-Futtaim Logistics","Emirates Transport","Abu Dhabi Ports","Agility MEA","DSV UAE","Aramex"][i],
    route:            ["DXB→AUH","SHJ→DXB","ABU→DXB","DXB→SHJ","FUJ→DXB","AUH→RKT"][i],
    delayProbability: [87, 74, 68, 61, 48, 34][i],
    currentEta:       ["14:30","15:45","16:00","13:15","17:30","12:00"][i],
    slaDeadline:      ["14:00","15:30","15:45","13:00","17:00","11:45"][i],
    revenueAtRisk:    [12400, 8700, 6300, 4200, 2800, 1500][i],
    delayReason:      ["Traffic congestion on E311","Vehicle running 18 min late","Customs clearance delay","Loading dock backlog","Driver HOS approaching limit","Route deviation detected"][i],
    aiRecommendation: [
      "Reroute via E611 — saves 14 min. Alert customer with revised ETA now.",
      "Assign backup vehicle VAN-305 from depot. Customer notification required immediately.",
      "Contact customs liaison. Pre-clearance for next 3 shipments recommended.",
      "Reassign stop 3 to another active route. Customer SLA breach in 30 min.",
      "Driver at HOS limit in 45 min. Hand-off to DRV-019 at Al Quoz depot.",
      "Route correction auto-suggested. Driver acknowledging update via app.",
    ][i],
  }));
}

const RISK_TREND = [
  { week: "W−7", maintenance: 3, safety: 8, sla: 2 },
  { week: "W−6", maintenance: 4, safety: 7, sla: 3 },
  { week: "W−5", maintenance: 3, safety: 9, sla: 2 },
  { week: "W−4", maintenance: 5, safety: 10, sla: 4 },
  { week: "W−3", maintenance: 6, safety: 8, sla: 3 },
  { week: "W−2", maintenance: 7, safety: 11, sla: 5 },
  { week: "W−1", maintenance: 8, safety: 12, sla: 6 },
  { week: "This wk", maintenance: 8, safety: 14, sla: 6 },
];

// ── API ────────────────────────────────────────────────────────────────────────

const predictiveApi = {
  maintenance: () => withFallback(
    unwrap<AnyRecord[]>(apiClient.get("/api/predictions/maintenance")).then((rows) =>
      rows.map((r) => ({ ...r, vehicleCode: r.vehicleCode ?? r.vehicle_code ?? "", confidencePct: Number(r.confidencePct ?? r.confidence_pct ?? 0) }))
    ),
    () => buildMaintenanceSeed()
  ),
  driverRisk: () => withFallback(
    unwrap<AnyRecord[]>(apiClient.get("/api/predictions/driver-risk")).then((rows) =>
      rows.map((r) => ({ ...r, driverName: r.driverName ?? r.driver_name ?? "", currentScore: Number(r.currentScore ?? r.current_score ?? 0) }))
    ),
    () => buildDriverRiskSeed()
  ),
  slaRisk: () => withFallback(
    unwrap<AnyRecord[]>(apiClient.get("/api/predictions/sla-risk")).then((rows) =>
      rows.map((r) => ({ ...r, jobNumber: r.jobNumber ?? r.job_number ?? "", delayProbability: Number(r.delayProbability ?? r.delay_probability ?? 0) }))
    ),
    () => buildSlaRiskSeed()
  ),
};

void seedVehicles; void seedDrivers;

// ── Helpers ───────────────────────────────────────────────────────────────────

function PriorityBadge({ p }: { p: string }) {
  const cls =
    p === "Critical" ? "bg-red-50 border-red-200 text-red-700" :
    p === "High"     ? "bg-amber-50 border-amber-200 text-amber-700" :
    p === "Urgent"   ? "bg-red-50 border-red-200 text-red-700" :
    "bg-slate-50 border-slate-200 text-slate-500";
  return <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase ${cls}`}>{p}</span>;
}

function ConfidenceBar({ pct }: { pct: number }) {
  const color = pct >= 85 ? "bg-red-500" : pct >= 70 ? "bg-amber-500" : "bg-slate-300";
  return (
    <div className="flex items-center gap-2 min-w-28">
      <div className="flex-1 h-1.5 bg-slate-100 rounded-full overflow-hidden">
        <div className={`h-full ${color} rounded-full`} style={{ width: `${pct}%` }} />
      </div>
      <span className={`text-xs font-bold tabular-nums ${pct >= 85 ? "text-red-600" : pct >= 70 ? "text-amber-600" : "text-slate-500"}`}>{pct}%</span>
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

type FeedTab = "maintenance" | "driver-risk" | "sla-risk";

export function PredictiveAnalyticsPage() {
  const [feed, setFeed] = useState<FeedTab>("maintenance");
  const [expandedId, setExpandedId] = useState<number | null>(null);

  const maintQ  = useQuery({ queryKey: ["predictions-maintenance"], queryFn: predictiveApi.maintenance, staleTime: 60_000 });
  const driverQ = useQuery({ queryKey: ["predictions-driver-risk"],  queryFn: predictiveApi.driverRisk,  staleTime: 60_000 });
  const slaQ    = useQuery({ queryKey: ["predictions-sla-risk"],     queryFn: predictiveApi.slaRisk,     staleTime: 60_000 });

  const maintenance = (maintQ.data  ?? []) as AnyRecord[];
  const driverRisk  = (driverQ.data ?? []) as AnyRecord[];
  const slaRisk     = (slaQ.data   ?? []) as AnyRecord[];

  const isLoading = maintQ.isLoading || driverQ.isLoading || slaQ.isLoading;

  const totalRevenueAtRisk =
    maintenance.reduce((s, r) => s + Number(r.revenueAtRisk ?? 0), 0) +
    driverRisk.reduce((s, r)  => s + Number(r.revenueImpact ?? 0), 0) +
    slaRisk.reduce((s, r)    => s + Number(r.revenueAtRisk ?? 0), 0);

  const criticalMaint  = maintenance.filter((r) => String(r.priority) === "Critical").length;
  const urgentDrivers  = driverRisk.filter((r) => ["Urgent","Critical"].includes(String(r.coachingStatus))).length;
  const highSlaRisk    = slaRisk.filter((r) => Number(r.delayProbability) >= 70).length;
  const avgConfidence  = maintenance.length > 0
    ? Math.round(maintenance.reduce((s, r) => s + Number(r.confidencePct ?? 0), 0) / maintenance.length)
    : 0;

  function handleExport() {
    const rows = [
      ...maintenance.map((r) => ({ type: "Maintenance", entity: String(r.vehicleCode), risk: String(r.failureType), confidence: `${Number(r.confidencePct)}%`, daysUntil: Number(r.daysUntilRisk), revenueAtRisk: Number(r.revenueAtRisk) })),
      ...driverRisk.map((r)  => ({ type: "Driver Safety", entity: String(r.driverName), risk: String(r.riskFactor), confidence: "—", daysUntil: 7, revenueAtRisk: Number(r.revenueImpact) })),
      ...slaRisk.map((r)    => ({ type: "SLA Risk", entity: String(r.jobNumber), risk: String(r.delayReason), confidence: `${Number(r.delayProbability)}%`, daysUntil: 0, revenueAtRisk: Number(r.revenueAtRisk) })),
    ];
    exportCsv("predictive-analytics", rows);
  }

  if (isLoading) return <LoadingState />;

  return (
    <div className="flex flex-col gap-6 py-6">

      {/* Header */}
      <PageHeader
        eyebrow="AI-Powered Intelligence"
        title="Fleet Intelligence Center"
        description="Predictive risk scoring for maintenance, driver safety, and SLA performance"
        actions={
          <>
            <div className="flex items-center gap-1.5 rounded-full border border-violet-200 bg-violet-50 px-3 py-1.5">
              <Bot className="h-3.5 w-3.5 text-violet-600" />
              <span className="text-xs font-semibold text-violet-700">ML Models Active</span>
            </div>
            <button type="button" className="btn-primary flex items-center gap-2 text-sm" onClick={handleExport}>
              <Download className="h-4 w-4" />Export All Risks
            </button>
          </>
        }
      />

      {/* KPI strip */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4 xl:grid-cols-5">
        {[
          { label: "Revenue at Risk",     value: `AED ${(totalRevenueAtRisk / 1000).toFixed(1)}K`, icon: <DollarSign className="h-5 w-5" />, accent: "text-red-700",    bg: "bg-red-50" },
          { label: "Maintenance Alerts",  value: `${criticalMaint} Critical`,                        icon: <Wrench className="h-5 w-5" />,     accent: "text-amber-700", bg: "bg-amber-50" },
          { label: "Drivers at Risk",     value: `${urgentDrivers} Urgent`,                          icon: <Shield className="h-5 w-5" />,     accent: "text-amber-700", bg: "bg-amber-50" },
          { label: "SLA Breach Risk",     value: `${highSlaRisk} High Risk`,                         icon: <Clock className="h-5 w-5" />,      accent: "text-red-700",   bg: "bg-red-50" },
          { label: "Avg ML Confidence",   value: `${avgConfidence}%`,                                icon: <Activity className="h-5 w-5" />,   accent: "text-violet-700",bg: "bg-violet-50" },
        ].map((k) => (
          <div key={k.label} className="panel flex items-start gap-3 p-4">
            <div className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-xl ${k.bg} ${k.accent}`}>
              {k.icon}
            </div>
            <div>
              <p className={`text-lg font-bold ${k.accent}`}>{k.value}</p>
              <p className="text-xs text-slate-500 font-medium">{k.label}</p>
            </div>
          </div>
        ))}
      </div>

      {/* Charts row */}
      <div className="grid gap-4 xl:grid-cols-[1.6fr_1fr]">

        {/* Risk trend */}
        <div className="panel p-5">
          <p className="section-title mb-0.5">Prediction Risk Trend (Weekly)</p>
          <p className="text-xs text-slate-400 mb-4">Open predictive risk items by category — maintenance, safety and SLA</p>
          <ResponsiveContainer width="100%" height={200}>
            <AreaChart data={RISK_TREND} margin={{ top: 4, right: 8, left: -16, bottom: 0 }}>
              <defs>
                <linearGradient id="maintGrad" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor="#f59e0b" stopOpacity={0.3} /><stop offset="95%" stopColor="#f59e0b" stopOpacity={0} />
                </linearGradient>
                <linearGradient id="safetyGrad" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor="#dc2626" stopOpacity={0.2} /><stop offset="95%" stopColor="#dc2626" stopOpacity={0} />
                </linearGradient>
                <linearGradient id="slaGrad" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor="#0d9488" stopOpacity={0.2} /><stop offset="95%" stopColor="#0d9488" stopOpacity={0} />
                </linearGradient>
              </defs>
              <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
              <XAxis dataKey="week" tick={{ fontSize: 10, fill: "#94a3b8" }} />
              <YAxis tick={{ fontSize: 10, fill: "#94a3b8" }} />
              <Tooltip contentStyle={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: 8 }} />
              <Area type="monotone" dataKey="maintenance" name="Maintenance" stroke="#f59e0b" fill="url(#maintGrad)" strokeWidth={2} />
              <Area type="monotone" dataKey="safety"      name="Safety"      stroke="#dc2626" fill="url(#safetyGrad)" strokeWidth={2} />
              <Area type="monotone" dataKey="sla"         name="SLA"         stroke="#0d9488" fill="url(#slaGrad)" strokeWidth={2} />
            </AreaChart>
          </ResponsiveContainer>
        </div>

        {/* Revenue at risk by category */}
        <div className="panel p-5">
          <p className="section-title mb-0.5">Revenue at Risk by Category</p>
          <p className="text-xs text-slate-400 mb-4">AED × estimated operational exposure</p>
          <ResponsiveContainer width="100%" height={200}>
            <BarChart
              data={[
                { cat: "SLA Breach",   value: Math.round(slaRisk.reduce((s, r) => s + Number(r.revenueAtRisk ?? 0), 0) / 1000) },
                { cat: "Maintenance",  value: Math.round(maintenance.reduce((s, r) => s + Number(r.revenueAtRisk ?? 0), 0) / 1000) },
                { cat: "Driver Risk",  value: Math.round(driverRisk.reduce((s, r) => s + Number(r.revenueImpact ?? 0), 0) / 1000) },
              ]}
              margin={{ top: 4, right: 8, left: -16, bottom: 0 }}
            >
              <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
              <XAxis dataKey="cat" tick={{ fontSize: 10, fill: "#94a3b8" }} />
              <YAxis tick={{ fontSize: 10, fill: "#94a3b8" }} unit="K" />
              <Tooltip formatter={(v: unknown) => [`AED ${Number(v ?? 0)}K`, "At Risk"]} contentStyle={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: 8 }} />
              <ReferenceLine y={0} stroke="#e2e8f0" />
              <Bar dataKey="value" name="AED K" fill="#dc2626" radius={[4, 4, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </div>

      {/* Feed tabs */}
      <div className="flex gap-1.5 p-2">
        {([
          { key: "maintenance", label: "Maintenance Predictions", icon: <Wrench className="h-3.5 w-3.5" />, count: maintenance.length },
          { key: "driver-risk", label: "Driver Safety Risk",      icon: <Shield className="h-3.5 w-3.5" />,  count: driverRisk.length },
          { key: "sla-risk",    label: "SLA Breach Risk",         icon: <Clock className="h-3.5 w-3.5" />,  count: slaRisk.length },
        ] as const).map(({ key, label, icon, count }) => (
          <button key={key} type="button" onClick={() => { setFeed(key); setExpandedId(null); }}
            className={`control-tab flex items-center gap-2 ${feed === key ? "control-tab-active" : ""}`}>
            {icon}{label}
            <span className={`rounded-full px-1.5 py-0.5 text-[10px] font-bold ${feed === key ? "bg-blue-100 text-blue-700" : "bg-slate-100 text-slate-500"}`}>{count}</span>
          </button>
        ))}
      </div>

      {/* ── Maintenance Predictions ── */}
      {feed === "maintenance" && (
        <div className="flex flex-col gap-3">
          {maintenance.map((r) => {
            const id = Number(r.id);
            const open = expandedId === id;
            return (
              <div key={id} className="panel overflow-hidden">
                <button type="button" className="w-full text-left p-4" onClick={() => setExpandedId(open ? null : id)}>
                  <div className="flex items-center gap-4 flex-wrap">
                    <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-amber-50">
                      <Truck className="h-4 w-4 text-amber-600" />
                    </div>
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-2 flex-wrap">
                        <span className="font-semibold text-slate-900">{String(r.vehicleCode)}</span>
                        <PriorityBadge p={String(r.priority)} />
                        <span className="text-xs text-slate-500">{String(r.vehicleType)}</span>
                      </div>
                      <p className="text-sm text-slate-600 mt-0.5">{String(r.failureType)} · {Number(r.daysUntilRisk)} days until predicted risk</p>
                    </div>
                    <div className="flex items-center gap-6 shrink-0">
                      <ConfidenceBar pct={Number(r.confidencePct)} />
                      <div className="text-right">
                        <p className="text-sm font-bold text-red-600">AED {Number(r.estimatedCost).toLocaleString()}</p>
                        <p className="text-[10px] text-slate-400">Est. repair cost</p>
                      </div>
                    </div>
                  </div>
                </button>
                {open && (
                  <div className="px-4 pb-4 border-t border-slate-100 pt-3">
                    <div className="flex items-start gap-2">
                      <Bot className="h-4 w-4 text-violet-500 shrink-0 mt-0.5" />
                      <p className="text-sm text-slate-700 leading-relaxed">{String(r.aiRecommendation)}</p>
                    </div>
                    <div className="mt-3 flex flex-wrap gap-3 text-xs text-slate-500">
                      <span>Last service: <strong>{String(r.lastService)}</strong></span>
                      <span>Revenue at risk: <strong className="text-amber-600">AED {Number(r.revenueAtRisk).toLocaleString()}</strong></span>
                    </div>
                    <div className="mt-3 flex gap-2">
                      <button type="button" className="btn-primary text-xs">Schedule Service</button>
                      <button type="button" className="btn-secondary text-xs">Create Work Order</button>
                      <button type="button" className="btn-ghost text-xs">Dismiss</button>
                    </div>
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}

      {/* ── Driver Safety Risk ── */}
      {feed === "driver-risk" && (
        <div className="flex flex-col gap-3">
          {driverRisk.map((r) => {
            const id = Number(r.id);
            const open = expandedId === id;
            const delta = Number(r.scoreDelta);
            return (
              <div key={id} className="panel overflow-hidden">
                <button type="button" className="w-full text-left p-4" onClick={() => setExpandedId(open ? null : id)}>
                  <div className="flex items-center gap-4 flex-wrap">
                    <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-red-50">
                      <Shield className="h-4 w-4 text-red-500" />
                    </div>
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-2 flex-wrap">
                        <span className="font-semibold text-slate-900">{String(r.driverName)}</span>
                        <PriorityBadge p={String(r.coachingStatus)} />
                        <span className="text-xs text-slate-400">{String(r.driverCode)}</span>
                      </div>
                      <p className="text-sm text-slate-600 mt-0.5">{String(r.riskFactor)}</p>
                    </div>
                    <div className="flex items-center gap-6 shrink-0">
                      <div className="text-right">
                        <p className="text-sm font-bold text-slate-900">{Number(r.currentScore)} → <span className="text-red-600">{Number(r.predictedScore)}</span></p>
                        <p className={`text-[10px] font-semibold ${delta < -8 ? "text-red-600" : delta < -4 ? "text-amber-600" : "text-slate-400"}`}>
                          <TrendingDown className="inline h-3 w-3 mr-0.5" />{delta}pt predicted
                        </p>
                      </div>
                      <div className="text-right">
                        <p className="text-sm font-bold text-amber-700">{Number(r.harshEvents)} events</p>
                        <p className="text-[10px] text-slate-400">this period</p>
                      </div>
                    </div>
                  </div>
                </button>
                {open && (
                  <div className="px-4 pb-4 border-t border-slate-100 pt-3">
                    <div className="flex items-start gap-2">
                      <Bot className="h-4 w-4 text-violet-500 shrink-0 mt-0.5" />
                      <p className="text-sm text-slate-700 leading-relaxed">{String(r.aiRecommendation)}</p>
                    </div>
                    {Number(r.revenueImpact) > 0 && (
                      <p className="mt-2 text-xs text-slate-500">Revenue impact if unaddressed: <strong className="text-amber-600">AED {Number(r.revenueImpact).toLocaleString()}</strong></p>
                    )}
                    <div className="mt-3 flex gap-2">
                      <button type="button" className="btn-primary text-xs">Assign Coaching</button>
                      <button type="button" className="btn-secondary text-xs">View Scorecards</button>
                      <button type="button" className="btn-ghost text-xs">Dismiss</button>
                    </div>
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}

      {/* ── SLA Breach Risk ── */}
      {feed === "sla-risk" && (
        <div className="flex flex-col gap-3">
          {slaRisk.map((r) => {
            const id = Number(r.id);
            const open = expandedId === id;
            const prob = Number(r.delayProbability);
            return (
              <div key={id} className="panel overflow-hidden">
                <button type="button" className="w-full text-left p-4" onClick={() => setExpandedId(open ? null : id)}>
                  <div className="flex items-center gap-4 flex-wrap">
                    <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-teal-50">
                      <Zap className="h-4 w-4 text-teal-600" />
                    </div>
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-2 flex-wrap">
                        <span className="font-semibold text-slate-900">{String(r.jobNumber)}</span>
                        <span className="text-xs text-slate-500">{String(r.customerName)}</span>
                        <span className="text-xs text-slate-400">{String(r.route)}</span>
                      </div>
                      <p className="text-sm text-slate-600 mt-0.5">{String(r.delayReason)}</p>
                    </div>
                    <div className="flex items-center gap-6 shrink-0">
                      <ConfidenceBar pct={prob} />
                      <div className="text-right">
                        <p className="text-sm font-bold text-red-600">AED {Number(r.revenueAtRisk).toLocaleString()}</p>
                        <p className="text-[10px] text-slate-400">at risk</p>
                      </div>
                      <div className="text-right min-w-20">
                        <p className="text-xs text-slate-500">ETA: <strong className="text-slate-700">{String(r.currentEta)}</strong></p>
                        <p className="text-xs text-slate-500">SLA: <strong className="text-red-600">{String(r.slaDeadline)}</strong></p>
                      </div>
                    </div>
                  </div>
                </button>
                {open && (
                  <div className="px-4 pb-4 border-t border-slate-100 pt-3">
                    <div className="flex items-start gap-2">
                      <Bot className="h-4 w-4 text-violet-500 shrink-0 mt-0.5" />
                      <p className="text-sm text-slate-700 leading-relaxed">{String(r.aiRecommendation)}</p>
                    </div>
                    <div className="mt-3 flex gap-2">
                      <button type="button" className="btn-primary text-xs">Alert Customer</button>
                      <button type="button" className="btn-secondary text-xs">Reroute</button>
                      <button type="button" className="btn-secondary text-xs">Reassign Vehicle</button>
                      <button type="button" className="btn-ghost text-xs">Dismiss</button>
                    </div>
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}

      {/* Empty alert */}
      {(feed === "maintenance" && maintenance.length === 0) ||
       (feed === "driver-risk" && driverRisk.length === 0)  ||
       (feed === "sla-risk"    && slaRisk.length === 0) ? (
        <div className="panel p-10 flex flex-col items-center gap-3 text-center">
          <AlertTriangle className="h-8 w-8 text-slate-300" />
          <p className="text-slate-500 text-sm">No predictions available at this time.</p>
        </div>
      ) : null}

    </div>
  );
}

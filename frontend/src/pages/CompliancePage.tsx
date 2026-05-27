import { useState } from "react";
import { AlertTriangle, CheckCircle, FileText, Globe, Package, Shield, ShieldAlert, Wifi, WifiOff, X, Zap } from "lucide-react";
import {
  useAcknowledgeViolation,
  useAuditPackages,
  useComplianceAiRecs,
  useComplianceSummary,
  useComplianceViolations,
  useCrossBorderWatch,
  useDriverComplianceStatus,
  useFinalizeAuditPackage,
  useResolveViolation,
  useVehicleComplianceStatus,
} from "@/hooks/useBatch6";
import { useI18n } from "@/i18n";
import type { AnyRecord } from "@/types";
import { formatDate } from "@/utils/formatters";

type TabId = "overview" | "violations" | "drivers" | "vehicles" | "documents" | "audit" | "cross-border" | "ai";

const TABS: { id: TabId; label: string }[] = [
  { id: "overview",     label: "Overview" },
  { id: "violations",   label: "Violations" },
  { id: "drivers",      label: "Driver Status" },
  { id: "vehicles",     label: "Vehicle Status" },
  { id: "audit",        label: "Audit Packages" },
  { id: "cross-border", label: "Cross-Border" },
  { id: "ai",           label: "AI Advisor" },
];

const SEV_COLOR: Record<string, string> = {
  Critical: "text-red-400 bg-red-400/10 border-red-400/20",
  High:     "text-amber-400 bg-amber-400/10 border-amber-400/20",
  Medium:   "text-yellow-400 bg-yellow-400/10 border-yellow-400/20",
  Low:      "text-sky-400 bg-sky-400/10 border-sky-400/20",
};

const STATUS_COLOR: Record<string, string> = {
  Compliant:    "text-emerald-400",
  Warning:      "text-amber-400",
  Violation:    "text-red-400",
  Open:         "text-red-400",
  Acknowledged: "text-amber-400",
  Resolved:     "text-emerald-400",
  Escalated:    "text-red-500",
  "Under Review": "text-sky-400",
  Active:       "text-emerald-400",
  Malfunction:  "text-red-400",
  Diagnostic:   "text-amber-400",
};

function StatusDot({ status }: { status: string }) {
  const col = STATUS_COLOR[status] ?? "text-slate-400";
  return <span className={`inline-flex items-center gap-1.5 text-xs font-semibold ${col}`}><span className="h-1.5 w-1.5 rounded-full bg-current" />{status}</span>;
}

function SeverityBadge({ severity }: { severity: string }) {
  return <span className={`rounded border px-1.5 py-0.5 text-[10px] font-bold uppercase tracking-wide ${SEV_COLOR[severity] ?? "text-slate-400 border-white/10"}`}>{severity}</span>;
}

function CountryBadge({ code }: { code: string }) {
  const labels: Record<string, string> = { US: "🇺🇸 US", CA: "🇨🇦 CA", SA: "🇸🇦 SA", AE: "🇦🇪 AE", PK: "🇵🇰 PK" };
  return <span className="rounded border border-white/10 bg-white/[0.04] px-1.5 py-0.5 text-[10px] font-bold text-slate-300">{labels[code] ?? code}</span>;
}

function Disclaimer() {
  return (
    <div className="rounded-xl border border-amber-400/20 bg-amber-400/5 p-3 text-[11px] text-amber-200/80 leading-relaxed">
      <span className="font-bold text-amber-300">Disclaimer: </span>
      OpsTrax provides compliance management, monitoring, and audit-readiness tools. Final regulatory compliance remains the carrier&apos;s responsibility. ELD certification depends on the connected ELD provider/device and applicable country requirements.
    </div>
  );
}

export function CompliancePage() {
  const { t } = useI18n();
  const [tab, setTab] = useState<TabId>("overview");
  const [drawer, setDrawer] = useState<AnyRecord | null>(null);

  const summaryQ     = useComplianceSummary();
  const violationsQ  = useComplianceViolations();
  const driversQ     = useDriverComplianceStatus();
  const vehiclesQ    = useVehicleComplianceStatus();
  const auditQ       = useAuditPackages();
  const crossQ       = useCrossBorderWatch();
  const aiQ          = useComplianceAiRecs();

  const ackMut       = useAcknowledgeViolation();
  const resolveMut   = useResolveViolation();
  const finalizeMut  = useFinalizeAuditPackage();

  const summary = summaryQ.data as AnyRecord | undefined;

  // KPI aggregates from summary
  const violationRows = (summary?.violations as AnyRecord[] | undefined) ?? [];
  const criticalOpen  = violationRows.filter(r => String(r.severity) === "Critical" && String(r.status) === "Open").reduce((s, r) => s + Number(r.cnt ?? 0), 0);
  const totalOpen     = violationRows.filter(r => ["Open","Escalated","Under Review"].includes(String(r.status))).reduce((s, r) => s + Number(r.cnt ?? 0), 0);
  const driverRows    = (summary?.drivers as AnyRecord[] | undefined) ?? [];
  const vehicleRows   = (summary?.vehicles as AnyRecord[] | undefined) ?? [];
  const eldRows       = (summary?.elDevices as AnyRecord[] | undefined) ?? [];
  const driverViolationCount = driverRows.filter(r => String(r.overall_status) === "Violation").reduce((s, r) => s + Number(r.cnt ?? 0), 0);
  const vehicleViolationCount = vehicleRows.filter(r => String(r.overall_status) === "Violation").reduce((s, r) => s + Number(r.cnt ?? 0), 0);
  const eldMalfunctions = eldRows.filter(r => String(r.status) === "Malfunction").reduce((s, r) => s + Number(r.cnt ?? 0), 0);

  const violations  = (violationsQ.data as AnyRecord[] | undefined) ?? [];
  const drivers     = (driversQ.data as AnyRecord[] | undefined) ?? [];
  const vehicles    = (vehiclesQ.data as AnyRecord[] | undefined) ?? [];
  const audits      = (auditQ.data as AnyRecord[] | undefined) ?? [];
  const crossBorder = (crossQ.data as AnyRecord[] | undefined) ?? [];
  const aiRecs      = (aiQ.data as AnyRecord[] | undefined) ?? [];

  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-extrabold text-white flex items-center gap-2"><Shield className="h-5 w-5 text-emerald-400" />{t("compliance_center")}</h1>
          <p className="text-xs text-slate-500 mt-0.5">Multi-country compliance monitoring, HOS tracking, and audit-readiness</p>
        </div>
        <div className="flex items-center gap-2">
          {(summary?.countries as AnyRecord[] | undefined)?.map(c => (
            <CountryBadge key={String(c.code)} code={String(c.code)} />
          ))}
        </div>
      </div>

      <Disclaimer />

      {/* KPI strip */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        {[
          { label: "Critical Open", value: criticalOpen, icon: ShieldAlert, color: criticalOpen > 0 ? "text-red-400" : "text-emerald-400", bg: criticalOpen > 0 ? "from-red-400/10" : "from-emerald-400/10" },
          { label: "Total Open Violations", value: totalOpen, icon: AlertTriangle, color: totalOpen > 0 ? "text-amber-400" : "text-emerald-400", bg: "from-amber-400/10" },
          { label: "Driver Violations", value: driverViolationCount, icon: Shield, color: driverViolationCount > 0 ? "text-red-400" : "text-emerald-400", bg: "from-sky-400/10" },
          { label: "ELD Malfunctions", value: eldMalfunctions, icon: WifiOff, color: eldMalfunctions > 0 ? "text-red-400" : "text-emerald-400", bg: "from-violet-400/10" },
        ].map(kpi => (
          <div key={kpi.label} className={`panel bg-gradient-to-br ${kpi.bg} to-transparent`}>
            <div className="flex items-center justify-between mb-1">
              <p className="text-[11px] text-slate-500 uppercase tracking-wide font-semibold">{kpi.label}</p>
              <kpi.icon className={`h-4 w-4 ${kpi.color}`} />
            </div>
            <p className={`text-2xl font-extrabold ${kpi.color}`}>{summaryQ.isLoading ? "—" : kpi.value}</p>
          </div>
        ))}
      </div>

      {/* Tabs */}
      <div className="flex gap-0.5 border-b border-white/[0.07] overflow-x-auto pb-0">
        {TABS.map(t2 => (
          <button
            key={t2.id}
            onClick={() => setTab(t2.id)}
            className={`px-3 py-2 text-[12px] font-semibold whitespace-nowrap transition-colors border-b-2 -mb-px ${
              tab === t2.id
                ? "border-emerald-400 text-emerald-300"
                : "border-transparent text-slate-500 hover:text-slate-300"
            }`}
          >
            {t2.label}
          </button>
        ))}
      </div>

      {/* Tab content */}
      {tab === "overview" && (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
          {/* Compliance profiles */}
          <div className="panel space-y-2">
            <p className="section-title flex items-center gap-2"><Globe className="h-3.5 w-3.5 text-emerald-400" />Active Compliance Profiles</p>
            {(summary?.profiles as AnyRecord[] | undefined)?.map(p => (
              <div key={String(p.id)} className="flex items-center justify-between rounded-lg bg-white/[0.03] px-3 py-2">
                <div>
                  <p className="text-sm font-semibold text-white">{String(p.profile_name)}</p>
                  <p className="text-xs text-slate-500">{String(p.authority ?? "—")} · {String(p.hos_ruleset ?? "—")}</p>
                </div>
                <div className="text-end">
                  <CountryBadge code={String(p.country_code)} />
                  {!!p.eld_required && <p className="text-[10px] text-amber-400 mt-0.5">ELD Required</p>}
                </div>
              </div>
            ))}
          </div>

          {/* Recent violations */}
          <div className="panel space-y-2">
            <p className="section-title flex items-center gap-2"><ShieldAlert className="h-3.5 w-3.5 text-red-400" />Recent Violations</p>
            {violations.slice(0, 5).map(v => (
              <div key={String(v.id)} className="flex items-start gap-2 rounded-lg bg-white/[0.03] px-3 py-2 cursor-pointer hover:bg-white/[0.06]" onClick={() => setDrawer(v)}>
                <SeverityBadge severity={String(v.severity)} />
                <div className="min-w-0 flex-1">
                  <p className="text-xs text-slate-200 truncate">{String(v.description ?? v.violation_code)}</p>
                  <p className="text-[11px] text-slate-500">{String(v.violation_code)} · <StatusDot status={String(v.status)} /></p>
                </div>
                <CountryBadge code={String(v.country_code)} />
              </div>
            ))}
          </div>
        </div>
      )}

      {tab === "violations" && (
        <div className="panel overflow-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-white/[0.07] text-left">
                {["Code","Severity","Category","Driver","Vehicle","Status","Detected",""].map(h => (
                  <th key={h} className="pb-2 pr-4 text-[10px] font-bold uppercase tracking-wide text-slate-500">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-white/[0.04]">
              {violations.map(v => (
                <tr key={String(v.id)} className="hover:bg-white/[0.03] cursor-pointer" onClick={() => setDrawer(v)}>
                  <td className="py-2 pr-4 font-mono text-xs text-teal-300">{String(v.violation_code)}</td>
                  <td className="py-2 pr-4"><SeverityBadge severity={String(v.severity)} /></td>
                  <td className="py-2 pr-4 text-slate-300">{String(v.category)}</td>
                  <td className="py-2 pr-4 text-slate-300">{String(v.driver_name ?? "—")}</td>
                  <td className="py-2 pr-4 text-slate-300">{String(v.vehicle_code ?? "—")}</td>
                  <td className="py-2 pr-4"><StatusDot status={String(v.status)} /></td>
                  <td className="py-2 pr-4 text-xs text-slate-500">{formatDate(String(v.detected_at))}</td>
                  <td className="py-2">
                    <div className="flex gap-1">
                      {String(v.status) === "Open" && (
                        <button className="rounded border border-amber-400/20 bg-amber-400/10 px-2 py-0.5 text-[10px] text-amber-300 hover:bg-amber-400/20" onClick={e => { e.stopPropagation(); ackMut.mutate(Number(v.id)); }}>Ack</button>
                      )}
                      {["Open","Acknowledged","Under Review"].includes(String(v.status)) && (
                        <button className="rounded border border-emerald-400/20 bg-emerald-400/10 px-2 py-0.5 text-[10px] text-emerald-300 hover:bg-emerald-400/20" onClick={e => { e.stopPropagation(); resolveMut.mutate(Number(v.id)); }}>Resolve</button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {tab === "drivers" && (
        <div className="panel overflow-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-white/[0.07] text-left">
                {["Driver","Country","Status","License Expiry","Med Cert","Drug Test","HOS","Violations"].map(h => (
                  <th key={h} className="pb-2 pr-4 text-[10px] font-bold uppercase tracking-wide text-slate-500">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-white/[0.04]">
              {drivers.map(d => (
                <tr key={String(d.id)} className="hover:bg-white/[0.03]">
                  <td className="py-2 pr-4">
                    <p className="font-semibold text-white">{String(d.driver_name)}</p>
                    <p className="text-[11px] text-slate-500">{String(d.driver_code)}</p>
                  </td>
                  <td className="py-2 pr-4"><CountryBadge code={String(d.country_code)} /></td>
                  <td className="py-2 pr-4"><StatusDot status={String(d.overall_status)} /></td>
                  <td className="py-2 pr-4 text-xs text-slate-300">{formatDate(String(d.license_expiry ?? ""))}</td>
                  <td className="py-2 pr-4"><span className={Number(d.medical_cert_valid) ? "text-emerald-400 text-xs" : "text-red-400 text-xs"}>{Number(d.medical_cert_valid) ? "✓" : "✗"} {formatDate(String(d.medical_cert_expiry ?? ""))}</span></td>
                  <td className="py-2 pr-4"><span className={Number(d.drug_test_valid) ? "text-emerald-400 text-xs" : "text-red-400 text-xs"}>{Number(d.drug_test_valid) ? "✓" : "✗"} {formatDate(String(d.drug_test_expiry ?? ""))}</span></td>
                  <td className="py-2 pr-4"><StatusDot status={String(d.hos_status)} /></td>
                  <td className="py-2 pr-4 text-center">
                    <span className={`font-bold ${Number(d.violation_count) > 0 ? "text-red-400" : "text-emerald-400"}`}>{String(d.violation_count)}</span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {tab === "vehicles" && (
        <div className="panel overflow-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-white/[0.07] text-left">
                {["Vehicle","Country","Status","Reg. Expiry","Insurance","Inspection","ELD","Violations"].map(h => (
                  <th key={h} className="pb-2 pr-4 text-[10px] font-bold uppercase tracking-wide text-slate-500">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-white/[0.04]">
              {vehicles.map(v => (
                <tr key={String(v.id)} className="hover:bg-white/[0.03]">
                  <td className="py-2 pr-4">
                    <p className="font-semibold text-white">{String(v.vehicle_code)}</p>
                    <p className="text-[11px] text-slate-500">{String(v.vehicle_type ?? "")}</p>
                  </td>
                  <td className="py-2 pr-4"><CountryBadge code={String(v.country_code)} /></td>
                  <td className="py-2 pr-4"><StatusDot status={String(v.overall_status)} /></td>
                  <td className="py-2 pr-4 text-xs text-slate-300">{formatDate(String(v.registration_expiry ?? ""))}</td>
                  <td className="py-2 pr-4"><span className={Number(v.insurance_valid) ? "text-emerald-400 text-xs" : "text-red-400 text-xs"}>{Number(v.insurance_valid) ? "✓" : "✗"} {formatDate(String(v.insurance_expiry ?? ""))}</span></td>
                  <td className="py-2 pr-4"><span className={Number(v.inspection_valid) ? "text-emerald-400 text-xs" : "text-red-400 text-xs"}>{Number(v.inspection_valid) ? "✓" : "✗"} {formatDate(String(v.inspection_expiry ?? ""))}</span></td>
                  <td className="py-2 pr-4">
                    {Number(v.eld_installed) ? <span className="text-emerald-400 text-xs flex items-center gap-1"><Wifi className="h-3 w-3" />Installed</span> : <span className="text-slate-500 text-xs flex items-center gap-1"><WifiOff className="h-3 w-3" />None</span>}
                  </td>
                  <td className="py-2 pr-4 text-center">
                    <span className={`font-bold ${Number(v.violation_count) > 0 ? "text-red-400" : "text-emerald-400"}`}>{String(v.violation_count)}</span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {tab === "audit" && (
        <div className="space-y-3">
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {audits.map(a => (
              <div key={String(a.id)} className="panel space-y-3">
                <div className="flex items-start justify-between">
                  <div>
                    <p className="font-mono text-xs text-teal-300">{String(a.package_code)}</p>
                    <p className="text-sm font-semibold text-white mt-0.5">{String(a.profile_name ?? "Fleet-Wide")}</p>
                  </div>
                  <div className="flex flex-col items-end gap-1">
                    <CountryBadge code={String(a.country_code)} />
                    <StatusDot status={String(a.status)} />
                  </div>
                </div>
                <div className="grid grid-cols-3 gap-2 text-center">
                  {[
                    { label: "Drivers",    value: a.included_drivers },
                    { label: "Vehicles",   value: a.included_vehicles },
                    { label: "HOS Logs",   value: a.hos_logs_count },
                  ].map(m => (
                    <div key={m.label} className="rounded bg-white/[0.03] py-1.5">
                      <p className="text-base font-bold text-white">{String(m.value)}</p>
                      <p className="text-[10px] text-slate-500">{m.label}</p>
                    </div>
                  ))}
                </div>
                <p className="text-[11px] text-slate-500">{formatDate(String(a.date_range_start))} – {formatDate(String(a.date_range_end))}</p>
                {!!a.notes && <p className="text-xs text-slate-400 leading-snug">{String(a.notes)}</p>}
                {String(a.status) === "Draft" || String(a.status) === "In Progress" ? (
                  <button
                    className="w-full rounded-lg border border-emerald-400/20 bg-emerald-400/10 py-1.5 text-xs font-semibold text-emerald-300 hover:bg-emerald-400/20 transition"
                    onClick={() => finalizeMut.mutate(Number(a.id))}
                  >
                    <Package className="h-3 w-3 inline-block mr-1" />Finalize Package
                  </button>
                ) : (
                  <div className="rounded-lg border border-emerald-400/20 bg-emerald-400/5 py-1.5 text-center text-xs font-semibold text-emerald-300">
                    <CheckCircle className="h-3 w-3 inline-block mr-1" />{String(a.status)}
                  </div>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      {tab === "cross-border" && (
        <div className="space-y-3">
          <div className="panel">
            <p className="section-title mb-3 flex items-center gap-2"><Globe className="h-3.5 w-3.5 text-sky-400" />Cross-Border Compliance Watch</p>
            <p className="text-xs text-slate-500 mb-4">Active violations and open items spanning multiple country compliance frameworks.</p>
            <div className="space-y-2">
              {crossBorder.length === 0 && <p className="text-sm text-slate-500">No cross-border issues found.</p>}
              {crossBorder.map(v => (
                <div key={String(v.id)} className="flex items-start gap-3 rounded-lg border border-white/[0.07] bg-white/[0.03] px-3 py-3">
                  <SeverityBadge severity={String(v.severity)} />
                  <div className="min-w-0 flex-1">
                    <p className="text-sm text-slate-200">{String(v.description)}</p>
                    <p className="text-xs text-slate-500 mt-0.5">{String(v.violation_code)} · {String(v.profile_name ?? "—")} · {String(v.authority ?? "—")}</p>
                  </div>
                  <div className="flex flex-col items-end gap-1 flex-shrink-0">
                    <CountryBadge code={String(v.country_code)} />
                    <StatusDot status={String(v.status)} />
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      {tab === "ai" && (
        <div className="space-y-3">
          <div className="panel">
            <p className="section-title mb-1 flex items-center gap-2"><Zap className="h-3.5 w-3.5 text-violet-400" />Compliance AI Advisor</p>
            <p className="text-xs text-slate-500 mb-4">AI-generated compliance recommendations based on live fleet data. Not a substitute for legal counsel.</p>
            <div className="space-y-3">
              {aiRecs.map((rec, i) => (
                <div key={i} className="rounded-xl border border-white/[0.07] bg-white/[0.03] p-4 space-y-1.5">
                  <div className="flex items-start justify-between gap-2">
                    <p className="text-sm font-semibold text-white">{String(rec.title)}</p>
                    <SeverityBadge severity={String(rec.priority)} />
                  </div>
                  <p className="text-xs text-slate-400 leading-relaxed">{String(rec.description)}</p>
                  {!!rec.action_label && (
                    <button className="mt-1 rounded border border-violet-400/25 bg-violet-400/10 px-3 py-1 text-xs font-semibold text-violet-300 hover:bg-violet-400/20 transition">
                      {String(rec.action_label)}
                    </button>
                  )}
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      {/* Violation drawer */}
      {drawer && (
        <div className="fixed inset-0 z-50 flex justify-end anim-fade-in">
          <div className="absolute inset-0 bg-black/60 backdrop-blur-sm" onClick={() => setDrawer(null)} />
          <aside className="anim-slide-left relative z-10 w-full max-w-md overflow-y-auto bg-slate-900 border-s border-white/[0.09] p-5 shadow-2xl">
            <div className="flex items-center justify-between mb-4">
              <p className="font-semibold text-white flex items-center gap-2"><FileText className="h-4 w-4 text-emerald-400" />Violation Details</p>
              <button className="icon-btn" onClick={() => setDrawer(null)}><X className="h-4 w-4" /></button>
            </div>
            <div className="space-y-3 text-sm">
              <div className="flex items-center gap-2 flex-wrap">
                <span className="font-mono text-teal-300 text-xs">{String(drawer.violation_code)}</span>
                <SeverityBadge severity={String(drawer.severity)} />
                <StatusDot status={String(drawer.status)} />
                <CountryBadge code={String(drawer.country_code)} />
              </div>
              <p className="text-slate-300 leading-relaxed">{String(drawer.description ?? "—")}</p>
              {[
                { label: "Category",    value: drawer.category },
                { label: "Driver",      value: drawer.driver_name },
                { label: "Vehicle",     value: drawer.vehicle_code },
                { label: "Profile",     value: drawer.profile_name },
                { label: "Detected",    value: formatDate(String(drawer.detected_at ?? "")) },
                { label: "Resolved",    value: drawer.resolved_at ? formatDate(String(drawer.resolved_at)) : null },
              ].filter(r => r.value).map(r => (
                <div key={r.label} className="flex justify-between border-b border-white/[0.05] pb-2">
                  <span className="text-slate-500">{r.label}</span>
                  <span className="text-slate-200">{String(r.value)}</span>
                </div>
              ))}
              <div className="flex gap-2 pt-2">
                {String(drawer.status) === "Open" && (
                  <button className="flex-1 rounded-lg border border-amber-400/20 bg-amber-400/10 py-2 text-xs font-semibold text-amber-300 hover:bg-amber-400/20" onClick={() => { ackMut.mutate(Number(drawer.id)); setDrawer(null); }}>Acknowledge</button>
                )}
                {["Open","Acknowledged","Under Review"].includes(String(drawer.status)) && (
                  <button className="flex-1 rounded-lg border border-emerald-400/20 bg-emerald-400/10 py-2 text-xs font-semibold text-emerald-300 hover:bg-emerald-400/20" onClick={() => { resolveMut.mutate(Number(drawer.id)); setDrawer(null); }}>Resolve</button>
                )}
              </div>
            </div>
          </aside>
        </div>
      )}
    </div>
  );
}

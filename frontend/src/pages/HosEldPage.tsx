import { useState } from "react";
import {
  Activity, AlertTriangle, CheckCircle, Clock, Cpu, FileText,
  Radio, Shield, Sparkles, WifiOff, X, Zap,
} from "lucide-react";
import {
  useCertifyHosLog,
  useEldDevices,
  useHosAiRecs,
  useHosDrivers,
  useHosLogs,
  useMarkEldMalfunction,
  useResolveEldMalfunction,
} from "@/hooks/useBatch6";
import { useI18n } from "@/i18n";
import type { AnyRecord } from "@/types";
import { formatDate, formatDateTime, formatMinutesAsClock } from "@/utils/formatters";

type TabId = "drivers" | "logs" | "eld" | "ai";

const TABS: { id: TabId; label: string; icon: typeof Clock }[] = [
  { id: "drivers", label: "Driver Clocks", icon: Clock },
  { id: "logs",    label: "HOS Logs", icon: FileText },
  { id: "eld",     label: "ELD Devices", icon: Radio },
  { id: "ai",      label: "Recommendations", icon: Zap },
];

const CLOCK_STATUS_COLOR: Record<string, string> = {
  OK:        "text-emerald-700",
  Warning:   "text-amber-700",
  Violation: "text-red-700",
};

const ELD_STATUS_COLOR: Record<string, string> = {
  Active:      "text-emerald-700",
  Diagnostic:  "text-amber-700",
  Malfunction: "text-red-700",
};

function ClockBar({ value, max, status }: { value: number; max: number; status: string }) {
  const pct = Math.max(0, Math.min(100, (value / max) * 100));
  const color = status === "Violation" ? "bg-red-500" : status === "Warning" ? "bg-amber-500" : "bg-emerald-500";
  return (
    <div className="h-2 w-full rounded-full bg-slate-100 overflow-hidden">
      <div className={`h-full rounded-full transition-all ${color}`} style={{ width: `${pct}%` }} />
    </div>
  );
}

function CountryBadge({ code }: { code: string }) {
  const labels: Record<string, string> = { US: "🇺🇸 US", CA: "🇨🇦 CA", SA: "🇸🇦 SA", AE: "🇦🇪 AE", PK: "🇵🇰 PK" };
  return <span className="rounded-lg border border-slate-200 bg-slate-50 px-2 py-0.5 text-[10px] font-bold text-slate-700">{labels[code] ?? code}</span>;
}

function Disclaimer() {
  return (
    <div className="flex items-start gap-3 rounded-2xl border border-amber-200 bg-amber-50 p-4">
      <AlertTriangle className="h-4 w-4 shrink-0 text-amber-500 mt-0.5" />
      <p className="text-xs text-amber-700 leading-relaxed">
        <span className="font-bold text-amber-800">Disclaimer: </span>
        OpsTrax provides HOS monitoring and ELD framework tools. Final HOS compliance remains the carrier&apos;s and driver&apos;s responsibility. OpsTrax is not a certified ELD. ELD certification depends on the connected ELD provider/device and applicable country requirements.
      </p>
    </div>
  );
}

export function HosEldPage() {
  const { t } = useI18n();
  const [tab, setTab] = useState<TabId>("drivers");
  const [drawer, setDrawer] = useState<AnyRecord | null>(null);
  const [malfForm, setMalfForm] = useState<{ id: number; code: string; desc: string } | null>(null);

  const driversQ    = useHosDrivers();
  const logsQ       = useHosLogs();
  const eldQ        = useEldDevices();
  const aiQ         = useHosAiRecs();

  const certifyMut     = useCertifyHosLog();
  const markMalfMut    = useMarkEldMalfunction();
  const resolveMalfMut = useResolveEldMalfunction();

  const drivers = (driversQ.data as AnyRecord[] | undefined) ?? [];
  const logs    = (logsQ.data  as AnyRecord[] | undefined) ?? [];
  const elds    = (eldQ.data   as AnyRecord[] | undefined) ?? [];
  const aiRecs  = (aiQ.data    as AnyRecord[] | undefined) ?? [];

  const okCount        = drivers.filter(d => String(d.status) === "OK").length;
  const warningCount   = drivers.filter(d => String(d.status) === "Warning").length;
  const violationCount = drivers.filter(d => String(d.status) === "Violation").length;
  const eldMalfCount   = elds.filter(e => String(e.status) === "Malfunction").length;

  const logColors: Record<string, string> = {
    "Driving":               "bg-emerald-500",
    "On Duty (Not Driving)": "bg-amber-500",
    "Off Duty":              "bg-slate-400",
    "Sleeper Berth":         "bg-blue-500",
  };

  return (
    <div className="flex flex-col gap-6 py-6">
      {/* Hero header */}
      <div className="fh-hero flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.2em] text-teal-300/80">Compliance</p>
          <h1 className="mt-1 text-2xl font-extrabold text-white flex items-center gap-2">
            <Clock className="h-6 w-6 text-amber-300" />{t("hos_eld")}
          </h1>
          <p className="mt-1 max-w-2xl text-sm text-slate-300">
            Hours of service monitoring, driver clocks, ELD device tracking and AI-powered compliance recommendations
          </p>
        </div>
      </div>

      <Disclaimer />

      {/* KPI cards */}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {[
          { label: "HOS OK",          value: okCount,        color: "text-emerald-600", icon: <CheckCircle className="h-5 w-5" />, iconBg: "bg-emerald-50 text-emerald-500" },
          { label: "HOS Warning",     value: warningCount,   color: "text-amber-600",   icon: <AlertTriangle className="h-5 w-5" />, iconBg: "bg-amber-50 text-amber-500" },
          { label: "HOS Violation",   value: violationCount, color: violationCount > 0 ? "text-red-600" : "text-slate-500",   icon: <AlertTriangle className="h-5 w-5" />, iconBg: violationCount > 0 ? "bg-red-50 text-red-500" : "bg-slate-50 text-slate-400" },
          { label: "ELD Malfunction", value: eldMalfCount,   color: eldMalfCount   > 0 ? "text-red-600" : "text-slate-500",   icon: <WifiOff className="h-5 w-5" />, iconBg: eldMalfCount > 0 ? "bg-red-50 text-red-500" : "bg-slate-50 text-slate-400" },
        ].map(kpi => (
          <div key={kpi.label} className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
            <div className="flex items-center justify-between mb-2">
              <span className={`grid h-9 w-9 place-items-center rounded-xl ${kpi.iconBg}`}>{kpi.icon}</span>
            </div>
            <p className={`text-2xl font-bold ${kpi.color}`}>{driversQ.isLoading ? "—" : kpi.value}</p>
            <p className="text-xs text-slate-500 font-medium mt-0.5">{kpi.label}</p>
          </div>
        ))}
      </div>

      {/* Compliance signal bar */}
      <div className="relative flex flex-col gap-4 overflow-hidden rounded-2xl border border-slate-700/20 bg-gradient-to-br from-slate-900 via-slate-800 to-slate-900 p-5 text-white shadow-xl sm:flex-row sm:items-center sm:justify-between">
        <div className="absolute -right-8 -top-8 h-32 w-32 rounded-full bg-amber-500/10 blur-2xl" />
        <div className="absolute -bottom-6 left-1/3 h-24 w-24 rounded-full bg-teal-500/8 blur-2xl" />
        <div className="relative flex items-center gap-4">
          <span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-amber-400/20 to-amber-600/10 ring-1 ring-amber-400/20">
            <Shield className="h-5 w-5 text-amber-300" />
          </span>
          <div>
            <p className="text-[10px] font-bold uppercase tracking-[0.22em] text-amber-300/80">Compliance status</p>
            <p className="mt-0.5 text-sm font-medium text-slate-200">
              {violationCount === 0 && eldMalfCount === 0
                ? "All drivers within HOS limits — no ELD malfunctions detected."
                : `${violationCount > 0 ? `${violationCount} HOS violation${violationCount > 1 ? "s" : ""}` : ""}${violationCount > 0 && eldMalfCount > 0 ? " · " : ""}${eldMalfCount > 0 ? `${eldMalfCount} ELD malfunction${eldMalfCount > 1 ? "s" : ""}` : ""} need attention`}
            </p>
          </div>
        </div>
        {violationCount > 0 && (
          <button
            type="button"
            onClick={() => setTab("drivers")}
            className="inline-flex items-center gap-1.5 self-start rounded-lg bg-gradient-to-r from-amber-500 to-amber-600 px-3.5 py-2 text-xs font-bold text-white shadow-md shadow-amber-500/20 transition hover:shadow-lg cursor-pointer sm:self-auto"
          >
            Review violations <AlertTriangle className="h-3.5 w-3.5" />
          </button>
        )}
      </div>

      {/* Tabs */}
      <div className="flex items-center gap-2 rounded-2xl border border-slate-200 bg-white p-2 shadow-sm">
        {TABS.map((t2) => {
          const Icon = t2.icon;
          return (
            <button
              key={t2.id}
              type="button"
              onClick={() => setTab(t2.id)}
              className={`flex items-center gap-1.5 rounded-xl px-4 py-2 text-sm font-semibold transition cursor-pointer ${
                tab === t2.id
                  ? "bg-teal-50 text-teal-700 shadow-sm ring-1 ring-teal-200/60"
                  : "text-slate-500 hover:bg-slate-50 hover:text-slate-700"
              }`}
            >
              <Icon className="h-4 w-4" /> {t2.label}
            </button>
          );
        })}
      </div>

      {/* Driver Clocks */}
      {tab === "drivers" && (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {drivers.map(d => {
            const driveRem = Number(d.drive_time_remaining_minutes ?? 660);
            const shiftRem = Number(d.shift_time_remaining_minutes ?? 840);
            const cycleRem = Number(d.cycle_time_remaining_minutes ?? 4200);
            const st       = String(d.status ?? "OK");
            const stColor  = CLOCK_STATUS_COLOR[st] ?? "text-slate-500";

            return (
              <div key={String(d.id)} className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm cursor-pointer transition hover:border-amber-300 hover:shadow-md" onClick={() => setDrawer(d)}>
                <div className="flex items-start justify-between">
                  <div>
                    <p className="font-semibold text-slate-900">{String(d.driver_name)}</p>
                    <p className="text-[11px] text-slate-500">{String(d.driver_code)} · {String(d.cycle_type)}</p>
                  </div>
                  <div className="flex flex-col items-end gap-1">
                    <CountryBadge code={String(d.country_code)} />
                    <span className={`text-xs font-bold ${stColor}`}>{st}</span>
                  </div>
                </div>
                {!!d.hos_warning && (
                  <div className="mt-2 flex items-center gap-1.5 rounded-lg bg-amber-50 border border-amber-200 px-2.5 py-1.5 text-[11px] text-amber-700">
                    <AlertTriangle className="h-3 w-3 shrink-0" />{String(d.hos_warning)}
                  </div>
                )}
                <div className="mt-3 space-y-2.5">
                  {[
                    { label: "Drive Remaining", value: driveRem, max: 660  },
                    { label: "Shift Remaining", value: shiftRem, max: 840  },
                    { label: "Cycle Remaining", value: cycleRem, max: 4200 },
                  ].map(bar => (
                    <div key={bar.label}>
                      <div className="flex justify-between text-[10px] mb-1">
                        <span className="text-slate-500 font-medium">{bar.label}</span>
                        <span className="text-slate-700 font-mono font-semibold">{formatMinutesAsClock(bar.value)}</span>
                      </div>
                      <ClockBar value={bar.value} max={bar.max} status={st} />
                    </div>
                  ))}
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* HOS Logs */}
      {tab === "logs" && (
        <div className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50/80">
                  {["Driver","Date","Status","Start","End","Duration","Location","Certified"].map(h => (
                    <th key={h} className="text-left px-4 py-3 text-[10px] font-semibold uppercase tracking-wide text-slate-500">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {logs.map(l => (
                  <tr key={String(l.id)} className="hover:bg-slate-50 transition-colors">
                    <td className="px-4 py-3">
                      <p className="font-semibold text-slate-900 text-xs">{String(l.driver_name)}</p>
                      <p className="text-[10px] text-slate-500">{String(l.driver_code)}</p>
                    </td>
                    <td className="px-4 py-3 text-xs text-slate-700">{formatDate(String(l.log_date))}</td>
                    <td className="px-4 py-3">
                      <span className="flex items-center gap-1.5 text-xs font-semibold text-slate-700">
                        <span className={`h-2 w-2 rounded-full shrink-0 ${logColors[String(l.status)] ?? "bg-slate-400"}`} />
                        {String(l.status)}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-xs text-slate-600 font-mono">{String(l.start_time ?? "").substring(11, 16)}</td>
                    <td className="px-4 py-3 text-xs text-slate-600 font-mono">{String(l.end_time ?? "").substring(11, 16)}</td>
                    <td className="px-4 py-3 text-xs text-slate-700">{String(l.duration_minutes)}m</td>
                    <td className="px-4 py-3 text-xs text-slate-600 truncate max-w-[120px]">{String(l.location ?? "—")}</td>
                    <td className="px-4 py-3">
                      {Number(l.is_certified) ? (
                        <span className="flex items-center gap-1 text-xs font-medium text-emerald-700"><CheckCircle className="h-3 w-3" /> Certified</span>
                      ) : (
                        <button
                          type="button"
                          className="rounded-lg border border-teal-200 bg-teal-50 px-2.5 py-1 text-[10px] font-semibold text-teal-700 transition hover:bg-teal-100 cursor-pointer"
                          onClick={() => certifyMut.mutate(Number(l.id))}
                        >
                          Certify
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* ELD Devices */}
      {tab === "eld" && (
        <div className="space-y-4">
          <div className="flex items-start gap-3 rounded-2xl border border-amber-200 bg-amber-50 p-4">
            <Radio className="h-4 w-4 shrink-0 text-amber-500 mt-0.5" />
            <p className="text-xs text-amber-700 leading-relaxed">
              <span className="font-bold text-amber-800">ELD Notice: </span>
              OpsTrax is not a certified ELD. This table shows third-party ELD device status as reported by the connected provider. FMCSA-registered ELD certification is the responsibility of the ELD provider.
            </p>
          </div>
          <div className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-slate-200 bg-slate-50/80">
                    {["Serial","Model","Provider","Vehicle","Driver","Status","Last Sync","Firmware","Actions"].map(h => (
                      <th key={h} className="text-left px-4 py-3 text-[10px] font-semibold uppercase tracking-wide text-slate-500">{h}</th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {elds.map(e => (
                    <tr key={String(e.id)} className="hover:bg-slate-50 transition-colors">
                      <td className="px-4 py-3 font-mono text-xs text-teal-700 font-semibold">{String(e.device_serial)}</td>
                      <td className="px-4 py-3 text-xs text-slate-700">{String(e.device_model ?? "—")}</td>
                      <td className="px-4 py-3 text-xs text-slate-700">{String(e.provider ?? "—")}</td>
                      <td className="px-4 py-3 text-xs text-slate-700">{String(e.vehicle_code ?? "—")}</td>
                      <td className="px-4 py-3 text-xs text-slate-700">{String(e.driver_name ?? "—")}</td>
                      <td className="px-4 py-3">
                        <span className={`flex items-center gap-1.5 text-xs font-semibold ${ELD_STATUS_COLOR[String(e.status)] ?? "text-slate-600"}`}>
                          {String(e.status) === "Active" ? <Radio className="h-3 w-3" /> : <WifiOff className="h-3 w-3" />}
                          {String(e.status)}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-xs text-slate-500">{formatDateTime(String(e.last_sync_at ?? ""))}</td>
                      <td className="px-4 py-3 text-xs font-mono text-slate-600">{String(e.firmware_version ?? "—")}</td>
                      <td className="px-4 py-3">
                        {String(e.status) === "Malfunction" ? (
                          <button
                            type="button"
                            className="rounded-lg border border-emerald-200 bg-emerald-50 px-2.5 py-1 text-[10px] font-semibold text-emerald-700 transition hover:bg-emerald-100 cursor-pointer"
                            onClick={() => resolveMalfMut.mutate(Number(e.id))}
                          >
                            Resolve
                          </button>
                        ) : String(e.status) !== "Active" ? null : (
                          <button
                            type="button"
                            className="rounded-lg border border-red-200 bg-red-50 px-2.5 py-1 text-[10px] font-semibold text-red-700 transition hover:bg-red-100 cursor-pointer"
                            onClick={() => setMalfForm({ id: Number(e.id), code: "", desc: "" })}
                          >
                            Mark Malfunction
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}

      {/* AI Recommendations */}
      {tab === "ai" && (
        <div className="space-y-4">
          <div className="relative flex items-center gap-4 overflow-hidden rounded-2xl border border-slate-700/20 bg-gradient-to-br from-slate-900 via-slate-800 to-slate-900 p-5 text-white shadow-xl">
            <div className="absolute -right-8 -top-8 h-32 w-32 rounded-full bg-violet-500/10 blur-2xl" />
            <span className="relative grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-violet-400/20 to-violet-600/10 ring-1 ring-violet-400/20">
              <Sparkles className="h-5 w-5 text-violet-300" />
            </span>
            <div className="relative">
              <p className="text-[10px] font-bold uppercase tracking-[0.22em] text-violet-300/80">AI-powered</p>
              <p className="mt-0.5 text-sm font-medium text-slate-200">HOS / ELD compliance recommendations based on driver behavior patterns and regulatory requirements.</p>
            </div>
          </div>
          <div className="grid gap-4">
            {aiRecs.map((rec, i) => {
              const priority = String(rec.priority);
              const pStyle = priority === "Critical"
                ? "border-red-200 bg-red-50"
                : priority === "High"
                ? "border-amber-200 bg-amber-50"
                : "border-sky-200 bg-sky-50";
              const pBadge = priority === "Critical"
                ? "border-red-200 bg-red-50 text-red-700"
                : priority === "High"
                ? "border-amber-200 bg-amber-50 text-amber-700"
                : "border-sky-200 bg-sky-50 text-sky-700";
              return (
                <div key={i} className={`rounded-2xl border p-5 space-y-2 ${pStyle}`}>
                  <div className="flex items-start justify-between gap-3">
                    <p className="text-sm font-semibold text-slate-900">{String(rec.title)}</p>
                    <span className={`shrink-0 rounded-lg border px-2 py-0.5 text-[10px] font-bold uppercase ${pBadge}`}>{String(rec.priority)}</span>
                  </div>
                  <p className="text-xs text-slate-600 leading-relaxed">{String(rec.description)}</p>
                  {!!rec.action_label && (
                    <button type="button" className="mt-1 rounded-lg border border-violet-200 bg-violet-50 px-3 py-1.5 text-xs font-semibold text-violet-700 transition hover:bg-violet-100 cursor-pointer">
                      {String(rec.action_label)}
                    </button>
                  )}
                </div>
              );
            })}
          </div>
        </div>
      )}

      {/* Driver clock drawer */}
      {drawer && tab === "drivers" && (
        <div className="fixed inset-0 z-50 flex justify-end anim-fade-in" onClick={() => setDrawer(null)}>
          <div className="absolute inset-0 bg-black/50 backdrop-blur-sm" />
          <aside className="anim-slide-left relative z-10 w-full max-w-sm overflow-y-auto bg-slate-900 border-l border-white/[0.09] p-5 shadow-2xl">
            <div className="flex items-center justify-between mb-4">
              <p className="font-semibold text-white flex items-center gap-2"><Clock className="h-4 w-4 text-amber-400" />HOS Clock</p>
              <button type="button" aria-label="Close" className="text-slate-400 hover:text-white cursor-pointer" onClick={() => setDrawer(null)}><X className="h-4 w-4" /></button>
            </div>
            <div className="space-y-3 text-sm">
              <div>
                <p className="text-base font-bold text-white">{String(drawer.driver_name)}</p>
                <p className="text-xs text-slate-500">{String(drawer.driver_code)} · {String(drawer.cycle_type)}</p>
              </div>
              <CountryBadge code={String(drawer.country_code)} />
              {[
                { label: "Drive Remaining", value: formatMinutesAsClock(Number(drawer.drive_time_remaining_minutes ?? 0)) },
                { label: "Shift Remaining", value: formatMinutesAsClock(Number(drawer.shift_time_remaining_minutes ?? 0)) },
                { label: "Cycle Remaining", value: formatMinutesAsClock(Number(drawer.cycle_time_remaining_minutes ?? 0)) },
                { label: "Profile",         value: drawer.profile_name },
                { label: "Status",          value: drawer.status },
              ].map(r => (
                <div key={r.label} className="flex justify-between border-b border-white/[0.05] pb-2">
                  <span className="text-slate-500">{r.label}</span>
                  <span className={`font-semibold ${String(drawer.status) === "Violation" ? "text-red-400" : String(drawer.status) === "Warning" ? "text-amber-400" : "text-white"}`}>{String(r.value ?? "—")}</span>
                </div>
              ))}
              {!!drawer.hos_warning && (
                <div className="flex items-center gap-2 rounded-lg bg-amber-400/10 border border-amber-400/20 px-3 py-2 text-xs text-amber-300">
                  <AlertTriangle className="h-3 w-3 shrink-0" />{String(drawer.hos_warning)}
                </div>
              )}
            </div>
          </aside>
        </div>
      )}

      {/* ELD malfunction modal */}
      {malfForm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center anim-fade-in" onClick={() => setMalfForm(null)}>
          <div className="absolute inset-0 bg-black/60 backdrop-blur-sm" />
          <div className="relative z-10 w-full max-w-sm rounded-2xl border border-white/[0.09] bg-slate-900 p-5 shadow-2xl space-y-4" onClick={e => e.stopPropagation()}>
            <div className="flex items-center justify-between">
              <p className="font-semibold text-white flex items-center gap-2"><Cpu className="h-4 w-4 text-red-400" />Mark ELD Malfunction</p>
              <button type="button" aria-label="Close" className="text-slate-400 hover:text-white cursor-pointer" onClick={() => setMalfForm(null)}><X className="h-4 w-4" /></button>
            </div>
            <div className="space-y-3">
              <div>
                <label className="block text-xs text-slate-400 mb-1">Malfunction Code</label>
                <input className="w-full rounded-xl border border-white/10 bg-white/5 px-3 py-2 text-sm text-white placeholder-slate-500 focus:border-red-400 focus:outline-none focus:ring-2 focus:ring-red-400/20" placeholder="e.g. P1, S1, E1" value={malfForm.code} onChange={e => setMalfForm(f => f ? { ...f, code: e.target.value } : f)} />
              </div>
              <div>
                <label className="block text-xs text-slate-400 mb-1">Description</label>
                <textarea className="w-full rounded-xl border border-white/10 bg-white/5 px-3 py-2 text-sm text-white placeholder-slate-500 focus:border-red-400 focus:outline-none focus:ring-2 focus:ring-red-400/20 h-20 resize-none" placeholder="Describe the malfunction..." value={malfForm.desc} onChange={e => setMalfForm(f => f ? { ...f, desc: e.target.value } : f)} />
              </div>
              <div className="flex items-start gap-2 rounded-lg border border-amber-400/20 bg-amber-400/5 p-3 text-[11px] text-amber-200/70">
                <AlertTriangle className="h-3 w-3 shrink-0 mt-0.5" />
                <span>Per FMCSA regulations, when an ELD malfunctions, the driver must note the malfunction and revert to paper records until the device is repaired or replaced.</span>
              </div>
            </div>
            <div className="flex gap-2">
              <button type="button" className="flex-1 rounded-xl border border-white/10 bg-white/5 px-4 py-2 text-sm font-medium text-slate-300 transition hover:bg-white/10 cursor-pointer" onClick={() => setMalfForm(null)}>Cancel</button>
              <button
                type="button"
                className="flex-1 rounded-xl bg-red-500/80 hover:bg-red-500 px-4 py-2 text-sm font-semibold text-white transition cursor-pointer"
                onClick={() => {
                  markMalfMut.mutate({ id: malfForm.id, body: { malfunctionCode: malfForm.code, malfunctionDescription: malfForm.desc } });
                  setMalfForm(null);
                }}
              >
                <Activity className="h-3.5 w-3.5 inline-block mr-1" />Confirm Malfunction
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

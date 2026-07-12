import { useState } from "react";
import { Activity, AlertTriangle, CheckCircle, Clock, Cpu, Radio, WifiOff, X, Zap } from "lucide-react";
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

const TABS: { id: TabId; label: string }[] = [
  { id: "drivers", label: "Driver Clocks" },
  { id: "logs",    label: "HOS Logs" },
  { id: "eld",     label: "ELD Devices" },
  { id: "ai",      label: "Recommendations" },
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
    <div className="h-1.5 w-full rounded-full bg-slate-200 overflow-hidden">
      <div className={`h-full rounded-full transition-all ${color}`} style={{ width: `${pct}%` }} />
    </div>
  );
}

function CountryBadge({ code }: { code: string }) {
  const labels: Record<string, string> = { US: "🇺🇸 US", CA: "🇨🇦 CA", SA: "🇸🇦 SA", AE: "🇦🇪 AE", PK: "🇵🇰 PK" };
  return <span className="rounded border border-slate-200 bg-slate-100 px-1.5 py-0.5 text-[10px] font-bold text-slate-700">{labels[code] ?? code}</span>;
}

function Disclaimer() {
  return (
    <div className="rounded-xl border border-amber-300 bg-amber-50 p-3 text-[11px] text-amber-700 leading-relaxed">
      <span className="font-bold text-amber-800">Disclaimer: </span>
      OpsTrax provides HOS monitoring and ELD framework tools. Final HOS compliance remains the carrier&apos;s and driver&apos;s responsibility. OpsTrax is not a certified ELD. ELD certification depends on the connected ELD provider/device and applicable country requirements.
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
    <div className="fleet-console flex h-full flex-col gap-3 overflow-y-auto">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-extrabold text-slate-900 flex items-center gap-2">
            <Clock className="h-5 w-5 text-amber-600" />{t("hos_eld")}
          </h1>
          <p className="text-xs text-slate-500 mt-0.5">Hours of service monitoring, driver clocks, ELD device tracking</p>
        </div>
      </div>

      <Disclaimer />

      {/* KPI strip */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        {[
          { label: "HOS OK",          value: okCount,        color: "text-emerald-700", icon: CheckCircle },
          { label: "HOS Warning",     value: warningCount,   color: "text-amber-700",   icon: AlertTriangle },
          { label: "HOS Violation",   value: violationCount, color: violationCount > 0 ? "text-red-700" : "text-slate-500",   icon: AlertTriangle },
          { label: "ELD Malfunction", value: eldMalfCount,   color: eldMalfCount   > 0 ? "text-red-700" : "text-slate-500",   icon: WifiOff },
        ].map(kpi => (
          <div key={kpi.label} className="fc-clay fc-clay-teal p-4">
            <div className="mb-1 flex items-center justify-between">
              <p className="text-[11px] font-bold uppercase tracking-wide text-slate-600">{kpi.label}</p>
              <span className="fc-blob"><kpi.icon className={`h-4 w-4 ${kpi.color}`} /></span>
            </div>
            <p className={`text-[30px] font-black leading-none tabular-nums ${kpi.color}`}>{driversQ.isLoading ? "—" : kpi.value}</p>
          </div>
        ))}
      </div>

      {/* Tabs */}
      <div className="flex gap-0.5 border-b border-slate-200 overflow-x-auto pb-0">
        {TABS.map(t2 => (
          <button
            key={t2.id}
            type="button"
            onClick={() => setTab(t2.id)}
            className={`px-3 py-2 text-[12px] font-semibold whitespace-nowrap transition-colors border-b-2 -mb-px ${
              tab === t2.id ? "border-amber-500 text-amber-700" : "border-transparent text-slate-500 hover:text-slate-700"
            }`}
          >
            {t2.label}
          </button>
        ))}
      </div>

      {/* Driver Clocks */}
      {tab === "drivers" && (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {drivers.map(d => {
            const driveRem = Number(d.drive_time_remaining_minutes ?? 660);
            const shiftRem = Number(d.shift_time_remaining_minutes ?? 840);
            const cycleRem = Number(d.cycle_time_remaining_minutes ?? 4200);
            const st       = String(d.status ?? "OK");
            const stColor  = CLOCK_STATUS_COLOR[st] ?? "text-slate-500";

            return (
              <div key={String(d.id)} className="panel space-y-3 cursor-pointer hover:border-amber-300 transition-colors" onClick={() => setDrawer(d)}>
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
                  <div className="rounded bg-amber-50 border border-amber-200 px-2 py-1 text-[11px] text-amber-700 flex items-center gap-1.5">
                    <AlertTriangle className="h-3 w-3 shrink-0" />{String(d.hos_warning)}
                  </div>
                )}
                <div className="space-y-2">
                  {[
                    { label: "Drive Remaining", value: driveRem, max: 660  },
                    { label: "Shift Remaining", value: shiftRem, max: 840  },
                    { label: "Cycle Remaining", value: cycleRem, max: 4200 },
                  ].map(bar => (
                    <div key={bar.label}>
                      <div className="flex justify-between text-[10px] mb-0.5">
                        <span className="text-slate-500">{bar.label}</span>
                        <span className="text-slate-700 font-mono">{formatMinutesAsClock(bar.value)}</span>
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
        <div className="panel overflow-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-200 text-left">
                {["Driver","Date","Status","Start","End","Duration","Location","Certified"].map(h => (
                  <th key={h} className="pb-2 pr-4 text-[10px] font-bold uppercase tracking-wide text-slate-500">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {logs.map(l => (
                <tr key={String(l.id)} className="hover:bg-slate-50">
                  <td className="py-2 pr-4">
                    <p className="font-semibold text-slate-900 text-xs">{String(l.driver_name)}</p>
                    <p className="text-[10px] text-slate-500">{String(l.driver_code)}</p>
                  </td>
                  <td className="py-2 pr-4 text-xs text-slate-700">{formatDate(String(l.log_date))}</td>
                  <td className="py-2 pr-4">
                    <span className="flex items-center gap-1.5 text-xs font-semibold text-slate-700">
                      <span className={`h-2 w-2 rounded-full shrink-0 ${logColors[String(l.status)] ?? "bg-slate-400"}`} />
                      {String(l.status)}
                    </span>
                  </td>
                  <td className="py-2 pr-4 text-xs text-slate-600 font-mono">{String(l.start_time ?? "").substring(11, 16)}</td>
                  <td className="py-2 pr-4 text-xs text-slate-600 font-mono">{String(l.end_time ?? "").substring(11, 16)}</td>
                  <td className="py-2 pr-4 text-xs text-slate-700">{String(l.duration_minutes)}m</td>
                  <td className="py-2 pr-4 text-xs text-slate-600 truncate max-w-[120px]">{String(l.location ?? "—")}</td>
                  <td className="py-2 pr-4">
                    {Number(l.is_certified) ? (
                      <span className="text-emerald-700 text-xs flex items-center gap-1"><CheckCircle className="h-3 w-3" />Yes</span>
                    ) : (
                      <button
                        type="button"
                        className="rounded border border-teal-300 bg-teal-50 px-2 py-0.5 text-[10px] text-teal-700 hover:bg-teal-100 transition"
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
      )}

      {/* ELD Devices */}
      {tab === "eld" && (
        <div className="space-y-3">
          <div className="rounded-xl border border-amber-300 bg-amber-50 p-3 text-[11px] text-amber-700">
            <span className="font-bold text-amber-800">ELD Notice: </span>
            OpsTrax is not a certified ELD. This table shows third-party ELD device status as reported by the connected provider. FMCSA-registered ELD certification is the responsibility of the ELD provider.
          </div>
          <div className="panel overflow-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 text-left">
                  {["Serial","Model","Provider","Vehicle","Driver","Status","Last Sync","Firmware","Actions"].map(h => (
                    <th key={h} className="pb-2 pr-4 text-[10px] font-bold uppercase tracking-wide text-slate-500">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {elds.map(e => (
                  <tr key={String(e.id)} className="hover:bg-slate-50">
                    <td className="py-2 pr-4 font-mono text-xs text-teal-700">{String(e.device_serial)}</td>
                    <td className="py-2 pr-4 text-xs text-slate-700">{String(e.device_model ?? "—")}</td>
                    <td className="py-2 pr-4 text-xs text-slate-700">{String(e.provider ?? "—")}</td>
                    <td className="py-2 pr-4 text-xs text-slate-700">{String(e.vehicle_code ?? "—")}</td>
                    <td className="py-2 pr-4 text-xs text-slate-700">{String(e.driver_name ?? "—")}</td>
                    <td className="py-2 pr-4">
                      <span className={`flex items-center gap-1.5 text-xs font-semibold ${ELD_STATUS_COLOR[String(e.status)] ?? "text-slate-600"}`}>
                        {String(e.status) === "Active" ? <Radio className="h-3 w-3" /> : <WifiOff className="h-3 w-3" />}
                        {String(e.status)}
                      </span>
                    </td>
                    <td className="py-2 pr-4 text-xs text-slate-500">{formatDateTime(String(e.last_sync_at ?? ""))}</td>
                    <td className="py-2 pr-4 text-xs font-mono text-slate-600">{String(e.firmware_version ?? "—")}</td>
                    <td className="py-2 pr-4">
                      {String(e.status) === "Malfunction" ? (
                        <button
                          type="button"
                          className="rounded border border-emerald-300 bg-emerald-50 px-2 py-0.5 text-[10px] text-emerald-700 hover:bg-emerald-100 transition"
                          onClick={() => resolveMalfMut.mutate(Number(e.id))}
                        >
                          Resolve
                        </button>
                      ) : String(e.status) !== "Active" ? null : (
                        <button
                          type="button"
                          className="rounded border border-red-300 bg-red-50 px-2 py-0.5 text-[10px] text-red-700 hover:bg-red-100 transition"
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
      )}

      {/* AI Recommendations */}
      {tab === "ai" && (
        <div className="panel space-y-3">
          <p className="section-title flex items-center gap-2"><Zap className="h-3.5 w-3.5 text-violet-600" />HOS / ELD Recommendations</p>
          {aiRecs.map((rec, i) => (
            <div key={i} className="rounded-xl border border-slate-200 bg-slate-50 p-4 space-y-1.5">
              <div className="flex items-start justify-between gap-2">
                <p className="text-sm font-semibold text-slate-900">{String(rec.title)}</p>
                <span className={`rounded border px-1.5 py-0.5 text-[10px] font-bold uppercase ${
                  String(rec.priority) === "Critical" ? "border-red-200 bg-red-50 text-red-700"
                  : String(rec.priority) === "High"   ? "border-amber-200 bg-amber-50 text-amber-700"
                  : "border-sky-200 bg-sky-50 text-sky-700"
                }`}>{String(rec.priority)}</span>
              </div>
              <p className="text-xs text-slate-600 leading-relaxed">{String(rec.description)}</p>
              {!!rec.action_label && (
                <p className="mt-1 text-xs font-semibold text-violet-700">
                  Recommended: {String(rec.action_label)}
                </p>
              )}
            </div>
          ))}
        </div>
      )}

      {/* Driver clock drawer — intentional dark context */}
      {drawer && tab === "drivers" && (
        <div className="fixed inset-0 z-50 flex justify-end anim-fade-in">
          <div className="absolute inset-0 bg-black/60 backdrop-blur-sm" onClick={() => setDrawer(null)} />
          <aside className="anim-slide-left relative z-10 w-full max-w-sm overflow-y-auto bg-slate-900 border-s border-white/[0.09] p-5 shadow-2xl">
            <div className="flex items-center justify-between mb-4">
              <p className="font-semibold text-white flex items-center gap-2"><Clock className="h-4 w-4 text-amber-400" />HOS Clock</p>
              <button type="button" aria-label="Close" className="icon-btn" onClick={() => setDrawer(null)}><X className="h-4 w-4" /></button>
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
                <div className="rounded bg-amber-400/10 border border-amber-400/20 px-3 py-2 text-xs text-amber-300">
                  <AlertTriangle className="h-3 w-3 inline-block mr-1" />{String(drawer.hos_warning)}
                </div>
              )}
            </div>
          </aside>
        </div>
      )}

      {/* ELD malfunction modal — intentional dark context */}
      {malfForm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center anim-fade-in">
          <div className="absolute inset-0 bg-black/70 backdrop-blur-sm" onClick={() => setMalfForm(null)} />
          <div className="relative z-10 w-full max-w-sm rounded-2xl border border-white/[0.09] bg-slate-900 p-5 shadow-2xl space-y-4">
            <div className="flex items-center justify-between">
              <p className="font-semibold text-white flex items-center gap-2"><Cpu className="h-4 w-4 text-red-400" />Mark ELD Malfunction</p>
              <button type="button" aria-label="Close" className="icon-btn" onClick={() => setMalfForm(null)}><X className="h-4 w-4" /></button>
            </div>
            <div className="space-y-3">
              <div>
                <label className="block text-xs text-slate-400 mb-1">Malfunction Code</label>
                <input className="field w-full" placeholder="e.g. P1, S1, E1" value={malfForm.code} onChange={e => setMalfForm(f => f ? { ...f, code: e.target.value } : f)} />
              </div>
              <div>
                <label className="block text-xs text-slate-400 mb-1">Description</label>
                <textarea className="field w-full h-20 resize-none" placeholder="Describe the malfunction..." value={malfForm.desc} onChange={e => setMalfForm(f => f ? { ...f, desc: e.target.value } : f)} />
              </div>
              <div className="rounded border border-amber-400/20 bg-amber-400/5 p-2 text-[11px] text-amber-200/70">
                Per FMCSA regulations, when an ELD malfunctions, the driver must note the malfunction and revert to paper records until the device is repaired or replaced.
              </div>
            </div>
            <div className="flex gap-2">
              <button type="button" className="flex-1 btn-ghost" onClick={() => setMalfForm(null)}>Cancel</button>
              <button
                type="button"
                className="flex-1 rounded-lg bg-red-500/80 hover:bg-red-500 text-white text-sm font-semibold py-2 transition"
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

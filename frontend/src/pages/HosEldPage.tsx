import { useState } from "react";
import { Activity, AlertTriangle, CheckCircle, Clock, Cpu, FileCheck, Radio, WifiOff, X, Zap } from "lucide-react";
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
  { id: "ai",      label: "AI Recommendations" },
];

const CLOCK_STATUS_COLOR: Record<string, string> = {
  OK:        "text-emerald-400",
  Warning:   "text-amber-400",
  Violation: "text-red-400",
};

const ELD_STATUS_COLOR: Record<string, string> = {
  Active:      "text-emerald-400",
  Diagnostic:  "text-amber-400",
  Malfunction: "text-red-400",
};

function ClockBar({ value, max, status }: { value: number; max: number; status: string }) {
  const pct = Math.max(0, Math.min(100, (value / max) * 100));
  const color = status === "Violation" ? "bg-red-500" : status === "Warning" ? "bg-amber-500" : "bg-emerald-500";
  return (
    <div className="h-1.5 w-full rounded-full bg-white/[0.08] overflow-hidden">
      <div className={`h-full rounded-full transition-all ${color}`} style={{ width: `${pct}%` }} />
    </div>
  );
}

function CountryBadge({ code }: { code: string }) {
  const labels: Record<string, string> = { US: "🇺🇸 US", CA: "🇨🇦 CA", SA: "🇸🇦 SA", AE: "🇦🇪 AE", PK: "🇵🇰 PK" };
  return <span className="rounded border border-white/10 bg-white/[0.04] px-1.5 py-0.5 text-[10px] font-bold text-slate-300">{labels[code] ?? code}</span>;
}

function Disclaimer() {
  return (
    <div className="rounded-xl border border-amber-400/20 bg-amber-400/5 p-3 text-[11px] text-amber-200/80 leading-relaxed">
      <span className="font-bold text-amber-300">Disclaimer: </span>
      OpsTrax provides HOS monitoring and ELD framework tools. Final HOS compliance remains the carrier&apos;s and driver&apos;s responsibility. OpsTrax is not a certified ELD. ELD certification depends on the connected ELD provider/device and applicable country requirements.
    </div>
  );
}

export function HosEldPage() {
  const { t } = useI18n();
  const [tab, setTab] = useState<TabId>("drivers");
  const [drawer, setDrawer] = useState<AnyRecord | null>(null);
  const [malfForm, setMalfForm] = useState<{ id: number; code: string; desc: string } | null>(null);

  const driversQ   = useHosDrivers();
  const logsQ      = useHosLogs();
  const eldQ       = useEldDevices();
  const aiQ        = useHosAiRecs();

  const certifyMut    = useCertifyHosLog();
  const markMalfMut   = useMarkEldMalfunction();
  const resolveMalfMut = useResolveEldMalfunction();

  const drivers = (driversQ.data as AnyRecord[] | undefined) ?? [];
  const logs    = (logsQ.data as AnyRecord[] | undefined) ?? [];
  const elds    = (eldQ.data as AnyRecord[] | undefined) ?? [];
  const aiRecs  = (aiQ.data as AnyRecord[] | undefined) ?? [];

  // KPIs from drivers
  const okCount        = drivers.filter(d => String(d.status) === "OK").length;
  const warningCount   = drivers.filter(d => String(d.status) === "Warning").length;
  const violationCount = drivers.filter(d => String(d.status) === "Violation").length;
  const eldMalfCount   = elds.filter(e => String(e.status) === "Malfunction").length;

  const logStatuses = ["Driving", "On Duty (Not Driving)", "Off Duty", "Sleeper Berth"];
  const logColors: Record<string, string> = {
    "Driving":               "bg-emerald-500",
    "On Duty (Not Driving)": "bg-amber-500",
    "Off Duty":              "bg-slate-600",
    "Sleeper Berth":         "bg-blue-600",
  };

  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-extrabold text-white flex items-center gap-2">
            <Clock className="h-5 w-5 text-amber-400" />{t("hos_eld")}
          </h1>
          <p className="text-xs text-slate-500 mt-0.5">Hours of service monitoring, driver clocks, ELD device tracking</p>
        </div>
      </div>

      <Disclaimer />

      {/* KPI strip */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        {[
          { label: "HOS OK",          value: okCount,        color: "text-emerald-400", icon: CheckCircle },
          { label: "HOS Warning",     value: warningCount,   color: "text-amber-400",   icon: AlertTriangle },
          { label: "HOS Violation",   value: violationCount, color: violationCount > 0 ? "text-red-400" : "text-slate-400", icon: AlertTriangle },
          { label: "ELD Malfunction", value: eldMalfCount,   color: eldMalfCount > 0 ? "text-red-400" : "text-slate-400",   icon: WifiOff },
        ].map(kpi => (
          <div key={kpi.label} className="panel">
            <div className="flex items-center justify-between mb-1">
              <p className="text-[11px] text-slate-500 uppercase tracking-wide font-semibold">{kpi.label}</p>
              <kpi.icon className={`h-4 w-4 ${kpi.color}`} />
            </div>
            <p className={`text-2xl font-extrabold ${kpi.color}`}>{driversQ.isLoading ? "—" : kpi.value}</p>
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
              tab === t2.id ? "border-amber-400 text-amber-300" : "border-transparent text-slate-500 hover:text-slate-300"
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
            const driveRem  = Number(d.drive_time_remaining_minutes ?? 660);
            const shiftRem  = Number(d.shift_time_remaining_minutes ?? 840);
            const cycleRem  = Number(d.cycle_time_remaining_minutes ?? 4200);
            const st        = String(d.status ?? "OK");
            const stColor   = CLOCK_STATUS_COLOR[st] ?? "text-slate-400";

            return (
              <div key={String(d.id)} className="panel space-y-3 cursor-pointer hover:border-amber-400/20 transition-colors" onClick={() => setDrawer(d)}>
                <div className="flex items-start justify-between">
                  <div>
                    <p className="font-semibold text-white">{String(d.driver_name)}</p>
                    <p className="text-[11px] text-slate-500">{String(d.driver_code)} · {String(d.cycle_type)}</p>
                  </div>
                  <div className="flex flex-col items-end gap-1">
                    <CountryBadge code={String(d.country_code)} />
                    <span className={`text-xs font-bold ${stColor}`}>{st}</span>
                  </div>
                </div>
                {!!d.hos_warning && (
                  <div className="rounded bg-amber-400/10 border border-amber-400/20 px-2 py-1 text-[11px] text-amber-300 flex items-center gap-1.5">
                    <AlertTriangle className="h-3 w-3 flex-shrink-0" />{String(d.hos_warning)}
                  </div>
                )}
                <div className="space-y-2">
                  {[
                    { label: "Drive Remaining",  value: driveRem,  max: 660  },
                    { label: "Shift Remaining",  value: shiftRem,  max: 840  },
                    { label: "Cycle Remaining",  value: cycleRem,  max: 4200 },
                  ].map(bar => (
                    <div key={bar.label}>
                      <div className="flex justify-between text-[10px] mb-0.5">
                        <span className="text-slate-500">{bar.label}</span>
                        <span className="text-slate-300 font-mono">{formatMinutesAsClock(bar.value)}</span>
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
              <tr className="border-b border-white/[0.07] text-left">
                {["Driver","Date","Status","Start","End","Duration","Location","Certified"].map(h => (
                  <th key={h} className="pb-2 pr-4 text-[10px] font-bold uppercase tracking-wide text-slate-500">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-white/[0.04]">
              {logs.map(l => (
                <tr key={String(l.id)} className="hover:bg-white/[0.03]">
                  <td className="py-2 pr-4">
                    <p className="font-semibold text-white text-xs">{String(l.driver_name)}</p>
                    <p className="text-[10px] text-slate-500">{String(l.driver_code)}</p>
                  </td>
                  <td className="py-2 pr-4 text-xs text-slate-300">{formatDate(String(l.log_date))}</td>
                  <td className="py-2 pr-4">
                    <span className={`flex items-center gap-1.5 text-xs font-semibold`}>
                      <span className={`h-2 w-2 rounded-full flex-shrink-0 ${logColors[String(l.status)] ?? "bg-slate-500"}`} />
                      {String(l.status)}
                    </span>
                  </td>
                  <td className="py-2 pr-4 text-xs text-slate-400 font-mono">{String(l.start_time ?? "").substring(11, 16)}</td>
                  <td className="py-2 pr-4 text-xs text-slate-400 font-mono">{String(l.end_time ?? "").substring(11, 16)}</td>
                  <td className="py-2 pr-4 text-xs text-slate-300">{String(l.duration_minutes)}m</td>
                  <td className="py-2 pr-4 text-xs text-slate-400 truncate max-w-[120px]">{String(l.location ?? "—")}</td>
                  <td className="py-2 pr-4">
                    {Number(l.is_certified) ? (
                      <span className="text-emerald-400 text-xs flex items-center gap-1"><CheckCircle className="h-3 w-3" />Yes</span>
                    ) : (
                      <button
                        className="rounded border border-teal-400/20 bg-teal-400/10 px-2 py-0.5 text-[10px] text-teal-300 hover:bg-teal-400/20"
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
          <div className="rounded-xl border border-amber-400/20 bg-amber-400/5 p-3 text-[11px] text-amber-200/80">
            <span className="font-bold text-amber-300">ELD Notice: </span>
            OpsTrax is not a certified ELD. This table shows third-party ELD device status as reported by the connected provider. FMCSA-registered ELD certification is the responsibility of the ELD provider.
          </div>
          <div className="panel overflow-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-white/[0.07] text-left">
                  {["Serial","Model","Provider","Vehicle","Driver","Status","Last Sync","Firmware","Actions"].map(h => (
                    <th key={h} className="pb-2 pr-4 text-[10px] font-bold uppercase tracking-wide text-slate-500">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-white/[0.04]">
                {elds.map(e => (
                  <tr key={String(e.id)} className="hover:bg-white/[0.03]">
                    <td className="py-2 pr-4 font-mono text-xs text-teal-300">{String(e.device_serial)}</td>
                    <td className="py-2 pr-4 text-xs text-slate-300">{String(e.device_model ?? "—")}</td>
                    <td className="py-2 pr-4 text-xs text-slate-300">{String(e.provider ?? "—")}</td>
                    <td className="py-2 pr-4 text-xs text-slate-300">{String(e.vehicle_code ?? "—")}</td>
                    <td className="py-2 pr-4 text-xs text-slate-300">{String(e.driver_name ?? "—")}</td>
                    <td className="py-2 pr-4">
                      <span className={`flex items-center gap-1.5 text-xs font-semibold ${ELD_STATUS_COLOR[String(e.status)] ?? "text-slate-400"}`}>
                        {String(e.status) === "Active" ? <Radio className="h-3 w-3" /> : <WifiOff className="h-3 w-3" />}
                        {String(e.status)}
                      </span>
                    </td>
                    <td className="py-2 pr-4 text-xs text-slate-500">{formatDateTime(String(e.last_sync_at ?? ""))}</td>
                    <td className="py-2 pr-4 text-xs font-mono text-slate-400">{String(e.firmware_version ?? "—")}</td>
                    <td className="py-2 pr-4">
                      {String(e.status) === "Malfunction" ? (
                        <button
                          className="rounded border border-emerald-400/20 bg-emerald-400/10 px-2 py-0.5 text-[10px] text-emerald-300 hover:bg-emerald-400/20"
                          onClick={() => resolveMalfMut.mutate(Number(e.id))}
                        >
                          Resolve
                        </button>
                      ) : String(e.status) !== "Active" ? null : (
                        <button
                          className="rounded border border-red-400/20 bg-red-400/10 px-2 py-0.5 text-[10px] text-red-300 hover:bg-red-400/20"
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
          <p className="section-title flex items-center gap-2"><Zap className="h-3.5 w-3.5 text-violet-400" />HOS / ELD AI Recommendations</p>
          {aiRecs.map((rec, i) => (
            <div key={i} className="rounded-xl border border-white/[0.07] bg-white/[0.03] p-4 space-y-1.5">
              <div className="flex items-start justify-between gap-2">
                <p className="text-sm font-semibold text-white">{String(rec.title)}</p>
                <span className={`rounded border px-1.5 py-0.5 text-[10px] font-bold uppercase ${
                  String(rec.priority) === "Critical" ? "border-red-400/20 bg-red-400/10 text-red-400"
                  : String(rec.priority) === "High"   ? "border-amber-400/20 bg-amber-400/10 text-amber-400"
                  : "border-sky-400/20 bg-sky-400/10 text-sky-400"
                }`}>{String(rec.priority)}</span>
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
      )}

      {/* Driver clock drawer */}
      {drawer && tab === "drivers" && (
        <div className="fixed inset-0 z-50 flex justify-end anim-fade-in">
          <div className="absolute inset-0 bg-black/60 backdrop-blur-sm" onClick={() => setDrawer(null)} />
          <aside className="anim-slide-left relative z-10 w-full max-w-sm overflow-y-auto bg-slate-900 border-s border-white/[0.09] p-5 shadow-2xl">
            <div className="flex items-center justify-between mb-4">
              <p className="font-semibold text-white flex items-center gap-2"><Clock className="h-4 w-4 text-amber-400" />HOS Clock</p>
              <button className="icon-btn" onClick={() => setDrawer(null)}><X className="h-4 w-4" /></button>
            </div>
            <div className="space-y-3 text-sm">
              <div>
                <p className="text-base font-bold text-white">{String(drawer.driver_name)}</p>
                <p className="text-xs text-slate-500">{String(drawer.driver_code)} · {String(drawer.cycle_type)}</p>
              </div>
              <CountryBadge code={String(drawer.country_code)} />
              {[
                { label: "Drive Remaining",  value: formatMinutesAsClock(Number(drawer.drive_time_remaining_minutes ?? 0)) },
                { label: "Shift Remaining",  value: formatMinutesAsClock(Number(drawer.shift_time_remaining_minutes ?? 0)) },
                { label: "Cycle Remaining",  value: formatMinutesAsClock(Number(drawer.cycle_time_remaining_minutes ?? 0)) },
                { label: "Profile",          value: drawer.profile_name },
                { label: "Status",           value: drawer.status },
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

      {/* ELD malfunction modal */}
      {malfForm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center anim-fade-in">
          <div className="absolute inset-0 bg-black/70 backdrop-blur-sm" onClick={() => setMalfForm(null)} />
          <div className="relative z-10 w-full max-w-sm rounded-2xl border border-white/[0.09] bg-slate-900 p-5 shadow-2xl space-y-4">
            <div className="flex items-center justify-between">
              <p className="font-semibold text-white flex items-center gap-2"><Cpu className="h-4 w-4 text-red-400" />Mark ELD Malfunction</p>
              <button className="icon-btn" onClick={() => setMalfForm(null)}><X className="h-4 w-4" /></button>
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
              <button className="flex-1 btn-ghost" onClick={() => setMalfForm(null)}>Cancel</button>
              <button
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

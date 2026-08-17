import { useState } from "react";
import { Activity, AlertTriangle, CheckCircle, Clock, Cpu, Download, Radio, WifiOff, X, Zap } from "lucide-react";
import {
  useEldDevices,
  useHosAiRecs,
  useHosDrivers,
  useHosLogs,
  useMarkEldMalfunction,
  useResolveEldMalfunction,
} from "@/hooks/useBatch6";
import { useI18n } from "@/i18n";
import { EmptyState, ErrorState, LoadingState, exportCsv } from "@/components/ui";
import { ConsoleNav, ConsoleRail } from "@/components/console";
import { useHasPermission } from "@/hooks/usePermission";
import { useDialogFocus } from "@/hooks/useDialogFocus";
import { useSingleFlight } from "@/hooks/useSingleFlight";
import { useAuth } from "@/hooks/useAuth";
import type { AnyRecord } from "@/types";
import { formatDate, formatDateTime, formatMinutesAsClock } from "@/utils/formatters";

type TabId = "drivers" | "logs" | "eld" | "ai";

// { key, label, description } to match ConsoleNav's shape (the same segmented-pill nav
// Vehicles/Fleet Compliance use), so this page's tabs look and behave the same way instead
// of a bespoke underline tab bar.
const TABS: { key: TabId; label: string; description: string }[] = [
  { key: "drivers", label: "Driver Clocks",    description: "Live HOS remaining time per driver" },
  { key: "logs",    label: "HOS Logs",         description: "Duty-status history and certification" },
  { key: "eld",     label: "ELD Devices",      description: "Device sync, status and malfunction actions" },
  { key: "ai",      label: "Recommendations",  description: "AI-surfaced HOS and ELD actions" },
];

const ELD_STATUS_COLOR: Record<string, string> = {
  Active:      "text-emerald-700",
  Diagnostic:  "text-amber-700",
  Malfunction: "text-red-700",
};

function ClockBar({ value, max, status }: { value: number | null; max: number; status: string }) {
  if (value == null) return <div className="h-1.5 w-full rounded-full bg-slate-200" aria-label="Clock value unavailable" />;
  const pct = Math.max(0, Math.min(100, (value / max) * 100));
  const color = status === "Violation" ? "bg-red-500" : status === "Warning" ? "bg-amber-500" : "bg-emerald-500";
  return (
    <div className="h-1.5 w-full rounded-full bg-slate-200 overflow-hidden">
      <div className={`h-full rounded-full transition-all ${color}`} style={{ width: `${pct}%` }} />
    </div>
  );
}

function minutes(value: unknown): number | null {
  if (value == null || value === "") return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null;
}

function clockLabel(value: number | null) {
  return value == null ? "Unavailable" : formatMinutesAsClock(value);
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
  const { session } = useAuth();
  const hasPermission = useHasPermission();
  const canManage = hasPermission("compliance:update") || hasPermission("compliance:manage") || hasPermission("telematics:manage");
  const eldEntitled = session?.entitlements && Object.prototype.hasOwnProperty.call(session.entitlements, "telematics")
    ? session.entitlements.telematics === true
    : session?.entitlementPolicyMode !== "package_allowlist";
  const [tab, setTab] = useState<TabId>("drivers");
  const [drawer, setDrawer] = useState<AnyRecord | null>(null);
  const [malfForm, setMalfForm] = useState<{ id: number; rowVersion: number; code: string; desc: string } | null>(null);
  const [resolveForm, setResolveForm] = useState<{ id: number; rowVersion: number; evidence: string; status: string } | null>(null);
  const [actionMessage, setActionMessage] = useState<{ kind: "success" | "error"; text: string } | null>(null);
  const clockDialogRef = useDialogFocus<HTMLElement>(drawer != null, () => setDrawer(null));
  const malfunctionDialogRef = useDialogFocus<HTMLDivElement>(malfForm != null, () => setMalfForm(null));
  const resolutionDialogRef = useDialogFocus<HTMLDivElement>(resolveForm != null, () => setResolveForm(null));

  const driversQ    = useHosDrivers();
  const logsQ       = useHosLogs();
  // HOS and ELD intentionally have different commercial owners. Do not issue an
  // ELD request when Telematics is excluded, and do not misrender its 403 as an
  // empty/healthy fleet. HOS remains available under the Compliance entitlement.
  const eldQ        = useEldDevices(eldEntitled);
  const aiQ         = useHosAiRecs();

  const markMalfMut    = useMarkEldMalfunction();
  const resolveMalfMut = useResolveEldMalfunction();
  const markSingleFlight = useSingleFlight();
  const resolveSingleFlight = useSingleFlight();

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
      {/* Header — ConsoleRail, matching Vehicles/Cold Chain/Fleet Compliance instead of a
          bespoke plain <h1>, with the same live-count subtitle convention those pages use. */}
      <ConsoleRail
        eyebrow="Compliance · HOS/ELD"
        icon={<Clock className="h-3.5 w-3.5 text-teal-700" />}
        title={t("hos_eld")}
        meta={<>
          <span className="font-bold text-slate-700 tabular-nums">{drivers.length}</span> drivers tracked ·{" "}
          <span className={`font-bold tabular-nums ${violationCount > 0 ? "text-red-600" : "text-emerald-600"}`}>{violationCount}</span> HOS violations ·{" "}
          <span className={`font-bold tabular-nums ${eldMalfCount > 0 ? "text-red-600" : "text-emerald-600"}`}>{eldMalfCount}</span> ELD malfunctions
        </>}
      />

      <Disclaimer />

      {!canManage && (
        <div className="rounded-xl border border-slate-200 bg-slate-50 p-3 text-xs text-slate-600" role="status">
          Read-only access: ELD malfunction actions require compliance or telematics management permission.
        </div>
      )}
      <div className="rounded-xl border border-blue-200 bg-blue-50 p-3 text-xs text-blue-800" role="status">
        Back-office users may review HOS records but cannot certify for a driver. Daily certification is available only to the authenticated driver in the Driver Portal and does not submit records to a regulator.
      </div>
      {actionMessage && (
        <div className={`rounded-xl border p-3 text-xs ${actionMessage.kind === "error" ? "border-red-300 bg-red-50 text-red-700" : "border-emerald-300 bg-emerald-50 text-emerald-700"}`} role={actionMessage.kind === "error" ? "alert" : "status"}>
          {actionMessage.text}
        </div>
      )}

      {/* KPI strip */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        {[
          { label: "HOS OK",          value: okCount,        color: "text-emerald-700", icon: CheckCircle,    tone: "fc-clay-emerald" },
          { label: "HOS Warning",     value: warningCount,   color: "text-amber-700",   icon: AlertTriangle,  tone: "fc-clay-amber" },
          { label: "HOS Violation",   value: violationCount, color: violationCount > 0 ? "text-red-700" : "text-slate-500",   icon: AlertTriangle, tone: "fc-clay-red" },
          { label: "ELD Malfunction", value: eldMalfCount,   color: eldMalfCount   > 0 ? "text-red-700" : "text-slate-500",   icon: WifiOff,        tone: "fc-clay-red" },
        ].map(kpi => (
          // Tone now matches each metric's own severity instead of every tile using the
          // same teal regardless of meaning, so Violation/Malfunction actually stand out.
          <div key={kpi.label} className={`fc-clay ${kpi.tone} p-4`}>
            <div className="mb-1 flex items-center justify-between">
              <p className="text-[11px] font-bold uppercase tracking-wide text-slate-600">{kpi.label}</p>
              <span className="fc-blob"><kpi.icon className={`h-4 w-4 ${kpi.color}`} /></span>
            </div>
            <p className={`text-[30px] font-black leading-none tabular-nums ${kpi.color}`}>{driversQ.isLoading ? "—" : kpi.value}</p>
          </div>
        ))}
      </div>

      {/* Tabs — same ConsoleNav segmented pill nav Vehicles/Fleet Compliance use, instead
          of a bespoke underline tab bar. */}
      <ConsoleNav sections={TABS} active={tab} onSelect={setTab} />

      {/* Driver Clocks */}
      {tab === "drivers" && (
        driversQ.isLoading ? <LoadingState /> : driversQ.isError ? (
          <ErrorState message={(driversQ.error as Error)?.message} onRetry={() => void driversQ.refetch()} />
        ) : drivers.length === 0 ? (
          <EmptyState title="No HOS clocks available" subtitle="No persisted driver-clock records are available for your authorized branch." />
        ) : <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {drivers.map(d => {
            const driveRem = minutes(d.driveTimeRemainingMinutes);
            const shiftRem = minutes(d.shiftTimeRemainingMinutes);
            const cycleRem = minutes(d.cycleTimeRemainingMinutes);
            const st       = String(d.status ?? "Unavailable");
            const stBorder = st === "Violation" ? "border-l-red-500" : st === "Warning" ? "border-l-amber-500" : "border-l-emerald-500";
            const stDotBg  = st === "Violation" ? "bg-red-100 text-red-700" : st === "Warning" ? "bg-amber-100 text-amber-700" : "bg-emerald-100 text-emerald-700";
            const initials = String(d.driverName ?? "?").trim().split(/\s+/).map(p => p[0]).slice(0, 2).join("").toUpperCase();

            return (
              <div key={String(d.id)} className={`panel space-y-3 p-4 cursor-pointer border-l-4 ${stBorder} transition-colors hover:shadow-md`} role="button" tabIndex={0} aria-label={`Open HOS clock for ${String(d.driverName ?? "driver")}`} onClick={() => setDrawer(d)} onKeyDown={(event) => { if (event.key === "Enter" || event.key === " ") { event.preventDefault(); setDrawer(d); } }}>
                <div className="flex items-start justify-between gap-2">
                  <div className="flex items-start gap-2.5">
                    <span className="grid h-9 w-9 shrink-0 place-items-center rounded-full bg-teal-50 text-[11px] font-bold text-teal-700">{initials}</span>
                    <div>
                      <p className="font-semibold text-slate-900">{String(d.driverName)}</p>
                      <p className="text-[11px] text-slate-500">{String(d.driverCode)} · {String(d.cycleType)}</p>
                    </div>
                  </div>
                  <div className="flex flex-col items-end gap-1.5">
                    <CountryBadge code={String(d.countryCode)} />
                    <span className={`rounded-full px-2 py-0.5 text-[10px] font-bold ${stDotBg}`}>{st}</span>
                  </div>
                </div>
                {!!d.hosWarning && (
                  <div className="rounded bg-amber-50 border border-amber-200 px-2 py-1 text-[11px] text-amber-700 flex items-center gap-1.5">
                    <AlertTriangle className="h-3 w-3 shrink-0" />{String(d.hosWarning)}
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
                        <span className="text-slate-700 font-mono">{clockLabel(bar.value)}</span>
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
        logsQ.isLoading ? <LoadingState /> : logsQ.isError ? (
          <ErrorState message={(logsQ.error as Error)?.message} onRetry={() => void logsQ.refetch()} />
        ) : logs.length === 0 ? (
          <EmptyState title="No HOS logs available" subtitle="No persisted duty-status segments are available for your authorized branch." />
        ) : <div className="panel p-5 overflow-auto">
          <div className="mb-3 flex items-center justify-between gap-2">
            <p className="section-title flex items-center gap-2"><Clock className="h-3.5 w-3.5 text-teal-600" />Duty-status history</p>
            <button type="button" className="btn-ghost h-9 gap-1.5 px-3 text-xs" onClick={() => exportCsv("hos-logs", logs)}>
              <Download className="h-3.5 w-3.5" /> Export visible logs
            </button>
          </div>
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
                    <p className="font-semibold text-slate-900 text-xs">{String(l.driverName)}</p>
                    <p className="text-[10px] text-slate-500">{String(l.driverCode)}</p>
                  </td>
                  <td className="py-2 pr-4 text-xs text-slate-700">{formatDate(String(l.logDate))}</td>
                  <td className="py-2 pr-4">
                    <span className="flex items-center gap-1.5 text-xs font-semibold text-slate-700">
                      <span className={`h-2 w-2 rounded-full shrink-0 ${logColors[String(l.status)] ?? "bg-slate-400"}`} />
                      {String(l.status)}
                    </span>
                  </td>
                  <td className="py-2 pr-4 text-xs text-slate-600 font-mono">{String(l.startTime ?? "").substring(11, 16)}</td>
                  <td className="py-2 pr-4 text-xs text-slate-600 font-mono">{String(l.endTime ?? "").substring(11, 16)}</td>
                  <td className="py-2 pr-4 text-xs text-slate-700">
                    {String(l.calculatedDurationMinutes ?? l.durationMinutes)}m
                    {Boolean(l.durationMismatch) && <span className="ml-1 text-red-700" title="Stored duration differs from start/end timestamps">Data issue</span>}
                  </td>
                  <td className="py-2 pr-4 text-xs text-slate-600 truncate max-w-[120px]">{String(l.location ?? "—")}</td>
                  <td className="py-2 pr-4">
                    {Boolean(l.isCertified) ? (
                      <span className="text-emerald-700 text-xs flex items-center gap-1"><CheckCircle className="h-3 w-3" />Yes</span>
                    ) : <span className="text-xs text-slate-500">Pending driver</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* ELD Devices */}
      {tab === "eld" && (
        !eldEntitled ? (
          <div className="rounded-xl border border-amber-300 bg-amber-50 p-5" role="status" data-testid="eld-entitlement-boundary">
            <p className="font-bold text-amber-900">ELD device operations are not included</p>
            <p className="mt-1 text-sm text-amber-800">Hours-of-service records remain available. A Platform Admin must enable the Telematics entitlement before device status, malfunction history or recovery actions can be accessed.</p>
          </div>
        ) : eldQ.isLoading ? <LoadingState /> : eldQ.isError ? (
          <ErrorState message={(eldQ.error as Error)?.message} onRetry={() => void eldQ.refetch()} />
        ) : elds.length === 0 ? (
          <EmptyState title="No ELD devices available" subtitle="No persisted third-party device records are available for your authorized branch." />
        ) : <div className="space-y-3">
          <div className="rounded-xl border border-amber-300 bg-amber-50 p-3 text-[11px] text-amber-700">
            <span className="font-bold text-amber-800">ELD Notice: </span>
            OpsTrax is not a certified ELD. This table shows third-party ELD device status as reported by the connected provider. FMCSA-registered ELD certification is the responsibility of the ELD provider.
          </div>
          <div className="panel p-5 overflow-auto">
            <p className="section-title mb-3 flex items-center gap-2"><Cpu className="h-3.5 w-3.5 text-teal-600" />Connected devices</p>
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
                    <td className="py-2 pr-4 font-mono text-xs text-teal-700">{String(e.deviceSerial)}</td>
                    <td className="py-2 pr-4 text-xs text-slate-700">{String(e.deviceModel ?? "—")}</td>
                    <td className="py-2 pr-4 text-xs text-slate-700">{String(e.provider ?? "—")}</td>
                    <td className="py-2 pr-4 text-xs text-slate-700">{String(e.vehicleCode ?? "—")}</td>
                    <td className="py-2 pr-4 text-xs text-slate-700">{String(e.driverName ?? "—")}</td>
                    <td className="py-2 pr-4">
                      <span className={`flex items-center gap-1.5 text-xs font-semibold ${ELD_STATUS_COLOR[String(e.status)] ?? "text-slate-600"}`}>
                        {String(e.status) === "Active" ? <Radio className="h-3 w-3" /> : <WifiOff className="h-3 w-3" />}
                        {String(e.status)}
                      </span>
                    </td>
                    <td className="py-2 pr-4 text-xs text-slate-500">{formatDateTime(String(e.lastSyncAt ?? ""))}</td>
                    <td className="py-2 pr-4 text-xs font-mono text-slate-600">{String(e.firmwareVersion ?? "—")}</td>
                    <td className="py-2 pr-4">
                      {String(e.status) === "Malfunction" ? (
                        <button
                          type="button"
                          disabled={!canManage || resolveMalfMut.isPending}
                          className="rounded border border-emerald-300 bg-emerald-50 px-2 py-0.5 text-[10px] text-emerald-700 hover:bg-emerald-100 transition"
                          title={!canManage ? "Requires compliance update permission" : "Resolve malfunction"}
                          onClick={() => setResolveForm({ id: Number(e.id), rowVersion: Number(e.rowVersion), evidence: "", status: String(e.status) })}
                        >
                          Resolve
                        </button>
                      ) : String(e.status) === "Diagnostic" ? (
                        <div className="flex gap-1">
                          <button
                            type="button"
                            disabled={!canManage}
                            className="rounded border border-red-300 bg-red-50 px-2 py-0.5 text-[10px] text-red-700 hover:bg-red-100 transition"
                            title={!canManage ? "Requires compliance update permission" : "Record a confirmed malfunction from the diagnostic state"}
                            onClick={() => setMalfForm({ id: Number(e.id), rowVersion: Number(e.rowVersion), code: "", desc: "" })}
                          >
                            Mark Malfunction
                          </button>
                          <button
                            type="button"
                            disabled={!canManage || resolveMalfMut.isPending}
                            className="rounded border border-emerald-300 bg-emerald-50 px-2 py-0.5 text-[10px] text-emerald-700 hover:bg-emerald-100 transition"
                            title={!canManage ? "Requires compliance update permission" : "Verify recovery after a healthy provider sync"}
                            onClick={() => setResolveForm({ id: Number(e.id), rowVersion: Number(e.rowVersion), evidence: "", status: String(e.status) })}
                          >
                            Verify Recovery
                          </button>
                        </div>
                      ) : String(e.status) === "Active" ? (
                        <button
                          type="button"
                          disabled={!canManage}
                          className="rounded border border-red-300 bg-red-50 px-2 py-0.5 text-[10px] text-red-700 hover:bg-red-100 transition"
                          title={!canManage ? "Requires compliance update permission" : "Record reported malfunction"}
                          onClick={() => setMalfForm({ id: Number(e.id), rowVersion: Number(e.rowVersion), code: "", desc: "" })}
                        >
                          Mark Malfunction
                        </button>
                      ) : null}
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
        aiQ.isLoading ? <LoadingState /> : aiQ.isError ? (
          <ErrorState message={(aiQ.error as Error)?.message} onRetry={() => void aiQ.refetch()} />
        ) : aiRecs.length === 0 ? (
          <EmptyState title="No recommendations available" subtitle="No persisted HOS/ELD recommendations are available for this tenant." />
        ) : <div className="panel p-5 space-y-3">
          <p className="section-title flex items-center gap-2"><Zap className="h-3.5 w-3.5 text-violet-600" />HOS / ELD Recommendations</p>
          {aiRecs.map((rec, i) => {
            const prioBorder = String(rec.priority) === "Critical" ? "border-l-red-500" : String(rec.priority) === "High" ? "border-l-amber-500" : "border-l-sky-500";
            return (
            <div key={i} className={`rounded-xl border border-slate-200 border-l-4 ${prioBorder} bg-slate-50 p-4 space-y-1.5`}>
              <div className="flex items-start justify-between gap-2">
                <p className="text-sm font-semibold text-slate-900">{String(rec.title)}</p>
                <span className={`rounded border px-1.5 py-0.5 text-[10px] font-bold uppercase ${
                  String(rec.priority) === "Critical" ? "border-red-200 bg-red-50 text-red-700"
                  : String(rec.priority) === "High"   ? "border-amber-200 bg-amber-50 text-amber-700"
                  : "border-sky-200 bg-sky-50 text-sky-700"
                }`}>{String(rec.priority)}</span>
              </div>
              <p className="text-xs text-slate-600 leading-relaxed">{String(rec.description)}</p>
              {!!rec.actionLabel && (
                <p className="mt-1 text-xs font-semibold text-violet-700">
                  Recommended: {String(rec.actionLabel)}
                </p>
              )}
            </div>
            );
          })}
        </div>
      )}

      {/* Driver clock drawer */}
      {drawer && tab === "drivers" && (
        <div className="fixed inset-0 z-50 flex justify-end anim-fade-in">
          <div className="absolute inset-0 bg-black/30 backdrop-blur-sm" onClick={() => setDrawer(null)} />
          <aside ref={clockDialogRef} className="anim-slide-left relative z-10 w-full max-w-sm overflow-y-auto border-s border-slate-200 bg-white p-5 shadow-2xl" role="dialog" aria-modal="true" aria-label="Driver HOS clock details">
            <div className="flex items-center justify-between mb-4">
              <p className="font-semibold text-slate-900 flex items-center gap-2"><Clock className="h-4 w-4 text-teal-600" />HOS Clock</p>
              <button type="button" aria-label="Close" className="icon-btn" onClick={() => setDrawer(null)}><X className="h-4 w-4" /></button>
            </div>
            <div className="space-y-3 text-sm">
              <div>
                <p className="text-base font-bold text-slate-900">{String(drawer.driverName)}</p>
                <p className="text-xs text-slate-500">{String(drawer.driverCode)} · {String(drawer.cycleType)}</p>
              </div>
              <CountryBadge code={String(drawer.countryCode)} />
              {[
                { label: "Drive Remaining", value: clockLabel(minutes(drawer.driveTimeRemainingMinutes)) },
                { label: "Shift Remaining", value: clockLabel(minutes(drawer.shiftTimeRemainingMinutes)) },
                { label: "Cycle Remaining", value: clockLabel(minutes(drawer.cycleTimeRemainingMinutes)) },
                { label: "Profile",         value: drawer.profileName },
                { label: "Status",          value: drawer.status },
              ].map(r => (
                <div key={r.label} className="flex justify-between border-b border-slate-100 pb-2">
                  <span className="text-slate-500">{r.label}</span>
                  <span className={`font-semibold ${String(drawer.status) === "Violation" ? "text-red-600" : String(drawer.status) === "Warning" ? "text-amber-600" : "text-slate-900"}`}>{String(r.value ?? "—")}</span>
                </div>
              ))}
              {!!drawer.hosWarning && (
                <div className="rounded bg-amber-50 border border-amber-200 px-3 py-2 text-xs text-amber-700">
                  <AlertTriangle className="h-3 w-3 inline-block mr-1" />{String(drawer.hosWarning)}
                </div>
              )}
            </div>
          </aside>
        </div>
      )}

      {/* ELD malfunction modal */}
      {malfForm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center anim-fade-in">
          <div className="absolute inset-0 bg-black/30 backdrop-blur-sm" onClick={() => setMalfForm(null)} />
          <div ref={malfunctionDialogRef} className="panel relative z-10 w-full max-w-sm p-6 space-y-4" role="dialog" aria-modal="true" aria-label="Mark ELD malfunction">
            <div className="flex items-center justify-between">
              <p className="font-semibold text-slate-900 flex items-center gap-2"><Cpu className="h-4 w-4 text-red-600" />Mark ELD Malfunction</p>
              <button type="button" aria-label="Close" className="icon-btn" onClick={() => setMalfForm(null)}><X className="h-4 w-4" /></button>
            </div>
            <div className="space-y-3">
              <div>
                <label className="block text-xs font-medium text-slate-700 mb-1">Malfunction Code</label>
                <input className="field w-full" placeholder="e.g. P1, S1, E1" value={malfForm.code} onChange={e => setMalfForm(f => f ? { ...f, code: e.target.value } : f)} />
              </div>
              <div>
                <label className="block text-xs font-medium text-slate-700 mb-1">Description</label>
                <textarea className="field w-full h-20 resize-none" placeholder="Describe the malfunction..." value={malfForm.desc} onChange={e => setMalfForm(f => f ? { ...f, desc: e.target.value } : f)} />
              </div>
              <div className="rounded border border-amber-200 bg-amber-50 p-2 text-[11px] text-amber-700">
                Follow the carrier&apos;s applicable jurisdiction and connected ELD provider procedure. In U.S. FMCSA operations, this generally includes documenting the malfunction and maintaining required records by an allowed alternate method until resolved.
              </div>
            </div>
            <div className="flex gap-2">
              <button type="button" className="flex-1 btn-ghost" onClick={() => setMalfForm(null)}>Cancel</button>
              <button
                type="button"
                disabled={!malfForm.code.trim() || !malfForm.desc.trim() || markMalfMut.isPending}
                className="flex-1 rounded-lg bg-red-600 hover:bg-red-700 text-white text-sm font-semibold py-2 transition"
                onClick={() => {
                  void markSingleFlight(() => markMalfMut.mutateAsync({ id: malfForm.id, body: { rowVersion: malfForm.rowVersion, malfunctionCode: malfForm.code.trim(), malfunctionDescription: malfForm.desc.trim() } }).then(() => {
                    setActionMessage({ kind: "success", text: "ELD malfunction recorded." }); setMalfForm(null);
                  }).catch((error) => {
                    setActionMessage({ kind: "error", text: error instanceof Error ? error.message : "ELD malfunction update failed." });
                    throw error;
                  }));
                }}
              >
                <Activity className="h-3.5 w-3.5 inline-block mr-1" />Confirm Malfunction
              </button>
            </div>
          </div>
        </div>
      )}
      {resolveForm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center anim-fade-in">
          <button type="button" className="absolute inset-0 bg-black/30 backdrop-blur-sm" aria-label="Close resolution dialog" onClick={() => setResolveForm(null)} />
          <div ref={resolutionDialogRef} className="panel relative z-10 w-full max-w-md p-6 space-y-4" role="dialog" aria-modal="true" aria-label="Resolve ELD malfunction">
            <div className="flex items-center justify-between">
              <p className="font-semibold text-slate-900 flex items-center gap-2"><Cpu className="h-4 w-4 text-emerald-600" />{resolveForm.status === "Diagnostic" ? "Verify ELD Recovery" : "Resolve ELD Malfunction"}</p>
              <button type="button" aria-label="Close" className="icon-btn" onClick={() => setResolveForm(null)}><X className="h-4 w-4" /></button>
            </div>
            <label className="block"><span className="mb-1 block text-xs font-medium text-slate-700">Operational verification evidence</span><textarea className="field min-h-28 w-full" maxLength={2000} value={resolveForm.evidence} onChange={(event) => setResolveForm((current) => current ? { ...current, evidence: event.target.value } : current)} placeholder="Describe the diagnostic check, provider confirmation, test drive, or other evidence…" /></label>
            <p className="text-[11px] leading-5 text-amber-700">Evidence is retained with the malfunction history. The device returns to Active only with valid provisioned credentials and a healthy provider sync within the last 15 minutes; otherwise it remains Diagnostic.</p>
            <div className="flex gap-2"><button type="button" className="btn-ghost flex-1" onClick={() => setResolveForm(null)}>Cancel</button><button type="button" className="btn-primary flex-1" disabled={!resolveForm.evidence.trim() || resolveMalfMut.isPending} onClick={() => { void resolveSingleFlight(() => resolveMalfMut.mutateAsync({ id: resolveForm.id, body: { rowVersion: resolveForm.rowVersion, resolutionEvidence: resolveForm.evidence.trim() } }).then((data) => {
              setActionMessage({ kind: "success", text: `Resolution recorded. Device status: ${String((data as AnyRecord).status ?? "Diagnostic")}.` }); setResolveForm(null);
            }).catch((error) => {
              setActionMessage({ kind: "error", text: error instanceof Error ? error.message : "ELD resolution failed." });
              throw error;
            })); }}>Record resolution</button></div>
          </div>
        </div>
      )}
    </div>
  );
}

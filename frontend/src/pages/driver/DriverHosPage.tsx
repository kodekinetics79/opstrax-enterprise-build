import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, CheckCircle, Clock, XCircle } from "lucide-react";
import { driverApi } from "@/services/driverApi";
import { useDialogFocus } from "@/hooks/useDialogFocus";
import { useSingleFlight } from "@/hooks/useSingleFlight";
import type { AnyRecord } from "@/types";

function HosBar({ label, hours, maxHours }: { label: string; hours: number | null; maxHours: number }) {
  const pct   = hours != null ? Math.max(0, Math.min(100, (hours / maxHours) * 100)) : 0;
  const color = hours == null ? "bg-slate-300" :
                hours < 1    ? "bg-red-500" :
                hours < 3    ? "bg-amber-500" :
                "bg-teal-500";
  return (
    <div>
      <div className="flex justify-between mb-1">
        <p className="text-xs font-semibold text-slate-500">{label}</p>
        <p className={`text-xs font-bold ${
          hours == null ? "text-slate-400" :
          hours < 1     ? "text-red-600" :
          hours < 3     ? "text-amber-600" :
          "text-teal-600"
        }`}>
          {hours != null ? `${Number(hours).toFixed(1)}h remaining` : "N/A"}
        </p>
      </div>
      <div className="h-3 w-full rounded-full bg-slate-100 overflow-hidden">
        <div className={`h-full rounded-full transition-all ${color}`} style={{ width: `${pct}%` }} />
      </div>
    </div>
  );
}

export function DriverHosPage() {
  const { data, isLoading, isError, error, refetch, isFetching } = useQuery<AnyRecord>({
    queryKey: ["driver", "hos"],
    queryFn: driverApi.hos,
    refetchInterval: 60_000,
  });

  if (isLoading) {
    return (
      <div className="flex flex-col items-center justify-center gap-4 p-12">
        <Clock className="h-12 w-12 animate-pulse text-slate-300" />
        <p className="text-sm text-slate-500">Loading HOS data…</p>
      </div>
    );
  }

  if (isError) {
    return (
      <div className="flex flex-col items-center justify-center gap-4 p-12" role="alert">
        <XCircle className="h-10 w-10 text-red-400" />
        <p className="text-sm font-medium text-red-700">{(error as Error)?.message}</p>
        <button type="button" className="rounded-xl border border-red-300 px-4 py-2 text-sm font-bold text-red-700 disabled:opacity-50" disabled={isFetching} onClick={() => void refetch()}>{isFetching ? "Retrying…" : "Retry"}</button>
      </div>
    );
  }

  const d = data ?? {};

  if (!d["dataAvailable"]) {
    return (
      <div className="p-4 space-y-4">
        <div className="pt-2">
          <p className="text-xs font-bold uppercase tracking-widest text-slate-400">Hours of Service</p>
          <h1 className="mt-1 text-xl font-bold text-slate-900">HOS</h1>
        </div>
        <div className="rounded-2xl border border-amber-200 bg-amber-50 p-6 text-center space-y-3">
          <AlertTriangle className="h-10 w-10 text-amber-500 mx-auto" />
          <p className="font-bold text-amber-800">HOS Data Unavailable</p>
          <p className="text-sm text-amber-700">{String(d["message"] ?? "ELD device is not paired or has not synced.")}</p>
        </div>
        {(d["guidance"] as AnyRecord[] ?? []).map((g, i) => (
          <div key={i} className="flex items-start gap-3 rounded-2xl border border-amber-200 bg-amber-50 p-4">
            <AlertTriangle className="h-4 w-4 text-amber-600 mt-0.5 shrink-0" />
            <p className="text-sm text-amber-800">{String(g["message"])}</p>
          </div>
        ))}
        <DailyCertificationPanel />
      </div>
    );
  }

  const driveHours = d["remainingDriveHours"]  != null ? Number(d["remainingDriveHours"])  : null;
  const shiftHours = d["remainingShiftHours"]  != null ? Number(d["remainingShiftHours"])  : null;
  const cycleHours = d["remainingCycleHours"]  != null ? Number(d["remainingCycleHours"])  : null;
  const warnings   = (d["warnings"] as AnyRecord[]) ?? [];

  return (
    <div className="p-4 space-y-4 pb-10">
      <div className="pt-2">
        <p className="text-xs font-bold uppercase tracking-widest text-slate-400">Hours of Service</p>
        <h1 className="mt-1 text-xl font-bold text-slate-900">HOS</h1>
        <p className="text-sm text-slate-400">
          Status: <span className="font-semibold text-slate-600">{String(d["hosStatus"] ?? "—")}</span>
        </p>
      </div>

      {/* Warnings */}
      {warnings.map((w, i) => (
        <div key={i} className={`flex items-start gap-3 rounded-2xl border p-4 ${
          String(w["level"]) === "critical"
            ? "bg-red-50 border-red-300 text-red-800"
            : "bg-amber-50 border-amber-300 text-amber-800"
        }`}>
          <AlertTriangle className="h-4 w-4 mt-0.5 shrink-0" />
          <p className="text-sm font-medium">{String(w["message"])}</p>
        </div>
      ))}

      {/* HOS bars */}
      <div className="rounded-2xl border border-slate-200 bg-white p-5 space-y-5">
        <HosBar label="Drive Time"  hours={driveHours} maxHours={11} />
        <HosBar label="Shift Time"  hours={shiftHours} maxHours={14} />
        <HosBar label="Cycle Hours" hours={cycleHours} maxHours={70} />
      </div>

      {/* Detail card */}
      <div className="rounded-2xl border border-slate-200 bg-white p-4 space-y-2">
        <p className="text-xs font-bold uppercase tracking-wider text-slate-400 mb-2">Details</p>
        <Row label="Shift Date"     value={d["shiftDate"] != null ? new Date(String(d["shiftDate"])).toLocaleDateString() : "—"} />
        <Row label="ELD Device"     value={d["eldIdentifier"] != null ? String(d["eldIdentifier"]) : "Not paired"} />
        <Row label="HOS Status"     value={String(d["hosStatus"] ?? "—")} />
      </div>

      <DailyCertificationPanel />

      <p className="text-xs text-center text-slate-400">
        HOS data from ELD sync. You are responsible for accurate hours compliance under FMCSA regulations.
      </p>
    </div>
  );
}

const HOS_ATTESTATION = "I certify that this daily HOS record is true and correct.";

function normalizedLogDate(value: unknown): string {
  if (value == null || value === "") return "";
  const parsed = new Date(String(value));
  return Number.isNaN(parsed.getTime()) ? "" : parsed.toISOString().slice(0, 10);
}

function DailyCertificationPanel() {
  const qc = useQueryClient();
  const [selected, setSelected] = useState<{ id: number; date: string } | null>(null);
  const [accepted, setAccepted] = useState(false);
  useDialogFocus<HTMLDivElement>(selected != null, () => setSelected(null));
  const certifySingleFlight = useSingleFlight();
  const logsQ = useQuery<AnyRecord[]>({ queryKey: ["driver", "hos-logs"], queryFn: driverApi.hosLogs });
  const certify = useMutation({
    mutationFn: (id: number) => driverApi.certifyHosDay(id),
    onSuccess: async () => { setSelected(null); setAccepted(false); await qc.invalidateQueries({ queryKey: ["driver", "hos-logs"] }); },
  });
  if (logsQ.isLoading) return <div className="rounded-2xl border border-slate-200 bg-white p-4 text-sm text-slate-500">Loading daily HOS records…</div>;
  if (logsQ.isError) return <div className="rounded-2xl border border-red-200 bg-red-50 p-4 text-sm text-red-700" role="alert"><p>Daily HOS records are unavailable. No certification state has been inferred: {(logsQ.error as Error)?.message}</p><button type="button" className="mt-3 rounded-xl border border-red-200 bg-white px-3 py-2 text-xs font-bold disabled:opacity-40" disabled={logsQ.isFetching} onClick={() => void logsQ.refetch()}>{logsQ.isFetching ? "Retrying…" : "Retry daily records"}</button></div>;
  const byDay = new Map<string, AnyRecord[]>();
  for (const row of logsQ.data ?? []) {
    const date = normalizedLogDate(row["logDate"] ?? row["log_date"]);
    byDay.set(date, [...(byDay.get(date) ?? []), row]);
  }
  return <section className="rounded-2xl border border-slate-200 bg-white p-4 space-y-3">
    <div><p className="text-xs font-bold uppercase tracking-wider text-slate-400">Daily records</p><p className="mt-1 text-xs text-slate-500">Certification records your attestation in OpsTrax. It does not submit data to FMCSA or another regulator.</p></div>
    {byDay.size === 0 ? <p className="text-sm text-slate-500">No persisted HOS duty-status records are available.</p> : [...byDay.entries()].map(([date, rows]) => {
      const certified = rows.every((row) => Boolean(row["isCertified"] ?? row["is_certified"]));
      const invalid = rows.some((row) => !row["endTime"] && !row["end_time"] || Boolean(row["durationMismatch"] ?? row["duration_mismatch"]));
      return <div key={date} className="flex items-center justify-between gap-3 rounded-xl border border-slate-200 p-3"><div><p className="text-sm font-semibold text-slate-800">{date ? new Date(`${date}T12:00:00`).toLocaleDateString() : "Unknown date"}</p><p className="text-xs text-slate-500">{rows.length} duty-status segment{rows.length === 1 ? "" : "s"}</p></div>{certified ? <span className="flex items-center gap-1 text-xs font-semibold text-emerald-700"><CheckCircle className="h-4 w-4" />Certified</span> : <button type="button" className="rounded-xl bg-teal-600 px-3 py-2 text-xs font-bold text-white disabled:opacity-40" disabled={invalid} title={invalid ? "Close/correct all segments before certification" : "Review and certify this complete day"} onClick={() => { setAccepted(false); setSelected({ id: Number(rows[0].id), date }); }}>Review &amp; certify</button>}</div>;
    })}
    {certify.isError && <p className="text-sm text-red-700" role="alert">{(certify.error as Error)?.message}</p>}
    {selected && <div className="fixed inset-0 z-50 grid place-items-center bg-black/60 p-4" role="dialog" aria-modal="true" aria-label="Certify daily HOS record"><div className="w-full max-w-md rounded-2xl bg-white p-5 shadow-2xl space-y-4"><div><h2 className="font-bold text-slate-900">Certify {selected.date}</h2><p className="mt-1 text-sm text-slate-600">Review the complete daily record before accepting this attestation.</p></div><label className="flex items-start gap-3 rounded-xl border border-slate-200 p-3"><input type="checkbox" className="mt-1 h-4 w-4" checked={accepted} onChange={(event) => setAccepted(event.target.checked)} /><span className="text-sm font-medium text-slate-800">{HOS_ATTESTATION}</span></label><div className="flex gap-2"><button type="button" className="flex-1 rounded-xl border border-slate-200 py-3 text-sm font-bold text-slate-600" onClick={() => setSelected(null)}>Cancel</button><button type="button" className="flex-1 rounded-xl bg-teal-600 py-3 text-sm font-bold text-white disabled:opacity-40" disabled={!accepted || certify.isPending} onClick={() => { void certifySingleFlight(() => certify.mutateAsync(selected.id)); }}>{certify.isPending ? "Certifying…" : "Certify daily record"}</button></div></div></div>}
  </section>;
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between text-sm">
      <span className="text-slate-500">{label}</span>
      <span className="font-semibold text-slate-800">{value}</span>
    </div>
  );
}

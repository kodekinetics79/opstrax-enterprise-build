import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, Clock, XCircle } from "lucide-react";
import { driverApi } from "@/services/driverApi";
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
  const { data, isLoading, isError, error } = useQuery<AnyRecord>({
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
      <div className="flex flex-col items-center justify-center gap-4 p-12">
        <XCircle className="h-10 w-10 text-red-400" />
        <p className="text-sm font-medium text-red-700">{(error as Error)?.message}</p>
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

      <p className="text-xs text-center text-slate-400">
        HOS data from ELD sync. You are responsible for accurate hours compliance under FMCSA regulations.
      </p>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between text-sm">
      <span className="text-slate-500">{label}</span>
      <span className="font-semibold text-slate-800">{value}</span>
    </div>
  );
}

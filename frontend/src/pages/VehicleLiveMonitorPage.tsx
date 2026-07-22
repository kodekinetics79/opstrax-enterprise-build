import type { ReactNode } from "react";
import { useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { Activity, Gauge, MapPin, Navigation, User, AlertTriangle, Fuel, Power, RefreshCw } from "lucide-react";
import { telemetryApi } from "@/services/telemetryApi";
import { ErrorState, LoadingState, PageHeader } from "@/components/ui";
import type { AnyRecord } from "@/types";

// Per-vehicle live monitor — a standalone screen for ONE vehicle, auto-refreshing every few seconds.
// Because it is its own URL (/vehicles/:id/live), an ops team can open a different vehicle in each
// window / monitor for a NOC-style wall (this is the "monitor individual vehicles on different screens"
// capability; the video-wall variant needs the dashcam pipeline, which is out of scope here).
export function VehicleLiveMonitorPage() {
  const { id } = useParams<{ id: string }>();
  const { data, isLoading, isError, refetch, isFetching, dataUpdatedAt } = useQuery({
    queryKey: ["vehicle-live", id],
    queryFn: () => telemetryApi.liveState(id!),
    refetchInterval: 5000,
    enabled: Boolean(id),
  });

  if (isLoading) return <LoadingState />;
  if (isError || !data) return <ErrorState message="This vehicle has no live state yet." onRetry={() => { void refetch(); }} />;

  const r = data as AnyRecord;
  const fresh = freshness(String(r.telemetryStatus ?? ""), Number(r.staleSeconds ?? 0));

  return (
    <div className="space-y-6">
      <PageHeader
        title={`${r.vehicleCode ?? `Vehicle ${id}`} · Live Monitor`}
        description={r.driverName ? `Driver: ${r.driverName}` : "Unassigned driver"}
        actions={
          <button onClick={() => refetch()} className="inline-flex items-center gap-2 rounded-lg border border-slate-200 px-3 py-1.5 text-sm text-slate-600 hover:bg-slate-50">
            <RefreshCw className={`h-4 w-4 ${isFetching ? "animate-spin" : ""}`} /> Refresh
          </button>
        }
      />

      {/* Freshness banner — the single most important signal on a live screen. */}
      <div className={`flex items-center justify-between rounded-xl border px-4 py-3 ${fresh.wrap}`}>
        <div className="flex items-center gap-2 font-medium">
          <Activity className="h-5 w-5" /> {fresh.label}
        </div>
        <div className="text-sm opacity-80">
          last fix {Number(r.staleSeconds ?? 0)}s ago · updated {new Date(dataUpdatedAt).toLocaleTimeString()}
        </div>
      </div>

      <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
        <Tile icon={<Gauge className="h-5 w-5" />} label="Speed" value={`${fmt(r.speedMph)} mph`} />
        <Tile icon={<Navigation className="h-5 w-5" />} label="Heading" value={`${fmt(r.heading)}°`} />
        <Tile icon={<Power className="h-5 w-5" />} label="Engine" value={String(r.engineStatus ?? "—")} />
        <Tile icon={<Fuel className="h-5 w-5" />} label="Fuel" value={r.fuelLevel != null ? `${fmt(r.fuelLevel)}%` : "—"} />
        <Tile icon={<MapPin className="h-5 w-5" />} label="Latitude" value={fmt(r.lat, 5)} />
        <Tile icon={<MapPin className="h-5 w-5" />} label="Longitude" value={fmt(r.lng, 5)} />
        <Tile icon={<AlertTriangle className="h-5 w-5" />} label="Open alerts" value={String(r.openAlertCount ?? 0)}
              tone={Number(r.openAlertCount ?? 0) > 0 ? "warn" : "ok"} />
        <Tile icon={<User className="h-5 w-5" />} label="Risk" value={String(r.riskLevel ?? "low")}
              tone={String(r.riskLevel ?? "").toLowerCase() === "high" ? "warn" : "ok"} />
      </div>

      {r.nextAction ? (
        <div className="rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          <span className="font-semibold">Next action: </span>{String(r.nextAction)}
        </div>
      ) : null}

      <p className="text-xs text-slate-400">
        Auto-refreshing every 5s. Open this vehicle's URL in a separate window to watch it on its own monitor.
      </p>
    </div>
  );
}

function Tile({ icon, label, value, tone = "ok" }: { icon: ReactNode; label: string; value: string; tone?: "ok" | "warn" }) {
  return (
    <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
      <div className={`mb-1 flex items-center gap-1.5 text-xs font-medium uppercase tracking-wide ${tone === "warn" ? "text-orange-500" : "text-slate-400"}`}>
        {icon} {label}
      </div>
      <div className="text-2xl font-semibold text-slate-800">{value}</div>
    </div>
  );
}

function freshness(status: string, staleSeconds: number): { label: string; wrap: string } {
  const s = status.toLowerCase();
  if (s === "offline" || staleSeconds > 900) return { label: "Offline", wrap: "border-red-200 bg-red-50 text-red-700" };
  if (s === "stale" || staleSeconds > 120) return { label: "Stale signal", wrap: "border-amber-200 bg-amber-50 text-amber-700" };
  return { label: "Live", wrap: "border-emerald-200 bg-emerald-50 text-emerald-700" };
}

function fmt(v: unknown, digits = 1): string {
  const n = Number(v);
  return Number.isFinite(n) ? n.toFixed(digits) : "—";
}

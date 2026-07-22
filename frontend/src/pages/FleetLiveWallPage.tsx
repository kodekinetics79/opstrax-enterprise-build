import { useQuery } from "@tanstack/react-query";
import { Gauge, ExternalLink, RefreshCw } from "lucide-react";
import { telemetryApi } from "@/services/telemetryApi";
import { EmptyState, ErrorState, LoadingState, PageHeader } from "@/components/ui";
import type { AnyRecord } from "@/types";

// Fleet live wall — every vehicle's live state as a compact panel, auto-refreshing. Each tile opens the
// single-vehicle monitor (/vehicles/:id/live) in a new window, so an operations team can spread specific
// vehicles across separate screens/monitors. Telemetry-only (no video).
export function FleetLiveWallPage() {
  const { data, isLoading, isError, refetch, isFetching } = useQuery({
    queryKey: ["fleet-live-wall"],
    queryFn: () => telemetryApi.liveStates(),
    refetchInterval: 5000,
  });

  if (isLoading) return <LoadingState />;
  if (isError) return <ErrorState message="Could not load live telemetry." onRetry={() => { void refetch(); }} />;

  const rows = (data as AnyRecord[] | undefined) ?? [];

  return (
    <div className="space-y-6">
      <PageHeader
        title="Fleet Live Wall"
        description={`${rows.length} vehicle${rows.length === 1 ? "" : "s"} · auto-refreshing`}
        actions={
          <button onClick={() => refetch()} className="inline-flex items-center gap-2 rounded-lg border border-slate-200 px-3 py-1.5 text-sm text-slate-600 hover:bg-slate-50">
            <RefreshCw className={`h-4 w-4 ${isFetching ? "animate-spin" : ""}`} /> Refresh
          </button>
        }
      />

      {rows.length === 0 ? (
        <EmptyState title="No live vehicles" subtitle="No vehicles are reporting live telemetry yet." />
      ) : (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
          {rows.map((r) => {
            const fresh = freshness(String(r.telemetryStatus ?? ""), Number(r.staleSeconds ?? 0));
            return (
              <a
                key={String(r.vehicleId ?? r.id)}
                href={`/vehicles/${r.vehicleId ?? r.id}/live`}
                target="_blank"
                rel="noreferrer"
                className="group rounded-xl border border-slate-200 bg-white p-4 shadow-sm transition hover:border-slate-300 hover:shadow-md"
              >
                <div className="mb-2 flex items-center justify-between">
                  <span className="font-semibold text-slate-800">{String(r.vehicleCode ?? `Vehicle ${r.vehicleId ?? r.id}`)}</span>
                  <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${fresh.badge}`}>{fresh.label}</span>
                </div>
                <div className="mb-2 flex items-center gap-1.5 text-2xl font-semibold text-slate-800">
                  <Gauge className="h-5 w-5 text-slate-400" /> {fmt(r.speedMph)} <span className="text-sm font-normal text-slate-400">mph</span>
                </div>
                <div className="flex items-center justify-between text-xs text-slate-500">
                  <span>{r.driverName ? String(r.driverName) : "Unassigned"}</span>
                  <span>{Number(r.staleSeconds ?? 0)}s ago</span>
                </div>
                {Number(r.openAlertCount ?? 0) > 0 ? (
                  <div className="mt-2 text-xs font-medium text-orange-600">{String(r.openAlertCount)} open alert(s)</div>
                ) : null}
                <div className="mt-3 flex items-center gap-1 text-xs text-slate-400 opacity-0 transition group-hover:opacity-100">
                  <ExternalLink className="h-3.5 w-3.5" /> open on its own screen
                </div>
              </a>
            );
          })}
        </div>
      )}
    </div>
  );
}

function freshness(status: string, staleSeconds: number): { label: string; badge: string } {
  const s = status.toLowerCase();
  if (s === "offline" || staleSeconds > 900) return { label: "Offline", badge: "bg-red-100 text-red-700" };
  if (s === "stale" || staleSeconds > 120) return { label: "Stale", badge: "bg-amber-100 text-amber-700" };
  return { label: "Live", badge: "bg-emerald-100 text-emerald-700" };
}

function fmt(v: unknown, digits = 0): string {
  const n = Number(v);
  return Number.isFinite(n) ? n.toFixed(digits) : "—";
}

import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient, unwrap } from "@/services/apiClient";
import { type LogisticsOverview, type LogisticsRoute, type LogisticsStop } from "@/services/logisticsApi";
import { exportCsv, LoadingState, ErrorState, EmptyState, StatusBadge } from "@/components/ui";
import type { AnyRecord } from "@/types";

// ── API ───────────────────────────────────────────────────────────────────────

const lastMileApi = {
  overview: () => unwrap<LogisticsOverview>(apiClient.get("/api/fleet-tms/logistics/overview")),
  routes: () => unwrap<{ items: LogisticsRoute[] }>(apiClient.get("/api/fleet-tms/logistics/routes", { params: { status: "Active" } })),
  routeStops: (id: string | number) => unwrap<{ items: LogisticsStop[] }>(apiClient.get(`/api/fleet-tms/logistics/routes/${id}/stops`)),
  sendEta: (jobId: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/customer-eta/${jobId}/send`, {})),
};

// ── Stop row ──────────────────────────────────────────────────────────────────

function StopRow({ stop, index }: { stop: AnyRecord; index: number }) {
  const status = String(stop.status ?? "Pending");
  const proof = String(stop.proofStatus ?? "Not Required");
  const isDone = status === "Completed";

  const dotColor =
    isDone ? "bg-teal-500" :
    status === "En Route" || status === "Arrived" ? "bg-amber-400" :
    status === "Failed" ? "bg-red-500" :
    "bg-slate-300";

  return (
    <div className={`flex gap-3 relative ${index === 0 ? "" : ""}`}>
      {/* Timeline spine */}
      <div className="flex flex-col items-center">
        <div className={`w-3 h-3 rounded-full shrink-0 mt-0.5 ${dotColor}`} />
        <div className="w-px flex-1 bg-slate-200 mt-1" />
      </div>

      <div className={`pb-4 flex-1 min-w-0 ${isDone ? "opacity-60" : ""}`}>
        <div className="flex items-start justify-between gap-2 flex-wrap">
          <div>
            <span className="text-xs font-semibold text-slate-500">#{String(stop.stopSequence ?? stop.sequenceNo ?? "--")} · {String(stop.stopType ?? "Delivery")}</span>
            <p className="text-sm font-medium text-slate-900 mt-0.5">{String(stop.customerName ?? stop.customer ?? "Customer")}</p>
            <p className="text-xs text-slate-500 mt-0.5">{String(stop.address ?? stop.addressLine ?? "Address")}</p>
          </div>
          <div className="flex flex-col items-end gap-1">
            <StatusBadge status={stop.status} />
            {proof === "Captured" && (
              <span className="text-xs px-2 py-0.5 rounded-full bg-teal-50 border border-teal-200 text-teal-700 font-medium">POD ✓</span>
            )}
            {proof === "Pending" && (
              <span className="text-xs px-2 py-0.5 rounded-full bg-amber-50 border border-amber-200 text-amber-700 font-medium">POD pending</span>
            )}
          </div>
        </div>
        <div className="flex gap-4 mt-1.5 text-xs text-slate-400">
          {stop.eta ? <span>ETA {String(stop.eta)}</span> : null}
          {stop.etaUtc ? <span>ETA {new Date(String(stop.etaUtc)).toLocaleString()}</span> : null}
          {stop.timeWindowStart ? <span>Window {String(stop.timeWindowStart)}–{String(stop.timeWindowEnd ?? "")}</span> : null}
          {stop.timeWindow ? <span>Window {String(stop.timeWindow)}</span> : null}
        </div>
        {stop.notes ? <p className="text-xs text-slate-500 mt-1 italic">{String(stop.notes)}</p> : null}
      </div>
    </div>
  );
}

// ── Route card ────────────────────────────────────────────────────────────────

function RouteCard({ route, selected, onSelect }: { route: AnyRecord; selected: boolean; onSelect: () => void }) {
  const status = String(route.status ?? "");
  const risk = String(route.slaRisk ?? route.sla_risk ?? "Low");
  const planned = Number(route.plannedStops ?? route.planned_stops ?? 0);
  const completed = Number(route.completedStops ?? route.completed_stops ?? 0);
  const pct = Number(route.completionPercent ?? route.completion_percent ?? (planned > 0 ? Math.round((completed / planned) * 100) : 0));

  const riskColor =
    risk === "High" ? "text-red-600" :
    risk === "Medium" ? "text-amber-600" :
    "text-teal-600";

  const statusColor =
    status === "Active" ? "bg-teal-50 border-teal-300 text-teal-700" :
    status === "Delayed" ? "bg-red-50 border-red-300 text-red-700" :
    status === "Completed" ? "bg-slate-100 border-slate-300 text-slate-600" :
    "bg-slate-50 border-slate-200 text-slate-600";

  return (
    <button
      type="button"
      className={`w-full text-left rounded-xl border p-4 transition-colors hover:bg-slate-50 ${
        selected ? "border-teal-400 bg-teal-50" : "border-slate-200 bg-white"
      }`}
      onClick={onSelect}
    >
      <div className="flex items-start justify-between gap-2">
        <div>
          <p className="font-semibold text-slate-900 text-sm">{String(route.routeCode ?? route.routeName ?? `Route ${route.id}`)}</p>
          <p className="text-xs text-slate-500 mt-0.5">{String(route.driverName ?? "--")} · {String(route.vehicleNumber ?? route.vehicleCode ?? "--")}</p>
        </div>
        <span className={`text-xs px-2 py-0.5 rounded-full border font-medium shrink-0 ${statusColor}`}>{status}</span>
      </div>

      {/* Progress bar */}
      <div className="mt-3">
        <div className="flex items-center justify-between text-xs text-slate-500 mb-1">
          <span>{completed}/{planned || completed} stops</span>
          <span>{pct}%</span>
        </div>
        <div className="h-1.5 rounded-full bg-slate-100 overflow-hidden">
          <div
            className={`h-full rounded-full transition-all ${
              pct === 100 ? "bg-teal-500" : status === "Delayed" ? "bg-red-400" : "bg-teal-400"
            }`}
            style={{ width: `${pct}%` }}
          />
        </div>
      </div>

      <div className="flex items-center justify-between mt-2.5 text-xs">
        <span className="text-slate-400">
          {route.plannedStartTime ? new Date(String(route.plannedStartTime)).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }) : route.departureTimeUtc ? new Date(String(route.departureTimeUtc)).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }) : "--"} → {route.plannedEndTime ? new Date(String(route.plannedEndTime)).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }) : route.etaCompleteUtc ? new Date(String(route.etaCompleteUtc)).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }) : "--"}
        </span>
        <span className={`font-semibold ${riskColor}`}>{risk} risk</span>
      </div>
    </button>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

export function LastMileDeliveryPage() {
  const qc = useQueryClient();
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [statusFilter, setStatusFilter] = useState<string>("All");
  const [search, setSearch] = useState("");
  const [toast, setToast] = useState<string | null>(null);

  const overviewQ = useQuery({ queryKey: ["last-mile", "overview"], queryFn: lastMileApi.overview });
  const routesQ = useQuery({ queryKey: ["last-mile", "routes"], queryFn: lastMileApi.routes, refetchInterval: 20_000 });

  const etaMutation = useMutation({
    mutationFn: (jobId: string | number) => lastMileApi.sendEta(jobId),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["last-mile"] });
      showToast("ETA notification sent to customer");
    },
  });

  function showToast(msg: string) {
    setToast(msg);
    setTimeout(() => setToast(null), 3500);
  }

  const routes = (routesQ.data?.items ?? []) as unknown as AnyRecord[];
  const s = (overviewQ.data?.summary ?? {}) as AnyRecord;

  const filtered = routes.filter((r) => {
    if (statusFilter !== "All" && r.status !== statusFilter) return false;
    if (search) {
      const q = search.toLowerCase();
      return (
        String(r.routeCode ?? r.routeName ?? "").toLowerCase().includes(q) ||
        String(r.driverName ?? "").toLowerCase().includes(q) ||
        String(r.vehicleNumber ?? r.vehicleCode ?? "").toLowerCase().includes(q)
      );
    }
    return true;
  });

  const selectedRoute = routes.find((r) => Number(r.id) === selectedId) ?? null;
  const routeStopsQ = useQuery({
    queryKey: ["last-mile", "route-stops", selectedId],
    queryFn: () => (selectedId ? lastMileApi.routeStops(selectedId) : Promise.resolve({ items: [] as LogisticsStop[] })),
    enabled: selectedId !== null,
  });
  const stops = (routeStopsQ.data?.items ?? []) as unknown as AnyRecord[];

  useEffect(() => {
    if (selectedId !== null || routes.length === 0) return;
    setSelectedId(Number(routes[0].id));
  }, [routes, selectedId]);

  const flatStops = stops.map((s) => ({
    route: String(selectedRoute?.routeCode ?? selectedRoute?.routeName ?? selectedRoute?.id ?? "--"),
    driver: String(selectedRoute?.driverName ?? ""),
    vehicle: String(selectedRoute?.vehicleNumber ?? selectedRoute?.vehicleCode ?? ""),
    stop: s.sequenceNo ?? s.stopSequence,
    type: s.stopType,
    customer: s.customerName,
    address: s.addressLine ?? s.address,
    eta: s.etaUtc ?? s.eta,
    status: s.status,
    pod: s.proofStatus,
  }));

  if (routesQ.isLoading || overviewQ.isLoading) return <LoadingState />;
  if (routesQ.isError) return <ErrorState message={(routesQ.error as Error)?.message} />;
  if (overviewQ.isError) return <ErrorState message={(overviewQ.error as Error)?.message} />;

  return (
    <div className="control-tower flex flex-col gap-6 py-6">
      {toast && (
        <div className="fixed top-4 right-4 z-50 bg-teal-600 text-white text-sm font-medium px-4 py-2.5 rounded-lg shadow-lg">
          {toast}
        </div>
      )}

      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Last Mile Delivery</h1>
          <p className="text-sm text-slate-500 mt-0.5">Real-time stop sequencing, ETA management, proof of delivery and customer notification</p>
        </div>
        <button
          type="button"
          className="btn-secondary text-sm"
          onClick={() => exportCsv("last-mile-delivery", flatStops)}
        >
          Export CSV
        </button>
      </div>

      {/* KPI strip */}
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Active Routes", val: s.activeRoutes ?? routes.filter((r) => r.status === "Active").length, accent: "text-teal-600" },
          { label: "Planned Routes", val: s.plannedRoutes ?? routes.filter((r) => r.status === "Planned").length },
          { label: "Delayed", val: s.delayedRoutes ?? routes.filter((r) => r.status === "Delayed").length, accent: "text-red-600" },
          { label: "Completed", val: s.completedRoutes ?? routes.filter((r) => r.status === "Completed").length, accent: "text-slate-600" },
          { label: "Avg Stops / Route", val: s.averageStopsPerRoute ?? "--" },
          { label: "Route Efficiency", val: s.routeEfficiencyScore ? `${s.routeEfficiencyScore}%` : "--", accent: "text-violet-600" },
          { label: "High Risk Routes", val: s.highRiskRoutes ?? 0, accent: "text-red-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-30">
            <span className={`text-2xl font-bold ${accent ?? "text-slate-900"}`}>{String(val)}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>

      {/* Filter bar */}
      <div className="panel flex flex-wrap gap-3 items-center">
        <div className="flex gap-1.5">
          {(["All", "Active", "Planned", "Delayed", "Completed"] as const).map((f) => (
            <button
              key={f}
              type="button"
              onClick={() => setStatusFilter(f)}
              className={`px-3 py-1.5 rounded-lg text-sm font-medium border transition-colors ${
                statusFilter === f
                  ? "bg-teal-50 border-teal-300 text-teal-700"
                  : "bg-slate-50 border-slate-200 text-slate-600 hover:bg-slate-100"
              }`}
            >
              {f}
            </button>
          ))}
        </div>
        <input
          type="search"
          placeholder="Search routes, drivers…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="ml-auto border border-slate-200 rounded-lg px-3 py-1.5 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400 w-52"
        />
      </div>

      {/* Split: route list + stop detail */}
      <div className="grid gap-4 xl:grid-cols-[380px_1fr]">
        {/* Route list */}
        <div className="flex flex-col gap-2">
          {filtered.length === 0 ? (
            <EmptyState title="No routes match your filters" subtitle="There are no live routes for this company yet, or the current filter is too narrow." />
          ) : (
            filtered.map((route) => (
              <RouteCard
                key={String(route.id)}
                route={route}
                selected={Number(route.id) === selectedId}
                onSelect={() => setSelectedId(Number(route.id) === selectedId ? null : Number(route.id))}
              />
            ))
          )}
        </div>

        {/* Stop detail panel */}
        {selectedRoute ? (
          <div className="panel p-5 flex flex-col gap-4">
            <div className="flex items-start justify-between">
              <div>
                <h2 className="text-base font-semibold text-slate-900">{String(selectedRoute.routeCode ?? selectedRoute.routeName)}</h2>
                <p className="text-sm text-slate-500 mt-0.5">
                  {String(selectedRoute.driverName ?? "--")} · {String(selectedRoute.vehicleNumber ?? selectedRoute.vehicleCode ?? "--")} · {stops.length} stops
                </p>
              </div>
              <div className="flex gap-2">
                <button
                  type="button"
                  disabled={etaMutation.isPending}
                  onClick={() => etaMutation.mutate(selectedRoute.id as string | number)}
                  className="text-sm px-3 py-1.5 rounded-lg bg-teal-50 border border-teal-200 text-teal-700 hover:bg-teal-100 transition-colors disabled:opacity-50"
                >
                  {etaMutation.isPending ? "Sending…" : "Send ETA Update"}
                </button>
              </div>
            </div>

            {stops.length === 0 ? (
              <EmptyState title="No stops loaded for this route" subtitle="This route has no live stop records yet." />
            ) : (
              <div className="flex flex-col">
                {stops.map((stop, i) => (
                  <StopRow key={String(stop.id ?? i)} stop={stop} index={i} />
                ))}
              </div>
            )}
          </div>
        ) : (
          <div className="panel p-10 flex items-center justify-center">
            <p className="text-slate-400 text-sm">Select a route to view stop-by-stop progress</p>
          </div>
        )}
      </div>
    </div>
  );
}

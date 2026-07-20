import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { MapPin, Search, Truck } from "lucide-react";
import { apiClient, unwrap } from "@/services/apiClient";
import { withFallback } from "@/services/fleetDomainApi";
import { developmentFleetSeedData } from "@/data/developmentFleetSeedData";
import { exportCsv, LoadingState, ErrorState, EmptyState, KpiCard, StatusBadge } from "@/components/ui";
import type { AnyRecord } from "@/types";

// ── Seed fallback ─────────────────────────────────────────────────────────────

const STOP_ADDRESSES = [
  "142 Elm St, Alexandria VA", "890 Commerce Blvd, Arlington VA", "315 River Rd, Manassas VA",
  "450 Park Ave, Woodbridge VA", "600 Oak Dr, Fairfax VA", "78 Main St, Reston VA",
  "229 Industrial Way, Herndon VA", "500 Delivery Ln, Chantilly VA",
];
const STOP_STATUSES = ["Pending", "En Route", "Arrived", "Completed", "Failed"];
const PROOF_STATUSES = ["Not Required", "Pending", "Captured"];

function buildSeedRoutes(): AnyRecord[] {
  const shipments = developmentFleetSeedData.shipments as AnyRecord[];
  return Array.from({ length: 6 }, (_, ri) => {
    const stopCount = 4 + (ri % 4);
    const stops = Array.from({ length: stopCount }, (_, si) => ({
      id: ri * 10 + si + 1,
      routeId: ri + 1,
      stopSequence: si + 1,
      stopType: si === 0 ? "Pickup" : "Delivery",
      address: STOP_ADDRESSES[(ri + si) % STOP_ADDRESSES.length],
      customerName: (shipments[(ri + si) % shipments.length]?.customerName ?? `Customer ${si + 1}`) as string,
      eta: `${8 + si}:${String(30 + ((ri + si) % 30)).padStart(2, "0")} AM`,
      timeWindowStart: `0${8 + si}:00`,
      timeWindowEnd: `${8 + si + 1}:00`,
      status: STOP_STATUSES[Math.min(si, STOP_STATUSES.length - 1)],
      proofStatus: si < 2 ? "Captured" : si === 2 ? "Pending" : "Not Required",
      notes: "",
    }));
    const completedStops = stops.filter((s) => s.status === "Completed").length;
    return {
      id: ri + 1,
      routeName: `Route ${String(ri + 1).padStart(3, "0")}`,
      status: ["Active", "Active", "Planned", "Active", "Delayed", "Completed"][ri],
      slaRisk: ["Low", "High", "Low", "Medium", "High", "Low"][ri],
      totalStops: stopCount,
      vehicleCode: (shipments[ri % shipments.length]?.vehicleCode ?? `VH-00${ri + 1}`) as string,
      driverName: (shipments[ri % shipments.length]?.driverName ?? `Driver ${ri + 1}`) as string,
      plannedStartTime: `2026-06-19T0${6 + ri}:00:00`,
      plannedEndTime: `2026-06-19T${12 + ri}:00:00`,
      efficiencyScore: 72 + ri * 3,
      completedStops,
      stops,
    };
  });
}

// ── API ───────────────────────────────────────────────────────────────────────

const lastMileApi = {
  routes: () => withFallback(
    unwrap<AnyRecord[]>(apiClient.get("/api/last-mile/deliveries")),
    () => buildSeedRoutes()
  ),
  summary: () => withFallback(
    unwrap<AnyRecord>(apiClient.get("/api/routes/summary")),
    () => ({
      totalRoutesToday: 6, activeRoutes: 3, plannedRoutes: 1, completedRoutes: 1,
      delayedRoutes: 1, averageStopsPerRoute: 5.3, averageRouteEta: "4h 12m",
      routeEfficiencyScore: 83, highRiskRoutes: 2,
    })
  ),
  sendEta: (jobId: string | number) => withFallback(
    unwrap<AnyRecord>(apiClient.post(`/api/customer-eta/${jobId}/send`, {})),
    () => ({ sent: true, jobId })
  ),
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
    <div className={`flex gap-4 relative ${isDone ? "opacity-60" : ""}`}>
      {/* Timeline spine */}
      <div className="flex flex-col items-center pt-4">
        <div className={`w-3 h-3 rounded-full shrink-0 ring-4 ring-white ${dotColor}`} />
        <div className="w-px flex-1 bg-slate-200 mt-1" />
      </div>

      <div className="pb-4 flex-1 min-w-0">
        <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm cursor-pointer transition hover:shadow-md hover:border-slate-300">
          <div className="flex items-start justify-between gap-2 flex-wrap">
            <div>
              <span className="text-xs font-semibold text-slate-500">#{String(stop.stopSequence)} · {String(stop.stopType ?? "Delivery")}</span>
              <p className="text-sm font-semibold text-slate-900 mt-0.5">{String(stop.customerName ?? "Customer")}</p>
              <p className="text-xs text-slate-500 mt-0.5">{String(stop.address ?? "Address")}</p>
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
          <div className="flex gap-4 mt-2 text-xs text-slate-400">
            {stop.eta ? <span>ETA {String(stop.eta)}</span> : null}
            {stop.timeWindowStart ? <span>Window {String(stop.timeWindowStart)}–{String(stop.timeWindowEnd ?? "")}</span> : null}
          </div>
          {stop.notes ? <p className="text-xs text-slate-500 mt-1 italic">{String(stop.notes)}</p> : null}
        </div>
      </div>
    </div>
  );
}

// ── Route card ────────────────────────────────────────────────────────────────

function RouteCard({ route, selected, onSelect }: { route: AnyRecord; selected: boolean; onSelect: () => void }) {
  const stops = (route.stops as AnyRecord[]) ?? [];
  const completed = stops.filter((s) => String(s.status) === "Completed").length;
  const pct = stops.length > 0 ? Math.round((completed / stops.length) * 100) : 0;
  const status = String(route.status ?? "");
  const risk = String(route.slaRisk ?? "Low");

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
      className={`cursor-pointer w-full text-left rounded-2xl border p-4 transition-all hover:shadow-md ${
        selected ? "border-teal-400 bg-teal-50 shadow-sm ring-1 ring-teal-200/60" : "border-slate-200 bg-white shadow-sm hover:border-slate-300"
      }`}
      onClick={onSelect}
    >
      <div className="flex items-start justify-between gap-2">
        <div>
          <p className="font-semibold text-slate-900 text-sm">{String(route.routeName ?? `Route ${route.id}`)}</p>
          <p className="text-xs text-slate-500 mt-0.5">{String(route.driverName ?? "--")} · {String(route.vehicleCode ?? "--")}</p>
        </div>
        <span className={`text-xs px-2 py-0.5 rounded-full border font-medium shrink-0 ${statusColor}`}>{status}</span>
      </div>

      {/* Progress bar */}
      <div className="mt-3">
        <div className="flex items-center justify-between text-xs text-slate-500 mb-1">
          <span>{completed}/{stops.length} stops</span>
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
          {route.plannedStartTime ? new Date(String(route.plannedStartTime)).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }) : "--"} → {route.plannedEndTime ? new Date(String(route.plannedEndTime)).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }) : "--"}
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

  const routesQ = useQuery({ queryKey: ["last-mile", "routes"], queryFn: lastMileApi.routes, refetchInterval: 20_000 });
  const summaryQ = useQuery({ queryKey: ["last-mile", "summary"], queryFn: lastMileApi.summary });

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

  const routes = (routesQ.data ?? []) as AnyRecord[];
  const s = (summaryQ.data ?? {}) as AnyRecord;

  const filtered = routes.filter((r) => {
    if (statusFilter !== "All" && r.status !== statusFilter) return false;
    if (search) {
      const q = search.toLowerCase();
      return (
        String(r.routeName ?? "").toLowerCase().includes(q) ||
        String(r.driverName ?? "").toLowerCase().includes(q) ||
        String(r.vehicleCode ?? "").toLowerCase().includes(q)
      );
    }
    return true;
  });

  const selectedRoute = routes.find((r) => Number(r.id) === selectedId) ?? null;
  const stops = selectedRoute ? (selectedRoute.stops as AnyRecord[]) ?? [] : [];

  const flatStops = routes.flatMap((r) =>
    ((r.stops as AnyRecord[]) ?? []).map((s) => ({
      route: r.routeName,
      driver: r.driverName,
      vehicle: r.vehicleCode,
      stop: s.stopSequence,
      type: s.stopType,
      customer: s.customerName,
      address: s.address,
      eta: s.eta,
      status: s.status,
      pod: s.proofStatus,
    }))
  );

  if (routesQ.isLoading) return <LoadingState />;
  if (routesQ.isError) return <ErrorState message={(routesQ.error as Error)?.message} />;

  return (
    <div className="space-y-6 pb-10">
      {toast && (
        <div className="fixed top-4 right-4 z-50 panel border-emerald-200 bg-emerald-50 text-emerald-800 text-sm font-medium px-4 py-2.5 shadow-lg">
          {toast}
        </div>
      )}

      <header className="fh-hero relative">
        <span className="fh-hero-bar" />
        <span className="fh-hero-glow-1" />
        <span className="fh-hero-glow-2" />
        <div className="relative px-7 py-6">
          <div className="flex flex-wrap items-start justify-between gap-6">
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-3 mb-3">
                <span className="inline-flex items-center gap-1.5 rounded-lg bg-white/90 px-3 py-1 text-[10px] font-bold uppercase tracking-[0.2em] text-teal-700 ring-1 ring-teal-200/50 shadow-sm">
                  <MapPin className="h-3 w-3" /> Last Mile
                </span>
                <span className="text-[11px] font-semibold text-slate-500">Stop sequencing and customer delivery</span>
              </div>
              <h1 className="text-[32px] font-black tracking-tight leading-none cc-gradient-text sm:text-[36px]">
                Last Mile Delivery
              </h1>
              <p className="mt-1 text-[13px] font-medium text-slate-400 tracking-wide">
                Real-time stop sequencing, ETA management, proof of delivery and customer notification
              </p>
            </div>
            <div className="flex items-center gap-2">
              <button
                type="button"
                className="fh-btn-ghost"
                onClick={() => exportCsv("last-mile-delivery", flatStops)}
              >
                Export CSV
              </button>
            </div>
          </div>
        </div>
      </header>

      {/* KPI strip */}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <KpiCard label="Active Routes" value={String(s.activeRoutes ?? routes.filter((r) => r.status === "Active").length)} />
        <KpiCard label="Planned Routes" value={String(s.plannedRoutes ?? routes.filter((r) => r.status === "Planned").length)} />
        <KpiCard label="Delayed" value={String(s.delayedRoutes ?? routes.filter((r) => r.status === "Delayed").length)} status={Number(s.delayedRoutes ?? 0) > 0 ? "Review" : undefined} />
        <KpiCard label="Completed" value={String(s.completedRoutes ?? routes.filter((r) => r.status === "Completed").length)} />
        <KpiCard label="Avg Stops / Route" value={String(s.averageStopsPerRoute ?? "--")} />
        <KpiCard label="Route Efficiency" value={s.routeEfficiencyScore ? `${s.routeEfficiencyScore}%` : "--"} />
        <KpiCard label="High Risk Routes" value={String(s.highRiskRoutes ?? 0)} status={Number(s.highRiskRoutes ?? 0) > 0 ? "Review" : undefined} />
      </div>

      {/* Filter bar */}
      <div className="sticky top-4 z-20 rounded-2xl border border-slate-200 bg-white/95 p-3 shadow-sm backdrop-blur flex flex-wrap gap-3 items-center">
        <div className="flex gap-1.5">
          {(["All", "Active", "Planned", "Delayed", "Completed"] as const).map((f) => (
            <button
              key={f}
              type="button"
              onClick={() => setStatusFilter(f)}
              className={`cursor-pointer px-3 py-1.5 rounded-xl text-sm font-medium border transition-colors ${
                statusFilter === f
                  ? "bg-teal-50 border-teal-300 text-teal-700 shadow-sm"
                  : "bg-slate-50 border-slate-200 text-slate-600 hover:bg-slate-100"
              }`}
            >
              {f}
            </button>
          ))}
        </div>
        <div className="ml-auto relative">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-slate-400 pointer-events-none" />
          <input
            type="search"
            placeholder="Search routes, drivers…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="rounded-xl border border-slate-200 bg-white pl-9 pr-3 py-2 text-sm text-slate-900 placeholder-slate-400 shadow-sm transition focus:border-teal-400 focus:outline-none focus:ring-2 focus:ring-teal-100 w-56"
          />
        </div>
      </div>

      {/* Split: route list + stop detail */}
      <div className="grid gap-5 xl:grid-cols-[440px_1fr]">
        {/* Route list */}
        <div className="flex flex-col gap-2.5">
          {filtered.length === 0 ? (
            <div className="panel p-10 flex flex-col items-center justify-center text-center">
              <div className="h-12 w-12 rounded-2xl bg-slate-100 flex items-center justify-center mb-3">
                <Truck className="h-6 w-6 text-slate-400" />
              </div>
              <p className="text-sm font-medium text-slate-500">No routes match your filters</p>
              <p className="text-xs text-slate-400 mt-1">Try adjusting the status filter or search term</p>
            </div>
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
          <div className="panel p-6 flex flex-col gap-5">
            <div className="flex items-start justify-between gap-4">
              <div className="min-w-0">
                <div className="flex items-center gap-2.5 mb-1">
                  <h2 className="text-lg font-bold text-slate-900">{String(selectedRoute.routeName)}</h2>
                  <StatusBadge status={String(selectedRoute.status)} />
                </div>
                <p className="text-sm text-slate-500">
                  {String(selectedRoute.driverName ?? "--")} · {String(selectedRoute.vehicleCode ?? "--")}
                </p>
                <div className="flex items-center gap-4 mt-2.5">
                  <div className="flex items-center gap-2 text-xs text-slate-500">
                    <span className="font-semibold text-slate-700">{String(selectedRoute.completedStops ?? 0)}</span>
                    <span>/ {stops.length} stops</span>
                  </div>
                  <div className="flex-1 max-w-50">
                    <div className="h-1.5 rounded-full bg-slate-100 overflow-hidden">
                      <div
                        className={`h-full rounded-full transition-all ${
                          stops.length > 0 && Number(selectedRoute.completedStops ?? 0) === stops.length ? "bg-teal-500" : "bg-teal-400"
                        }`}
                        style={{ width: `${stops.length > 0 ? Math.round(((Number(selectedRoute.completedStops ?? 0)) / stops.length) * 100) : 0}%` }}
                      />
                    </div>
                  </div>
                  <span className="text-xs font-semibold text-teal-600">
                    {stops.length > 0 ? Math.round(((Number(selectedRoute.completedStops ?? 0)) / stops.length) * 100) : 0}%
                  </span>
                </div>
              </div>
              <button
                type="button"
                disabled={etaMutation.isPending}
                onClick={() => etaMutation.mutate(selectedRoute.id as string | number)}
                className="cursor-pointer shrink-0 text-sm px-4 py-2 rounded-xl bg-teal-50 border border-teal-200 text-teal-700 font-medium hover:bg-teal-100 transition-colors disabled:opacity-50 shadow-sm"
              >
                {etaMutation.isPending ? "Sending…" : "Send ETA Update"}
              </button>
            </div>

            <div className="h-px bg-slate-100" />

            {stops.length === 0 ? (
              <EmptyState title="No stops loaded for this route" />
            ) : (
              <div className="flex flex-col">
                {stops.map((stop, i) => (
                  <StopRow key={String(stop.id ?? i)} stop={stop} index={i} />
                ))}
              </div>
            )}
          </div>
        ) : (
          <div className="panel p-12 flex flex-col items-center justify-center text-center">
            <div className="h-14 w-14 rounded-2xl bg-slate-100 flex items-center justify-center mb-3">
              <MapPin className="h-7 w-7 text-slate-400" />
            </div>
            <p className="text-sm font-medium text-slate-500">Select a route to view stops</p>
            <p className="text-xs text-slate-400 mt-1">Stop-by-stop progress will appear here</p>
          </div>
        )}
      </div>
    </div>
  );
}

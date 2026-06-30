import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowRight, CheckCircle2, Clock3, Download, RefreshCw, ShieldAlert, Truck } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { DataTable, EmptyState, ErrorState, KpiCard, LoadingState, PageHeader, RiskBadge, StatusBadge, exportCsv, labelize } from "@/components/ui";
import { useHasPermission } from "@/hooks/usePermission";
import { tripApi } from "@/services/tripApi";
import type { AnyRecord } from "@/types";

type Toast = { kind: "success" | "error" | "info"; message: string };

const FILTERS = ["All", "planned", "active", "completed", "exception"] as const;

function value(row: AnyRecord, ...keys: string[]) {
  for (const key of keys) {
    if (row?.[key] != null && row[key] !== "") return row[key];
  }
  return undefined;
}

function num(v: unknown) {
  return Number.isFinite(Number(v)) ? Number(v) : 0;
}

function tripId(row: AnyRecord | null | undefined): string | number | null {
  const id = row?.id;
  return typeof id === "string" || typeof id === "number" ? id : null;
}

export function TripsPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const hasPermission = useHasPermission();
  const [selectedTrip, setSelectedTrip] = useState<AnyRecord | null>(null);
  const [filter, setFilter] = useState<(typeof FILTERS)[number]>("All");
  const [toast, setToast] = useState<Toast | null>(null);

  const tripsQ = useQuery({
    queryKey: ["trips", filter],
    queryFn: () => tripApi.list(filter === "All" ? { limit: 100 } : { status: filter, limit: 100 }),
    refetchInterval: 30_000,
  });
  const trips = (tripsQ.data ?? []) as AnyRecord[];

  useEffect(() => {
    if (selectedTrip != null && trips.length > 0 && !trips.some((row) => String(row.id) === String(selectedTrip.id))) {
      setSelectedTrip(trips[0] ?? null);
    }
    if (selectedTrip == null && trips.length > 0) {
      setSelectedTrip(trips[0] ?? null);
    }
  }, [selectedTrip, trips]);

  const detailQ = useQuery({
    queryKey: ["trips", "detail", selectedTrip?.id],
    queryFn: () => tripApi.detail(tripId(selectedTrip) as string | number),
    enabled: tripId(selectedTrip) != null,
  });
  const breadcrumbsQ = useQuery({
    queryKey: ["trips", "breadcrumbs", selectedTrip?.id],
    queryFn: () => tripApi.breadcrumbs(tripId(selectedTrip) as string | number),
    enabled: tripId(selectedTrip) != null,
  });
  const complianceQ = useQuery({
    queryKey: ["trips", "compliance", selectedTrip?.id],
    queryFn: () => tripApi.compliance(tripId(selectedTrip) as string | number),
    enabled: tripId(selectedTrip) != null,
  });

  const startMutation = useMutation({
    mutationFn: (id: string | number) => tripApi.start(id),
    onSuccess: async () => {
      setToast({ kind: "success", message: "Trip started." });
      await queryClient.invalidateQueries({ queryKey: ["trips"] });
    },
    onError: () => setToast({ kind: "error", message: "Trip start failed." }),
  });
  const completeMutation = useMutation({
    mutationFn: (id: string | number) => tripApi.complete(id),
    onSuccess: async () => {
      setToast({ kind: "success", message: "Trip marked complete." });
      await queryClient.invalidateQueries({ queryKey: ["trips"] });
    },
    onError: () => setToast({ kind: "error", message: "Trip completion failed." }),
  });
  const exceptionMutation = useMutation({
    mutationFn: (payload: { id: string | number; notes?: string }) => tripApi.exception(payload.id, payload.notes),
    onSuccess: async () => {
      setToast({ kind: "success", message: "Trip flagged as exception." });
      await queryClient.invalidateQueries({ queryKey: ["trips"] });
    },
    onError: () => setToast({ kind: "error", message: "Trip exception update failed." }),
  });

  useEffect(() => {
    if (!toast) return;
    const t = window.setTimeout(() => setToast(null), 3200);
    return () => window.clearTimeout(t);
  }, [toast]);

  if (tripsQ.isLoading) return <LoadingState />;
  if (tripsQ.isError) {
    return <ErrorState message={tripsQ.error instanceof Error ? tripsQ.error.message : "Unable to load trips."} />;
  }

  const detail = (((detailQ.data ?? {}) as AnyRecord).trip ?? selectedTrip ?? {}) as AnyRecord;
  const stops = (((detailQ.data ?? {}) as AnyRecord).stops ?? []) as AnyRecord[];
  const breadcrumbs = (breadcrumbsQ.data ?? []) as AnyRecord[];
  const compliance = (complianceQ.data ?? {}) as AnyRecord;
  const complianceScore = num(compliance.complianceScore ?? detail.compliance_score ?? selectedTrip?.compliance_score);
  const tableRows: AnyRecord[] = trips.map((row) => ({
    ...row,
    trip_ref: String(value(row, "trip_ref", "tripRef", "trip_number", "tripNumber") ?? row.id),
    status: String(value(row, "status") ?? "unknown"),
    driver_name: String(value(row, "driver_name", "driverName") ?? "Unassigned"),
    vehicle_code: String(value(row, "vehicle_code", "vehicleCode") ?? "Unassigned"),
    route_name: String(value(row, "route_name", "routeName") ?? "Not configured"),
    compliance_score: value(row, "compliance_score") != null ? `${value(row, "compliance_score")}%` : "—",
  }));
  const planned = trips.filter((row) => /planned/i.test(String(value(row, "status")))).length;
  const active = trips.filter((row) => /active|in transit/i.test(String(value(row, "status")))).length;
  const completed = trips.filter((row) => /completed/i.test(String(value(row, "status")))).length;
  const exceptionCount = trips.filter((row) => /exception|failed/i.test(String(value(row, "status")))).length;

  const selectedStatus = String(value(detail, "status", "tripStatus") ?? "unknown");
  const canUpdate = hasPermission("dispatch:update");

  return (
    <div className="space-y-6 pb-10">
      {toast && (
        <div
          className={`fixed right-4 top-4 z-50 rounded-lg px-4 py-2.5 text-sm font-medium shadow-lg ${
            toast.kind === "error" ? "bg-rose-600 text-white" : "bg-teal-600 text-white"
          }`}
        >
          {toast.message}
        </div>
      )}

      <PageHeader
        eyebrow="Transport Operations"
        title="Trips"
        description="A dedicated trip register with live compliance, breadcrumbs, stops and dispatch actions. This surface stays honest: no seeded rows, no fake trip success."
        actions={<>
          <button type="button" className="btn-ghost" onClick={() => tripsQ.refetch()}>
            <RefreshCw className="h-4 w-4" /> Refresh
          </button>
          <button type="button" className="btn-ghost" onClick={() => exportCsv("trips", trips)}>
            <Download className="h-4 w-4" /> Export CSV
          </button>
        </>}
      />

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <KpiCard label="Trips" value={trips.length} trend="Live tenant count" />
        <KpiCard label="Planned" value={planned} trend="Awaiting dispatch start" />
        <KpiCard label="Active" value={active} trend="In motion" />
        <KpiCard label="Exceptions" value={exceptionCount} trend={exceptionCount > 0 ? "Needs review" : "Clear"} />
      </div>

      <div className="grid gap-6 xl:grid-cols-[1.15fr_.95fr]">
        <section className="panel p-5">
          <div className="flex flex-wrap items-center gap-2">
            {FILTERS.map((item) => (
              <button
                key={item}
                type="button"
                onClick={() => setFilter(item)}
                className={`rounded-full border px-3 py-1.5 text-xs font-semibold transition ${
                  filter === item ? "border-teal-300 bg-teal-50 text-teal-700" : "border-slate-200 bg-white text-slate-600 hover:bg-slate-50"
                }`}
              >
                {item}
              </button>
            ))}
            <div className="ml-auto flex flex-wrap gap-2">
              <button type="button" className="btn-ghost h-9" onClick={() => navigate("/dispatch")}>
                <ArrowRight className="h-4 w-4" /> Open Dispatch
              </button>
              <button type="button" className="btn-ghost h-9" onClick={() => navigate("/jobs")}>
                <Truck className="h-4 w-4" /> Open Jobs
              </button>
            </div>
          </div>

          {trips.length === 0 ? (
            <EmptyState
              title="No trips found"
              subtitle="Trips will appear once routes and dispatch create them for the current tenant."
            />
          ) : (
            <div className="mt-4 overflow-hidden rounded-2xl border border-slate-200">
              <DataTable
                rows={tableRows}
                columns={["trip_ref", "status", "driver_name", "vehicle_code", "route_name", "compliance_score"]}
                onSelect={(row) => setSelectedTrip(row)}
              />
            </div>
          )}
        </section>

        <section className="panel p-5">
          {selectedTrip ? (
            <div className="space-y-5">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <p className="text-xs font-bold uppercase tracking-[0.18em] text-slate-500">Selected trip</p>
                  <h2 className="mt-1 text-2xl font-black tracking-tight text-slate-900">
                    {String(value(detail, "trip_ref", "tripRef", "trip_number") ?? `Trip ${String(selectedTrip.id)}`)}
                  </h2>
                  <p className="mt-1 text-sm text-slate-500">
                    {String(value(detail, "route_name", "routeName") ?? "No route name")} · {String(value(detail, "vehicle_code", "vehicleCode") ?? "No vehicle")} · {String(value(detail, "driver_name", "driverName") ?? "No driver")}
                  </p>
                </div>
                <div className="flex flex-col items-end gap-2">
                  <StatusBadge status={selectedStatus} />
                  <RiskBadge risk={String(complianceScore >= 85 ? "Low" : complianceScore >= 70 ? "Medium" : "High")} />
                </div>
              </div>

              <div className="grid gap-3 sm:grid-cols-2">
                <Info label="Trip ID" value={String(detail.id ?? selectedTrip.id)} />
                <Info label="Route" value={String(value(detail, "route_name", "routeName") ?? "Not configured")} />
                <Info label="Driver" value={String(value(detail, "driver_name", "driverName") ?? "Unassigned")} />
                <Info label="Vehicle" value={String(value(detail, "vehicle_code", "vehicleCode") ?? "Unassigned")} />
                <Info label="Start" value={formatDate(value(detail, "actual_start_time", "actualStartTime", "start_time", "startTime"))} />
                <Info label="End" value={formatDate(value(detail, "actual_end_time", "actualEndTime", "end_time", "endTime"))} />
              </div>

              <div className="rounded-2xl border border-slate-200 bg-slate-50/80 p-4">
                <div className="flex items-center justify-between gap-3">
                  <div>
                    <p className="text-sm font-bold text-slate-900">Actions</p>
                    <p className="text-xs text-slate-500">Backend actions are permissioned and fail closed.</p>
                  </div>
                  <p className="text-xs font-semibold text-slate-500">Compliance {Number.isFinite(complianceScore) ? `${Math.round(complianceScore)}%` : "—"}</p>
                </div>
                {canUpdate ? (
                  <div className="mt-3 flex flex-wrap gap-2">
                    <button
                      type="button"
                      disabled={startMutation.isPending || !/planned/i.test(selectedStatus)}
                      className="btn-primary h-9"
                      onClick={() => startMutation.mutate(tripId(selectedTrip) as string | number)}
                    >
                      <Clock3 className="h-4 w-4" /> Start Trip
                    </button>
                    <button
                      type="button"
                      disabled={completeMutation.isPending || !/active/i.test(selectedStatus)}
                      className="btn-ghost h-9"
                      onClick={() => completeMutation.mutate(tripId(selectedTrip) as string | number)}
                    >
                      <CheckCircle2 className="h-4 w-4" /> Complete Trip
                    </button>
                    <button
                      type="button"
                      disabled={exceptionMutation.isPending || !/active/i.test(selectedStatus)}
                      className="btn-ghost h-9"
                      onClick={() => exceptionMutation.mutate({ id: tripId(selectedTrip) as string | number, notes: "Marked from Trips page" })}
                    >
                      <ShieldAlert className="h-4 w-4" /> Flag Exception
                    </button>
                  </div>
                ) : (
                  <p className="mt-3 text-sm text-slate-500">Read-only access: dispatch update permission is not available in this session.</p>
                )}
              </div>

              <div className="grid gap-3 sm:grid-cols-3">
                <button type="button" className="rounded-2xl border border-slate-200 bg-white px-4 py-3 text-left shadow-sm transition hover:bg-slate-50" onClick={() => navigate(`/jobs?tripId=${selectedTrip.id}`)}>
                  <p className="text-xs font-bold uppercase tracking-[0.18em] text-slate-500">Job context</p>
                  <p className="mt-1 text-sm font-semibold text-slate-900">Open linked jobs</p>
                  <p className="mt-1 text-xs text-slate-500">Jump to the execution record behind this trip.</p>
                </button>
                <button type="button" className="rounded-2xl border border-slate-200 bg-white px-4 py-3 text-left shadow-sm transition hover:bg-slate-50" onClick={() => navigate(`/dispatch?tripId=${selectedTrip.id}`)}>
                  <p className="text-xs font-bold uppercase tracking-[0.18em] text-slate-500">Dispatch context</p>
                  <p className="mt-1 text-sm font-semibold text-slate-900">Open dispatch board</p>
                  <p className="mt-1 text-xs text-slate-500">See pairing and exception handling.</p>
                </button>
                <button type="button" className="rounded-2xl border border-slate-200 bg-white px-4 py-3 text-left shadow-sm transition hover:bg-slate-50" onClick={() => navigate(`/operations/proof-center?tripId=${selectedTrip.id}`)}>
                  <p className="text-xs font-bold uppercase tracking-[0.18em] text-slate-500">Proof context</p>
                  <p className="mt-1 text-sm font-semibold text-slate-900">Open proof center</p>
                  <p className="mt-1 text-xs text-slate-500">Review POD, access and billing confidence.</p>
                </button>
              </div>

              <div className="grid gap-4 lg:grid-cols-2">
                <section className="rounded-2xl border border-slate-200 bg-white p-4">
                  <h3 className="text-sm font-bold text-slate-900">Trip stops</h3>
                  <div className="mt-3 space-y-2">
                    {stops.length === 0 ? (
                      <EmptyState title="No trip stops" subtitle="Trip stop rows are not present for this trip yet." />
                    ) : (
                      stops.map((stop) => (
                        <div key={String(stop.id ?? stop.stop_id)} className="rounded-xl border border-slate-200 bg-slate-50 px-3 py-2">
                          <div className="flex items-center justify-between gap-3">
                            <p className="text-sm font-semibold text-slate-900">{String(value(stop, "stop_type", "stopType", "name") ?? "Stop")}</p>
                            <StatusBadge status={value(stop, "status")} />
                          </div>
                          <p className="mt-1 text-xs text-slate-500">{String(value(stop, "route_stop_address", "address", "stop_address") ?? "No address")}</p>
                        </div>
                      ))
                    )}
                  </div>
                </section>

                <section className="rounded-2xl border border-slate-200 bg-white p-4">
                  <h3 className="text-sm font-bold text-slate-900">Compliance breakdown</h3>
                  <div className="mt-3 space-y-2">
                    {Array.isArray(compliance.breakdown) && compliance.breakdown.length > 0 ? (
                      compliance.breakdown.map((item: AnyRecord, index: number) => (
                        <div key={String(item.key ?? index)} className="flex items-center justify-between rounded-xl border border-slate-200 bg-slate-50 px-3 py-2">
                          <div>
                            <p className="text-sm font-semibold text-slate-900">{String(labelize(String(item.key ?? item.label ?? "factor")))}</p>
                            <p className="text-xs text-slate-500">{String(item.note ?? item.description ?? "Trip compliance signal")}</p>
                          </div>
                          <p className="text-sm font-bold text-slate-700">{String(item.value ?? item.count ?? item.score ?? "—")}</p>
                        </div>
                      ))
                    ) : (
                      <EmptyState title="No compliance breakdown" subtitle="The backend did not return a breakdown for this trip." />
                    )}
                  </div>
                </section>
              </div>

              <section className="rounded-2xl border border-slate-200 bg-white p-4">
                <div className="flex items-center justify-between gap-3">
                  <h3 className="text-sm font-bold text-slate-900">Breadcrumbs</h3>
                  <span className="text-xs text-slate-500">{breadcrumbs.length} points</span>
                </div>
                <div className="mt-3 overflow-hidden rounded-xl border border-slate-200">
                  {breadcrumbs.length === 0 ? (
                    <EmptyState title="No breadcrumbs" subtitle="Trip breadcrumb history is not available yet." />
                  ) : (
                    <DataTable rows={breadcrumbs} columns={["event_time", "lat", "lng", "speed_mph", "event_type"]} />
                  )}
                </div>
              </section>
            </div>
          ) : (
            <EmptyState title="Select a trip" subtitle="Choose a trip on the left to see stops, compliance and actions." />
          )}
        </section>
      </div>
    </div>
  );
}

function Info({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-2xl border border-slate-200 bg-white px-4 py-3">
      <p className="text-xs font-bold uppercase tracking-[0.18em] text-slate-500">{label}</p>
      <p className="mt-1 text-sm font-semibold text-slate-900">{value || "—"}</p>
    </div>
  );
}

function formatDate(value: unknown) {
  if (!value) return "Not available";
  const date = new Date(String(value));
  return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleString();
}

import { useEffect, useMemo, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useNavigate } from "react-router";
import { Activity, AlertTriangle, CheckCircle2, Download, Gauge, MapPinned, RadioTower, Search, ShieldCheck, Wrench } from "lucide-react";
import { EmptyState, ErrorState, KpiCard, LoadingState, PageHeader, StatusBadge } from "@/components/ui";
import { PERMISSIONS, useHasPermission } from "@/hooks/usePermission";
import { telematicsService, type DeviceCommandRecord, type DevicePageResult } from "@/services/telematicsService";

type ExceptionRow = {
  id: string;
  device: string;
  vehicle: string;
  provider: string;
  status: string;
  reason: string;
  evidence: string;
  action: string;
  severity: number;
};

function exceptionFor(device: DeviceCommandRecord): ExceptionRow {
  const neverConnected = !device.lastCheckIn || device.lastCheckIn === "—";
  const governedHold = /quarantined|suspended/i.test(device.deviceState);

  if (governedHold) return {
    id: String(device.id), device: device.deviceName, vehicle: device.assignedVehicleCode || "Unassigned", provider: device.provider,
    status: "Needs attention", reason: `${device.deviceState} commissioning or lifecycle hold`,
    evidence: `device state: ${device.deviceState} · check-in: ${device.lastCheckIn}`,
    action: "Resolve the governed device hold before returning it to service", severity: 5,
  };

  if (neverConnected) return {
    id: String(device.id), device: device.deviceName, vehicle: device.assignedVehicleCode || "Unassigned", provider: device.provider,
    status: "Never connected", reason: "No device check-in has been observed", evidence: "last check-in: none",
    action: "Verify installation, power, SIM, and activation", severity: 4,
  };
  if (/offline/i.test(device.connectionStatus)) return {
    id: String(device.id), device: device.deviceName, vehicle: device.assignedVehicleCode || "Unassigned", provider: device.provider,
    status: "Offline", reason: "The latest trusted signal is outside the freshness window",
    evidence: `check-in: ${device.lastCheckIn}`,
    action: "Check vehicle power, cellular coverage, and device cable", severity: 3,
  };
  const needsAttention = /attention|malfunction|quarantined|suspended|rotation|diagnostic/i.test(`${device.connectionStatus} ${device.lifecycleStatus} ${device.deviceState}`) || device.openAlertCount > 0 || device.activeFaultCount > 0;
  return needsAttention ? {
    id: String(device.id), device: device.deviceName, vehicle: device.assignedVehicleCode || "Unassigned", provider: device.provider,
    status: "Needs attention", reason: device.openAlertCount
      ? `${device.openAlertCount} open telemetry alert${device.openAlertCount === 1 ? "" : "s"}`
      : device.activeFaultCount > 0
        ? `${device.activeFaultCount} active diagnostic fault${device.activeFaultCount === 1 ? "" : "s"}`
        : /quarantined|suspended/i.test(device.deviceState) ? device.deviceState : device.lifecycleStatus,
    evidence: `health: ${device.dataHealthAvailable ? `${device.dataHealthScore}/100` : "unknown"} · lifecycle: ${device.lifecycleStatus}`,
    action: "Open Device Health and resolve the evidence-backed issue", severity: 2,
  } : {
    id: String(device.id), device: device.deviceName, vehicle: device.assignedVehicleCode || "Unassigned", provider: device.provider,
    status: device.connectionStatus || "Observed", reason: "No connectivity or lifecycle exception is currently observed",
    evidence: `check-in: ${device.lastCheckIn} · lifecycle: ${device.lifecycleStatus}`,
    action: "No DeviceOps action required", severity: 0,
  };
}

export function TelematicsControlTowerPage() {
  const navigate = useNavigate();
  const hasPermission = useHasPermission();
  const canExport = hasPermission(PERMISSIONS.TELEMATICS_DEVICES_EXPORT);
  const [search, setSearch] = useState("");
  const [settledSearch, setSettledSearch] = useState("");
  const [view, setView] = useState("attention");
  const [sort, setSort] = useState<"priority" | "serial" | "provider" | "vehicle" | "lastCheckIn">("priority");
  const [page, setPage] = useState(1);
  const hasLoadedQueue = useRef(false);
  const lastSuccessfulSummary = useRef<DevicePageResult["summary"] | null>(null);
  const pageSize = 100;

  useEffect(() => {
    const timer = window.setTimeout(() => { setSettledSearch(search.trim()); }, 250);
    return () => window.clearTimeout(timer);
  }, [search]);

  const query = useQuery({
    queryKey: ["telematics-control-tower", page, settledSearch, view, sort],
    queryFn: () => telematicsService.getDevicePage({ page, pageSize, search: settledSearch, view, sort, direction: sort === "priority" ? "desc" : "asc" }),
    refetchInterval: 30_000,
  });

  useEffect(() => {
    if (query.data) {
      hasLoadedQueue.current = true;
      lastSuccessfulSummary.current = query.data.summary;
    }
  }, [query.data]);

  // Ordering is applied before LIMIT/OFFSET on the server so page 1 is the
  // fleet-wide highest-priority page rather than a page-local re-sort.
  const exceptions = useMemo(() => (query.data?.items ?? []).map(exceptionFor), [query.data?.items]);

  const total = query.data?.total ?? 0;
  const pageCount = Math.max(1, Math.ceil(total / pageSize));
  useEffect(() => {
    if (query.data && page > pageCount) setPage(pageCount);
  }, [page, pageCount, query.data]);

  const searchPending = search.trim() !== settledSearch;
  // Once the queue has rendered, query-key transitions should retain page
  // context while suppressing stale rows and announcing the pending result.
  // Background refreshes keep the current truthful rows visible because they
  // have data and are not a query-key loading transition.
  const queueTransitionPending = searchPending || (query.isLoading && hasLoadedQueue.current);

  if (query.isLoading && !hasLoadedQueue.current) return <LoadingState />;
  if (query.isError) return <ErrorState message={query.error instanceof Error ? query.error.message : "Unable to load device signals."} onRetry={() => { void query.refetch(); }} />;

  // Search, view, sort, and pagination only change the queue rows; the API
  // summary is for the complete authorized fleet. Keep the last successful
  // summary during a query-key transition so the KPI cards never flash false
  // zeroes while the replacement queue is loading.
  const summary = query.data?.summary ?? lastSuccessfulSummary.current;
  const denominator = summary?.active ?? 0;
  const rangeStart = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const rangeEnd = Math.min(page * pageSize, total);
  return (
    <div className="space-y-5">
      <PageHeader title="Telematics Control Tower" description="Exception-first DeviceOps for connectivity and lifecycle evidence, with permission-scoped GPS and diagnostics workspaces." eyebrow="Telematics & IoT" />

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
        <KpiCard label="Managed devices" value={denominator} icon={<RadioTower className="h-4 w-4" />} />
        <KpiCard label="Online" value={summary?.online ?? 0} icon={<CheckCircle2 className="h-4 w-4" />} />
        <KpiCard label="Needs action" value={summary?.attention ?? 0} icon={<AlertTriangle className="h-4 w-4" />} />
        <KpiCard label="Never connected" value={summary?.neverConnected ?? 0} icon={<Activity className="h-4 w-4" />} />
        <KpiCard label="Faulted assets" value={summary?.faulted ?? "Unknown"} icon={<Gauge className="h-4 w-4" />} />
      </div>

      <section className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
        <div>
          <h2 className="font-semibold text-slate-900">Pilot evidence scorecard</h2>
          <p className="mt-1 text-sm text-slate-500">Coverage counts from current API fields. “Unknown” is shown when there are no managed devices; these are evidence ratios, not a synthetic readiness score.</p>
        </div>
        <div className="mt-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          {[
            { label: "Check-in evidence", count: denominator - (summary?.neverConnected ?? 0), definition: "Device has reported at least one check-in." },
            { label: "Current position", count: null, definition: "Open GPS for permission-scoped position freshness." },
            { label: "Known provenance", count: null, definition: "Open GPS for provider and source evidence." },
            { label: "Health evidence", count: denominator - (summary?.neverConnected ?? 0), definition: "A device check-in provides the baseline health evidence." },
          ].map((item) => (
            <div key={item.label} className="rounded-xl border border-slate-200 bg-slate-50 p-4">
              <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">{item.label}</p>
              <p className="mt-2 text-2xl font-bold text-slate-900">{denominator && item.count != null ? `${item.count}/${denominator}` : "Unknown"}</p>
              <p className="mt-2 text-xs text-slate-500">{item.definition}</p>
            </div>
          ))}
        </div>
      </section>

      <section className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm" aria-busy={queueTransitionPending}>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h2 className="font-semibold text-slate-900">Priority action queue</h2>
            <p className="mt-1 text-sm text-slate-500">Ranked by observed risk. Unknown signals stay unknown; the page does not manufacture modem, power, GPS, or engine readings.</p>
          </div>
          <div className="flex flex-wrap gap-2">
            {canExport ? <button className="btn-ghost" onClick={() => void telematicsService.exportDevices()}><Download className="h-4 w-4" /> Export devices CSV</button> : null}
            <button className="btn-ghost" onClick={() => navigate("/gps-tracking")}><MapPinned className="h-4 w-4" /> GPS</button>
            <button className="btn-ghost" onClick={() => navigate("/obd-j1939")}><Gauge className="h-4 w-4" /> Diagnostics</button>
            <button className="btn-primary" onClick={() => navigate("/iot-devices")}><Wrench className="h-4 w-4" /> Device Health</button>
          </div>
        </div>

        <div className="mt-4 grid gap-3 md:grid-cols-[minmax(0,1fr)_220px_220px]">
          <label className="relative block">
            <Search className="pointer-events-none absolute left-3 top-3 h-4 w-4 text-slate-400" aria-hidden />
            <span className="sr-only">Search priority queue</span>
            <input className="field pl-9" value={search} onChange={(event) => { setSearch(event.target.value); setPage(1); }} placeholder="Search device, serial, provider, vehicle, or driver" />
          </label>
          <label><span className="sr-only">Queue filter</span><select className="field" value={view} onChange={(event) => { setView(event.target.value); setPage(1); }}>
            <option value="attention">Needs action</option><option value="offline">Offline or never connected</option><option value="provisioning">Provisioning or unassigned</option><option value="all">All managed devices</option>
          </select></label>
          <label><span className="sr-only">Queue sort</span><select className="field" value={sort} onChange={(event) => { setSort(event.target.value as typeof sort); setPage(1); }}>
            <option value="priority">Highest risk first</option><option value="serial">Device serial</option><option value="provider">Provider</option><option value="vehicle">Vehicle</option><option value="lastCheckIn">Last check-in</option>
          </select></label>
        </div>

        {queueTransitionPending ? (
          <div className="mt-4" role="status" aria-live="polite" aria-busy="true">
            <p className="mb-3 text-sm font-medium text-slate-600">Updating priority queue…</p>
            <LoadingState />
          </div>
        ) : exceptions.length === 0 ? (
          <div className="mt-4"><EmptyState title="No matching devices" subtitle="No connectivity or lifecycle rows match this queue view. GPS and Diagnostics retain their own permission-scoped evidence." /></div>
        ) : (
          <div className="mt-4 overflow-x-auto">
            <table className="min-w-full text-left text-sm">
              <thead className="border-b border-slate-200 text-xs uppercase tracking-wide text-slate-500"><tr>
                {['Device','Vehicle','Provider','State','Why it is here','Evidence','Next action'].map((header) => <th key={header} className="px-3 py-2 font-semibold">{header}</th>)}
              </tr></thead>
              <tbody className="divide-y divide-slate-100">{exceptions.map((row) => <tr key={row.id} className="align-top hover:bg-slate-50">
                <td className="px-3 py-3 font-semibold text-slate-900">{row.device}</td><td className="px-3 py-3">{row.vehicle}</td><td className="px-3 py-3">{row.provider}</td>
                <td className="px-3 py-3"><StatusBadge status={row.status} /></td><td className="max-w-xs px-3 py-3">{row.reason}</td>
                <td className="max-w-xs px-3 py-3 text-slate-600">{row.evidence}</td><td className="max-w-xs px-3 py-3 font-medium text-slate-800">{row.action}</td>
              </tr>)}</tbody>
            </table>
          </div>
        )}
        {!queueTransitionPending ? <div className="mt-4 flex flex-wrap items-center justify-between gap-3 border-t border-slate-200 pt-4 text-sm text-slate-600" aria-live="polite">
          <span>{rangeStart}–{rangeEnd} of {total} · Page {page} of {pageCount}</span>
          <div className="flex gap-2"><button className="btn-ghost" disabled={page <= 1 || query.isFetching} onClick={() => setPage((value) => Math.max(1, value - 1))}>Previous</button><button className="btn-ghost" disabled={page >= pageCount || query.isFetching} onClick={() => setPage((value) => Math.min(pageCount, value + 1))}>Next</button></div>
        </div> : null}
      </section>

      <section className="grid gap-3 md:grid-cols-3">
        <button className="rounded-xl border border-slate-200 bg-white p-4 text-left hover:border-teal-400" onClick={() => navigate('/iot-devices')}><ShieldCheck className="h-5 w-5 text-teal-700" /><h3 className="mt-3 font-semibold">Trust & lifecycle</h3><p className="mt-1 text-sm text-slate-500">Provision, assign, rotate credentials, and inspect evidence-backed health.</p></button>
        <button className="rounded-xl border border-slate-200 bg-white p-4 text-left hover:border-teal-400" onClick={() => navigate('/gps-tracking')}><MapPinned className="h-5 w-5 text-teal-700" /><h3 className="mt-3 font-semibold">Location truth</h3><p className="mt-1 text-sm text-slate-500">Separate device fix time, ingress time, freshness, and operational position.</p></button>
        <button className="rounded-xl border border-slate-200 bg-white p-4 text-left hover:border-teal-400" onClick={() => navigate('/obd-j1939')}><Gauge className="h-5 w-5 text-teal-700" /><h3 className="mt-3 font-semibold">Vehicle intelligence</h3><p className="mt-1 text-sm text-slate-500">Review immutable OBD/J1939 evidence before maintenance or safety action.</p></button>
      </section>
    </div>
  );
}

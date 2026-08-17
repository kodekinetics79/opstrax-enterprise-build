import { useEffect, useMemo, useRef, useState } from 'react';
import type { ReactElement } from 'react';
import { Link } from 'react-router';
import {
  AlertTriangle, ArrowRight, CheckCircle2, Clock3, Download, FileCheck2, MapPin,
  PackageCheck, RefreshCw, Search, ShieldCheck, Truck, UserRoundCheck,
} from 'lucide-react';
import { EmptyState, ErrorState, StatusBadge } from '@/components/ui';
import { usePermissions } from '@/hooks/usePermission';
import {
  ACTIVE_LIFECYCLES, activeShipmentsApi, type ActiveShipment, type ActiveShipmentFilters,
  type ActiveShipmentResponse,
} from '@/services/activeShipmentsApi';
import { notifyApiError } from '@/services/fleetTmsApi';

const PAGE_SIZE = 20;
const normalizePermission = (value: string) => value.trim().toLowerCase().replaceAll('.', ':');

function displayToken(value?: string | null) {
  if (!value) return '—';
  return value.replace(/([a-z])([A-Z])/g, '$1 $2').replaceAll('_', ' ');
}

function formatTime(value?: string | null) {
  if (!value) return 'No ETA';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? 'No ETA' : new Intl.DateTimeFormat(undefined, {
    month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit',
  }).format(date);
}

function etaCopy(shipment: ActiveShipment) {
  if (shipment.minutesToEta == null) return 'No persisted ETA';
  const minutes = Math.abs(shipment.minutesToEta);
  const duration = minutes >= 60 ? `${Math.floor(minutes / 60)}h ${minutes % 60}m` : `${minutes}m`;
  return shipment.minutesToEta < 0 ? `${duration} overdue` : `${duration} remaining`;
}

function riskClass(risk: ActiveShipment['riskStatus']) {
  if (risk === 'Breached') return 'border-red-200 bg-red-50 text-red-800';
  if (risk === 'AtRisk') return 'border-amber-200 bg-amber-50 text-amber-800';
  if (risk === 'NoEta') return 'border-slate-200 bg-slate-100 text-slate-700';
  return 'border-emerald-200 bg-emerald-50 text-emerald-800';
}

export function ActiveShipmentsPage() {
  const rawPermissions = usePermissions();
  const permissions = useMemo(() => rawPermissions.map(normalizePermission), [rawPermissions]);
  const canExport = permissions.some((permission) => permission === '*' || permission === 'shipments:export');
  const [lifecycle, setLifecycle] = useState('All');
  const [risk, setRisk] = useState('All');
  const [assignment, setAssignment] = useState('All');
  const [readiness, setReadiness] = useState('All');
  const [sort, setSort] = useState<ActiveShipmentFilters['sort']>('risk');
  const [searchInput, setSearchInput] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [reload, setReload] = useState(0);
  const [data, setData] = useState<ActiveShipmentResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [exporting, setExporting] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const requestSequence = useRef(0);

  useEffect(() => {
    const timeout = window.setTimeout(() => { setSearch(searchInput.trim()); setPage(1); }, 300);
    return () => window.clearTimeout(timeout);
  }, [searchInput]);

  const filters = useMemo<ActiveShipmentFilters>(() => ({ lifecycle, risk, assignment, readiness, search, sort, direction: sort === 'created' ? 'desc' : 'asc' }),
    [assignment, lifecycle, readiness, risk, search, sort]);

  useEffect(() => {
    const controller = new AbortController();
    const sequence = ++requestSequence.current;
    setLoading(true);
    setError(null);
    activeShipmentsApi.list({ ...filters, page, pageSize: PAGE_SIZE, signal: controller.signal })
      .then((response) => { if (sequence === requestSequence.current) setData(response); })
      .catch((err) => {
        if (!controller.signal.aborted && sequence === requestSequence.current)
          setError(notifyApiError(err, 'Unable to load active shipments.'));
      })
      .finally(() => { if (sequence === requestSequence.current) setLoading(false); });
    return () => controller.abort();
  }, [filters, page, reload]);

  const changeFilter = (setter: (value: string) => void, value: string) => { setter(value); setPage(1); };
  const rows = data?.items ?? [];
  const total = data?.total ?? 0;
  const summary = data?.summary;
  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));

  const handleExport = async () => {
    if (!canExport) return;
    setExporting(true);
    setNotice(null);
    try {
      await activeShipmentsApi.export(filters);
      setNotice('Filtered active-shipment CSV export started.');
    } catch (err) {
      setError(notifyApiError(err, 'Unable to export active shipments.'));
    } finally {
      setExporting(false);
    }
  };

  return (
    <main className="fleet-console space-y-4" aria-labelledby="active-shipments-title">
      {/* from-slate-950/via-slate-900/to-teal-950: via-slate-900 is caught by a global
          "legacy dark surfaces -> light" override that zeroes its gradient stop, which
          rendered this header fading from solid navy at the top to white/invisible text by
          the bottom. from-slate-800/to-teal-900 aren't touched by that override. */}
      <header className="overflow-hidden rounded-[28px] border border-slate-200 bg-gradient-to-br from-slate-800 to-teal-900 p-5 text-white shadow-xl sm:p-7">
        <div className="flex flex-col gap-5 lg:flex-row lg:items-end lg:justify-between">
          <div className="max-w-3xl">
            <div className="mb-3 inline-flex items-center gap-2 rounded-full border border-teal-300/20 bg-teal-300/10 px-3 py-1.5 text-[10px] font-bold uppercase tracking-[0.2em] text-teal-200">
              <span className="live-dot h-1.5 w-1.5" /> Live operations
            </div>
            <h1 id="active-shipments-title" className="text-3xl font-black tracking-tight sm:text-4xl">Active shipments</h1>
            <p className="mt-2 max-w-2xl text-sm leading-6 text-slate-300">
              Active-only execution truth: lifecycle, persisted ETA risk, assigned resources, proof and invoice readiness within your branch scope.
            </p>
            <p className="mt-3 text-xs text-slate-400" aria-live="polite">
              {data ? `Last refreshed ${formatTime(data.generatedAtUtc)} · ${total} matching active shipment${total === 1 ? '' : 's'}` : 'Connecting to live shipment data…'}
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            <button type="button" onClick={() => setReload((value) => value + 1)} disabled={loading} className="inline-flex min-h-11 items-center gap-2 rounded-xl border border-white/15 bg-white/10 px-4 py-2 text-sm font-bold text-white hover:bg-white/15 disabled:opacity-50">
              <RefreshCw className={`h-4 w-4 ${loading ? 'animate-spin' : ''}`} aria-hidden /> Refresh
            </button>
            <button type="button" onClick={handleExport} disabled={!canExport || exporting} title={!canExport ? 'shipments:export is required' : undefined} className="inline-flex min-h-11 items-center gap-2 rounded-xl bg-teal-400 px-4 py-2 text-sm font-black text-slate-950 hover:bg-teal-300 disabled:cursor-not-allowed disabled:opacity-50">
              <Download className="h-4 w-4" aria-hidden /> {exporting ? 'Exporting…' : 'Export CSV'}
            </button>
          </div>
        </div>
      </header>

      {!canExport && (
        <div className="flex items-start gap-3 rounded-2xl border border-blue-200 bg-blue-50 p-4 text-sm text-blue-800" role="status">
          <ShieldCheck className="mt-0.5 h-5 w-5 shrink-0" aria-hidden />
          <div><p className="font-bold">Read-only live view</p><p className="mt-0.5">You can monitor branch shipments. CSV export requires shipments:export.</p></div>
        </div>
      )}
      {notice && <div className="rounded-2xl border border-emerald-200 bg-emerald-50 p-3 text-sm text-emerald-800" role="status" aria-live="polite">{notice}</div>}

      <section aria-label="Active shipment health" className="grid gap-3 sm:grid-cols-2 xl:grid-cols-7">
        <Metric label="Active" value={summary?.activeTotal} icon={<PackageCheck />} />
        <Metric label="SLA breached" value={summary?.breached} icon={<AlertTriangle />} danger />
        <Metric label="At risk" value={summary?.atRisk} icon={<Clock3 />} warning />
        <Metric label="No ETA" value={summary?.noEta} icon={<MapPin />} warning />
        <Metric label="Needs resources" value={summary?.needsResources} icon={<UserRoundCheck />} />
        <Metric label="POD pending" value={summary?.podPending} icon={<FileCheck2 />} />
        <Metric label="Invoice blocked" value={summary?.invoiceBlocked} icon={<Truck />} />
      </section>

      <section className="rounded-[24px] border border-slate-200 bg-white p-4 shadow-sm" aria-label="Active shipment filters">
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-6">
          <label className="text-xs font-bold text-slate-600 xl:col-span-2">Search active shipments
            <span className="relative mt-1 block"><Search className="pointer-events-none absolute left-3 top-3 h-4 w-4 text-slate-400" aria-hidden /><input value={searchInput} onChange={(event) => setSearchInput(event.target.value)} maxLength={120} placeholder="Shipment, customer, lane, driver, vehicle, route" className="field min-h-11 w-full pl-9!" /></span>
          </label>
          <Filter label="Lifecycle" value={lifecycle} options={['All', ...ACTIVE_LIFECYCLES]} onChange={(value) => changeFilter(setLifecycle, value)} />
          <Filter label="SLA / ETA risk" value={risk} options={['All', 'Breached', 'AtRisk', 'NoEta', 'OnTrack']} onChange={(value) => changeFilter(setRisk, value)} />
          <Filter label="Assignment" value={assignment} options={['All', 'FullyAssigned', 'NeedsResources']} onChange={(value) => changeFilter(setAssignment, value)} />
          <Filter label="Readiness" value={readiness} options={['All', 'PodReady', 'PodPending', 'InvoiceReady', 'InvoiceBlocked']} onChange={(value) => changeFilter(setReadiness, value)} />
        </div>
        <div className="mt-3 flex flex-wrap items-end justify-between gap-3 border-t border-slate-100 pt-3">
          <label className="text-xs font-bold text-slate-600">Sort by
            <select value={sort} onChange={(event) => { setSort(event.target.value as ActiveShipmentFilters['sort']); setPage(1); }} className="field ml-2 min-h-10">
              <option value="risk">Highest risk</option><option value="eta">Nearest ETA</option><option value="priority">Priority</option><option value="created">Newest created</option>
            </select>
          </label>
          <button type="button" onClick={() => { setLifecycle('All'); setRisk('All'); setAssignment('All'); setReadiness('All'); setSearchInput(''); setSearch(''); setSort('risk'); setPage(1); }} className="btn-ghost text-xs">Clear filters</button>
        </div>
      </section>

      {error && <ErrorState message={error} onRetry={() => setReload((value) => value + 1)} />}
      {loading && !data ? <LoadingRows /> : !error && rows.length === 0 ? (
        <EmptyState title="No active shipments match" subtitle="Terminal shipments are intentionally excluded. Adjust the live-operation filters or confirm this branch has active work." />
      ) : !error && (
        <section className="overflow-hidden rounded-[24px] border border-slate-200 bg-white shadow-sm" aria-busy={loading}>
          {loading && <div className="h-1 animate-pulse bg-teal-400" />}
          <div className="hidden overflow-x-auto md:block">
            <table className="w-full min-w-[1180px] border-collapse text-left text-sm">
              <caption className="sr-only">Active shipments, live risk, resources, proof and invoice readiness</caption>
              <thead className="bg-slate-50 text-[10px] uppercase tracking-[0.16em] text-slate-500">
                <tr>{['Shipment / customer', 'Lane / lifecycle', 'SLA / ETA', 'Resources', 'Tracking', 'POD', 'Invoice', 'Open'].map((heading) => <th key={heading} scope="col" className="px-4 py-3 font-black">{heading}</th>)}</tr>
              </thead>
              <tbody className="divide-y divide-slate-100">{rows.map((shipment) => <ShipmentTableRow key={shipment.id} shipment={shipment} />)}</tbody>
            </table>
          </div>
          <div className="space-y-3 p-3 md:hidden">{rows.map((shipment) => <ShipmentMobileCard key={shipment.id} shipment={shipment} />)}</div>
          <div className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-200 bg-slate-50 px-4 py-3 text-xs text-slate-600">
            <span aria-live="polite">Page {page} of {totalPages} · {total} matching records</span>
            <div className="flex gap-2">
              <button type="button" disabled={page <= 1 || loading} onClick={() => setPage((value) => Math.max(1, value - 1))} className="btn-secondary min-h-10 text-xs disabled:opacity-40">Previous</button>
              <button type="button" disabled={page >= totalPages || loading} onClick={() => setPage((value) => value + 1)} className="btn-secondary min-h-10 text-xs disabled:opacity-40">Next</button>
            </div>
          </div>
        </section>
      )}

      <nav aria-label="Related active shipment modules" className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
        <p className="mb-3 text-[10px] font-black uppercase tracking-[0.18em] text-slate-500">Continue the workflow</p>
        <div className="flex flex-wrap gap-2">
          {[['/shipments', 'All shipments'], ['/route-plans', 'Route plans'], ['/map-view', 'Live map'], ['/proof-of-delivery', 'Proof of delivery'], ['/finance/billing', 'Billing']].map(([to, label]) => <Link key={to} to={to} className="inline-flex min-h-10 items-center gap-1 rounded-full border border-slate-200 bg-white px-3 py-2 text-xs font-bold text-slate-700 hover:border-teal-300 hover:text-teal-700">{label}<ArrowRight className="h-3.5 w-3.5" aria-hidden /></Link>)}
        </div>
      </nav>
    </main>
  );
}

function Metric({ label, value, icon, danger, warning }: { label: string; value?: number; icon: ReactElement; danger?: boolean; warning?: boolean }) {
  return <div className={`rounded-2xl border p-4 ${danger ? 'border-red-200 bg-red-50' : warning ? 'border-amber-200 bg-amber-50' : 'border-slate-200 bg-white'}`}><div className="flex items-center justify-between"><p className="text-[10px] font-black uppercase tracking-[0.15em] text-slate-500">{label}</p><span className="[&>svg]:h-4 [&>svg]:w-4 text-slate-500">{icon}</span></div><p className="mt-2 text-2xl font-black text-slate-950">{value ?? '—'}</p></div>;
}

function Filter({ label, value, options, onChange }: { label: string; value: string; options: readonly string[]; onChange: (value: string) => void }) {
  return <label className="text-xs font-bold text-slate-600">{label}<select aria-label={`${label} filter`} value={value} onChange={(event) => onChange(event.target.value)} className="field mt-1 min-h-11 w-full">{options.map((option) => <option key={option} value={option}>{displayToken(option)}</option>)}</select></label>;
}

function ShipmentTableRow({ shipment }: { shipment: ActiveShipment }) {
  return <tr className="align-top hover:bg-slate-50/70">
    <td className="px-4 py-4"><p className="font-black text-slate-900">{shipment.shipmentNumber}</p><p className="mt-1 max-w-48 truncate text-xs text-slate-500">{shipment.customerName}</p><StatusBadge status={shipment.priority} /></td>
    <td className="px-4 py-4"><p className="max-w-52 text-xs font-semibold text-slate-700">{shipment.origin} → {shipment.destination}</p><div className="mt-2"><StatusBadge status={shipment.status} /></div></td>
    <td className="px-4 py-4"><span className={`inline-flex rounded-full border px-2.5 py-1 text-[10px] font-black uppercase tracking-[0.12em] ${riskClass(shipment.riskStatus)}`}>{displayToken(shipment.riskStatus)}</span><p className="mt-2 text-xs font-bold text-slate-700">{formatTime(shipment.etaUtc)}</p><p className="text-[10px] text-slate-500">{etaCopy(shipment)} · {shipment.etaSource}</p></td>
    <td className="px-4 py-4 text-xs"><p className="font-bold text-slate-700">{shipment.driverName || 'Driver needed'}</p><p className="mt-1 text-slate-500">{shipment.vehicleNumber || 'Vehicle needed'} · {shipment.routeCode || 'Route pending'}</p><p className="mt-1 font-semibold text-slate-600">{displayToken(shipment.assignmentStatus)}</p></td>
    <td className="px-4 py-4 text-xs"><p className="font-bold text-slate-700">{shipment.latestLocation || 'No tracking location'}</p><p className="mt-1 text-slate-500">{displayToken(shipment.trackingFreshness)}</p></td>
    <td className="px-4 py-4"><StatusBadge status={shipment.podReady ? 'POD Ready' : 'POD Pending'} /><p className="mt-2 text-[10px] text-slate-500">{shipment.verifiedPods}/{shipment.deliveryStops} delivery stops verified</p></td>
    <td className="px-4 py-4"><StatusBadge status={shipment.isInvoiceReady ? 'Invoice Ready' : 'Invoice Blocked'} /></td>
    <td className="px-4 py-4"><div className="flex flex-col gap-2"><Link to={`/shipments?jobId=${encodeURIComponent(shipment.id)}`} className="text-xs font-black text-teal-700 hover:underline">Lifecycle</Link><Link to={`/proof-of-delivery?jobId=${encodeURIComponent(shipment.id)}`} className="text-xs font-bold text-slate-600 hover:text-teal-700">Proof</Link></div></td>
  </tr>;
}

function ShipmentMobileCard({ shipment }: { shipment: ActiveShipment }) {
  return <article className="rounded-2xl border border-slate-200 bg-white p-4"><div className="flex items-start justify-between gap-3"><div><h2 className="font-black text-slate-900">{shipment.shipmentNumber}</h2><p className="text-xs text-slate-500">{shipment.customerName}</p></div><StatusBadge status={shipment.status} /></div><p className="mt-3 text-sm font-semibold text-slate-700">{shipment.origin} → {shipment.destination}</p><div className="mt-3 grid grid-cols-2 gap-2 text-xs"><div className="rounded-xl bg-slate-50 p-3"><p className="text-[10px] font-bold uppercase text-slate-400">ETA risk</p><p className="mt-1 font-black">{displayToken(shipment.riskStatus)}</p><p className="text-slate-500">{etaCopy(shipment)}</p></div><div className="rounded-xl bg-slate-50 p-3"><p className="text-[10px] font-bold uppercase text-slate-400">Resources</p><p className="mt-1 font-black">{shipment.driverName || 'Driver needed'}</p><p className="text-slate-500">{shipment.vehicleNumber || 'Vehicle needed'}</p></div></div><div className="mt-3 flex flex-wrap gap-2"><StatusBadge status={shipment.podReady ? 'POD Ready' : 'POD Pending'} /><StatusBadge status={shipment.isInvoiceReady ? 'Invoice Ready' : 'Invoice Blocked'} /></div><div className="mt-4 grid grid-cols-2 gap-2"><Link to={`/shipments?jobId=${encodeURIComponent(shipment.id)}`} className="btn-secondary justify-center text-xs">Lifecycle</Link><Link to={`/proof-of-delivery?jobId=${encodeURIComponent(shipment.id)}`} className="btn-secondary justify-center text-xs">Proof</Link></div></article>;
}

function LoadingRows() {
  return <div className="space-y-3 rounded-2xl border border-slate-200 bg-white p-4" role="status" aria-label="Loading active shipments">{Array.from({ length: 6 }).map((_, index) => <div key={index} className="h-16 animate-pulse rounded-xl bg-slate-100" />)}<span className="sr-only">Loading active shipments</span></div>;
}

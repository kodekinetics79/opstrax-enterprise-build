import { useEffect, useMemo, useState } from 'react';
import {
  AlertTriangle, BadgeCheck, CalendarClock, ChevronUp, Download, FileText,
  Globe, Landmark, MapPin, Plus, ReceiptText, RefreshCw, ShieldCheck, X,
} from 'lucide-react';
import {
  fleetReadinessApi, notifyApiError,
  type FleetReadinessDocument, type FleetReadinessDocumentRequest,
  type FleetReadinessExpiry, type FleetInvoiceReadySummary, type SaudiRegionReference,
} from '@/services/fleetTmsApi';
import { useTenantCountry, useTenantCurrency, countryLabel } from '@/hooks/useTenantRegion';
import { exportCsv } from '@/components/ui';

/* ── Domain constants (KSA regulatory facts, not seed data) ─────────────── */
const KSA_VAT_RATE = '15% VAT';
const KSA_INVOICING = 'ZATCA Phase 2';

const DOC_KINDS = ['Compliance', 'Transport', 'Driver'] as const;
const SUBJECT_TYPES = ['Branch', 'Carrier', 'Shipment', 'Driver', 'Vehicle', 'Customer', 'Location'] as const;

function blankForm(): FleetReadinessDocumentRequest {
  return {
    kind: 'Compliance',
    subjectType: 'Branch',
    subjectName: '',
    documentType: '',
    requiresExpiry: true,
    countryCode: 'SA',
    gregorianExpiryDate: new Date(Date.now() + 1000 * 60 * 60 * 24 * 180).toISOString().slice(0, 10),
  };
}

/* Umm al-Qura Hijri rendering for the dual-calendar column. Pure computation
   from the stored Gregorian date — no fabricated values. */
function hijriDate(iso?: string | null): string | null {
  if (!iso) return null;
  try {
    return new Intl.DateTimeFormat('en-u-ca-islamic-umalqura', { day: 'numeric', month: 'short', year: 'numeric' }).format(new Date(iso));
  } catch {
    return null;
  }
}

function daysUntil(iso?: string | null): number | null {
  if (!iso) return null;
  const ms = new Date(iso).getTime() - Date.now();
  return Math.ceil(ms / (1000 * 60 * 60 * 24));
}

function docToRequest(doc: FleetReadinessDocument): FleetReadinessDocumentRequest {
  return {
    kind: doc.kind,
    subjectType: doc.subjectType,
    subjectId: doc.subjectId,
    subjectName: doc.subjectName,
    documentType: doc.documentType,
    documentNumber: doc.documentNumber,
    transportDocumentNo: doc.transportDocumentNo,
    permitNo: doc.permitNo,
    vatNumber: doc.vatNumber,
    commercialRegistrationNo: doc.commercialRegistrationNo,
    countryCode: doc.countryCode,
    nationalAddressBuildingNo: doc.nationalAddressBuildingNo,
    nationalAddressAdditionalNo: doc.nationalAddressAdditionalNo,
    district: doc.district,
    city: doc.city,
    region: doc.region,
    postalCode: doc.postalCode,
    documentStatus: doc.documentStatus,
    issueDate: doc.issueDate,
    hijriExpiryDate: doc.hijriExpiryDate,
    gregorianExpiryDate: doc.gregorianExpiryDate ? doc.gregorianExpiryDate.slice(0, 10) : null,
    notes: doc.notes,
    requiresExpiry: Boolean(doc.gregorianExpiryDate),
  };
}

/* ── Small presentational pieces ────────────────────────────────────────── */

function StatusStamp({ status }: { status: string }) {
  const tone =
    status === 'Expired' ? 'text-rose-700 border-rose-300 bg-rose-50' :
    status === 'ExpiringSoon' ? 'text-amber-700 border-amber-300 bg-amber-50' :
    status === 'NoExpiry' ? 'text-slate-500 border-slate-300 bg-slate-50' :
    'text-emerald-700 border-emerald-300 bg-emerald-50';
  const label = status === 'ExpiringSoon' ? 'Expiring' : status === 'NoExpiry' ? 'No expiry' : status;
  return (
    <span className={`sr-stamp inline-flex items-center rounded-lg border-2 px-2 py-0.5 text-[10px] font-black uppercase tracking-[0.14em] ${tone}`}>
      {label}
    </span>
  );
}

function DaysMeter({ days, windowDays }: { days: number | null; windowDays: number }) {
  if (days === null) return <span className="text-[11px] text-slate-400">—</span>;
  const pct = days < 0 ? 0 : Math.min(100, Math.round((days / Math.max(windowDays * 3, 1)) * 100));
  const bar = days < 0 ? 'bg-rose-500' : days <= 7 ? 'bg-amber-500' : days <= windowDays ? 'bg-amber-400' : 'bg-emerald-500';
  return (
    <div className="min-w-24">
      <div className={`text-[11px] font-bold tabular-nums ${days < 0 ? 'text-rose-600' : days <= windowDays ? 'text-amber-700' : 'text-slate-700'}`}>
        {days < 0 ? `${Math.abs(days)}d overdue` : `${days}d left`}
      </div>
      <div className="sr-neu-in mt-1 h-1.5 overflow-hidden rounded-full">
        <div className={`h-full rounded-full ${bar}`} style={{ width: `${pct}%` }} />
      </div>
    </div>
  );
}

function KpiTile({ label, value, sub, tone, meterPct }: {
  label: string; value: string; sub: string;
  tone: 'teal' | 'emerald' | 'amber' | 'rose' | 'blue' | 'violet';
  meterPct?: number;
}) {
  const accent = {
    teal: 'text-teal-700', emerald: 'text-emerald-700', amber: 'text-amber-700',
    rose: 'text-rose-700', blue: 'text-blue-700', violet: 'text-violet-700',
  }[tone];
  const bar = {
    teal: 'bg-teal-500', emerald: 'bg-emerald-500', amber: 'bg-amber-500',
    rose: 'bg-rose-500', blue: 'bg-blue-500', violet: 'bg-violet-500',
  }[tone];
  return (
    <div className="sr-clay p-3.5">
      <p className="text-[10px] font-black uppercase tracking-[0.18em] text-slate-400">{label}</p>
      <div className="sr-neu-in mt-2 rounded-xl px-3 py-2">
        <p className={`text-2xl font-black tabular-nums tracking-tight ${accent}`}>{value}</p>
      </div>
      {typeof meterPct === 'number' && (
        <div className="sr-neu-in mt-2 h-1.5 overflow-hidden rounded-full">
          <div className={`h-full rounded-full ${bar}`} style={{ width: `${Math.min(100, Math.max(0, meterPct))}%` }} />
        </div>
      )}
      <p className="mt-1.5 truncate text-[11px] text-slate-500">{sub}</p>
    </div>
  );
}

function PanelHeader({ icon: Icon, title, sub, action }: { icon: React.ElementType; title: string; sub?: string; action?: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-3 border-b border-slate-200/70 px-4 py-3">
      <div className="flex min-w-0 items-center gap-2.5">
        <span className="sr-neu-out grid h-8 w-8 shrink-0 place-items-center rounded-xl">
          <Icon className="h-4 w-4 text-teal-700" />
        </span>
        <div className="min-w-0">
          <h2 className="truncate text-[13px] font-black text-slate-900">{title}</h2>
          {sub && <p className="truncate text-[11px] text-slate-400">{sub}</p>}
        </div>
      </div>
      {action}
    </div>
  );
}

function EmptyState({ icon: Icon, title, body, action }: { icon: React.ElementType; title: string; body: string; action?: React.ReactNode }) {
  return (
    <div className="grid place-items-center px-6 py-10 text-center">
      <span className="sr-neu-in grid h-12 w-12 place-items-center rounded-2xl">
        <Icon className="h-5 w-5 text-slate-400" />
      </span>
      <p className="mt-3 text-sm font-bold text-slate-700">{title}</p>
      <p className="mt-1 max-w-sm text-xs leading-5 text-slate-500">{body}</p>
      {action && <div className="mt-3">{action}</div>}
    </div>
  );
}

function Chip({ active, onClick, children }: { active: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`rounded-full px-2.5 py-1 text-[11px] font-bold transition ${
        active ? 'sr-neu-in text-teal-800' : 'sr-neu-out text-slate-500 hover:text-slate-800'
      }`}
    >
      {children}
    </button>
  );
}

function Field({ label, children, required }: { label: string; children: React.ReactNode; required?: boolean }) {
  return (
    <label className="block min-w-0">
      <span className="mb-1 block text-[10px] font-black uppercase tracking-[0.16em] text-slate-500">
        {label}{required ? ' *' : ''}
      </span>
      {children}
    </label>
  );
}

/* ── Page ───────────────────────────────────────────────────────────────── */

type ExpirySummary = { totalDocuments: number; expiringSoon: number; expired: number; healthy: number; windowDays: number };

export function FleetSaudiReadinessPage() {
  const tenantCountry = useTenantCountry();
  const tenantCurrency = useTenantCurrency();

  const [regions, setRegions] = useState<SaudiRegionReference[]>([]);
  const [documents, setDocuments] = useState<FleetReadinessDocument[]>([]);
  const [expiries, setExpiries] = useState<{ items: FleetReadinessExpiry[]; summary: ExpirySummary } | null>(null);
  const [invoiceReady, setInvoiceReady] = useState<FleetInvoiceReadySummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [syncedAt, setSyncedAt] = useState<Date | null>(null);

  const [search, setSearch] = useState('');
  const [kindFilter, setKindFilter] = useState<string>('All');
  const [statusFilter, setStatusFilter] = useState<string>('All');

  const [formOpen, setFormOpen] = useState(false);
  const [form, setForm] = useState<FleetReadinessDocumentRequest>(blankForm());
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');

  const [renewingId, setRenewingId] = useState<string | null>(null);
  const [renewDate, setRenewDate] = useState('');
  const [renewSaving, setRenewSaving] = useState(false);

  const load = async () => {
    const [regionsRes, docsRes, expiryRes, invoiceRes] = await Promise.allSettled([
      fleetReadinessApi.regions(),
      fleetReadinessApi.documents(),
      fleetReadinessApi.expiries(),
      fleetReadinessApi.invoiceReady(),
    ]);
    setRegions(regionsRes.status === 'fulfilled' ? regionsRes.value.items : []);
    setDocuments(docsRes.status === 'fulfilled' ? docsRes.value.items : []);
    setExpiries(expiryRes.status === 'fulfilled' ? expiryRes.value : null);
    setInvoiceReady(invoiceRes.status === 'fulfilled' ? invoiceRes.value : null);
    setSyncedAt(new Date());
    const failed = [regionsRes, docsRes, expiryRes, invoiceRes].find((r) => r.status === 'rejected');
    if (failed && failed.status === 'rejected') throw failed.reason;
  };

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    load()
      .catch((err) => { if (!cancelled) notifyApiError(err, 'Some Saudi readiness data failed to load.'); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const refresh = async () => {
    setRefreshing(true);
    try {
      await load();
    } catch (err) {
      notifyApiError(err, 'Unable to refresh Saudi readiness data.');
    } finally {
      setRefreshing(false);
    }
  };

  const createDocument = async () => {
    setError('');
    setMessage('');
    if (!form.subjectName.trim() || !form.documentType.trim()) {
      setError('Subject name and document type are required.');
      return;
    }
    setSaving(true);
    try {
      const created = await fleetReadinessApi.createDocument(form);
      setDocuments((current) => [created, ...current]);
      setMessage(`${created.documentType} for ${created.subjectName} saved.`);
      setForm(blankForm());
      await refresh();
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'Failed to save document.');
    } finally {
      setSaving(false);
    }
  };

  const renewDocument = async (doc: FleetReadinessDocument) => {
    if (!renewDate) return;
    setRenewSaving(true);
    try {
      const updated = await fleetReadinessApi.updateDocument(doc.id, { ...docToRequest(doc), gregorianExpiryDate: renewDate, requiresExpiry: true });
      setDocuments((current) => current.map((d) => (d.id === doc.id ? updated : d)));
      setRenewingId(null);
      setRenewDate('');
      await refresh();
    } catch (err) {
      notifyApiError(err, 'Failed to renew document.');
    } finally {
      setRenewSaving(false);
    }
  };

  const windowDays = expiries?.summary.windowDays ?? 30;

  const filteredDocs = useMemo(() => {
    const q = search.trim().toLowerCase();
    return documents
      .filter((doc) => kindFilter === 'All' || doc.kind === kindFilter)
      .filter((doc) => {
        if (statusFilter === 'All') return true;
        if (statusFilter === 'Expiring') return doc.expiryStatus === 'ExpiringSoon';
        return doc.expiryStatus === statusFilter;
      })
      .filter((doc) => {
        if (!q) return true;
        return [doc.subjectName, doc.documentType, doc.documentNumber, doc.vatNumber, doc.commercialRegistrationNo, doc.city, doc.region, doc.permitNo, doc.transportDocumentNo]
          .join(' ').toLowerCase().includes(q);
      })
      .sort((a, b) => {
        const ad = a.gregorianExpiryDate ? new Date(a.gregorianExpiryDate).getTime() : Number.MAX_SAFE_INTEGER;
        const bd = b.gregorianExpiryDate ? new Date(b.gregorianExpiryDate).getTime() : Number.MAX_SAFE_INTEGER;
        return ad - bd;
      });
  }, [documents, kindFilter, statusFilter, search]);

  const sortedExpiries = useMemo(
    () => [...(expiries?.items ?? [])].sort((a, b) => a.daysRemaining - b.daysRemaining),
    [expiries],
  );

  const totalCities = useMemo(() => regions.reduce((sum, region) => sum + region.cities.length, 0), [regions]);
  const readinessPercent = invoiceReady?.summary.readinessPercent ?? 0;

  const exportLedger = () => {
    exportCsv('saudi_readiness_documents', filteredDocs.map((doc) => ({
      subject: doc.subjectName,
      subjectType: doc.subjectType,
      kind: doc.kind,
      documentType: doc.documentType,
      documentNumber: doc.documentNumber,
      vatNumber: doc.vatNumber,
      commercialRegistrationNo: doc.commercialRegistrationNo,
      city: doc.city,
      region: doc.region,
      gregorianExpiry: doc.gregorianExpiryDate ?? '',
      hijriExpiry: doc.hijriExpiryDate || hijriDate(doc.gregorianExpiryDate) || '',
      status: doc.expiryStatus,
      notes: doc.notes,
    })));
  };

  if (loading) {
    return (
      <div className="grid gap-4">
        <div className="sr-clay h-24 animate-pulse" />
        <div className="grid gap-4 md:grid-cols-3 xl:grid-cols-6">
          {Array.from({ length: 6 }).map((_, i) => <div key={i} className="sr-clay h-28 animate-pulse" />)}
        </div>
        <div className="sr-clay h-96 animate-pulse" />
        <style>{srStyles}</style>
      </div>
    );
  }

  return (
    <div className="flex h-full min-h-0 flex-col gap-4">
      <style>{srStyles}</style>

      {/* ── Command header ── */}
      <header className="sr-clay flex flex-wrap items-center gap-3 px-4 py-3">
        <span className="sr-neu-out grid h-11 w-11 shrink-0 place-items-center rounded-2xl">
          <ShieldCheck className="h-5 w-5 text-teal-700" />
        </span>
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="text-lg font-black tracking-tight text-slate-950">Saudi Readiness</h1>
            <span className="sr-neu-in rounded-full px-2.5 py-0.5 text-[10px] font-black uppercase tracking-[0.16em] text-teal-800">
              {countryLabel(tenantCountry)}
            </span>
          </div>
          <p className="mt-0.5 flex flex-wrap items-center gap-x-2 text-[11px] font-semibold text-slate-500">
            <span>{KSA_INVOICING}</span><span className="text-slate-300">·</span>
            <span>{KSA_VAT_RATE}</span><span className="text-slate-300">·</span>
            <span>{tenantCurrency ?? 'SAR'}</span><span className="text-slate-300">·</span>
            <span>Gregorian / Hijri dual calendar</span>
          </p>
        </div>
        <div className="ml-auto flex flex-wrap items-center gap-2">
          {syncedAt && (
            <span className="hidden text-[11px] font-semibold text-slate-400 md:inline">
              Synced {syncedAt.toLocaleTimeString('en-GB', { hour12: false })}
            </span>
          )}
          <button type="button" onClick={refresh} className="btn-secondary inline-flex items-center gap-1.5 text-xs">
            <RefreshCw className={`h-3.5 w-3.5 ${refreshing ? 'animate-spin' : ''}`} /> Refresh
          </button>
          <button
            type="button"
            onClick={exportLedger}
            disabled={filteredDocs.length === 0}
            className="btn-secondary inline-flex items-center gap-1.5 text-xs disabled:cursor-not-allowed disabled:opacity-50"
          >
            <Download className="h-3.5 w-3.5" /> Export CSV
          </button>
          <button type="button" onClick={() => setFormOpen((v) => !v)} className="btn-primary inline-flex items-center gap-1.5 text-xs">
            {formOpen ? <ChevronUp className="h-3.5 w-3.5" /> : <Plus className="h-3.5 w-3.5" />}
            Log document
          </button>
        </div>
      </header>

      {/* ── KPI band ── */}
      <div className="grid gap-3 sm:grid-cols-2 md:grid-cols-3 xl:grid-cols-6">
        <KpiTile
          label="Documents" tone="teal"
          value={String(expiries?.summary.totalDocuments ?? documents.length)}
          sub="Compliance · transport · driver"
        />
        <KpiTile
          label="Healthy" tone="emerald"
          value={String(expiries?.summary.healthy ?? 0)}
          sub={`Valid beyond ${windowDays} days`}
          meterPct={(expiries?.summary.totalDocuments ?? 0) > 0 ? ((expiries?.summary.healthy ?? 0) / (expiries?.summary.totalDocuments ?? 1)) * 100 : 0}
        />
        <KpiTile
          label={`Expiring ≤${windowDays}d`} tone="amber"
          value={String(expiries?.summary.expiringSoon ?? 0)}
          sub="Renewal follow-up needed"
        />
        <KpiTile
          label="Expired" tone="rose"
          value={String(expiries?.summary.expired ?? 0)}
          sub="Blocking operational use"
        />
        <KpiTile
          label="Invoice ready" tone="blue"
          value={`${readinessPercent}%`}
          sub={`${invoiceReady?.summary.readyCount ?? 0} shipments ZATCA-ready`}
          meterPct={readinessPercent}
        />
        <KpiTile
          label="Invoice blocked" tone="violet"
          value={String(invoiceReady?.summary.blockedCount ?? 0)}
          sub="Missing VAT / CR data"
        />
      </div>

      {/* ── Log document (collapsible) ── */}
      {formOpen && (
        <section className="sr-clay sr-paper overflow-hidden">
          <PanelHeader
            icon={FileText}
            title="Log compliance document"
            sub="Stored tenant-scoped; drives expiry radar and invoice readiness"
            action={
              <button type="button" onClick={() => setFormOpen(false)} className="icon-btn h-7 w-7" aria-label="Close form">
                <X className="h-3.5 w-3.5" />
              </button>
            }
          />
          <div className="grid gap-3 p-4 md:grid-cols-3 xl:grid-cols-6">
            <Field label="Kind" required>
              <select className="input w-full" value={form.kind} onChange={(e) => setForm((x) => ({ ...x, kind: e.target.value }))}>
                {DOC_KINDS.map((kind) => <option key={kind}>{kind}</option>)}
              </select>
            </Field>
            <Field label="Subject type" required>
              <select className="input w-full" value={form.subjectType} onChange={(e) => setForm((x) => ({ ...x, subjectType: e.target.value }))}>
                {SUBJECT_TYPES.map((type) => <option key={type}>{type}</option>)}
              </select>
            </Field>
            <Field label="Subject name" required>
              <input className="input w-full" value={form.subjectName} onChange={(e) => setForm((x) => ({ ...x, subjectName: e.target.value }))} />
            </Field>
            <Field label="Document type" required>
              <input className="input w-full" value={form.documentType} onChange={(e) => setForm((x) => ({ ...x, documentType: e.target.value }))} />
            </Field>
            <Field label="Document number">
              <input className="input w-full" value={form.documentNumber ?? ''} onChange={(e) => setForm((x) => ({ ...x, documentNumber: e.target.value }))} />
            </Field>
            <Field label="Expiry date (Gregorian)">
              <input className="input w-full" type="date" value={form.gregorianExpiryDate ?? ''} onChange={(e) => setForm((x) => ({ ...x, gregorianExpiryDate: e.target.value || null }))} />
            </Field>
            <Field label="VAT number">
              <input className="input w-full" value={form.vatNumber ?? ''} onChange={(e) => setForm((x) => ({ ...x, vatNumber: e.target.value }))} />
            </Field>
            <Field label="CR number">
              <input className="input w-full" value={form.commercialRegistrationNo ?? ''} onChange={(e) => setForm((x) => ({ ...x, commercialRegistrationNo: e.target.value }))} />
            </Field>
            <Field label="Transport doc #">
              <input className="input w-full" value={form.transportDocumentNo ?? ''} onChange={(e) => setForm((x) => ({ ...x, transportDocumentNo: e.target.value }))} />
            </Field>
            <Field label="Permit #">
              <input className="input w-full" value={form.permitNo ?? ''} onChange={(e) => setForm((x) => ({ ...x, permitNo: e.target.value }))} />
            </Field>
            <Field label="City">
              <input className="input w-full" value={form.city ?? ''} onChange={(e) => setForm((x) => ({ ...x, city: e.target.value }))} />
            </Field>
            <Field label="Region">
              <input className="input w-full" value={form.region ?? ''} onChange={(e) => setForm((x) => ({ ...x, region: e.target.value }))} />
            </Field>
            <div className="md:col-span-3 xl:col-span-4">
              <Field label="Notes">
                <input className="input w-full" value={form.notes ?? ''} onChange={(e) => setForm((x) => ({ ...x, notes: e.target.value }))} />
              </Field>
            </div>
            <div className="flex items-end gap-2 md:col-span-3 xl:col-span-2">
              {(message || error) && (
                <p className={`min-w-0 flex-1 truncate rounded-lg px-2 py-1.5 text-[11px] font-semibold ${error ? 'bg-rose-50 text-rose-700' : 'bg-emerald-50 text-emerald-700'}`}>
                  {error || message}
                </p>
              )}
              <button type="button" onClick={createDocument} disabled={saving} className="btn-primary ml-auto inline-flex items-center gap-1.5 text-xs disabled:opacity-60">
                {saving ? <RefreshCw className="h-3.5 w-3.5 animate-spin" /> : <ShieldCheck className="h-3.5 w-3.5" />}
                Save document
              </button>
            </div>
          </div>
        </section>
      )}

      {/* ── Workspace grid ── */}
      <div className="grid min-h-0 flex-1 gap-4 xl:grid-cols-12">

        {/* Document ledger */}
        <section className="sr-clay flex min-h-0 flex-col overflow-hidden xl:col-span-8">
          <PanelHeader
            icon={FileText}
            title="Document ledger"
            sub={`${filteredDocs.length} of ${documents.length} documents · sorted by nearest expiry`}
            action={
              <div className="hidden items-center gap-1.5 md:flex">
                {['All', ...DOC_KINDS].map((kind) => (
                  <Chip key={kind} active={kindFilter === kind} onClick={() => setKindFilter(kind)}>{kind}</Chip>
                ))}
              </div>
            }
          />
          <div className="flex flex-wrap items-center gap-2 border-b border-slate-200/70 px-4 py-2.5">
            <div className="sr-neu-in flex min-w-0 flex-1 items-center gap-2 rounded-xl px-3 py-1.5">
              <input
                className="w-full bg-transparent text-xs font-semibold text-slate-800 outline-none placeholder:text-slate-400"
                placeholder="Search subject, document, VAT, CR, city…"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
            </div>
            {['All', 'Healthy', 'Expiring', 'Expired'].map((status) => (
              <Chip key={status} active={statusFilter === status} onClick={() => setStatusFilter(status)}>{status}</Chip>
            ))}
          </div>

          {filteredDocs.length === 0 ? (
            <EmptyState
              icon={FileText}
              title={documents.length === 0 ? 'No compliance documents yet' : 'No documents match the current filters'}
              body={documents.length === 0
                ? 'Log commercial registrations, transport cards, permits and driver certifications to activate the expiry radar and ZATCA invoice-readiness checks.'
                : 'Adjust the kind or status filters, or clear the search to see the full ledger.'}
              action={documents.length === 0 ? (
                <button type="button" onClick={() => setFormOpen(true)} className="btn-primary inline-flex items-center gap-1.5 text-xs">
                  <Plus className="h-3.5 w-3.5" /> Log first document
                </button>
              ) : undefined}
            />
          ) : (
            <div className="min-h-0 flex-1 overflow-auto">
              <table className="min-w-full text-sm">
                <thead className="sticky top-0 z-10 bg-[#f4f8fc] text-left text-[10px] font-black uppercase tracking-[0.16em] text-slate-500">
                  <tr className="border-b border-slate-200/70">
                    <th className="px-4 py-2.5">Subject</th>
                    <th className="px-3 py-2.5">Registry IDs</th>
                    <th className="px-3 py-2.5">Location</th>
                    <th className="px-3 py-2.5">Expiry (Greg / Hijri)</th>
                    <th className="px-3 py-2.5">Runway</th>
                    <th className="px-3 py-2.5">Status</th>
                    <th className="px-3 py-2.5 text-right">Action</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {filteredDocs.map((doc) => {
                    const days = daysUntil(doc.gregorianExpiryDate);
                    const hijri = doc.hijriExpiryDate || hijriDate(doc.gregorianExpiryDate);
                    return (
                      <tr key={doc.id} className="align-top transition hover:bg-slate-50/80">
                        <td className="px-4 py-2.5">
                          <p className="font-bold text-slate-900">{doc.subjectName}</p>
                          <p className="text-[11px] text-slate-500">{doc.documentType} · {doc.subjectType} · {doc.kind}</p>
                        </td>
                        <td className="px-3 py-2.5 text-[11px] leading-5 text-slate-600">
                          {doc.documentNumber && <p>Doc {doc.documentNumber}</p>}
                          {doc.vatNumber && <p>VAT {doc.vatNumber}</p>}
                          {doc.commercialRegistrationNo && <p>CR {doc.commercialRegistrationNo}</p>}
                          {doc.permitNo && <p>Permit {doc.permitNo}</p>}
                          {!doc.documentNumber && !doc.vatNumber && !doc.commercialRegistrationNo && !doc.permitNo && <p className="text-slate-400">—</p>}
                        </td>
                        <td className="px-3 py-2.5 text-[11px] text-slate-600">
                          {(doc.city || doc.region) ? (
                            <span className="inline-flex items-center gap-1"><MapPin className="h-3 w-3 text-slate-400" />{[doc.city, doc.region].filter(Boolean).join(', ')}</span>
                          ) : <span className="text-slate-400">—</span>}
                        </td>
                        <td className="px-3 py-2.5 text-[11px] text-slate-600">
                          {doc.gregorianExpiryDate ? (
                            <>
                              <p className="font-semibold text-slate-800">{new Date(doc.gregorianExpiryDate).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' })}</p>
                              {hijri && <p className="text-slate-400">{hijri} AH</p>}
                            </>
                          ) : <span className="text-slate-400">No expiry</span>}
                        </td>
                        <td className="px-3 py-2.5"><DaysMeter days={days} windowDays={windowDays} /></td>
                        <td className="px-3 py-2.5"><StatusStamp status={doc.expiryStatus} /></td>
                        <td className="px-3 py-2.5 text-right">
                          {renewingId === doc.id ? (
                            <span className="inline-flex items-center gap-1.5">
                              <input type="date" aria-label="New expiry date" className="input h-7 px-2 py-0 text-[11px]" value={renewDate} onChange={(e) => setRenewDate(e.target.value)} />
                              <button type="button" disabled={renewSaving || !renewDate} onClick={() => renewDocument(doc)} className="btn-primary h-7 px-2 text-[11px] disabled:opacity-50">
                                {renewSaving ? '…' : 'Save'}
                              </button>
                              <button type="button" onClick={() => { setRenewingId(null); setRenewDate(''); }} className="icon-btn h-7 w-7" aria-label="Cancel renewal">
                                <X className="h-3 w-3" />
                              </button>
                            </span>
                          ) : (
                            <button
                              type="button"
                              onClick={() => { setRenewingId(doc.id); setRenewDate(doc.gregorianExpiryDate ? doc.gregorianExpiryDate.slice(0, 10) : ''); }}
                              className="sr-neu-out rounded-lg px-2.5 py-1 text-[11px] font-bold text-teal-800 transition hover:text-teal-600"
                            >
                              Renew
                            </button>
                          )}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </section>

        {/* Right rail */}
        <div className="flex min-h-0 flex-col gap-4 xl:col-span-4">

          {/* ZATCA invoice readiness */}
          <section className="sr-clay overflow-hidden">
            <PanelHeader icon={ReceiptText} title="ZATCA invoice readiness" sub="Shipments with complete VAT + CR data" />
            {invoiceReady ? (
              <div className="p-4">
                <div className="flex items-center gap-4">
                  <div className="sr-neu-in grid h-20 w-20 shrink-0 place-items-center rounded-2xl">
                    <span className="text-2xl font-black tabular-nums text-blue-700">{readinessPercent}%</span>
                  </div>
                  <div className="min-w-0 flex-1 space-y-2">
                    <div className="sr-neu-in h-2 overflow-hidden rounded-full">
                      <div className="h-full rounded-full bg-linear-to-r from-teal-500 to-blue-500" style={{ width: `${readinessPercent}%` }} />
                    </div>
                    <div className="grid grid-cols-2 gap-2 text-[11px]">
                      <div className="sr-neu-out rounded-lg px-2 py-1.5">
                        <p className="text-slate-400">Ready</p>
                        <p className="font-black text-emerald-700">{invoiceReady.summary.readyCount}</p>
                      </div>
                      <div className="sr-neu-out rounded-lg px-2 py-1.5">
                        <p className="text-slate-400">Blocked</p>
                        <p className="font-black text-rose-700">{invoiceReady.summary.blockedCount}</p>
                      </div>
                      <div className="sr-neu-out rounded-lg px-2 py-1.5">
                        <p className="text-slate-400">Carriers ready</p>
                        <p className="font-black text-slate-800">{invoiceReady.summary.carrierReady}</p>
                      </div>
                      <div className="sr-neu-out rounded-lg px-2 py-1.5">
                        <p className="text-slate-400">Branches ready</p>
                        <p className="font-black text-slate-800">{invoiceReady.summary.branchReady}</p>
                      </div>
                    </div>
                  </div>
                </div>
                {invoiceReady.blockedShipments.length > 0 && (
                  <div className="mt-3 space-y-1.5">
                    <p className="text-[10px] font-black uppercase tracking-[0.16em] text-slate-400">Blocked shipments</p>
                    {invoiceReady.blockedShipments.slice(0, 4).map((shipment) => (
                      <div key={shipment.id} className="sr-neu-in flex items-center justify-between gap-2 rounded-xl px-3 py-2">
                        <div className="min-w-0">
                          <p className="truncate text-xs font-bold text-slate-800">{shipment.shipmentNumber} · {shipment.customerName}</p>
                          <p className="truncate text-[10px] text-slate-500">{shipment.invoiceReadinessNotes || 'Missing VAT or CR registration'}</p>
                        </div>
                        <AlertTriangle className="h-3.5 w-3.5 shrink-0 text-amber-600" />
                      </div>
                    ))}
                    {invoiceReady.blockedShipments.length > 4 && (
                      <p className="text-[10px] font-semibold text-slate-400">+{invoiceReady.blockedShipments.length - 4} more blocked</p>
                    )}
                  </div>
                )}
                {invoiceReady.readyShipments.length > 0 && (
                  <p className="mt-3 inline-flex items-center gap-1.5 text-[11px] font-semibold text-emerald-700">
                    <BadgeCheck className="h-3.5 w-3.5" /> {invoiceReady.readyShipments.length} shipments cleared for e-invoicing
                  </p>
                )}
              </div>
            ) : (
              <EmptyState icon={ReceiptText} title="No invoice readiness data" body="Shipment VAT readiness appears once shipments carry customer VAT and commercial-registration data." />
            )}
          </section>

          {/* Expiry radar */}
          <section className="sr-clay flex min-h-0 flex-1 flex-col overflow-hidden">
            <PanelHeader icon={CalendarClock} title="Expiry radar" sub={`Documents inside the ${windowDays}-day window`} />
            {sortedExpiries.length === 0 ? (
              <EmptyState icon={CalendarClock} title="Nothing expiring" body={`No tracked documents expire inside ${windowDays} days. Renewals will surface here automatically.`} />
            ) : (
              <div className="min-h-0 flex-1 space-y-2 overflow-y-auto p-3">
                {sortedExpiries.map((item) => (
                  <div key={item.id} className="sr-neu-in rounded-xl px-3 py-2">
                    <div className="flex items-center justify-between gap-2">
                      <p className="min-w-0 truncate text-xs font-bold text-slate-800">{item.subjectName}</p>
                      <span className={`shrink-0 text-[11px] font-black tabular-nums ${item.daysRemaining < 0 ? 'text-rose-600' : item.daysRemaining <= 7 ? 'text-amber-700' : 'text-slate-600'}`}>
                        {item.daysRemaining < 0 ? `${Math.abs(item.daysRemaining)}d overdue` : `${item.daysRemaining}d`}
                      </span>
                    </div>
                    <p className="truncate text-[10px] text-slate-500">{item.documentType} · {item.subjectType}{item.documentNumber ? ` · ${item.documentNumber}` : ''}</p>
                    <div className="mt-1.5 h-1 overflow-hidden rounded-full bg-white/70 shadow-inner">
                      <div
                        className={`h-full rounded-full ${item.daysRemaining < 0 ? 'bg-rose-500' : item.daysRemaining <= 7 ? 'bg-amber-500' : 'bg-emerald-500'}`}
                        style={{ width: `${Math.max(4, Math.min(100, (item.daysRemaining / windowDays) * 100))}%` }}
                      />
                    </div>
                  </div>
                ))}
              </div>
            )}
          </section>

          {/* Kingdom coverage */}
          <section className="sr-clay overflow-hidden">
            <PanelHeader
              icon={Globe}
              title="Kingdom coverage"
              sub={regions.length > 0 ? `${regions.length} regions · ${totalCities} cities on record` : undefined}
            />
            {regions.length === 0 ? (
              <EmptyState icon={Landmark} title="Region reference unavailable" body="The Saudi region reference could not be loaded from the API." />
            ) : (
              <div className="grid grid-cols-1 gap-1.5 p-3 sm:grid-cols-2">
                {regions.map((region) => (
                  <div key={region.id} className="sr-neu-out flex items-center justify-between gap-2 rounded-xl px-3 py-2">
                    <div className="min-w-0">
                      <p className="truncate text-xs font-bold text-slate-800">{region.nameEn}</p>
                      <p className="truncate text-[10px] text-slate-400" dir="rtl">{region.nameAr}</p>
                    </div>
                    <div className="shrink-0 text-right">
                      <span className={`block text-[9px] font-black uppercase tracking-[0.12em] ${region.isGccReady ? 'text-emerald-600' : 'text-amber-600'}`}>
                        {region.isGccReady ? 'GCC ready' : 'Review'}
                      </span>
                      <span className="text-[10px] font-semibold text-slate-400">{region.cities.length} cities</span>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </section>
        </div>
      </div>
    </div>
  );
}

/* Clay / neumorphic / skeuomorphic layer for this console, built on the
   light-enterprise tokens. Clay: puffy double shadow + soft top highlight.
   Neu: extruded (out) and socketed (in) controls. Skeuo: stamped status
   pills and a faint ruled-paper texture on the document form. */
const srStyles = `
  .sr-clay {
    border-radius: 20px;
    border: 1px solid rgba(203, 213, 225, .55);
    background: linear-gradient(150deg, #ffffff 0%, #f1f6fb 100%);
    box-shadow:
      0 2px 4px rgba(15, 23, 42, .04),
      0 18px 32px -20px rgba(15, 23, 42, .28),
      inset 0 1px 0 rgba(255, 255, 255, .95);
  }
  .sr-neu-out {
    background: linear-gradient(145deg, #ffffff, #e8eef6);
    box-shadow: 3px 3px 8px rgba(148, 163, 184, .32), -3px -3px 8px rgba(255, 255, 255, .95);
    border: 1px solid rgba(226, 232, 240, .6);
  }
  .sr-neu-in {
    background: linear-gradient(145deg, #e9eef5, #f6f9fd);
    box-shadow: inset 3px 3px 7px rgba(148, 163, 184, .32), inset -3px -3px 7px rgba(255, 255, 255, .95);
  }
  .sr-stamp {
    transform: rotate(-1.5deg);
    box-shadow: 0 1px 2px rgba(15, 23, 42, .12);
  }
  .sr-paper {
    background-image:
      linear-gradient(150deg, rgba(255, 255, 255, .96) 0%, rgba(241, 246, 251, .96) 100%),
      repeating-linear-gradient(0deg, rgba(13, 148, 136, .05) 0px, rgba(13, 148, 136, .05) 1px, transparent 1px, transparent 26px);
  }
`;

export default FleetSaudiReadinessPage;

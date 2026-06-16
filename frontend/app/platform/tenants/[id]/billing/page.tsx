'use client';

import { useState, useEffect, useCallback } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import {
  ArrowLeft, Plus, Download, Send, RefreshCw, X,
  Pencil, Trash2, CheckCircle, AlertCircle, Clock, Ban,
} from 'lucide-react';
import { platformApi, type TenantInvoice } from '@/src/api/platform';

// ── Helpers ───────────────────────────────────────────────────────────────────

const TODAY = () => new Date().toISOString().slice(0, 10);
const DUE30 = () => new Date(Date.now() + 30 * 86400000).toISOString().slice(0, 10);
const YM    = () => new Date().toISOString().slice(0, 7).replace('-', '');

const STATUSES = ['Draft', 'Sent', 'Paid', 'Overdue', 'Cancelled'] as const;
type Status = typeof STATUSES[number];

const STATUS_BADGE: Record<Status, string> = {
  Draft:     'text-slate-400 bg-slate-700/50 border-slate-600',
  Sent:      'text-blue-400 bg-blue-900/30 border-blue-700/30',
  Paid:      'text-emerald-400 bg-emerald-900/30 border-emerald-700/30',
  Overdue:   'text-rose-400 bg-rose-900/30 border-rose-700/30',
  Cancelled: 'text-slate-600 bg-transparent border-slate-800',
};

const STATUS_ICON: Record<Status, React.ElementType> = {
  Draft:     Clock,
  Sent:      Send,
  Paid:      CheckCircle,
  Overdue:   AlertCircle,
  Cancelled: Ban,
};

const CURRENCIES = ['USD', 'SAR', 'AED', 'EUR', 'GBP', 'QAR', 'KWD', 'BHD', 'OMR'];
const PAYMENT_METHODS = ['Bank Transfer', 'Credit Card', 'Cheque', 'Online', 'Cash', 'Other'];

function fmtDate(d: string) {
  return new Date(d).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });
}

function fmtAmount(amount: number, currency: string) {
  return `${currency} ${amount.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

// ── Form primitives ───────────────────────────────────────────────────────────

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <label className="block text-xs text-slate-400 mb-1">{label}</label>
      {children}
    </div>
  );
}

function FInput({ value, onChange, type = 'text', required, placeholder, min }: {
  value: string | number; onChange: (v: string) => void;
  type?: string; required?: boolean; placeholder?: string; min?: string;
}) {
  return (
    <input
      type={type} value={value} onChange={e => onChange(e.target.value)}
      required={required} placeholder={placeholder} min={min}
      className="w-full bg-white/[0.04] border border-white/[0.08] rounded-lg px-3 py-2 text-sm text-white placeholder-slate-600 focus:outline-none focus:border-blue-500/60 transition-colors"
    />
  );
}

function FSelect({ value, onChange, options, placeholder, label }: {
  value: string; onChange: (v: string) => void;
  options: readonly string[]; placeholder?: string; label?: string;
}) {
  return (
    <select
      value={value} onChange={e => onChange(e.target.value)}
      aria-label={label ?? placeholder ?? 'Select option'}
      title={label ?? placeholder ?? 'Select option'}
      className="w-full bg-white/[0.04] border border-white/[0.08] rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-blue-500/60 transition-colors"
    >
      {placeholder && <option value="">{placeholder}</option>}
      {options.map(o => <option key={o} value={o}>{o}</option>)}
    </select>
  );
}

// ── Create Invoice Modal ──────────────────────────────────────────────────────

interface CreateForm {
  invoiceNumber: string;
  amount: string;
  currencyCode: string;
  status: string;
  periodDescription: string;
  invoiceDate: string;
  dueDate: string;
  notes: string;
}

function CreateModal({ tenantId, onClose, onCreated }: {
  tenantId: string; onClose: () => void; onCreated: () => void;
}) {
  const [form, setForm] = useState<CreateForm>({
    invoiceNumber: `INV-${YM()}-001`,
    amount: '',
    currencyCode: 'USD',
    status: 'Draft',
    periodDescription: '',
    invoiceDate: TODAY(),
    dueDate: DUE30(),
    notes: '',
  });
  const [saving, setSaving] = useState(false);
  const [err, setErr] = useState('');

  const set = (k: keyof CreateForm) => (v: string) => setForm(f => ({ ...f, [k]: v }));

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    const amount = parseFloat(form.amount);
    if (isNaN(amount) || amount <= 0) { setErr('Amount must be a positive number.'); return; }
    setSaving(true); setErr('');
    try {
      await platformApi.createInvoice(tenantId, {
        invoiceNumber: form.invoiceNumber.trim(),
        amount,
        currencyCode: form.currencyCode,
        status: form.status,
        periodDescription: form.periodDescription.trim() || undefined,
        invoiceDate: form.invoiceDate,
        dueDate: form.dueDate,
        notes: form.notes.trim() || undefined,
      });
      onCreated(); onClose();
    } catch (ex: unknown) {
      setErr((ex as { response?: { data?: { message?: string } } })?.response?.data?.message ?? 'Failed to create invoice.');
    } finally { setSaving(false); }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-black/70 backdrop-blur-sm" onClick={onClose} />
      <div className="relative w-full max-w-lg bg-[#0d1117] border border-white/10 rounded-2xl shadow-2xl overflow-hidden">
        <div className="flex items-center justify-between px-6 py-4 border-b border-white/[0.07]">
          <div>
            <h2 className="text-sm font-semibold text-white">New Invoice</h2>
            <p className="text-[11px] text-slate-500 mt-0.5">Fill in the details below to create an invoice</p>
          </div>
          <button type="button" onClick={onClose} className="text-slate-500 hover:text-white transition-colors">
            <X className="h-4 w-4" />
          </button>
        </div>

        <form onSubmit={submit} className="px-6 py-5 space-y-4 max-h-[80vh] overflow-y-auto">
          {err && (
            <div className="text-xs text-rose-400 bg-rose-500/10 border border-rose-500/20 rounded-lg px-3 py-2">{err}</div>
          )}

          <div className="grid grid-cols-2 gap-3">
            <div className="col-span-2">
              <Field label="Invoice Number *">
                <FInput value={form.invoiceNumber} onChange={set('invoiceNumber')} required placeholder="INV-202606-001" />
              </Field>
            </div>

            <Field label="Amount *">
              <FInput value={form.amount} onChange={set('amount')} type="number" required placeholder="0.00" min="0.01" />
            </Field>

            <Field label="Currency">
              <FSelect value={form.currencyCode} onChange={set('currencyCode')} options={CURRENCIES} label="Currency" />
            </Field>

            <Field label="Invoice Date *">
              <FInput value={form.invoiceDate} onChange={set('invoiceDate')} type="date" required />
            </Field>

            <Field label="Due Date *">
              <FInput value={form.dueDate} onChange={set('dueDate')} type="date" required />
            </Field>

            <div className="col-span-2">
              <Field label="Period / Description">
                <FInput value={form.periodDescription} onChange={set('periodDescription')} placeholder="e.g. June 2026 — Monthly Subscription" />
              </Field>
            </div>

            <div className="col-span-2">
              <Field label="Initial Status">
                <FSelect value={form.status} onChange={set('status')} options={STATUSES} label="Initial Status" />
              </Field>
            </div>

            <div className="col-span-2">
              <Field label="Internal Notes">
                <textarea
                  value={form.notes} onChange={e => set('notes')(e.target.value)} rows={3}
                  placeholder="Internal notes visible to platform admins only…"
                  className="w-full bg-white/[0.04] border border-white/[0.08] rounded-lg px-3 py-2 text-sm text-white placeholder-slate-600 focus:outline-none focus:border-blue-500/60 transition-colors resize-none"
                />
              </Field>
            </div>
          </div>

          <div className="flex gap-3 pt-1 border-t border-white/[0.07]">
            <button type="button" onClick={onClose}
              className="flex-1 border border-white/10 text-slate-400 hover:text-white rounded-lg py-2 text-sm transition-colors">
              Cancel
            </button>
            <button type="submit" disabled={saving}
              className="flex-1 bg-blue-600 hover:bg-blue-500 text-white rounded-lg py-2 text-sm font-semibold transition-colors disabled:opacity-40">
              {saving ? 'Creating…' : 'Create Invoice'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

// ── Edit Invoice Modal ────────────────────────────────────────────────────────

interface EditForm {
  status: Status;
  paymentMethod: string;
  paymentReference: string;
  paidDate: string;
  notes: string;
}

function EditModal({ tenantId, invoice, onClose, onSaved }: {
  tenantId: string; invoice: TenantInvoice; onClose: () => void; onSaved: () => void;
}) {
  const [form, setForm] = useState<EditForm>({
    status: invoice.status as Status,
    paymentMethod: invoice.paymentMethod ?? '',
    paymentReference: invoice.paymentReference ?? '',
    paidDate: invoice.paidDate ? invoice.paidDate.slice(0, 10) : '',
    notes: invoice.notes ?? '',
  });
  const [saving, setSaving] = useState(false);
  const [err, setErr] = useState('');

  const set = (k: keyof EditForm) => (v: string) => setForm(f => ({ ...f, [k]: v }));
  const showPayment = form.status === 'Paid' || !!form.paymentMethod;

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setSaving(true); setErr('');
    try {
      await platformApi.updateInvoice(tenantId, invoice.id, {
        status: form.status,
        paymentMethod: form.paymentMethod.trim() || undefined,
        paymentReference: form.paymentReference.trim() || undefined,
        paidDate: form.paidDate || undefined,
        notes: form.notes.trim() || undefined,
      });
      onSaved(); onClose();
    } catch (ex: unknown) {
      setErr((ex as { response?: { data?: { message?: string } } })?.response?.data?.message ?? 'Failed to save.');
    } finally { setSaving(false); }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-black/70 backdrop-blur-sm" onClick={onClose} />
      <div className="relative w-full max-w-lg bg-[#0d1117] border border-white/10 rounded-2xl shadow-2xl overflow-hidden">
        <div className="flex items-center justify-between px-6 py-4 border-b border-white/[0.07]">
          <div>
            <h2 className="text-sm font-semibold text-white">Edit Invoice</h2>
            <p className="text-[11px] text-slate-500 mt-0.5 font-mono">{invoice.invoiceNumber}</p>
          </div>
          <button type="button" onClick={onClose} className="text-slate-500 hover:text-white transition-colors">
            <X className="h-4 w-4" />
          </button>
        </div>

        <form onSubmit={submit} className="px-6 py-5 space-y-4 max-h-[80vh] overflow-y-auto">
          {/* Read-only invoice summary */}
          <div className="grid grid-cols-2 gap-3 bg-white/[0.02] border border-white/[0.06] rounded-xl p-4 text-sm">
            <div>
              <p className="text-[10px] text-slate-600 uppercase tracking-widest mb-1">Amount</p>
              <p className="text-white font-semibold">{fmtAmount(invoice.amount, invoice.currencyCode)}</p>
            </div>
            <div>
              <p className="text-[10px] text-slate-600 uppercase tracking-widest mb-1">Invoice Date</p>
              <p className="text-slate-300">{fmtDate(invoice.invoiceDate)}</p>
            </div>
            <div>
              <p className="text-[10px] text-slate-600 uppercase tracking-widest mb-1">Due Date</p>
              <p className="text-slate-300">{fmtDate(invoice.dueDate)}</p>
            </div>
            {invoice.periodDescription && (
              <div>
                <p className="text-[10px] text-slate-600 uppercase tracking-widest mb-1">Period</p>
                <p className="text-slate-300 text-xs">{invoice.periodDescription}</p>
              </div>
            )}
          </div>

          {err && (
            <div className="text-xs text-rose-400 bg-rose-500/10 border border-rose-500/20 rounded-lg px-3 py-2">{err}</div>
          )}

          <div className="grid grid-cols-2 gap-3">
            <div className="col-span-2">
              <Field label="Status">
                <FSelect value={form.status} onChange={v => set('status')(v as Status)} options={STATUSES} label="Status" />
              </Field>
            </div>

            {showPayment && (
              <>
                <Field label="Payment Method">
                  <FSelect value={form.paymentMethod} onChange={set('paymentMethod')} options={PAYMENT_METHODS} placeholder="— select method —" label="Payment Method" />
                </Field>

                <Field label="Payment Reference">
                  <FInput value={form.paymentReference} onChange={set('paymentReference')} placeholder="TXN-12345 / cheque #" />
                </Field>

                <div className="col-span-2">
                  <Field label="Date Paid">
                    <FInput value={form.paidDate} onChange={set('paidDate')} type="date" />
                  </Field>
                </div>
              </>
            )}

            <div className="col-span-2">
              <Field label="Internal Notes">
                <textarea
                  value={form.notes} onChange={e => set('notes')(e.target.value)} rows={3}
                  placeholder="Internal notes…"
                  className="w-full bg-white/[0.04] border border-white/[0.08] rounded-lg px-3 py-2 text-sm text-white placeholder-slate-600 focus:outline-none focus:border-blue-500/60 transition-colors resize-none"
                />
              </Field>
            </div>
          </div>

          <div className="flex gap-3 pt-1 border-t border-white/[0.07]">
            <button type="button" onClick={onClose}
              className="flex-1 border border-white/10 text-slate-400 hover:text-white rounded-lg py-2 text-sm transition-colors">
              Cancel
            </button>
            <button type="submit" disabled={saving}
              className="flex-1 bg-blue-600 hover:bg-blue-500 text-white rounded-lg py-2 text-sm font-semibold transition-colors disabled:opacity-40">
              {saving ? 'Saving…' : 'Save Changes'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

// ── Delete Confirm ────────────────────────────────────────────────────────────

function DeleteDialog({ invoice, onClose, onDeleted }: {
  invoice: TenantInvoice; onClose: () => void; onDeleted: () => void;
}) {
  const [deleting, setDeleting] = useState(false);
  const [err, setErr]           = useState('');

  async function confirm() {
    setDeleting(true); setErr('');
    try {
      await platformApi.deleteInvoice(invoice.tenantId, invoice.id);
      onDeleted(); onClose();
    } catch {
      setErr('Failed to delete. The invoice may have already been removed.');
      setDeleting(false);
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-black/70 backdrop-blur-sm" onClick={onClose} />
      <div className="relative w-full max-w-sm bg-[#0d1117] border border-white/10 rounded-2xl shadow-2xl p-6 space-y-4">
        <div className="flex items-center gap-3">
          <div className="h-9 w-9 rounded-xl bg-rose-500/10 border border-rose-500/20 flex items-center justify-center shrink-0">
            <Trash2 className="h-4 w-4 text-rose-400" />
          </div>
          <div>
            <h3 className="text-sm font-semibold text-white">Delete Invoice</h3>
            <p className="text-xs text-slate-500 mt-0.5 font-mono">{invoice.invoiceNumber}</p>
          </div>
        </div>
        <p className="text-sm text-slate-400">
          Permanently delete{' '}
          <span className="text-white font-mono">{invoice.invoiceNumber}</span> for{' '}
          <span className="text-white">{fmtAmount(invoice.amount, invoice.currencyCode)}</span>?
          This cannot be undone.
        </p>
        {err && <p className="text-xs text-rose-400">{err}</p>}
        <div className="flex gap-3">
          <button type="button" onClick={onClose}
            className="flex-1 border border-white/10 text-slate-400 hover:text-white rounded-lg py-2 text-sm transition-colors">
            Cancel
          </button>
          <button type="button" onClick={confirm} disabled={deleting}
            className="flex-1 bg-rose-600 hover:bg-rose-500 text-white rounded-lg py-2 text-sm font-semibold transition-colors disabled:opacity-40">
            {deleting ? 'Deleting…' : 'Delete'}
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Main Page ─────────────────────────────────────────────────────────────────

export default function TenantBillingPage() {
  const { id } = useParams<{ id: string }>();
  const router  = useRouter();

  const [invoices, setInvoices]       = useState<TenantInvoice[]>([]);
  const [loading, setLoading]         = useState(true);
  const [showCreate, setShowCreate]   = useState(false);
  const [editing, setEditing]         = useState<TenantInvoice | null>(null);
  const [deleting, setDeleting]       = useState<TenantInvoice | null>(null);
  const [sending, setSending]         = useState<string | null>(null);
  const [downloading, setDownloading] = useState<string | null>(null);
  const [toasts, setToasts]           = useState<Record<string, { text: string; ok: boolean }>>({});

  useEffect(() => {
    const token = typeof window !== 'undefined' ? localStorage.getItem('platform_access_token') : null;
    if (!token) { router.replace('/platform/login'); return; }
    load();
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);

  const load = useCallback(async () => {
    setLoading(true);
    try { setInvoices(await platformApi.listInvoices(id)); }
    finally { setLoading(false); }
  }, [id]);

  function toast(invoiceId: string, text: string, ok: boolean) {
    setToasts(t => ({ ...t, [invoiceId]: { text, ok } }));
    setTimeout(() => setToasts(t => { const n = { ...t }; delete n[invoiceId]; return n; }), 5000);
  }

  async function downloadPdf(inv: TenantInvoice) {
    setDownloading(inv.id);
    try { await platformApi.downloadInvoicePdf(id, inv.id, inv.invoiceNumber); }
    catch { toast(inv.id, 'PDF download failed.', false); }
    finally { setDownloading(null); }
  }

  async function sendEmail(inv: TenantInvoice) {
    setSending(inv.id);
    try {
      const r = await platformApi.sendInvoiceEmail(id, inv.id);
      if (r.smtpRequired) {
        toast(inv.id, 'SMTP not configured — download PDF and email manually.', false);
      } else {
        toast(inv.id, `Sent to ${r.billingEmail} ✓`, true);
        await load();
      }
    } catch { toast(inv.id, 'Failed to send email.', false); }
    finally { setSending(null); }
  }

  async function quickStatus(inv: TenantInvoice, status: Status) {
    try {
      await platformApi.updateInvoice(id, inv.id, { status });
      await load();
    } catch { toast(inv.id, `Could not mark as ${status}.`, false); }
  }

  const currency = invoices[0]?.currencyCode ?? 'USD';
  const totals = invoices.reduce(
    (a, inv) => {
      a.total += inv.amount;
      if (inv.status === 'Paid') a.paid += inv.amount;
      if (inv.status === 'Overdue') a.overdue += inv.amount;
      if (inv.status === 'Draft' || inv.status === 'Sent') a.outstanding += inv.amount;
      return a;
    },
    { total: 0, paid: 0, overdue: 0, outstanding: 0 },
  );

  return (
    <div className="space-y-5">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <Link href={`/platform/tenants/${id}`}
            className="h-8 w-8 flex items-center justify-center text-slate-500 hover:text-white border border-white/10 rounded-lg transition-colors shrink-0">
            <ArrowLeft className="h-4 w-4" />
          </Link>
          <div>
            <h1 className="text-lg font-bold text-white">Billing & Invoices</h1>
            <p className="text-xs text-slate-500 mt-0.5">{invoices.length} invoice{invoices.length !== 1 ? 's' : ''}</p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <button type="button" onClick={load} disabled={loading}
            className="h-8 w-8 flex items-center justify-center text-slate-500 hover:text-white border border-white/10 rounded-lg transition-colors disabled:opacity-40">
            <RefreshCw className={`h-3.5 w-3.5 ${loading ? 'animate-spin' : ''}`} />
          </button>
          <button type="button" onClick={() => setShowCreate(true)}
            className="flex items-center gap-1.5 bg-blue-600 hover:bg-blue-500 text-white px-3 py-1.5 rounded-lg text-sm font-semibold transition-colors">
            <Plus className="h-3.5 w-3.5" /> New Invoice
          </button>
        </div>
      </div>

      {/* Summary cards */}
      {invoices.length > 0 && (
        <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
          {([
            { label: 'Total Billed', value: fmtAmount(totals.total, currency), cls: 'text-white' },
            { label: 'Paid',         value: fmtAmount(totals.paid, currency),  cls: 'text-emerald-400' },
            { label: 'Outstanding',  value: fmtAmount(totals.outstanding, currency), cls: 'text-blue-400' },
            { label: 'Overdue',      value: fmtAmount(totals.overdue, currency), cls: totals.overdue > 0 ? 'text-rose-400' : 'text-slate-600' },
          ] as const).map(c => (
            <div key={c.label} className="bg-[#161b22] border border-white/[0.07] rounded-xl p-4">
              <p className="text-[10px] font-semibold text-slate-600 uppercase tracking-widest mb-2">{c.label}</p>
              <p className={`text-lg font-bold tabular-nums ${c.cls}`}>{c.value}</p>
            </div>
          ))}
        </div>
      )}

      {/* Invoice table */}
      <div className="bg-[#161b22] border border-white/[0.07] rounded-xl overflow-hidden">
        {loading ? (
          <div className="flex items-center justify-center py-12">
            <div className="h-4 w-4 animate-spin rounded-full border-2 border-blue-500 border-t-transparent" />
          </div>
        ) : invoices.length === 0 ? (
          <div className="text-center py-14">
            <div className="h-10 w-10 mx-auto mb-3 rounded-xl bg-white/[0.03] border border-white/[0.07] flex items-center justify-center">
              <Plus className="h-5 w-5 text-slate-600" />
            </div>
            <p className="text-sm text-slate-500 mb-1">No invoices yet</p>
            <p className="text-xs text-slate-600 mb-4">Create the first invoice for this tenant</p>
            <button type="button" onClick={() => setShowCreate(true)}
              className="inline-flex items-center gap-1.5 bg-blue-600 hover:bg-blue-500 text-white px-4 py-2 rounded-lg text-sm font-semibold transition-colors">
              <Plus className="h-3.5 w-3.5" /> New Invoice
            </button>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[860px]">
              <thead>
                <tr className="border-b border-white/[0.06]">
                  {['Invoice #', 'Period', 'Amount', 'Status', 'Issued', 'Due', 'Payment', 'Actions'].map(h => (
                    <th key={h} className="px-4 py-3 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-widest whitespace-nowrap">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {invoices.map(inv => {
                  const Icon = STATUS_ICON[inv.status as Status] ?? Clock;
                  return (
                    <>
                      <tr key={inv.id} className="border-b border-white/[0.04] hover:bg-white/[0.015] transition-colors">
                        {/* Invoice # */}
                        <td className="px-4 py-3">
                          <p className="text-sm text-white font-mono font-medium">{inv.invoiceNumber}</p>
                          <p className="text-[10px] text-slate-600 mt-0.5">
                            {new Date(inv.createdAtUtc).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })}
                          </p>
                        </td>

                        {/* Period */}
                        <td className="px-3 py-3 text-xs text-slate-400 max-w-[140px]">
                          {inv.periodDescription ?? <span className="text-slate-700">—</span>}
                        </td>

                        {/* Amount */}
                        <td className="px-3 py-3">
                          <p className="text-sm text-white font-semibold tabular-nums">{fmtAmount(inv.amount, inv.currencyCode)}</p>
                          {inv.paidDate && (
                            <p className="text-[10px] text-emerald-600 mt-0.5">Paid {fmtDate(inv.paidDate)}</p>
                          )}
                        </td>

                        {/* Status */}
                        <td className="px-3 py-3">
                          <span className={`inline-flex items-center gap-1 text-[10px] font-semibold uppercase px-1.5 py-0.5 rounded border ${STATUS_BADGE[inv.status as Status] ?? ''}`}>
                            <Icon className="h-2.5 w-2.5" />
                            {inv.status}
                          </span>
                        </td>

                        {/* Issued */}
                        <td className="px-3 py-3 text-xs text-slate-500 whitespace-nowrap">{fmtDate(inv.invoiceDate)}</td>

                        {/* Due */}
                        <td className="px-3 py-3 text-xs whitespace-nowrap">
                          <span className={inv.status === 'Overdue' ? 'text-rose-400 font-medium' : 'text-slate-500'}>
                            {fmtDate(inv.dueDate)}
                          </span>
                        </td>

                        {/* Payment */}
                        <td className="px-3 py-3 text-xs max-w-[120px]">
                          {inv.paymentMethod ? (
                            <div>
                              <p className="text-slate-400">{inv.paymentMethod}</p>
                              {inv.paymentReference && (
                                <p className="text-[10px] text-slate-600 font-mono truncate">{inv.paymentReference}</p>
                              )}
                            </div>
                          ) : (
                            <span className="text-slate-700">—</span>
                          )}
                        </td>

                        {/* Actions */}
                        <td className="px-3 py-3">
                          <div className="flex items-center gap-1 flex-wrap">
                            {inv.status === 'Draft' && (
                              <button type="button" onClick={() => quickStatus(inv, 'Sent')}
                                className="text-[10px] text-blue-400 border border-blue-500/20 hover:border-blue-500/50 px-2 py-1 rounded transition-colors whitespace-nowrap">
                                Mark Sent
                              </button>
                            )}
                            {(inv.status === 'Draft' || inv.status === 'Sent') && (
                              <button type="button" onClick={() => quickStatus(inv, 'Paid')}
                                className="text-[10px] text-emerald-400 border border-emerald-500/20 hover:border-emerald-500/50 px-2 py-1 rounded transition-colors whitespace-nowrap">
                                Mark Paid
                              </button>
                            )}
                            {inv.status === 'Sent' && (
                              <button type="button" onClick={() => quickStatus(inv, 'Overdue')}
                                className="text-[10px] text-rose-400 border border-rose-500/20 hover:border-rose-500/50 px-2 py-1 rounded transition-colors whitespace-nowrap">
                                Overdue
                              </button>
                            )}

                            <button type="button" onClick={() => downloadPdf(inv)} disabled={downloading === inv.id} title="Download PDF"
                              className="h-7 w-7 flex items-center justify-center text-slate-500 hover:text-white border border-white/[0.08] hover:border-white/20 rounded transition-colors disabled:opacity-40">
                              <Download className="h-3 w-3" />
                            </button>

                            {(inv.status === 'Draft' || inv.status === 'Sent' || inv.status === 'Overdue') && (
                              <button type="button" onClick={() => sendEmail(inv)} disabled={sending === inv.id} title="Email to billing contact"
                                className="h-7 w-7 flex items-center justify-center text-slate-500 hover:text-blue-400 border border-white/[0.08] hover:border-blue-500/30 rounded transition-colors disabled:opacity-40">
                                <Send className="h-3 w-3" />
                              </button>
                            )}

                            <button type="button" onClick={() => setEditing(inv)} title="Edit invoice"
                              className="h-7 w-7 flex items-center justify-center text-slate-500 hover:text-white border border-white/[0.08] hover:border-white/20 rounded transition-colors">
                              <Pencil className="h-3 w-3" />
                            </button>

                            {(inv.status === 'Draft' || inv.status === 'Cancelled') && (
                              <button type="button" onClick={() => setDeleting(inv)} title="Delete invoice"
                                className="h-7 w-7 flex items-center justify-center text-slate-500 hover:text-rose-400 border border-white/[0.08] hover:border-rose-500/30 rounded transition-colors">
                                <Trash2 className="h-3 w-3" />
                              </button>
                            )}
                          </div>
                        </td>
                      </tr>

                      {toasts[inv.id] && (
                        <tr key={`toast-${inv.id}`} className="border-b border-white/[0.04]">
                          <td colSpan={8} className="px-4 py-2">
                            <p className={`text-xs ${toasts[inv.id].ok ? 'text-emerald-400' : 'text-rose-400'}`}>{toasts[inv.id].text}</p>
                          </td>
                        </tr>
                      )}

                      {inv.notes && (
                        <tr key={`notes-${inv.id}`} className="border-b border-white/[0.04]">
                          <td colSpan={8} className="px-4 pb-2.5 pt-0">
                            <p className="text-[11px] text-slate-600 italic">Note: {inv.notes}</p>
                          </td>
                        </tr>
                      )}
                    </>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {showCreate && <CreateModal tenantId={id} onClose={() => setShowCreate(false)} onCreated={load} />}
      {editing    && <EditModal tenantId={id} invoice={editing} onClose={() => setEditing(null)} onSaved={load} />}
      {deleting   && <DeleteDialog invoice={deleting} onClose={() => setDeleting(null)} onDeleted={load} />}
    </div>
  );
}

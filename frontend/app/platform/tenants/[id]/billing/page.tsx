'use client';

import { useState, useEffect, useCallback } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, Plus, Download, Send, RefreshCw, X } from 'lucide-react';
import { platformApi, type TenantInvoice } from '@/src/api/platform';

const STATUS_CLS: Record<string, string> = {
  Draft:     'text-slate-400 bg-slate-700/50 border-slate-600',
  Sent:      'text-blue-400 bg-blue-900/30 border-blue-700/30',
  Paid:      'text-emerald-400 bg-emerald-900/30 border-emerald-700/30',
  Overdue:   'text-rose-400 bg-rose-900/30 border-rose-700/30',
  Cancelled: 'text-slate-600 bg-transparent border-slate-800',
};

function CreateInvoiceModal({ tenantId, onClose, onCreated }: { tenantId: string; onClose: () => void; onCreated: () => void }) {
  const today = new Date().toISOString().slice(0, 10);
  const due   = new Date(Date.now() + 30 * 86400000).toISOString().slice(0, 10);
  const ym    = new Date().toISOString().slice(0, 7).replace('-', '');

  const [form, setForm] = useState({
    invoiceNumber: `INV-${ym}-001`,
    amount: 0,
    currencyCode: 'USD',
    periodDescription: '',
    invoiceDate: today,
    dueDate: due,
    notes: '',
    status: 'Draft',
  });
  const [saving, setSaving] = useState(false);
  const [err, setErr] = useState('');

  function change(k: string, v: string | number) { setForm(f => ({ ...f, [k]: v })); }

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setSaving(true); setErr('');
    try {
      await platformApi.createInvoice(tenantId, form);
      onCreated(); onClose();
    } catch (ex: unknown) {
      setErr((ex as { response?: { data?: { message?: string } } })?.response?.data?.message ?? 'Failed.');
    } finally { setSaving(false); }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-black/70 backdrop-blur-sm" onClick={onClose} />
      <div className="relative w-full max-w-md bg-[#0d1117] border border-white/10 rounded-2xl shadow-2xl overflow-hidden">
        <div className="flex items-center justify-between px-6 py-4 border-b border-white/[0.07]">
          <h2 className="text-sm font-semibold text-white">Create Invoice</h2>
          <button type="button" onClick={onClose} className="text-slate-500 hover:text-white transition-colors">
            <X className="h-4 w-4" />
          </button>
        </div>
        <form onSubmit={submit} className="px-6 py-5 space-y-3 max-h-[80vh] overflow-y-auto">
          {err && <div className="text-xs text-rose-400 bg-rose-500/10 border border-rose-500/20 rounded-lg px-3 py-2">{err}</div>}
          {[
            { label: 'Invoice Number *', key: 'invoiceNumber', type: 'text' },
            { label: 'Amount *', key: 'amount', type: 'number' },
            { label: 'Currency', key: 'currencyCode', type: 'text' },
            { label: 'Period Description', key: 'periodDescription', type: 'text' },
            { label: 'Invoice Date *', key: 'invoiceDate', type: 'date' },
            { label: 'Due Date *', key: 'dueDate', type: 'date' },
          ].map(({ label, key, type }) => (
            <div key={key}>
              <label className="block text-xs text-slate-400 mb-1">{label}</label>
              <input type={type} value={(form as Record<string, string | number>)[key]}
                onChange={e => change(key, type === 'number' ? parseFloat(e.target.value) || 0 : e.target.value)}
                required={label.includes('*')}
                className="w-full bg-white/[0.04] border border-white/[0.08] rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-sapphire/60" />
            </div>
          ))}
          <div>
            <label className="block text-xs text-slate-400 mb-1">Notes</label>
            <textarea value={form.notes} onChange={e => change('notes', e.target.value)} rows={2} className="w-full bg-white/[0.04] border border-white/[0.08] rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-sapphire/60 resize-none" />
          </div>
          <div className="flex gap-3 pt-1">
            <button type="button" onClick={onClose} className="flex-1 border border-white/10 text-slate-400 rounded-lg py-2 text-sm transition-colors">Cancel</button>
            <button type="submit" disabled={saving} className="flex-1 bg-sapphire text-white rounded-lg py-2 text-sm font-semibold transition-colors disabled:opacity-40">
              {saving ? 'Creating…' : 'Create'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

export default function TenantBillingPage() {
  const { id } = useParams<{ id: string }>();
  const router  = useRouter();
  const [invoices, setInvoices] = useState<TenantInvoice[]>([]);
  const [loading, setLoading]   = useState(true);
  const [showCreate, setShowCreate] = useState(false);
  const [sending, setSending]   = useState<string | null>(null);
  const [downloading, setDownloading] = useState<string | null>(null);
  const [msgs, setMsgs]         = useState<Record<string, { text: string; ok: boolean }>>({});

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

  async function downloadPdf(inv: TenantInvoice) {
    setDownloading(inv.id);
    try { await platformApi.downloadInvoicePdf(id, inv.id, inv.invoiceNumber); }
    catch { setMsgs(m => ({ ...m, [inv.id]: { text: 'Download failed.', ok: false } })); }
    finally { setDownloading(null); }
  }

  async function sendEmail(inv: TenantInvoice) {
    setSending(inv.id);
    try {
      const r = await platformApi.sendInvoiceEmail(id, inv.id);
      if (r.smtpRequired) {
        setMsgs(m => ({ ...m, [inv.id]: { text: 'SMTP not configured — download PDF and send manually.', ok: false } }));
      } else {
        setMsgs(m => ({ ...m, [inv.id]: { text: `Sent to ${r.billingEmail} with PDF.`, ok: true } }));
        await load();
      }
    } catch { setMsgs(m => ({ ...m, [inv.id]: { text: 'Send failed.', ok: false } })); }
    finally { setSending(null); }
  }

  return (
    <div className="space-y-5">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <Link href={`/platform/tenants/${id}`} className="h-8 w-8 flex items-center justify-center text-slate-500 hover:text-white border border-white/10 rounded-lg transition-colors shrink-0">
            <ArrowLeft className="h-4 w-4" />
          </Link>
          <h1 className="text-lg font-bold text-white">Billing & Invoices</h1>
        </div>
        <div className="flex items-center gap-2">
          <button type="button" onClick={load} disabled={loading}
            className="h-8 w-8 flex items-center justify-center text-slate-500 hover:text-white border border-white/10 rounded-lg transition-colors disabled:opacity-40">
            <RefreshCw className={`h-3.5 w-3.5 ${loading ? 'animate-spin' : ''}`} />
          </button>
          <button type="button" onClick={() => setShowCreate(true)}
            className="flex items-center gap-1.5 bg-sapphire hover:bg-blue-500 text-white px-3 py-1.5 rounded-lg text-sm font-semibold transition-colors">
            <Plus className="h-3.5 w-3.5" />
            New Invoice
          </button>
        </div>
      </div>

      <div className="bg-[#161b22] border border-white/[0.07] rounded-xl overflow-hidden">
        {loading ? (
          <div className="flex items-center justify-center py-10">
            <div className="h-4 w-4 animate-spin rounded-full border-2 border-sapphire border-t-transparent" />
          </div>
        ) : invoices.length === 0 ? (
          <div className="text-center py-10">
            <p className="text-sm text-slate-600 mb-2">No invoices yet.</p>
            <button type="button" onClick={() => setShowCreate(true)} className="text-xs text-sapphire hover:text-blue-300">+ Create Invoice</button>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[700px]">
              <thead>
                <tr className="border-b border-white/[0.06]">
                  {['Invoice', 'Amount', 'Status', 'Date', 'Due', ''].map(h => (
                    <th key={h} className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-widest">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {invoices.map(inv => (
                  <>
                    <tr key={inv.id} className="border-b border-white/[0.04] hover:bg-white/[0.02] transition-colors">
                      <td className="px-4 py-3">
                        <p className="text-sm text-white font-mono">{inv.invoiceNumber}</p>
                        {inv.periodDescription && <p className="text-[11px] text-slate-600">{inv.periodDescription}</p>}
                      </td>
                      <td className="px-3 py-3 text-sm text-slate-300 tabular-nums">
                        {inv.currencyCode} {inv.amount.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                      </td>
                      <td className="px-3 py-3">
                        <span className={`text-[10px] font-semibold uppercase px-1.5 py-0.5 rounded border ${STATUS_CLS[inv.status] ?? ''}`}>
                          {inv.status}
                        </span>
                      </td>
                      <td className="px-3 py-3 text-xs text-slate-500">
                        {new Date(inv.invoiceDate).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })}
                      </td>
                      <td className="px-3 py-3 text-xs text-slate-500">
                        {new Date(inv.dueDate).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })}
                      </td>
                      <td className="px-3 py-3">
                        <div className="flex items-center gap-1 justify-end">
                          <button type="button"
                            onClick={() => downloadPdf(inv)}
                            disabled={downloading === inv.id}
                            title="Download PDF"
                            className="flex items-center gap-1 text-[11px] text-slate-400 hover:text-white border border-white/10 hover:border-white/20 px-2 py-1 rounded transition-colors disabled:opacity-40">
                            <Download className="h-3 w-3" />
                            PDF
                          </button>
                          {(inv.status === 'Draft' || inv.status === 'Overdue') && (
                            <button type="button"
                              onClick={() => sendEmail(inv)}
                              disabled={sending === inv.id}
                              title="Send via email"
                              className="flex items-center gap-1 text-[11px] text-blue-400 border border-blue-500/20 hover:border-blue-500/40 px-2 py-1 rounded transition-colors disabled:opacity-40">
                              <Send className="h-3 w-3" />
                              {sending === inv.id ? '…' : 'Send'}
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                    {msgs[inv.id] && (
                      <tr key={`msg-${inv.id}`} className="border-b border-white/[0.04]">
                        <td colSpan={6} className="px-4 py-2">
                          <p className={`text-xs ${msgs[inv.id].ok ? 'text-emerald-400' : 'text-rose-400'}`}>
                            {msgs[inv.id].text}
                          </p>
                        </td>
                      </tr>
                    )}
                  </>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {showCreate && <CreateInvoiceModal tenantId={id} onClose={() => setShowCreate(false)} onCreated={load} />}
    </div>
  );
}

'use client';

import { useCallback, useEffect, useState } from 'react';
import { useAutoTranslate } from '../hooks/useAutoTranslate';
import {
  CreditCard, DollarSign, Gift, Plus, CheckCircle, XCircle,
  FileText, TrendingUp, AlertTriangle, BookOpen, ShieldCheck,
} from 'lucide-react';
import {
  loanTypesApi, loansApi, advancePolicyApi, advancesApi,
  bonusTypesApi, bonusBatchesApi,
} from '../api/loans';
import type {
  LoanType, EmployeeLoan, LoanApproval, LoanInstallment,
  AdvancePolicy, SalaryAdvance, BonusType, BonusBatch, EmployeeBonus,
  FinanceGlEntry, AuditLogEntry,
} from '../api/loans';
import { Modal } from '../components/Modal';
import { useTenantSettings } from '../contexts/TenantSettingsContext';
import { EmployeeSearchSelect } from '../components/EmployeeSearchSelect';
import type { EmployeeSelection } from '../components/EmployeeSearchSelect';

type Tab = 'loans' | 'loanTypes' | 'advances' | 'advancePolicy' | 'bonusTypes' | 'bonusBatches' | 'auditReport';

const tabs: { id: Tab; label: string; icon: React.ElementType }[] = [
  { id: 'loans', label: 'Loans', icon: CreditCard },
  { id: 'loanTypes', label: 'Loan Types', icon: DollarSign },
  { id: 'advances', label: 'Salary Advances', icon: DollarSign },
  { id: 'advancePolicy', label: 'Advance Policy', icon: DollarSign },
  { id: 'bonusTypes', label: 'Bonus Types', icon: Gift },
  { id: 'bonusBatches', label: 'Bonus Batches', icon: Gift },
  { id: 'auditReport', label: 'Audit Report', icon: ShieldCheck },
];

// ── Shared helpers ────────────────────────────────────────────────────────────

function StatusBadge({ status }: { status: string }) {
  const colors: Record<string, string> = {
    Active: 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400',
    Approved: 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400',
    Paid: 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400',
    PaidInPayroll: 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400',
    Pending: 'bg-amber-500/10 text-amber-600 dark:text-amber-400',
    PendingApproval: 'bg-amber-500/10 text-amber-600 dark:text-amber-400',
    Draft: 'bg-slate-100 text-slate-500 dark:bg-white/10 dark:text-slate-400',
    Rejected: 'bg-red-500/10 text-red-500 dark:text-red-400',
    Settled: 'bg-blue-500/10 text-blue-600 dark:text-blue-400',
    Cancelled: 'bg-slate-100 text-slate-500 dark:bg-white/10 dark:text-slate-400',
    Closed: 'bg-slate-100 text-slate-500 dark:bg-white/10 dark:text-slate-400',
    Overdue: 'bg-red-500/10 text-red-500 dark:text-red-400',
  };
  return (
    <span className={`rounded-full px-2 py-0.5 text-xs font-semibold ${colors[status] ?? 'bg-slate-100 text-slate-500'}`}>
      {status}
    </span>
  );
}

function FormField({ label, required, children }: { label: string; required?: boolean; children: React.ReactNode }) {
  return (
    <div>
      <label className="mb-1.5 block text-sm font-medium text-slate-700 dark:text-slate-300">
        {label}{required && <span className="ml-0.5 text-red-500">*</span>}
      </label>
      {children}
    </div>
  );
}

function FormError({ error }: { error: string }) {
  if (!error) return null;
  return <p className="mb-3 rounded-lg bg-red-50 px-3 py-2.5 text-sm text-red-600 dark:bg-red-500/10 dark:text-red-400">{error}</p>;
}

function SummaryCard({ label, value, sub, icon: Icon, color }: { label: string; value: string | number; sub?: string; icon: React.ElementType; color: string }) {
  return (
    <div className="surface p-4">
      <div className="flex items-start justify-between">
        <div>
          <p className="text-xs font-medium uppercase tracking-wide text-slate-500 dark:text-slate-400">{label}</p>
          <p className={`mt-1 text-2xl font-extrabold ${color}`}>{value}</p>
          {sub && <p className="mt-0.5 text-xs text-slate-400">{sub}</p>}
        </div>
        <span className={`rounded-lg p-2.5 ${color.replace('text-', 'bg-').replace('-600', '-100').replace('-400', '-900/20')}`}>
          <Icon className={`h-5 w-5 ${color}`} />
        </span>
      </div>
    </div>
  );
}

function GlEntriesTable({ entries, fmt }: { entries: FinanceGlEntry[]; fmt: (n: number) => string }) {
  if (entries.length === 0) return <p className="py-4 text-center text-xs text-slate-400">No GL entries recorded yet</p>;
  return (
    <div className="surface overflow-hidden">
      <table className="w-full text-xs">
        <thead>
          <tr className="border-b border-slate-100 dark:border-white/[0.07]">
            {['Date', 'Event', 'Debit Account', 'Credit Account', 'Amount', 'Posted By'].map((h) => (
              <th key={h} className="px-3 py-2 text-left font-bold uppercase tracking-wide text-slate-400">{h}</th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100 dark:divide-white/[0.05]">
          {entries.map((e) => (
            <tr key={e.id} className="hover:bg-slate-50 dark:hover:bg-white/[0.03]">
              <td className="px-3 py-2 font-mono text-slate-500">{e.entryDate}</td>
              <td className="px-3 py-2 text-slate-700 dark:text-slate-300">{e.eventType}</td>
              <td className="px-3 py-2 text-slate-600 dark:text-slate-400">{e.debitAccount}</td>
              <td className="px-3 py-2 text-slate-600 dark:text-slate-400">{e.creditAccount}</td>
              <td className="px-3 py-2 font-semibold text-slate-900 dark:text-white">{fmt(e.amount)}</td>
              <td className="px-3 py-2 text-slate-500">{e.postedByName}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function AuditTrailTable({ logs }: { logs: AuditLogEntry[] }) {
  if (logs.length === 0) return <p className="py-4 text-center text-xs text-slate-400">No audit events recorded</p>;
  return (
    <div className="surface overflow-hidden max-h-48 overflow-y-auto">
      <table className="w-full text-xs">
        <thead>
          <tr className="border-b border-slate-100 dark:border-white/[0.07]">
            {['Timestamp', 'Action', 'Performed By', 'Details'].map((h) => (
              <th key={h} className="px-3 py-2 text-left font-bold uppercase tracking-wide text-slate-400">{h}</th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100 dark:divide-white/[0.05]">
          {logs.map((l) => (
            <tr key={l.id} className="hover:bg-slate-50 dark:hover:bg-white/[0.03]">
              <td className="px-3 py-2 font-mono text-slate-500">{new Date(l.createdAtUtc).toLocaleString()}</td>
              <td className="px-3 py-2 font-semibold text-slate-700 dark:text-slate-300">{l.action}</td>
              <td className="px-3 py-2 text-slate-500">{l.performedByName}</td>
              <td className="px-3 py-2 text-slate-500 truncate max-w-xs">{l.newValuesJson}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// ── Loan Types ────────────────────────────────────────────────────────────────

function LoanTypesTab() {
  const { currencyCode } = useTenantSettings();
  const [items, setItems] = useState<LoanType[]>([]);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [form, setForm] = useState({ code: '', nameEn: '', nameAr: '', maxAmount: 0, maxInstallments: 12, repaymentFrequency: 'Monthly', isInterestFree: true, interestRate: 0, minServiceMonths: 6, requiresApproval: true });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const fmt = (n: number) => n.toLocaleString('en-US', { style: 'currency', currency: currencyCode, minimumFractionDigits: 0, maximumFractionDigits: 0 });

  const load = useCallback(async () => {
    setLoading(true);
    try { setItems(await loanTypesApi.list()); } catch { /**/ }
    finally { setLoading(false); }
  }, []);
  useEffect(() => { load(); }, [load]);

  const save = async () => {
    if (!form.code.trim() || !form.nameEn.trim()) { setError('Code and name are required'); return; }
    setSaving(true); setError('');
    try { await loanTypesApi.create(form); setModalOpen(false); load(); }
    catch { setError('Failed to save.'); }
    finally { setSaving(false); }
  };
  const f = (key: string, v: string | boolean | number) => setForm(x => ({ ...x, [key]: v }));
  const { translation: autoLoanNameAr, isTranslating: translatingLoanNameAr } = useAutoTranslate(form.nameEn);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => { if (autoLoanNameAr && !form.nameAr) f('nameAr', autoLoanNameAr); }, [autoLoanNameAr]);

  return (
    <>
      <div className="space-y-4">
        <div className="flex justify-end">
          <button type="button" onClick={() => { setForm({ code: '', nameEn: '', nameAr: '', maxAmount: 0, maxInstallments: 12, repaymentFrequency: 'Monthly', isInterestFree: true, interestRate: 0, minServiceMonths: 6, requiresApproval: true }); setError(''); setModalOpen(true); }} className="btn-primary">
            <Plus className="h-4 w-4" /> New Loan Type
          </button>
        </div>
        <div className="surface overflow-hidden">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-100 dark:border-white/[0.07]">
                {['Code', 'Name', `Max Amount (${currencyCode})`, 'Max Installments', 'Frequency', 'Interest', 'Min Service', 'Approval', 'Status'].map((h) => (
                  <th key={h} className="px-4 py-3 text-left text-xs font-bold uppercase tracking-wide text-slate-400">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 dark:divide-white/[0.05]">
              {loading ? (
                <tr><td colSpan={9} className="py-12 text-center"><div className="mx-auto h-6 w-6 animate-spin rounded-full border-2 border-sapphire border-t-transparent" /></td></tr>
              ) : items.length === 0 ? (
                <tr><td colSpan={9} className="py-12 text-center text-slate-400">No loan types yet</td></tr>
              ) : items.map((t) => (
                <tr key={t.id} className="hover:bg-slate-50 dark:hover:bg-white/[0.03]">
                  <td className="px-4 py-3 font-mono text-xs text-slate-500">{t.code}</td>
                  <td className="px-4 py-3 font-medium text-slate-900 dark:text-white">{t.nameEn}</td>
                  <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{fmt(t.maxAmount)}</td>
                  <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{t.maxInstallments}</td>
                  <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{t.repaymentFrequency}</td>
                  <td className="px-4 py-3">{t.isInterestFree ? <span className="text-xs text-emerald-600 font-semibold">Interest-Free</span> : <span className="text-xs text-slate-500">{t.interestRate}%</span>}</td>
                  <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{t.minServiceMonths}m</td>
                  <td className="px-4 py-3">{t.requiresApproval ? '✓' : '—'}</td>
                  <td className="px-4 py-3"><StatusBadge status={t.isActive ? 'Active' : 'Closed'} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <Modal isOpen={modalOpen} title="New Loan Type" onClose={() => setModalOpen(false)} size="lg"
        footer={<><button type="button" onClick={() => setModalOpen(false)} className="btn-secondary">Cancel</button><button type="button" onClick={save} disabled={saving} className="btn-primary disabled:opacity-60">{saving ? 'Saving…' : 'Save'}</button></>}>
        <FormError error={error} />
        <div className="grid grid-cols-2 gap-3">
          <FormField label="Code" required><input value={form.code} onChange={(e) => f('code', e.target.value)} className="input w-full" placeholder="PERSONAL_LOAN" /></FormField>
          <FormField label="Name (EN)" required><input value={form.nameEn} onChange={(e) => f('nameEn', e.target.value)} className="input w-full" title="Name (EN)" /></FormField>
          <FormField label="Name (AR)"><input value={form.nameAr} onChange={(e) => f('nameAr', e.target.value)} className="input w-full" dir="rtl" title="Name (AR)" placeholder={translatingLoanNameAr && !form.nameAr ? 'Translating…' : undefined} /></FormField>
          <FormField label={`Max Amount (${currencyCode})`} required><input type="number" value={form.maxAmount} onChange={(e) => f('maxAmount', Number(e.target.value))} className="input w-full" title="Max Amount" /></FormField>
          <FormField label="Max Installments"><input type="number" value={form.maxInstallments} onChange={(e) => f('maxInstallments', Number(e.target.value))} className="input w-full" title="Max Installments" /></FormField>
          <FormField label="Repayment Frequency">
            <select value={form.repaymentFrequency} onChange={(e) => f('repaymentFrequency', e.target.value)} className="select w-full" title="Repayment Frequency">
              {['Monthly', 'BiMonthly', 'Weekly'].map((v) => <option key={v}>{v}</option>)}
            </select>
          </FormField>
          <FormField label="Min Service (months)"><input type="number" value={form.minServiceMonths} onChange={(e) => f('minServiceMonths', Number(e.target.value))} className="input w-full" title="Min Service (months)" /></FormField>
          <div className="col-span-2 flex gap-6">
            <label className="flex items-center gap-2 text-sm text-slate-700 dark:text-slate-300"><input type="checkbox" checked={form.isInterestFree} onChange={(e) => f('isInterestFree', e.target.checked)} className="h-4 w-4 accent-sapphire" title="Interest Free" /> Interest-Free</label>
            <label className="flex items-center gap-2 text-sm text-slate-700 dark:text-slate-300"><input type="checkbox" checked={form.requiresApproval} onChange={(e) => f('requiresApproval', e.target.checked)} className="h-4 w-4 accent-sapphire" title="Requires Approval" /> Requires Approval</label>
          </div>
          {!form.isInterestFree && (
            <FormField label="Interest Rate (%)"><input type="number" step="0.1" value={form.interestRate} onChange={(e) => f('interestRate', Number(e.target.value))} className="input w-full" title="Interest Rate (%)" /></FormField>
          )}
        </div>
      </Modal>
    </>
  );
}

// ── Loans ─────────────────────────────────────────────────────────────────────

function LoansTab({ loanTypes }: { loanTypes: LoanType[] }) {
  const { currencyCode } = useTenantSettings();
  const fmt = (n: number) => n.toLocaleString('en-US', { style: 'currency', currency: currencyCode });
  const [items, setItems] = useState<EmployeeLoan[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [filterStatus, setFilterStatus] = useState('');
  const [page, setPage] = useState(1);
  const pageSize = 20;

  const [createModal, setCreateModal] = useState(false);
  const [detailModal, setDetailModal] = useState(false);
  const [settleModal, setSettleModal] = useState(false);
  const [selected, setSelected] = useState<{ loan: EmployeeLoan; installments: LoanInstallment[]; approvals: LoanApproval[]; auditLogs: AuditLogEntry[]; glEntries: FinanceGlEntry[] } | null>(null);
  const [decideModal, setDecideModal] = useState(false);
  const [decidingApproval, setDecidingApproval] = useState<LoanApproval | null>(null);
  const [createForm, setCreateForm] = useState({ loanTypeId: '', requestedAmount: 0, requestedInstallments: 12, notes: '' });
  const [selectedEmployee, setSelectedEmployee] = useState<EmployeeSelection | null>(null);
  const [decideForm, setDecideForm] = useState({ decision: 'Approved', comments: '', approvedAmount: 0, approvedInstallments: 0, repaymentStartDate: '' });
  const [settleForm, setSettleForm] = useState({ settlementType: 'Early', settlementAmount: 0, settlementDate: '', notes: '' });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [detailTab, setDetailTab] = useState<'schedule' | 'gl' | 'audit'>('schedule');

  const load = useCallback(async () => {
    setLoading(true);
    try { const r = await loansApi.list({ status: filterStatus || undefined, page, pageSize }); setItems(r.items); setTotal(r.total); } catch { /**/ }
    finally { setLoading(false); }
  }, [filterStatus, page]);
  useEffect(() => { load(); }, [load]);

  const openDetail = async (loan: EmployeeLoan) => {
    try { const d = await loansApi.get(loan.id); setSelected(d); setDetailTab('schedule'); setDetailModal(true); } catch { /**/ }
  };

  const createLoan = async () => {
    if (!selectedEmployee || !createForm.loanTypeId) { setError('Select an employee and loan type'); return; }
    setSaving(true); setError('');
    try {
      await loansApi.create({
        ...createForm,
        employeeId: '00000000-0000-0000-0000-000000000000', // server auto-generates
        employeeName: selectedEmployee.fullName,
        employeeIntId: selectedEmployee.intId,
      });
      setCreateModal(false); setSelectedEmployee(null); load();
    }
    catch (e: any) { setError(e?.response?.data || 'Failed to create loan.'); }
    finally { setSaving(false); }
  };

  const openDecide = (approval: LoanApproval, loan: EmployeeLoan) => {
    setDecidingApproval(approval);
    setDecideForm({ decision: 'Approved', comments: '', approvedAmount: loan.requestedAmount, approvedInstallments: loan.requestedInstallments, repaymentStartDate: '' });
    setError(''); setDecideModal(true);
  };

  const decide = async () => {
    if (!selected || !decidingApproval) return;
    setSaving(true); setError('');
    try {
      await loansApi.decide(selected.loan.id, decidingApproval.id, decideForm);
      setDecideModal(false);
      const d = await loansApi.get(selected.loan.id); setSelected(d);
      load();
    } catch (e: any) { setError(e?.response?.data || 'Failed to submit decision.'); }
    finally { setSaving(false); }
  };

  const settle = async () => {
    if (!selected) return;
    setSaving(true); setError('');
    try {
      await loansApi.settle(selected.loan.id, settleForm);
      setSettleModal(false);
      const d = await loansApi.get(selected.loan.id); setSelected(d);
      load();
    } catch (e: any) { setError(e?.response?.data || 'Failed to settle loan.'); }
    finally { setSaving(false); }
  };

  const pendingApprovals = selected?.approvals.filter((a) => a.status === 'Pending') ?? [];

  return (
    <>
      <div className="space-y-4">
        <div className="flex items-center justify-between">
          <select value={filterStatus} onChange={(e) => { setFilterStatus(e.target.value); setPage(1); }} className="select" title="Filter by status">
            <option value="">All Statuses</option>
            {['PendingApproval', 'Approved', 'Active', 'Settled', 'Rejected', 'Closed'].map((s) => <option key={s}>{s}</option>)}
          </select>
          <button type="button" onClick={() => { setCreateForm({ loanTypeId: loanTypes[0]?.id ?? '', requestedAmount: 0, requestedInstallments: 12, notes: '' }); setSelectedEmployee(null); setError(''); setCreateModal(true); }} className="btn-primary">
            <Plus className="h-4 w-4" /> New Loan Request
          </button>
        </div>
        <div className="surface overflow-hidden">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-100 dark:border-white/[0.07]">
                {['Loan #', 'Employee', 'Type', 'Requested', 'Approved', 'Outstanding', 'Installment', 'Status', ''].map((h) => (
                  <th key={h} className="px-4 py-3 text-left text-xs font-bold uppercase tracking-wide text-slate-400">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 dark:divide-white/[0.05]">
              {loading ? (
                <tr><td colSpan={9} className="py-12 text-center"><div className="mx-auto h-6 w-6 animate-spin rounded-full border-2 border-sapphire border-t-transparent" /></td></tr>
              ) : items.length === 0 ? (
                <tr><td colSpan={9} className="py-12 text-center text-slate-400">No loans found</td></tr>
              ) : items.map((l) => (
                <tr key={l.id} className="hover:bg-slate-50 dark:hover:bg-white/[0.03] cursor-pointer" onClick={() => openDetail(l)}>
                  <td className="px-4 py-3 font-mono text-xs text-slate-500">{l.loanNumber}</td>
                  <td className="px-4 py-3 font-medium text-slate-900 dark:text-white">{l.employeeName}</td>
                  <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{l.loanTypeName}</td>
                  <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{fmt(l.requestedAmount)}</td>
                  <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{l.approvedAmount > 0 ? fmt(l.approvedAmount) : '—'}</td>
                  <td className="px-4 py-3 font-semibold text-slate-900 dark:text-white">{fmt(l.outstandingBalance)}</td>
                  <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{l.installmentAmount > 0 ? fmt(l.installmentAmount) : '—'}</td>
                  <td className="px-4 py-3"><StatusBadge status={l.status} /></td>
                  <td className="px-4 py-3 text-sapphire text-xs">View →</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {total > pageSize && (
          <div className="flex items-center justify-between text-sm text-slate-500">
            <span>{total} total</span>
            <div className="flex gap-2">
              <button type="button" onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page === 1} className="btn-secondary h-7 px-2 text-xs disabled:opacity-40">← Prev</button>
              <span className="px-2">Page {page} of {Math.ceil(total / pageSize)}</span>
              <button type="button" onClick={() => setPage((p) => p + 1)} disabled={page >= Math.ceil(total / pageSize)} className="btn-secondary h-7 px-2 text-xs disabled:opacity-40">Next →</button>
            </div>
          </div>
        )}
      </div>

      {/* Create Loan Modal */}
      <Modal isOpen={createModal} title="New Loan Request" onClose={() => setCreateModal(false)} size="lg"
        footer={<><button type="button" onClick={() => setCreateModal(false)} className="btn-secondary">Cancel</button><button type="button" onClick={createLoan} disabled={saving} className="btn-primary disabled:opacity-60">{saving ? 'Submitting…' : 'Submit Request'}</button></>}>
        <FormError error={error} />
        <div className="space-y-3">
          <FormField label="Employee" required>
            <EmployeeSearchSelect value={selectedEmployee} onChange={setSelectedEmployee} required />
          </FormField>
          <div className="grid grid-cols-2 gap-3">
            <FormField label="Loan Type" required>
              <select value={createForm.loanTypeId} onChange={(e) => setCreateForm(x => ({ ...x, loanTypeId: e.target.value }))} className="select w-full" title="Loan Type">
                <option value="">Select type</option>
                {loanTypes.map((t) => <option key={t.id} value={t.id}>{t.nameEn} {t.isInterestFree ? '(Interest-Free)' : `(${t.interestRate}%)`}</option>)}
              </select>
            </FormField>
            <FormField label={`Amount (${currencyCode})`} required>
              <input type="number" value={createForm.requestedAmount} onChange={(e) => setCreateForm(x => ({ ...x, requestedAmount: Number(e.target.value) }))} className="input w-full" title="Requested Amount" min="1" />
            </FormField>
            <FormField label="Installments">
              <input type="number" value={createForm.requestedInstallments} onChange={(e) => setCreateForm(x => ({ ...x, requestedInstallments: Number(e.target.value) }))} className="input w-full" title="Requested Installments" min="1" />
            </FormField>
            <FormField label="Notes">
              <textarea value={createForm.notes} onChange={(e) => setCreateForm(x => ({ ...x, notes: e.target.value }))} className="input w-full" rows={2} title="Notes" />
            </FormField>
          </div>
        </div>
      </Modal>

      {/* Detail Modal */}
      <Modal isOpen={detailModal && !!selected} title={`Loan — ${selected?.loan.loanNumber}`} onClose={() => setDetailModal(false)} size="lg"
        footer={
          <div className="flex items-center gap-2">
            {selected?.loan.status === 'Active' && (
              <button type="button" onClick={() => { setSettleForm({ settlementType: 'Early', settlementAmount: selected.loan.outstandingBalance, settlementDate: new Date().toISOString().split('T')[0], notes: '' }); setError(''); setSettleModal(true); }} className="btn-primary h-8 px-3 text-sm">Settle Loan</button>
            )}
            <button type="button" onClick={() => setDetailModal(false)} className="btn-secondary ml-auto">Close</button>
          </div>
        }>
        {selected && (
          <div className="space-y-4">
            <div className="grid grid-cols-3 gap-3 text-sm">
              <div className="surface p-3 rounded-lg"><p className="text-xs text-slate-400 mb-1">Employee</p><p className="font-semibold text-slate-900 dark:text-white">{selected.loan.employeeName}</p></div>
              <div className="surface p-3 rounded-lg"><p className="text-xs text-slate-400 mb-1">Outstanding</p><p className="font-semibold text-slate-900 dark:text-white">{fmt(selected.loan.outstandingBalance)}</p></div>
              <div className="surface p-3 rounded-lg"><p className="text-xs text-slate-400 mb-1">Status</p><StatusBadge status={selected.loan.status} /></div>
            </div>

            {pendingApprovals.length > 0 && (
              <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 dark:border-amber-800 dark:bg-amber-900/20">
                <p className="mb-2 text-sm font-semibold text-amber-700 dark:text-amber-400">Pending Approval</p>
                {pendingApprovals.map((a) => (
                  <div key={a.id} className="flex items-center justify-between">
                    <span className="text-sm text-amber-700 dark:text-amber-300">Step {a.stepOrder} — {a.approverRole}</span>
                    <button type="button" onClick={() => openDecide(a, selected.loan)} className="btn-primary h-7 px-2 text-xs">Decide</button>
                  </div>
                ))}
              </div>
            )}

            {/* Sub-tabs */}
            <div className="flex gap-3 border-b border-slate-100 dark:border-white/[0.07]">
              {(['schedule', 'gl', 'audit'] as const).map((t) => (
                <button key={t} type="button" onClick={() => setDetailTab(t)}
                  className={`pb-2 text-xs font-semibold uppercase tracking-wide transition border-b-2 ${detailTab === t ? 'border-sapphire text-sapphire' : 'border-transparent text-slate-400 hover:text-slate-600'}`}>
                  {t === 'schedule' ? 'Installments' : t === 'gl' ? 'GL Entries' : 'Audit Trail'}
                </button>
              ))}
            </div>

            {detailTab === 'schedule' && selected.installments.length > 0 && (
              <div className="max-h-48 overflow-y-auto surface">
                <table className="w-full text-xs">
                  <thead><tr className="border-b border-slate-100 dark:border-white/10">{['#', 'Due Date', 'Amount Due', 'Paid', 'Status'].map((h) => <th key={h} className="px-3 py-2 text-left font-bold uppercase tracking-wide text-slate-400">{h}</th>)}</tr></thead>
                  <tbody className="divide-y divide-slate-100 dark:divide-white/[0.05]">
                    {selected.installments.map((ins) => (
                      <tr key={ins.id} className="hover:bg-slate-50 dark:hover:bg-white/[0.03]">
                        <td className="px-3 py-2 text-slate-500">{ins.installmentNumber}</td>
                        <td className="px-3 py-2 text-slate-600 dark:text-slate-300">{ins.dueDate}</td>
                        <td className="px-3 py-2 text-slate-600 dark:text-slate-300">{fmt(ins.amountDue)}</td>
                        <td className="px-3 py-2 text-slate-600 dark:text-slate-300">{fmt(ins.amountPaid)}</td>
                        <td className="px-3 py-2"><StatusBadge status={ins.status} /></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
            {detailTab === 'schedule' && selected.installments.length === 0 && (
              <p className="text-center text-sm text-slate-400 py-4">No installments scheduled yet</p>
            )}
            {detailTab === 'gl' && <GlEntriesTable entries={selected.glEntries ?? []} fmt={fmt} />}
            {detailTab === 'audit' && <AuditTrailTable logs={selected.auditLogs ?? []} />}
          </div>
        )}
      </Modal>

      {/* Settle Modal */}
      <Modal isOpen={settleModal} title={`Settle Loan — ${selected?.loan.loanNumber}`} onClose={() => setSettleModal(false)}
        footer={<><button type="button" onClick={() => setSettleModal(false)} className="btn-secondary">Cancel</button><button type="button" onClick={settle} disabled={saving} className="btn-primary disabled:opacity-60">{saving ? 'Processing…' : 'Confirm Settlement'}</button></>}>
        <FormError error={error} />
        <div className="space-y-3">
          <FormField label="Settlement Type">
            <select value={settleForm.settlementType} onChange={(e) => setSettleForm(x => ({ ...x, settlementType: e.target.value }))} className="select w-full" title="Settlement Type">
              {['Early', 'Normal', 'Waiver'].map((v) => <option key={v}>{v}</option>)}
            </select>
          </FormField>
          <FormField label={`Settlement Amount (${currencyCode})`} required>
            <input type="number" value={settleForm.settlementAmount} onChange={(e) => setSettleForm(x => ({ ...x, settlementAmount: Number(e.target.value) }))} className="input w-full" title="Settlement Amount" />
          </FormField>
          <FormField label="Settlement Date" required>
            <input type="date" value={settleForm.settlementDate} onChange={(e) => setSettleForm(x => ({ ...x, settlementDate: e.target.value }))} className="input w-full" title="Settlement Date" />
          </FormField>
          <FormField label="Notes"><textarea value={settleForm.notes} onChange={(e) => setSettleForm(x => ({ ...x, notes: e.target.value }))} className="input w-full" rows={2} title="Notes" /></FormField>
        </div>
      </Modal>

      {/* Decide Modal */}
      <Modal isOpen={decideModal} title="Loan Approval Decision" onClose={() => setDecideModal(false)}
        footer={<><button type="button" onClick={() => setDecideModal(false)} className="btn-secondary">Cancel</button><button type="button" onClick={decide} disabled={saving} className="btn-primary disabled:opacity-60">{saving ? 'Submitting…' : 'Submit Decision'}</button></>}>
        <FormError error={error} />
        <div className="space-y-3">
          <FormField label="Decision" required>
            <div className="flex gap-3">
              {['Approved', 'Rejected'].map((d) => (
                <button key={d} type="button" onClick={() => setDecideForm(x => ({ ...x, decision: d }))}
                  className={`flex-1 flex items-center justify-center gap-2 rounded-lg border px-3 py-2 text-sm font-semibold transition ${decideForm.decision === d ? (d === 'Approved' ? 'border-emerald-500 bg-emerald-50 text-emerald-700 dark:bg-emerald-900/20 dark:text-emerald-400' : 'border-red-400 bg-red-50 text-red-600 dark:bg-red-900/20 dark:text-red-400') : 'border-slate-200 text-slate-500 dark:border-white/10'}`}>
                  {d === 'Approved' ? <CheckCircle className="h-4 w-4" /> : <XCircle className="h-4 w-4" />} {d}
                </button>
              ))}
            </div>
          </FormField>
          {decideForm.decision === 'Approved' && (
            <>
              <FormField label={`Approved Amount (${currencyCode})`}><input type="number" value={decideForm.approvedAmount} onChange={(e) => setDecideForm(x => ({ ...x, approvedAmount: Number(e.target.value) }))} className="input w-full" title="Approved Amount" /></FormField>
              <FormField label="Approved Installments"><input type="number" value={decideForm.approvedInstallments} onChange={(e) => setDecideForm(x => ({ ...x, approvedInstallments: Number(e.target.value) }))} className="input w-full" title="Approved Installments" /></FormField>
              <FormField label="Repayment Start Date"><input type="date" value={decideForm.repaymentStartDate} onChange={(e) => setDecideForm(x => ({ ...x, repaymentStartDate: e.target.value }))} className="input w-full" title="Repayment Start Date" /></FormField>
            </>
          )}
          <FormField label="Comments"><textarea value={decideForm.comments} onChange={(e) => setDecideForm(x => ({ ...x, comments: e.target.value }))} className="input w-full" rows={3} placeholder="Optional comments…" /></FormField>
        </div>
      </Modal>
    </>
  );
}

// ── Advance Policy ────────────────────────────────────────────────────────────

function AdvancePolicyTab() {
  const [policy, setPolicy] = useState<AdvancePolicy | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState(false);
  const [form, setForm] = useState<Partial<AdvancePolicy>>({
    policyName: 'Default Advance Policy', maxPercentageOfSalary: 50, maxAdvancesPerYear: 2,
    minServiceMonths: 6, allowInstallments: true, maxInstallments: 3, cooldownMonths: 3, requiresApproval: true, isActive: true,
  });

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const p = await advancePolicyApi.get();
      if (p) { setPolicy(p); setForm({ ...p }); }
    } catch { /**/ }
    finally { setLoading(false); }
  }, []);
  useEffect(() => { load(); }, [load]);

  const save = async () => {
    setSaving(true); setError(''); setSuccess(false);
    try { const p = await advancePolicyApi.upsert(form); setPolicy(p); setForm({ ...p }); setSuccess(true); }
    catch { setError('Failed to save policy.'); }
    finally { setSaving(false); }
  };
  const f = (key: string, v: string | boolean | number) => setForm(x => ({ ...x, [key]: v }));

  if (loading) return <div className="flex justify-center py-16"><div className="h-6 w-6 animate-spin rounded-full border-2 border-sapphire border-t-transparent" /></div>;

  return (
    <div className="max-w-xl space-y-4">
      <div className="surface p-5">
        <h3 className="mb-4 text-base font-semibold text-slate-900 dark:text-white">{policy ? 'Advance Policy' : 'No policy configured'}</h3>
        {error && <FormError error={error} />}
        {success && <p className="mb-3 rounded-lg bg-emerald-50 px-3 py-2.5 text-sm text-emerald-600 dark:bg-emerald-500/10 dark:text-emerald-400">Policy saved successfully.</p>}
        <div className="space-y-3">
          <FormField label="Policy Name"><input value={form.policyName ?? ''} onChange={(e) => f('policyName', e.target.value)} className="input w-full" title="Policy Name" /></FormField>
          <FormField label="Max % of Salary"><input type="number" value={form.maxPercentageOfSalary ?? 50} onChange={(e) => f('maxPercentageOfSalary', Number(e.target.value))} className="input w-full" title="Max % of Salary" /></FormField>
          <FormField label="Max Advances Per Year"><input type="number" value={form.maxAdvancesPerYear ?? 2} onChange={(e) => f('maxAdvancesPerYear', Number(e.target.value))} className="input w-full" title="Max Advances Per Year" /></FormField>
          <FormField label="Min Service (months)"><input type="number" value={form.minServiceMonths ?? 6} onChange={(e) => f('minServiceMonths', Number(e.target.value))} className="input w-full" title="Min Service (months)" /></FormField>
          <FormField label="Cooldown (months)"><input type="number" value={form.cooldownMonths ?? 3} onChange={(e) => f('cooldownMonths', Number(e.target.value))} className="input w-full" title="Cooldown (months)" /></FormField>
          <div className="flex gap-6">
            <label className="flex items-center gap-2 text-sm text-slate-700 dark:text-slate-300"><input type="checkbox" checked={form.allowInstallments ?? false} onChange={(e) => f('allowInstallments', e.target.checked)} className="h-4 w-4 accent-sapphire" title="Allow Installments" /> Allow Installments</label>
            <label className="flex items-center gap-2 text-sm text-slate-700 dark:text-slate-300"><input type="checkbox" checked={form.requiresApproval ?? false} onChange={(e) => f('requiresApproval', e.target.checked)} className="h-4 w-4 accent-sapphire" title="Requires Approval" /> Requires Approval</label>
          </div>
          {form.allowInstallments && (
            <FormField label="Max Installments"><input type="number" value={form.maxInstallments ?? 3} onChange={(e) => f('maxInstallments', Number(e.target.value))} className="input w-full" title="Max Installments" /></FormField>
          )}
        </div>
        <div className="mt-4">
          <button type="button" onClick={save} disabled={saving} className="btn-primary disabled:opacity-60">{saving ? 'Saving…' : 'Save Policy'}</button>
        </div>
      </div>
    </div>
  );
}

// ── Salary Advances ───────────────────────────────────────────────────────────

function AdvancesTab() {
  const { currencyCode } = useTenantSettings();
  const fmt = (n: number) => n.toLocaleString('en-US', { style: 'currency', currency: currencyCode });
  const [items, setItems] = useState<SalaryAdvance[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [filterStatus, setFilterStatus] = useState('');
  const [page, setPage] = useState(1);
  const pageSize = 20;

  const [createModal, setCreateModal] = useState(false);
  const [approveModal, setApproveModal] = useState(false);
  const [selected, setSelected] = useState<SalaryAdvance | null>(null);
  const [createForm, setCreateForm] = useState({ requestedAmount: 0, repaymentType: 'FullDeduction', installments: 1, repaymentStartDate: '', reason: '' });
  const [selectedEmployee, setSelectedEmployee] = useState<EmployeeSelection | null>(null);
  const [approveForm, setApproveForm] = useState({ approvedAmount: 0, installments: 1, repaymentStartDate: '' });
  const [rejectReason, setRejectReason] = useState('');
  const [rejectModal, setRejectModal] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try { const r = await advancesApi.list({ status: filterStatus || undefined, page, pageSize }); setItems(r.items); setTotal(r.total); } catch { /**/ }
    finally { setLoading(false); }
  }, [filterStatus, page]);
  useEffect(() => { load(); }, [load]);

  const createAdvance = async () => {
    if (!selectedEmployee) { setError('Select an employee'); return; }
    setSaving(true); setError('');
    try {
      await advancesApi.create({
        ...createForm,
        employeeId: '00000000-0000-0000-0000-000000000000',
        employeeName: selectedEmployee.fullName,
        employeeIntId: selectedEmployee.intId,
      });
      setCreateModal(false); setSelectedEmployee(null); load();
    }
    catch (e: any) { setError(e?.response?.data || 'Failed to create advance.'); }
    finally { setSaving(false); }
  };

  const approve = async () => {
    if (!selected) return;
    setSaving(true); setError('');
    try { await advancesApi.approve(selected.id, approveForm); setApproveModal(false); load(); }
    catch { setError('Failed to approve.'); }
    finally { setSaving(false); }
  };

  const reject = async () => {
    if (!selected) return;
    setSaving(true); setError('');
    try { await advancesApi.reject(selected.id, rejectReason); setRejectModal(false); load(); }
    catch { setError('Failed to reject.'); }
    finally { setSaving(false); }
  };

  return (
    <>
      <div className="space-y-4">
        <div className="flex items-center justify-between">
          <select value={filterStatus} onChange={(e) => { setFilterStatus(e.target.value); setPage(1); }} className="select" title="Filter by status">
            <option value="">All Statuses</option>
            {['Pending', 'Approved', 'Active', 'Settled', 'Rejected'].map((s) => <option key={s}>{s}</option>)}
          </select>
          <button type="button" onClick={() => { setCreateForm({ requestedAmount: 0, repaymentType: 'FullDeduction', installments: 1, repaymentStartDate: '', reason: '' }); setSelectedEmployee(null); setError(''); setCreateModal(true); }} className="btn-primary">
            <Plus className="h-4 w-4" /> New Advance Request
          </button>
        </div>
        <div className="surface overflow-hidden">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-100 dark:border-white/[0.07]">
                {['Advance #', 'Employee', 'Requested', 'Approved', 'Outstanding', 'Repayment', 'Status', ''].map((h) => (
                  <th key={h} className="px-4 py-3 text-left text-xs font-bold uppercase tracking-wide text-slate-400">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 dark:divide-white/[0.05]">
              {loading ? (
                <tr><td colSpan={8} className="py-12 text-center"><div className="mx-auto h-6 w-6 animate-spin rounded-full border-2 border-sapphire border-t-transparent" /></td></tr>
              ) : items.length === 0 ? (
                <tr><td colSpan={8} className="py-12 text-center text-slate-400">No advances found</td></tr>
              ) : items.map((a) => (
                <tr key={a.id} className="hover:bg-slate-50 dark:hover:bg-white/[0.03]">
                  <td className="px-4 py-3 font-mono text-xs text-slate-500">{a.advanceNumber}</td>
                  <td className="px-4 py-3 font-medium text-slate-900 dark:text-white">{a.employeeName}</td>
                  <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{fmt(a.requestedAmount)}</td>
                  <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{a.approvedAmount > 0 ? fmt(a.approvedAmount) : '—'}</td>
                  <td className="px-4 py-3 font-semibold text-slate-900 dark:text-white">{fmt(a.outstandingBalance)}</td>
                  <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{a.repaymentType}</td>
                  <td className="px-4 py-3"><StatusBadge status={a.status} /></td>
                  <td className="px-4 py-3">
                    {a.status === 'Pending' && (
                      <div className="flex gap-1">
                        <button type="button" onClick={() => { setSelected(a); setApproveForm({ approvedAmount: a.requestedAmount, installments: a.installments || 1, repaymentStartDate: '' }); setError(''); setApproveModal(true); }} className="btn-primary h-6 px-2 text-xs">Approve</button>
                        <button type="button" onClick={() => { setSelected(a); setRejectReason(''); setError(''); setRejectModal(true); }} className="h-6 px-2 text-xs rounded-lg border border-red-200 text-red-500 hover:bg-red-50 dark:border-red-800 dark:hover:bg-red-900/20">Reject</button>
                      </div>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {total > pageSize && (
          <div className="flex items-center justify-between text-sm text-slate-500">
            <span>{total} total</span>
            <div className="flex gap-2">
              <button type="button" onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page === 1} className="btn-secondary h-7 px-2 text-xs disabled:opacity-40">← Prev</button>
              <span className="px-2">Page {page} of {Math.ceil(total / pageSize)}</span>
              <button type="button" onClick={() => setPage((p) => p + 1)} disabled={page >= Math.ceil(total / pageSize)} className="btn-secondary h-7 px-2 text-xs disabled:opacity-40">Next →</button>
            </div>
          </div>
        )}
      </div>

      <Modal isOpen={createModal} title="New Advance Request" onClose={() => setCreateModal(false)}
        footer={<><button type="button" onClick={() => setCreateModal(false)} className="btn-secondary">Cancel</button><button type="button" onClick={createAdvance} disabled={saving} className="btn-primary disabled:opacity-60">{saving ? 'Submitting…' : 'Submit'}</button></>}>
        <FormError error={error} />
        <div className="space-y-3">
          <FormField label="Employee" required>
            <EmployeeSearchSelect value={selectedEmployee} onChange={setSelectedEmployee} required />
          </FormField>
          <FormField label={`Requested Amount (${currencyCode})`} required><input type="number" value={createForm.requestedAmount} onChange={(e) => setCreateForm(x => ({ ...x, requestedAmount: Number(e.target.value) }))} className="input w-full" title="Requested Amount" min="1" /></FormField>
          <FormField label="Repayment Type">
            <select value={createForm.repaymentType} onChange={(e) => setCreateForm(x => ({ ...x, repaymentType: e.target.value }))} className="select w-full" title="Repayment Type">
              {['FullDeduction', 'Installments'].map((v) => <option key={v}>{v}</option>)}
            </select>
          </FormField>
          {createForm.repaymentType === 'Installments' && (
            <FormField label="Installments"><input type="number" value={createForm.installments} onChange={(e) => setCreateForm(x => ({ ...x, installments: Number(e.target.value) }))} className="input w-full" title="Installments" /></FormField>
          )}
          <FormField label="Reason"><textarea value={createForm.reason} onChange={(e) => setCreateForm(x => ({ ...x, reason: e.target.value }))} className="input w-full" rows={2} title="Reason" /></FormField>
        </div>
      </Modal>

      <Modal isOpen={approveModal} title={`Approve Advance — ${selected?.advanceNumber}`} onClose={() => setApproveModal(false)}
        footer={<><button type="button" onClick={() => setApproveModal(false)} className="btn-secondary">Cancel</button><button type="button" onClick={approve} disabled={saving} className="btn-primary disabled:opacity-60">{saving ? 'Approving…' : 'Approve'}</button></>}>
        <FormError error={error} />
        <div className="space-y-3">
          <FormField label={`Approved Amount (${currencyCode})`} required><input type="number" value={approveForm.approvedAmount} onChange={(e) => setApproveForm(x => ({ ...x, approvedAmount: Number(e.target.value) }))} className="input w-full" title="Approved Amount" /></FormField>
          <FormField label="Installments"><input type="number" value={approveForm.installments} onChange={(e) => setApproveForm(x => ({ ...x, installments: Number(e.target.value) }))} className="input w-full" title="Installments" /></FormField>
          <FormField label="Repayment Start Date"><input type="date" value={approveForm.repaymentStartDate} onChange={(e) => setApproveForm(x => ({ ...x, repaymentStartDate: e.target.value }))} className="input w-full" title="Repayment Start Date" /></FormField>
        </div>
      </Modal>

      <Modal isOpen={rejectModal} title="Reject Advance" onClose={() => setRejectModal(false)}
        footer={<><button type="button" onClick={() => setRejectModal(false)} className="btn-secondary">Cancel</button><button type="button" onClick={reject} disabled={saving} className="h-8 px-4 rounded-lg bg-red-500 text-white text-sm font-semibold hover:bg-red-600 disabled:opacity-60">{saving ? 'Rejecting…' : 'Reject'}</button></>}>
        <FormError error={error} />
        <FormField label="Rejection Reason"><textarea value={rejectReason} onChange={(e) => setRejectReason(e.target.value)} className="input w-full" rows={3} placeholder="Reason for rejection…" /></FormField>
      </Modal>
    </>
  );
}

// ── Bonus Types ───────────────────────────────────────────────────────────────

function BonusTypesTab() {
  const [items, setItems] = useState<BonusType[]>([]);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [form, setForm] = useState({ code: '', nameEn: '', nameAr: '', calculationMethod: 'Fixed', isTaxable: true });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try { setItems(await bonusTypesApi.list()); } catch { /**/ }
    finally { setLoading(false); }
  }, []);
  useEffect(() => { load(); }, [load]);

  const { translation: autoBonusNameAr, isTranslating: translatingBonusNameAr } = useAutoTranslate(form.nameEn);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => { if (autoBonusNameAr && !form.nameAr) setForm(x => ({ ...x, nameAr: autoBonusNameAr })); }, [autoBonusNameAr]);

  const save = async () => {
    if (!form.code.trim() || !form.nameEn.trim()) { setError('Code and name are required'); return; }
    setSaving(true); setError('');
    try { await bonusTypesApi.create(form); setModalOpen(false); load(); }
    catch { setError('Failed to save.'); }
    finally { setSaving(false); }
  };

  return (
    <>
      <div className="space-y-4">
        <div className="flex justify-end">
          <button type="button" onClick={() => { setForm({ code: '', nameEn: '', nameAr: '', calculationMethod: 'Fixed', isTaxable: true }); setError(''); setModalOpen(true); }} className="btn-primary">
            <Plus className="h-4 w-4" /> New Bonus Type
          </button>
        </div>
        <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm dark:border-amber-800 dark:bg-amber-900/20">
          <p className="font-semibold text-amber-700 dark:text-amber-400">Tax Notice (US)</p>
          <p className="text-amber-600 dark:text-amber-300 text-xs mt-0.5">Taxable bonuses are subject to 22% federal supplemental withholding rate. Net amount = Gross − 22% federal withholding. State/FICA taxes calculated separately at payroll time.</p>
        </div>
        <div className="surface overflow-hidden">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-100 dark:border-white/[0.07]">
                {['Code', 'Name', 'Calculation Method', 'Tax Treatment', 'Status'].map((h) => (
                  <th key={h} className="px-4 py-3 text-left text-xs font-bold uppercase tracking-wide text-slate-400">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 dark:divide-white/[0.05]">
              {loading ? (
                <tr><td colSpan={5} className="py-12 text-center"><div className="mx-auto h-6 w-6 animate-spin rounded-full border-2 border-sapphire border-t-transparent" /></td></tr>
              ) : items.length === 0 ? (
                <tr><td colSpan={5} className="py-12 text-center text-slate-400">No bonus types yet</td></tr>
              ) : items.map((t) => (
                <tr key={t.id} className="hover:bg-slate-50 dark:hover:bg-white/[0.03]">
                  <td className="px-4 py-3 font-mono text-xs text-slate-500">{t.code}</td>
                  <td className="px-4 py-3 font-medium text-slate-900 dark:text-white">{t.nameEn}</td>
                  <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{t.calculationMethod}</td>
                  <td className="px-4 py-3">{t.isTaxable ? <span className="text-xs text-amber-600 font-semibold">Taxable (22% federal withholding)</span> : <span className="text-xs text-emerald-600 font-semibold">Tax-Exempt</span>}</td>
                  <td className="px-4 py-3"><StatusBadge status={t.isActive ? 'Active' : 'Closed'} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <Modal isOpen={modalOpen} title="New Bonus Type" onClose={() => setModalOpen(false)}
        footer={<><button type="button" onClick={() => setModalOpen(false)} className="btn-secondary">Cancel</button><button type="button" onClick={save} disabled={saving} className="btn-primary disabled:opacity-60">{saving ? 'Saving…' : 'Save'}</button></>}>
        <FormError error={error} />
        <div className="space-y-3">
          <FormField label="Code" required><input value={form.code} onChange={(e) => setForm(x => ({ ...x, code: e.target.value }))} className="input w-full" placeholder="ANNUAL_BONUS" /></FormField>
          <FormField label="Name (EN)" required><input value={form.nameEn} onChange={(e) => setForm(x => ({ ...x, nameEn: e.target.value }))} className="input w-full" title="Name (EN)" /></FormField>
          <FormField label="Name (AR)"><input value={form.nameAr} onChange={(e) => setForm(x => ({ ...x, nameAr: e.target.value }))} className="input w-full" dir="rtl" title="Name (AR)" placeholder={translatingBonusNameAr && !form.nameAr ? 'Translating…' : undefined} /></FormField>
          <FormField label="Calculation Method">
            <select value={form.calculationMethod} onChange={(e) => setForm(x => ({ ...x, calculationMethod: e.target.value }))} className="select w-full" title="Calculation Method">
              {['Fixed', 'PercentageSalary'].map((v) => <option key={v}>{v}</option>)}
            </select>
          </FormField>
          <label className="flex items-center gap-2 text-sm text-slate-700 dark:text-slate-300">
            <input type="checkbox" checked={form.isTaxable} onChange={(e) => setForm(x => ({ ...x, isTaxable: e.target.checked }))} className="h-4 w-4 accent-sapphire" title="Taxable" />
            Taxable (applies 22% federal supplemental withholding)
          </label>
        </div>
      </Modal>
    </>
  );
}

// ── Bonus Batches ─────────────────────────────────────────────────────────────

function BonusBatchesTab({ bonusTypes }: { bonusTypes: BonusType[] }) {
  const { currencyCode } = useTenantSettings();
  const fmt = (n: number) => n.toLocaleString('en-US', { style: 'currency', currency: currencyCode });
  const [items, setItems] = useState<BonusBatch[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [filterStatus, setFilterStatus] = useState('');
  const [page, setPage] = useState(1);
  const pageSize = 20;

  const [createModal, setCreateModal] = useState(false);
  const [detailModal, setDetailModal] = useState(false);
  const [selected, setSelected] = useState<{ batch: BonusBatch; bonuses: EmployeeBonus[]; auditLogs: AuditLogEntry[]; glEntries: FinanceGlEntry[] } | null>(null);
  const [addEmployeeModal, setAddEmployeeModal] = useState(false);
  const [createForm, setCreateForm] = useState({ bonusTypeId: '', batchName: '', paymentPeriod: '', paymentDate: '', notes: '' });
  const [empForm, setEmpForm] = useState({ basicSalary: 0, calculationMethod: 'Fixed', calculationValue: 0, notes: '' });
  const [selectedBonusEmp, setSelectedBonusEmp] = useState<EmployeeSelection | null>(null);
  const [addResult, setAddResult] = useState<{ grossBonusAmount: number; taxWithheld: number; netBonusAmount: number } | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [detailTab, setDetailTab] = useState<'employees' | 'gl' | 'audit'>('employees');

  const load = useCallback(async () => {
    setLoading(true);
    try { const r = await bonusBatchesApi.list({ status: filterStatus || undefined, page, pageSize }); setItems(r.items); setTotal(r.total); } catch { /**/ }
    finally { setLoading(false); }
  }, [filterStatus, page]);
  useEffect(() => { load(); }, [load]);

  const openDetail = async (batch: BonusBatch) => {
    try { const d = await bonusBatchesApi.get(batch.id); setSelected(d); setDetailTab('employees'); setDetailModal(true); } catch { /**/ }
  };

  const createBatch = async () => {
    if (!createForm.bonusTypeId || !createForm.batchName.trim() || !createForm.paymentPeriod.trim() || !createForm.paymentDate) { setError('All fields are required'); return; }
    setSaving(true); setError('');
    try { await bonusBatchesApi.create(createForm); setCreateModal(false); load(); }
    catch { setError('Failed to create batch.'); }
    finally { setSaving(false); }
  };

  const addEmployee = async () => {
    if (!selected || !selectedBonusEmp) { setError('Select an employee'); return; }
    setSaving(true); setError(''); setAddResult(null);
    try {
      const r = await bonusBatchesApi.addEmployee(selected.batch.id, {
        ...empForm,
        employeeId: '00000000-0000-0000-0000-000000000000',
        employeeName: selectedBonusEmp.fullName,
        department: selectedBonusEmp.department,
      });
      setAddResult({ grossBonusAmount: r.grossBonusAmount, taxWithheld: r.taxWithheld, netBonusAmount: r.netBonusAmount });
      const d = await bonusBatchesApi.get(selected.batch.id); setSelected(d);
    } catch { setError('Failed to add employee.'); }
    finally { setSaving(false); }
  };

  const submitBatch = async (id: string) => {
    if (!confirm('Submit this batch for approval?')) return;
    try { await bonusBatchesApi.submit(id); load(); if (selected?.batch.id === id) { const d = await bonusBatchesApi.get(id); setSelected(d); } } catch { /**/ }
  };

  const approveBatch = async (id: string) => {
    if (!confirm('Approve this bonus batch? This will generate GL entries.')) return;
    try { await bonusBatchesApi.approve(id); load(); if (selected?.batch.id === id) { const d = await bonusBatchesApi.get(id); setSelected(d); } } catch { /**/ }
  };

  const markPaidBatch = async (id: string) => {
    if (!confirm('Mark this batch as paid? This action is final.')) return;
    try { await bonusBatchesApi.markPaid(id); load(); if (selected?.batch.id === id) { const d = await bonusBatchesApi.get(id); setSelected(d); } } catch { /**/ }
  };

  return (
    <>
      <div className="space-y-4">
        <div className="flex items-center justify-between">
          <select value={filterStatus} onChange={(e) => { setFilterStatus(e.target.value); setPage(1); }} className="select" title="Filter by status">
            <option value="">All Statuses</option>
            {['Draft', 'PendingApproval', 'Approved', 'Cancelled', 'Paid'].map((s) => <option key={s}>{s}</option>)}
          </select>
          <button type="button" onClick={() => { setCreateForm({ bonusTypeId: bonusTypes[0]?.id ?? '', batchName: '', paymentPeriod: '', paymentDate: '', notes: '' }); setError(''); setCreateModal(true); }} className="btn-primary">
            <Plus className="h-4 w-4" /> New Bonus Batch
          </button>
        </div>
        <div className="surface overflow-hidden">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-100 dark:border-white/[0.07]">
                {['Batch #', 'Name', 'Type', 'Period', 'Payment Date', 'Employees', 'Total Amount', 'Status', ''].map((h) => (
                  <th key={h} className="px-4 py-3 text-left text-xs font-bold uppercase tracking-wide text-slate-400">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 dark:divide-white/[0.05]">
              {loading ? (
                <tr><td colSpan={9} className="py-12 text-center"><div className="mx-auto h-6 w-6 animate-spin rounded-full border-2 border-sapphire border-t-transparent" /></td></tr>
              ) : items.length === 0 ? (
                <tr><td colSpan={9} className="py-12 text-center text-slate-400">No bonus batches</td></tr>
              ) : items.map((b) => (
                <tr key={b.id} className="hover:bg-slate-50 dark:hover:bg-white/[0.03] cursor-pointer" onClick={() => openDetail(b)}>
                  <td className="px-4 py-3 font-mono text-xs text-slate-500">{b.batchNumber}</td>
                  <td className="px-4 py-3 font-medium text-slate-900 dark:text-white">{b.batchName}</td>
                  <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{b.bonusTypeName}</td>
                  <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{b.paymentPeriod}</td>
                  <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{b.paymentDate}</td>
                  <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{b.employeeCount}</td>
                  <td className="px-4 py-3 font-semibold text-slate-900 dark:text-white">{fmt(b.totalAmount)}</td>
                  <td className="px-4 py-3"><StatusBadge status={b.status} /></td>
                  <td className="px-4 py-3 text-sapphire text-xs">View →</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {total > pageSize && (
          <div className="flex items-center justify-between text-sm text-slate-500">
            <span>{total} total</span>
            <div className="flex gap-2">
              <button type="button" onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page === 1} className="btn-secondary h-7 px-2 text-xs disabled:opacity-40">← Prev</button>
              <span className="px-2">Page {page} of {Math.ceil(total / pageSize)}</span>
              <button type="button" onClick={() => setPage((p) => p + 1)} disabled={page >= Math.ceil(total / pageSize)} className="btn-secondary h-7 px-2 text-xs disabled:opacity-40">Next →</button>
            </div>
          </div>
        )}
      </div>

      {/* Create Batch Modal */}
      <Modal isOpen={createModal} title="New Bonus Batch" onClose={() => setCreateModal(false)}
        footer={<><button type="button" onClick={() => setCreateModal(false)} className="btn-secondary">Cancel</button><button type="button" onClick={createBatch} disabled={saving} className="btn-primary disabled:opacity-60">{saving ? 'Creating…' : 'Create'}</button></>}>
        <FormError error={error} />
        <div className="space-y-3">
          <FormField label="Bonus Type" required>
            <select value={createForm.bonusTypeId} onChange={(e) => setCreateForm(x => ({ ...x, bonusTypeId: e.target.value }))} className="select w-full" title="Bonus Type">
              <option value="">Select type</option>
              {bonusTypes.map((t) => <option key={t.id} value={t.id}>{t.nameEn}</option>)}
            </select>
          </FormField>
          <FormField label="Batch Name" required><input value={createForm.batchName} onChange={(e) => setCreateForm(x => ({ ...x, batchName: e.target.value }))} className="input w-full" placeholder="Q4 2026 Annual Bonus" /></FormField>
          <FormField label="Payment Period" required><input value={createForm.paymentPeriod} onChange={(e) => setCreateForm(x => ({ ...x, paymentPeriod: e.target.value }))} className="input w-full" placeholder="2026-12" /></FormField>
          <FormField label="Payment Date" required><input type="date" value={createForm.paymentDate} onChange={(e) => setCreateForm(x => ({ ...x, paymentDate: e.target.value }))} className="input w-full" title="Payment Date" /></FormField>
          <FormField label="Notes"><textarea value={createForm.notes} onChange={(e) => setCreateForm(x => ({ ...x, notes: e.target.value }))} className="input w-full" rows={2} title="Notes" /></FormField>
        </div>
      </Modal>

      {/* Detail Modal */}
      <Modal isOpen={detailModal && !!selected} title={`Batch — ${selected?.batch.batchNumber}`} onClose={() => setDetailModal(false)} size="lg"
        footer={
          <div className="flex items-center gap-2">
            {selected?.batch.status === 'Draft' && <button type="button" onClick={() => submitBatch(selected!.batch.id)} className="btn-primary h-8 px-3 text-sm">Submit for Approval</button>}
            {selected?.batch.status === 'PendingApproval' && <button type="button" onClick={() => approveBatch(selected!.batch.id)} className="btn-primary h-8 px-3 text-sm">Approve Batch</button>}
            {selected?.batch.status === 'Approved' && <button type="button" onClick={() => markPaidBatch(selected!.batch.id)} className="h-8 px-3 text-sm rounded-lg bg-emerald-500 text-white text-sm font-semibold hover:bg-emerald-600">Mark Paid</button>}
            {selected?.batch.status === 'Draft' && <button type="button" onClick={() => { setEmpForm({ basicSalary: 0, calculationMethod: bonusTypes.find(t => t.id === selected?.batch.bonusTypeId)?.calculationMethod ?? 'Fixed', calculationValue: 0, notes: '' }); setSelectedBonusEmp(null); setAddResult(null); setError(''); setAddEmployeeModal(true); }} className="btn-secondary h-8 px-3 text-sm"><Plus className="h-3.5 w-3.5" /> Add Employee</button>}
            <button type="button" onClick={() => setDetailModal(false)} className="btn-secondary ml-auto">Close</button>
          </div>
        }>
        {selected && (
          <div className="space-y-4">
            <div className="grid grid-cols-4 gap-3 text-sm">
              <div className="surface p-3 rounded-lg"><p className="text-xs text-slate-400">Status</p><StatusBadge status={selected.batch.status} /></div>
              <div className="surface p-3 rounded-lg"><p className="text-xs text-slate-400">Employees</p><p className="font-semibold text-slate-900 dark:text-white">{selected.batch.employeeCount}</p></div>
              <div className="surface p-3 rounded-lg"><p className="text-xs text-slate-400">Total Gross</p><p className="font-semibold text-slate-900 dark:text-white">{fmt(selected.batch.totalAmount)}</p></div>
              <div className="surface p-3 rounded-lg"><p className="text-xs text-slate-400">Period</p><p className="font-semibold text-slate-900 dark:text-white">{selected.batch.paymentPeriod}</p></div>
            </div>

            {/* Sub-tabs */}
            <div className="flex gap-3 border-b border-slate-100 dark:border-white/[0.07]">
              {(['employees', 'gl', 'audit'] as const).map((t) => (
                <button key={t} type="button" onClick={() => setDetailTab(t)}
                  className={`pb-2 text-xs font-semibold uppercase tracking-wide transition border-b-2 ${detailTab === t ? 'border-sapphire text-sapphire' : 'border-transparent text-slate-400 hover:text-slate-600'}`}>
                  {t === 'employees' ? 'Employee Breakdown' : t === 'gl' ? 'GL Entries' : 'Audit Trail'}
                </button>
              ))}
            </div>

            {detailTab === 'employees' && (
              <div className="surface overflow-hidden max-h-64 overflow-y-auto">
                <table className="w-full text-xs">
                  <thead><tr className="border-b border-slate-100 dark:border-white/10">{['Employee', 'Department', 'Basic Salary', 'Method', 'Value', 'Gross Bonus', 'Status'].map((h) => <th key={h} className="px-3 py-2 text-left font-bold uppercase tracking-wide text-slate-400">{h}</th>)}</tr></thead>
                  <tbody className="divide-y divide-slate-100 dark:divide-white/[0.05]">
                    {selected.bonuses.length === 0 ? (
                      <tr><td colSpan={7} className="py-6 text-center text-slate-400">No employees added yet</td></tr>
                    ) : selected.bonuses.map((b) => (
                      <tr key={b.id} className="hover:bg-slate-50 dark:hover:bg-white/[0.03]">
                        <td className="px-3 py-2 font-medium text-slate-900 dark:text-white">{b.employeeName}</td>
                        <td className="px-3 py-2 text-slate-500">{b.department || '—'}</td>
                        <td className="px-3 py-2 text-slate-600 dark:text-slate-300">{fmt(b.basicSalary)}</td>
                        <td className="px-3 py-2 text-slate-500">{b.calculationMethod}</td>
                        <td className="px-3 py-2 text-slate-500">{b.calculationValue}</td>
                        <td className="px-3 py-2 font-semibold text-slate-900 dark:text-white">{fmt(b.bonusAmount)}</td>
                        <td className="px-3 py-2"><StatusBadge status={b.status} /></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
            {detailTab === 'gl' && <GlEntriesTable entries={selected.glEntries ?? []} fmt={fmt} />}
            {detailTab === 'audit' && <AuditTrailTable logs={selected.auditLogs ?? []} />}
          </div>
        )}
      </Modal>

      {/* Add Employee Modal */}
      <Modal isOpen={addEmployeeModal} title="Add Employee to Batch" onClose={() => { setAddEmployeeModal(false); setAddResult(null); }}
        footer={<><button type="button" onClick={() => { setAddEmployeeModal(false); setAddResult(null); }} className="btn-secondary">{addResult ? 'Done' : 'Cancel'}</button>{!addResult && <button type="button" onClick={addEmployee} disabled={saving} className="btn-primary disabled:opacity-60">{saving ? 'Adding…' : 'Add'}</button>}</>}>
        <FormError error={error} />
        {addResult ? (
          <div className="space-y-3">
            <div className="rounded-lg bg-emerald-50 dark:bg-emerald-900/20 p-4 text-sm">
              <p className="font-semibold text-emerald-700 dark:text-emerald-400 mb-2">Employee Added — Tax Summary</p>
              <div className="grid grid-cols-3 gap-3">
                <div><p className="text-xs text-slate-400">Gross Bonus</p><p className="font-bold text-slate-900 dark:text-white">{fmt(addResult.grossBonusAmount)}</p></div>
                <div><p className="text-xs text-slate-400">Federal Withholding (22%)</p><p className="font-bold text-amber-600 dark:text-amber-400">−{fmt(addResult.taxWithheld)}</p></div>
                <div><p className="text-xs text-slate-400">Net to Employee</p><p className="font-bold text-emerald-600 dark:text-emerald-400">{fmt(addResult.netBonusAmount)}</p></div>
              </div>
            </div>
            <button type="button" onClick={() => { setEmpForm({ basicSalary: 0, calculationMethod: empForm.calculationMethod, calculationValue: 0, notes: '' }); setSelectedBonusEmp(null); setAddResult(null); }} className="btn-secondary w-full">Add Another Employee</button>
          </div>
        ) : (
          <div className="space-y-3">
            <FormField label="Employee" required>
              <EmployeeSearchSelect value={selectedBonusEmp} onChange={setSelectedBonusEmp} required />
            </FormField>
            <FormField label={`Basic Salary (${currencyCode})`} required><input type="number" value={empForm.basicSalary} onChange={(e) => setEmpForm(x => ({ ...x, basicSalary: Number(e.target.value) }))} className="input w-full" title="Basic Salary" /></FormField>
            <FormField label="Calculation Method">
              <select value={empForm.calculationMethod} onChange={(e) => setEmpForm(x => ({ ...x, calculationMethod: e.target.value }))} className="select w-full" title="Calculation Method">
                {['Fixed', 'PercentageSalary'].map((v) => <option key={v}>{v}</option>)}
              </select>
            </FormField>
            <FormField label={empForm.calculationMethod === 'Fixed' ? `Fixed Amount (${currencyCode})` : 'Percentage (%)'}><input type="number" step="0.1" value={empForm.calculationValue} onChange={(e) => setEmpForm(x => ({ ...x, calculationValue: Number(e.target.value) }))} className="input w-full" title="Calculation Value" /></FormField>
          </div>
        )}
      </Modal>
    </>
  );
}

// ── Audit Report ──────────────────────────────────────────────────────────────

function AuditReportTab() {
  const { currencyCode } = useTenantSettings();
  const fmt = (n: number) => n.toLocaleString('en-US', { style: 'currency', currency: currencyCode });
  const [loanAudit, setLoanAudit] = useState<any>(null);
  const [advanceAudit, setAdvanceAudit] = useState<any>(null);
  const [bonusAudit, setBonusAudit] = useState<any>(null);
  const [loading, setLoading] = useState(true);
  const [section, setSection] = useState<'loans' | 'advances' | 'bonuses'>('loans');

  useEffect(() => {
    setLoading(true);
    Promise.all([
      loansApi.audit(),
      advancesApi.audit(),
      bonusBatchesApi.audit(),
    ]).then(([la, aa, ba]) => {
      setLoanAudit(la); setAdvanceAudit(aa); setBonusAudit(ba);
    }).catch(() => {}).finally(() => setLoading(false));
  }, []);

  if (loading) return <div className="flex justify-center py-16"><div className="h-6 w-6 animate-spin rounded-full border-2 border-sapphire border-t-transparent" /></div>;

  return (
    <div className="space-y-5">
      {/* Compliance Header */}
      <div className="rounded-xl border border-emerald-200 bg-emerald-50 p-4 dark:border-emerald-800 dark:bg-emerald-900/20">
        <div className="flex items-center gap-3">
          <ShieldCheck className="h-6 w-6 text-emerald-600 dark:text-emerald-400" />
          <div>
            <p className="font-semibold text-emerald-700 dark:text-emerald-400">Finance Module — Audit Ready</p>
            <p className="text-xs text-emerald-600 dark:text-emerald-300 mt-0.5">
              All financial transactions are recorded with double-entry GL journal entries, immutable audit trails, and reconciliation checks.
              Report generated: {new Date().toLocaleString('en-US', { timeZone: 'America/New_York', dateStyle: 'medium', timeStyle: 'short' })} EST
            </p>
          </div>
        </div>
      </div>

      {/* Section tabs */}
      <div className="flex gap-3 border-b border-slate-200 dark:border-white/[0.08]">
        {(['loans', 'advances', 'bonuses'] as const).map((s) => (
          <button key={s} type="button" onClick={() => setSection(s)}
            className={`pb-2.5 px-1 text-sm font-semibold capitalize transition border-b-2 ${section === s ? 'border-sapphire text-sapphire' : 'border-transparent text-slate-500 hover:text-slate-700'}`}>
            {s}
          </button>
        ))}
      </div>

      {section === 'loans' && loanAudit && (
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <SummaryCard label="Total Loans" value={loanAudit.totalLoans} icon={CreditCard} color="text-blue-600 dark:text-blue-400" />
            <SummaryCard label="Active" value={loanAudit.activeLoans} icon={TrendingUp} color="text-emerald-600 dark:text-emerald-400" />
            <SummaryCard label="Total Disbursed" value={fmt(loanAudit.totalDisbursed)} icon={DollarSign} color="text-sapphire" />
            <SummaryCard label="Outstanding" value={fmt(loanAudit.totalOutstanding)} icon={AlertTriangle} color="text-amber-600 dark:text-amber-400" />
          </div>
          <div className="surface overflow-hidden">
            <p className="px-4 py-3 text-xs font-bold uppercase tracking-wide text-slate-400 border-b border-slate-100 dark:border-white/[0.07]">Reconciliation — Loan Register</p>
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-100 dark:border-white/[0.07]">
                  {['Loan #', 'Employee', 'Type', 'Disbursed', 'Repaid', 'Outstanding', 'Status', 'Reconciled'].map((h) => (
                    <th key={h} className="px-4 py-3 text-left text-xs font-bold uppercase tracking-wide text-slate-400">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 dark:divide-white/[0.05]">
                {loanAudit.reconciliation?.map((r: any) => (
                  <tr key={r.loanNumber} className="hover:bg-slate-50 dark:hover:bg-white/[0.03]">
                    <td className="px-4 py-3 font-mono text-xs text-slate-500">{r.loanNumber}</td>
                    <td className="px-4 py-3 font-medium text-slate-900 dark:text-white">{r.employeeName}</td>
                    <td className="px-4 py-3 text-slate-500">{r.loanTypeName}</td>
                    <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{fmt(r.approvedAmount)}</td>
                    <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{fmt(r.totalRepaid)}</td>
                    <td className="px-4 py-3 font-semibold text-slate-900 dark:text-white">{fmt(r.outstandingBalance)}</td>
                    <td className="px-4 py-3"><StatusBadge status={r.status} /></td>
                    <td className="px-4 py-3">{r.isReconciled ? <span className="text-xs font-semibold text-emerald-600">✓ OK</span> : <span className="text-xs font-semibold text-red-500">✗ Variance</span>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {section === 'advances' && advanceAudit && (
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <SummaryCard label="Total Advances" value={advanceAudit.totalAdvances} icon={DollarSign} color="text-blue-600 dark:text-blue-400" />
            <SummaryCard label="Active" value={advanceAudit.activeAdvances} icon={TrendingUp} color="text-emerald-600 dark:text-emerald-400" />
            <SummaryCard label="Total Disbursed" value={fmt(advanceAudit.totalDisbursed)} icon={DollarSign} color="text-sapphire" />
            <SummaryCard label="Outstanding" value={fmt(advanceAudit.totalOutstanding)} icon={AlertTriangle} color="text-amber-600 dark:text-amber-400" />
          </div>
          <div className="surface overflow-hidden">
            <p className="px-4 py-3 text-xs font-bold uppercase tracking-wide text-slate-400 border-b border-slate-100 dark:border-white/[0.07]">Reconciliation — Advance Register</p>
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-100 dark:border-white/[0.07]">
                  {['Advance #', 'Employee', 'Disbursed', 'Repaid', 'Outstanding', 'Status', 'Reconciled'].map((h) => (
                    <th key={h} className="px-4 py-3 text-left text-xs font-bold uppercase tracking-wide text-slate-400">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 dark:divide-white/[0.05]">
                {advanceAudit.reconciliation?.map((r: any) => (
                  <tr key={r.advanceNumber} className="hover:bg-slate-50 dark:hover:bg-white/[0.03]">
                    <td className="px-4 py-3 font-mono text-xs text-slate-500">{r.advanceNumber}</td>
                    <td className="px-4 py-3 font-medium text-slate-900 dark:text-white">{r.employeeName}</td>
                    <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{fmt(r.approvedAmount)}</td>
                    <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{fmt(r.totalRepaid)}</td>
                    <td className="px-4 py-3 font-semibold text-slate-900 dark:text-white">{fmt(r.outstandingBalance)}</td>
                    <td className="px-4 py-3"><StatusBadge status={r.status} /></td>
                    <td className="px-4 py-3">{r.isReconciled ? <span className="text-xs font-semibold text-emerald-600">✓ OK</span> : <span className="text-xs font-semibold text-red-500">✗ Variance</span>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {section === 'bonuses' && bonusAudit && (
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <SummaryCard label="Total Batches" value={bonusAudit.totalBatches} icon={Gift} color="text-blue-600 dark:text-blue-400" />
            <SummaryCard label="Paid Batches" value={bonusAudit.paidBatches} icon={CheckCircle} color="text-emerald-600 dark:text-emerald-400" />
            <SummaryCard label="Total Bonus Amount" value={fmt(bonusAudit.totalBonusAmount)} icon={DollarSign} color="text-sapphire" />
            <SummaryCard label="Pending Payment" value={fmt(bonusAudit.pendingPaymentAmount)} icon={AlertTriangle} color="text-amber-600 dark:text-amber-400" />
          </div>
          {bonusAudit.byDepartment?.length > 0 && (
            <div className="surface overflow-hidden">
              <p className="px-4 py-3 text-xs font-bold uppercase tracking-wide text-slate-400 border-b border-slate-100 dark:border-white/[0.07]">Bonus Distribution by Department</p>
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-slate-100 dark:border-white/[0.07]">
                    {['Department', 'Employees', 'Total Bonus'].map((h) => (
                      <th key={h} className="px-4 py-3 text-left text-xs font-bold uppercase tracking-wide text-slate-400">{h}</th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100 dark:divide-white/[0.05]">
                  {bonusAudit.byDepartment.map((d: any) => (
                    <tr key={d.department} className="hover:bg-slate-50 dark:hover:bg-white/[0.03]">
                      <td className="px-4 py-3 font-medium text-slate-900 dark:text-white">{d.department || '—'}</td>
                      <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{d.count}</td>
                      <td className="px-4 py-3 font-semibold text-slate-900 dark:text-white">{fmt(d.totalAmount)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

// ── LoansPage ─────────────────────────────────────────────────────────────────

export function LoansPage() {
  const { currencyCode } = useTenantSettings();
  const fmt = (n: number) => n.toLocaleString('en-US', { style: 'currency', currency: currencyCode });
  const [activeTab, setActiveTab] = useState<Tab>('loans');
  const [loanTypes, setLoanTypes] = useState<LoanType[]>([]);
  const [bonusTypes, setBonusTypes] = useState<BonusType[]>([]);
  const [summary, setSummary] = useState<{ activeLoans: number; totalOutstanding: number; pendingLoans: number; pendingBonuses: number } | null>(null);

  useEffect(() => {
    loanTypesApi.list().then(setLoanTypes).catch(() => {});
    bonusTypesApi.list().then(setBonusTypes).catch(() => {});
    Promise.all([loansApi.audit(), advancesApi.audit()]).then(([la, aa]) => {
      setSummary({
        activeLoans: (la.activeLoans ?? 0) + (aa.activeAdvances ?? 0),
        totalOutstanding: (la.totalOutstanding ?? 0) + (aa.totalOutstanding ?? 0),
        pendingLoans: (la.pendingLoans ?? 0) + (aa.pendingAdvances ?? 0),
        pendingBonuses: 0,
      });
    }).catch(() => {});
  }, []);

  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-2xl font-extrabold text-slate-950 dark:text-white">Loans, Advances & Bonuses</h1>
        <p className="mt-0.5 text-sm text-slate-500 dark:text-slate-400">
          Finance module — audit-ready with GL journal entries, reconciliation reports, and compliance trails
        </p>
      </div>

      {summary && (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          <SummaryCard label="Active Obligations" value={summary.activeLoans} icon={CreditCard} color="text-blue-600 dark:text-blue-400" sub="loans + advances" />
          <SummaryCard label="Total Outstanding" value={fmt(summary.totalOutstanding)} icon={DollarSign} color="text-amber-600 dark:text-amber-400" sub="loans + advances" />
          <SummaryCard label="Pending Approval" value={summary.pendingLoans} icon={AlertTriangle} color="text-sapphire" sub="awaiting decision" />
          <SummaryCard label="Audit Status" value="Ready" icon={ShieldCheck} color="text-emerald-600 dark:text-emerald-400" sub="GL entries current" />
        </div>
      )}

      <div className="flex items-center gap-1 border-b border-slate-200 dark:border-white/[0.08]">
        {tabs.map(({ id, label, icon: Icon }) => (
          <button
            key={id}
            type="button"
            onClick={() => setActiveTab(id)}
            className={`flex items-center gap-2 border-b-2 px-4 py-2.5 text-sm font-semibold transition whitespace-nowrap ${
              activeTab === id
                ? 'border-sapphire text-sapphire'
                : 'border-transparent text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-slate-200'
            }`}
          >
            <Icon className="h-4 w-4" />
            {label}
          </button>
        ))}
      </div>

      <div>
        {activeTab === 'loans' && <LoansTab loanTypes={loanTypes} />}
        {activeTab === 'loanTypes' && <LoanTypesTab />}
        {activeTab === 'advances' && <AdvancesTab />}
        {activeTab === 'advancePolicy' && <AdvancePolicyTab />}
        {activeTab === 'bonusTypes' && <BonusTypesTab />}
        {activeTab === 'bonusBatches' && <BonusBatchesTab bonusTypes={bonusTypes} />}
        {activeTab === 'auditReport' && <AuditReportTab />}
      </div>
    </div>
  );
}

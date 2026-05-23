import { useCallback, useEffect, useState } from 'react';
import { Lock, Play, Plus, FileText } from 'lucide-react';
import { payrollApi } from '../api/payroll';
import type { PayrollRun, PayrollSlip } from '../api/payroll';
import { Modal } from '../components/Modal';
import { StatusChip } from '../components/StatusChip';

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

const runStatusTone = (s: string): { label: string; tone: 'slate' | 'amber' | 'emerald' | 'blue' } => {
  switch (s) {
    case 'Draft': return { label: 'Draft', tone: 'slate' };
    case 'Processed': return { label: 'Processed', tone: 'amber' };
    case 'Locked': return { label: 'Locked · Final', tone: 'emerald' };
    default: return { label: s, tone: 'slate' };
  }
};

const fmt = (n: number) => new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(n);

export function PayrollPage() {
  const [runs, setRuns] = useState<PayrollRun[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [selectedRun, setSelectedRun] = useState<PayrollRun | null>(null);
  const [slips, setSlips] = useState<PayrollSlip[]>([]);
  const [slipsLoading, setSlipsLoading] = useState(false);
  const [createOpen, setCreateOpen] = useState(false);
  const [createYear, setCreateYear] = useState(new Date().getFullYear());
  const [createMonth, setCreateMonth] = useState(new Date().getMonth() + 1);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try { const r = await payrollApi.listRuns(); setRuns(r.items); setTotal(r.total); } catch { /**/ }
    finally { setLoading(false); }
  }, []);

  useEffect(() => { load(); }, [load]);

  const openSlips = async (run: PayrollRun) => {
    setSelectedRun(run);
    setSlipsLoading(true);
    try { const r = await payrollApi.slips(run.id, { pageSize: 100 }); setSlips(r.items); } catch { /**/ }
    finally { setSlipsLoading(false); }
  };

  const handleCreate = async () => {
    setSaving(true); setError('');
    try {
      await payrollApi.createRun(createYear, createMonth);
      setCreateOpen(false);
      load();
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: { message?: string } } })?.response?.data?.message;
      setError(msg ?? 'Failed to create payroll run.');
    } finally { setSaving(false); }
  };

  const handleProcess = async (id: string) => {
    try {
      const updated = await payrollApi.processRun(id);
      setRuns((rs) => rs.map((r) => r.id === id ? updated : r));
      if (selectedRun?.id === id) { setSelectedRun(updated); openSlips(updated); }
    } catch { /**/ }
  };

  const handleLock = async (id: string) => {
    try {
      const updated = await payrollApi.lockRun(id);
      setRuns((rs) => rs.map((r) => r.id === id ? updated : r));
      if (selectedRun?.id === id) setSelectedRun(updated);
    } catch { /**/ }
  };

  return (
    <div className="space-y-5">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-bold text-slate-900 dark:text-white">Payroll</h1>
          <p className="mt-0.5 text-sm text-slate-500 dark:text-slate-400">{total} payroll run{total !== 1 ? 's' : ''}</p>
        </div>
        <button type="button" onClick={() => { setCreateOpen(true); setError(''); }} className="btn-primary">
          <Plus className="h-4 w-4" /> New Payroll Run
        </button>
      </div>

      <div className="grid gap-5 lg:grid-cols-3">
        {/* Runs list */}
        <div className="surface overflow-hidden lg:col-span-1">
          <div className="border-b border-slate-100 px-4 py-3 dark:border-white/[0.07]">
            <p className="text-sm font-semibold text-slate-900 dark:text-white">Payroll Runs</p>
          </div>
          {loading && <div className="flex justify-center py-12"><div className="h-6 w-6 animate-spin rounded-full border-2 border-sapphire border-t-transparent" /></div>}
          {!loading && runs.length === 0 && <p className="py-12 text-center text-sm text-slate-400 dark:text-slate-500">No payroll runs yet</p>}
          <div className="divide-y divide-slate-100 dark:divide-white/[0.05]">
            {runs.map((run) => (
              <button
                key={run.id}
                type="button"
                onClick={() => openSlips(run)}
                className={`w-full px-4 py-3 text-left transition hover:bg-slate-50 dark:hover:bg-white/[0.03] ${selectedRun?.id === run.id ? 'bg-sapphire/5 dark:bg-sapphire/10' : ''}`}
              >
                <div className="flex items-center justify-between">
                  <p className="font-semibold text-slate-900 dark:text-white">{MONTHS[run.month - 1]} {run.year}</p>
                  <StatusChip {...runStatusTone(run.status)} />
                </div>
                <div className="mt-1 flex items-center gap-3 text-xs text-slate-500 dark:text-slate-400">
                  <span>{run.employeeCount} employees</span>
                  <span>Net: {fmt(run.totalNetSalary)}</span>
                </div>
                {run.status !== 'Locked' && (
                  <div className="mt-2 flex gap-1">
                    <button type="button" onClick={(e) => { e.stopPropagation(); handleProcess(run.id); }}
                      className="btn-secondary h-6 gap-1 px-2 text-xs">
                      <Play className="h-3 w-3" /> Process
                    </button>
                    {run.status === 'Processed' && (
                      <button type="button" onClick={(e) => { e.stopPropagation(); handleLock(run.id); }}
                        className="btn-secondary h-6 gap-1 px-2 text-xs text-emeraldZ">
                        <Lock className="h-3 w-3" /> Lock
                      </button>
                    )}
                  </div>
                )}
              </button>
            ))}
          </div>
        </div>

        {/* Slips panel */}
        <div className="surface overflow-hidden lg:col-span-2">
          {!selectedRun ? (
            <div className="flex flex-col items-center justify-center py-24 text-center">
              <FileText className="mb-3 h-10 w-10 text-slate-200 dark:text-slate-700" />
              <p className="text-sm text-slate-400 dark:text-slate-500">Select a payroll run to view salary slips</p>
            </div>
          ) : (
            <>
              <div className="border-b border-slate-100 px-4 py-3 dark:border-white/[0.07]">
                <div className="flex items-center justify-between">
                  <p className="text-sm font-semibold text-slate-900 dark:text-white">
                    Salary Slips — {MONTHS[selectedRun.month - 1]} {selectedRun.year}
                  </p>
                  <div className="flex items-center gap-3 text-xs text-slate-500 dark:text-slate-400">
                    <span>Gross: <span className="font-semibold text-slate-900 dark:text-white">{fmt(selectedRun.totalGrossSalary)}</span></span>
                    <span>Net: <span className="font-semibold text-emeraldZ">{fmt(selectedRun.totalNetSalary)}</span></span>
                  </div>
                </div>
              </div>
              {slipsLoading ? (
                <div className="flex justify-center py-12"><div className="h-6 w-6 animate-spin rounded-full border-2 border-sapphire border-t-transparent" /></div>
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full min-w-[560px] text-sm">
                    <thead>
                      <tr className="border-b border-slate-100 dark:border-white/[0.07]">
                        {['Employee', 'Dept', 'Basic', 'Housing', 'Transport', 'Gross', 'Deductions', 'Net'].map((h) => (
                          <th key={h} className="px-3 py-3 text-left text-xs font-bold uppercase tracking-wide text-slate-400 dark:text-slate-500">{h}</th>
                        ))}
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-100 dark:divide-white/[0.05]">
                      {slips.length === 0 && <tr><td colSpan={8} className="py-10 text-center text-sm text-slate-400">No slips. Click Process to generate.</td></tr>}
                      {slips.map((s) => (
                        <tr key={s.id} className="hover:bg-slate-50 dark:hover:bg-white/[0.03]">
                          <td className="px-3 py-2.5">
                            <p className="font-medium text-slate-900 dark:text-white">{s.employeeName}</p>
                            <p className="text-xs text-slate-400">{s.employeeCode}</p>
                          </td>
                          <td className="px-3 py-2.5 text-xs text-slate-500 dark:text-slate-400">{s.department || '—'}</td>
                          <td className="px-3 py-2.5 text-right text-slate-700 dark:text-slate-300">{fmt(s.basicSalary)}</td>
                          <td className="px-3 py-2.5 text-right text-slate-700 dark:text-slate-300">{fmt(s.housingAllowance)}</td>
                          <td className="px-3 py-2.5 text-right text-slate-700 dark:text-slate-300">{fmt(s.transportAllowance)}</td>
                          <td className="px-3 py-2.5 text-right font-semibold text-slate-900 dark:text-white">{fmt(s.grossSalary)}</td>
                          <td className="px-3 py-2.5 text-right text-rose-500">({fmt(s.deductions)})</td>
                          <td className="px-3 py-2.5 text-right font-bold text-emeraldZ">{fmt(s.netSalary)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </>
          )}
        </div>
      </div>

      {/* Create Run Modal */}
      <Modal isOpen={createOpen} title="New Payroll Run" onClose={() => setCreateOpen(false)} size="sm"
        footer={<><button type="button" onClick={() => setCreateOpen(false)} className="btn-secondary">Cancel</button><button type="button" onClick={handleCreate} disabled={saving} className="btn-primary disabled:opacity-60">{saving ? 'Creating…' : 'Create'}</button></>}>
        {error && <p className="mb-3 rounded-lg bg-red-50 px-3 py-2.5 text-sm text-red-600 dark:bg-red-500/10 dark:text-red-400">{error}</p>}
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className="mb-1.5 block text-sm font-medium text-slate-700 dark:text-slate-300">Year</label>
            <input type="number" aria-label="Year" value={createYear} onChange={(e) => setCreateYear(parseInt(e.target.value))} className="input w-full" min={2020} max={2035} />
          </div>
          <div>
            <label className="mb-1.5 block text-sm font-medium text-slate-700 dark:text-slate-300">Month</label>
            <select aria-label="Month" value={createMonth} onChange={(e) => setCreateMonth(parseInt(e.target.value))} className="select w-full">
              {MONTHS.map((m, i) => <option key={m} value={i + 1}>{m}</option>)}
            </select>
          </div>
        </div>
      </Modal>
    </div>
  );
}

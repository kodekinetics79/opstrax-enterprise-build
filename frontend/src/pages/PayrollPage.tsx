import { useEffect, useState } from 'react';
import {
  AlertTriangle, BarChart2, Bot, CheckCircle2, ChevronRight,
  FileText, Landmark, Layers3, Lock, Play, Plus,
  Settings, TrendingUp, Users, WalletCards, X,
  Zap, Shield,
} from 'lucide-react';
import {
  payrollApi,
  type PayrollRun, type PayrollSlip, type PayrollValidationResult,
  type SalaryStructure, type EmployeeSalaryStructure, type Payslip,
  type PayrollPaymentBatch, type PayrollPaymentRecord,
  type PayrollApproval, type PayrollSummary,
} from '../api/payroll';

// ── Shared helpers ──────────────────────────────────────────────────────────────

function fmtDate(s: string | null | undefined) {
  if (!s) return '—';
  return new Date(s).toLocaleDateString('en-AE', { day: 'numeric', month: 'short', year: 'numeric' });
}

function fmtAmt(n: number, currency = 'AED') {
  return `${currency} ${n.toLocaleString('en-AE', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

const STATUS_COLOR: Record<string, string> = {
  Draft: 'bg-slate-100 text-slate-600 dark:bg-slate-700 dark:text-slate-300',
  Processed: 'bg-amber-50 text-amber-700 dark:bg-amber-500/10 dark:text-amber-400',
  Approved: 'bg-blue-50 text-blue-700 dark:bg-blue-500/10 dark:text-blue-400',
  Locked: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400',
  Pending: 'bg-amber-50 text-amber-700 dark:bg-amber-500/10 dark:text-amber-400',
  Paid: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400',
  Failed: 'bg-rose-50 text-rose-700 dark:bg-rose-500/10 dark:text-rose-400',
  Generated: 'bg-teal-50 text-teal-700 dark:bg-teal-500/10 dark:text-teal-400',
  FileGenerated: 'bg-teal-50 text-teal-700 dark:bg-teal-500/10 dark:text-teal-400',
  Warning: 'bg-amber-50 text-amber-700 dark:bg-amber-500/10 dark:text-amber-400',
  Error: 'bg-rose-50 text-rose-700 dark:bg-rose-500/10 dark:text-rose-400',
  Info: 'bg-sky-50 text-sky-700 dark:bg-sky-500/10 dark:text-sky-400',
};

function StatusBadge({ status }: { status: string }) {
  const cls = STATUS_COLOR[status] ?? 'bg-slate-100 text-slate-500 dark:bg-slate-700 dark:text-slate-400';
  return <span className={`inline-flex rounded-md px-2 py-0.5 text-xs font-medium ${cls}`}>{status.replace(/([A-Z])/g, ' $1').trim()}</span>;
}

function KpiCard({ label, value, sub, icon: Icon, color }: {
  label: string; value: string | number; sub?: string;
  icon: React.ComponentType<{ className?: string }>; color: string;
}) {
  return (
    <div className="surface flex items-center gap-4 p-4">
      <div className={`grid h-10 w-10 shrink-0 place-items-center rounded-xl ${color}`}>
        <Icon className="h-5 w-5" />
      </div>
      <div className="min-w-0">
        <p className="text-2xl font-bold tabular-nums text-slate-900 dark:text-white">{value}</p>
        <p className="mt-0.5 truncate text-xs text-slate-500 dark:text-slate-400">{label}</p>
        {sub && <p className="text-[10px] text-slate-400 dark:text-slate-500">{sub}</p>}
      </div>
    </div>
  );
}

function Modal({ title, onClose, children, wide }: { title: string; onClose: () => void; children: React.ReactNode; wide?: boolean }) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4">
      <div className={`relative w-full rounded-2xl bg-white shadow-2xl dark:bg-slate-800 ${wide ? 'max-w-3xl' : 'max-w-lg'} max-h-[90vh] overflow-y-auto`}>
        <div className="sticky top-0 flex items-center justify-between border-b border-slate-100 bg-white px-6 py-4 dark:border-white/10 dark:bg-slate-800">
          <h2 className="text-sm font-semibold text-slate-900 dark:text-white">{title}</h2>
          <button type="button" onClick={onClose} aria-label="Close" className="rounded-lg p-1 hover:bg-slate-100 dark:hover:bg-white/10">
            <X className="h-4 w-4 text-slate-500" />
          </button>
        </div>
        <div className="p-6">{children}</div>
      </div>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-300">{label}</label>
      {children}
    </div>
  );
}

const inp = 'w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-900 focus:border-sapphire focus:outline-none dark:border-white/10 dark:bg-white/5 dark:text-white';
const sel = `${inp} appearance-none`;
const btn = {
  primary: 'inline-flex items-center gap-1.5 rounded-lg bg-sapphire px-4 py-2 text-sm font-medium text-white hover:bg-sapphire/90 disabled:opacity-50',
  ghost: 'inline-flex items-center gap-1.5 rounded-lg border border-slate-200 px-4 py-2 text-sm font-medium text-slate-600 hover:bg-slate-50 dark:border-white/10 dark:text-slate-300 dark:hover:bg-white/5',
  danger: 'inline-flex items-center gap-1.5 rounded-lg bg-rose-600 px-4 py-2 text-sm font-medium text-white hover:bg-rose-700 disabled:opacity-50',
  sm: 'inline-flex items-center gap-1 rounded-lg border border-slate-200 px-3 py-1.5 text-xs font-medium text-slate-600 hover:bg-slate-50 dark:border-white/10 dark:text-slate-300 dark:hover:bg-white/5',
};

// ── Tabs ────────────────────────────────────────────────────────────────────────

type Tab = 'dashboard' | 'salary-structures' | 'employee-salary' | 'runs' | 'validation' | 'approvals' | 'payslips' | 'bank-wps' | 'payment-tracking' | 'reports' | 'ai-validation';

const TABS: { key: Tab; label: string; icon: React.ComponentType<{ className?: string }> }[] = [
  { key: 'dashboard', label: 'Dashboard', icon: BarChart2 },
  { key: 'salary-structures', label: 'Salary Structures', icon: Layers3 },
  { key: 'employee-salary', label: 'Employee Salary', icon: Users },
  { key: 'runs', label: 'Payroll Runs', icon: Play },
  { key: 'validation', label: 'Validation', icon: AlertTriangle },
  { key: 'approvals', label: 'Approvals', icon: CheckCircle2 },
  { key: 'payslips', label: 'Payslips', icon: FileText },
  { key: 'bank-wps', label: 'Bank / WPS Files', icon: Landmark },
  { key: 'payment-tracking', label: 'Payment Tracking', icon: TrendingUp },
  { key: 'reports', label: 'Reports', icon: BarChart2 },
  { key: 'ai-validation', label: 'AI Validation', icon: Bot },
];

// ── Dashboard Tab ───────────────────────────────────────────────────────────────

function DashboardTab({ onNavigate }: { onNavigate: (t: Tab) => void }) {
  const [summary, setSummary] = useState<PayrollSummary | null>(null);
  const [runs, setRuns] = useState<PayrollRun[]>([]);

  useEffect(() => {
    payrollApi.reportSummary().then(setSummary).catch(() => {});
    payrollApi.listRuns({ pageSize: 6 }).then(r => setRuns(r.items)).catch(() => {});
  }, []);

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        <KpiCard label="Total Payroll Runs" value={summary?.totalRuns ?? '—'} icon={FileText} color="bg-sapphire/10 text-sapphire dark:bg-sapphire/20" />
        <KpiCard label="Locked Runs" value={summary?.lockedRuns ?? '—'} icon={Lock} color="bg-emerald-100 text-emerald-600 dark:bg-emerald-500/20 dark:text-emerald-400" />
        <KpiCard label="Gross YTD" value={summary?.totalGrossYtd != null ? fmtAmt(summary.totalGrossYtd) : '—'} icon={WalletCards} color="bg-cyan-100 text-cyan-600 dark:bg-cyan-500/20 dark:text-cyan-400" />
        <KpiCard label="Net YTD" value={summary?.totalNetYtd != null ? fmtAmt(summary.totalNetYtd) : '—'} icon={TrendingUp} color="bg-amber-100 text-amber-600 dark:bg-amber-500/20 dark:text-amber-400" />
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <div className="surface p-5 lg:col-span-2">
          <div className="mb-4 flex items-center justify-between">
            <h3 className="text-sm font-semibold text-slate-800 dark:text-white">Recent Payroll Runs</h3>
            <button type="button" onClick={() => onNavigate('runs')} className="text-xs text-sapphire hover:underline dark:text-cyanAccent">View all</button>
          </div>
          {runs.length === 0 ? <p className="text-sm text-slate-400">No payroll runs yet.</p> : (
            <div className="divide-y divide-slate-100 dark:divide-white/5">
              {runs.map(r => (
                <div key={r.id} className="flex items-center justify-between py-3">
                  <div>
                    <p className="text-sm font-semibold text-slate-800 dark:text-white">{MONTHS[r.month - 1]} {r.year}</p>
                    <p className="text-xs text-slate-400">{r.employeeCount} employees · Net {fmtAmt(r.totalNetSalary)}</p>
                  </div>
                  <StatusBadge status={r.status} />
                </div>
              ))}
            </div>
          )}
        </div>

        <div className="surface p-5">
          <h3 className="mb-4 text-sm font-semibold text-slate-800 dark:text-white">Quick Actions</h3>
          <div className="space-y-2">
            {([
              ['New Payroll Run', 'runs', Plus],
              ['Salary Structures', 'salary-structures', Layers3],
              ['Employee Salary Setup', 'employee-salary', Users],
              ['Payroll Validation', 'validation', AlertTriangle],
              ['Payroll Approvals', 'approvals', CheckCircle2],
              ['AI Validation', 'ai-validation', Bot],
            ] as [string, Tab, React.ComponentType<{ className?: string }>][]).map(([label, t, Icon]) => (
              <button key={t} type="button" onClick={() => onNavigate(t)}
                className="flex w-full items-center gap-3 rounded-lg p-2.5 text-left hover:bg-slate-50 dark:hover:bg-white/5">
                <Icon className="h-4 w-4 shrink-0 text-sapphire dark:text-cyanAccent" />
                <span className="text-sm text-slate-700 dark:text-slate-300">{label}</span>
                <ChevronRight className="ml-auto h-3.5 w-3.5 text-slate-300" />
              </button>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

// ── Salary Structures Tab ───────────────────────────────────────────────────────

function CreateSalaryStructureModal({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({ code: '', name: '', currency: 'AED', effectiveDate: new Date().toISOString().slice(0, 10) });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const set = (k: keyof typeof form, v: string) => setForm(f => ({ ...f, [k]: v }));

  const save = async () => {
    if (!form.code || !form.name) { setError('Code and name are required.'); return; }
    setSaving(true); setError('');
    try { await payrollApi.createSalaryStructure({ ...form, components: [] }); onSaved(); } catch { setError('Save failed.'); setSaving(false); }
  };

  return (
    <Modal title="New Salary Structure" onClose={onClose}>
      <div className="space-y-4">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Code *"><input aria-label="Structure code" className={inp} value={form.code} onChange={e => set('code', e.target.value)} placeholder="e.g. GCC-STD" /></Field>
          <Field label="Name *"><input aria-label="Structure name" className={inp} value={form.name} onChange={e => set('name', e.target.value)} placeholder="e.g. GCC Standard" /></Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Currency">
            <select aria-label="Currency" className={sel} value={form.currency} onChange={e => set('currency', e.target.value)}>
              {['AED', 'SAR', 'QAR', 'KWD', 'BHD', 'OMR', 'USD', 'EUR', 'GBP', 'INR', 'PKR', 'PHP', 'EGP', 'JOD', 'LBP', 'ZAR', 'NGN', 'KES', 'SGD', 'AUD', 'CAD'].map(c => <option key={c}>{c}</option>)}
            </select>
          </Field>
          <Field label="Effective Date"><input type="date" aria-label="Effective date" className={inp} value={form.effectiveDate} onChange={e => set('effectiveDate', e.target.value)} /></Field>
        </div>
        {error && <p className="text-xs text-rose-500">{error}</p>}
        <div className="flex justify-end gap-2">
          <button type="button" className={btn.ghost} onClick={onClose}>Cancel</button>
          <button type="button" className={btn.primary} onClick={save} disabled={saving}>{saving ? 'Saving…' : 'Create Structure'}</button>
        </div>
      </div>
    </Modal>
  );
}

function SalaryStructuresTab() {
  const [structures, setStructures] = useState<SalaryStructure[]>([]);
  const [loading, setLoading] = useState(true);
  const [showCreate, setShowCreate] = useState(false);

  const load = () => { setLoading(true); payrollApi.listSalaryStructures().then(setStructures).catch(() => {}).finally(() => setLoading(false)); };
  useEffect(load, []);

  return (
    <div className="space-y-4">
      <div className="flex justify-end">
        <button type="button" className={btn.primary} onClick={() => setShowCreate(true)}><Plus className="h-4 w-4" /> New Structure</button>
      </div>
      {loading ? <p className="text-sm text-slate-400">Loading…</p> : structures.length === 0 ? (
        <div className="surface flex flex-col items-center py-16 text-center">
          <Layers3 className="mb-3 h-8 w-8 text-slate-300 dark:text-slate-600" />
          <p className="text-sm font-medium text-slate-600 dark:text-slate-400">No salary structures yet</p>
          <p className="text-xs text-slate-400">Create a salary structure to define components like Basic, Housing, Transport allowances.</p>
        </div>
      ) : (
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {structures.map(s => (
            <div key={s.id} className="surface p-4">
              <div className="mb-2 flex items-start justify-between gap-2">
                <div>
                  <p className="font-semibold text-slate-900 dark:text-white">{s.name}</p>
                  <p className="text-xs text-slate-400">{s.code}</p>
                </div>
                <span className={`rounded px-1.5 py-0.5 text-[10px] font-medium ${s.isActive ? 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400' : 'bg-slate-100 text-slate-500'}`}>{s.isActive ? 'Active' : 'Inactive'}</span>
              </div>
              <p className="mt-2 text-xs text-slate-500">Currency: <span className="font-medium text-slate-700 dark:text-slate-300">{s.currency}</span></p>
              <p className="text-xs text-slate-500">Effective: <span className="font-medium text-slate-700 dark:text-slate-300">{fmtDate(s.effectiveDate)}</span></p>
            </div>
          ))}
        </div>
      )}
      {showCreate && <CreateSalaryStructureModal onClose={() => setShowCreate(false)} onSaved={() => { setShowCreate(false); load(); }} />}
    </div>
  );
}

// ── Employee Salary Setup Tab ───────────────────────────────────────────────────

function AssignSalaryModal({ structures, onClose, onSaved }: { structures: SalaryStructure[]; onClose: () => void; onSaved: () => void }) {
  const today = new Date().toISOString().slice(0, 10);
  const [form, setForm] = useState({
    employeeId: '', salaryStructureId: '', basicSalary: '', housingAllowance: '',
    transportAllowance: '', foodAllowance: '', mobileAllowance: '', otherAllowance: '',
    fixedDeduction: '', effectiveDate: today, currency: 'AED',
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const set = (k: keyof typeof form, v: string) => setForm(f => ({ ...f, [k]: v }));

  const save = async () => {
    if (!form.employeeId || !form.salaryStructureId || !form.basicSalary) { setError('Employee ID, salary structure, and basic salary are required.'); return; }
    setSaving(true); setError('');
    try {
      await payrollApi.assignEmployeeSalary({
        employeeId: Number(form.employeeId), salaryStructureId: form.salaryStructureId,
        basicSalary: Number(form.basicSalary), housingAllowance: Number(form.housingAllowance) || 0,
        transportAllowance: Number(form.transportAllowance) || 0, foodAllowance: Number(form.foodAllowance) || 0,
        mobileAllowance: Number(form.mobileAllowance) || 0, otherAllowance: Number(form.otherAllowance) || 0,
        fixedDeduction: Number(form.fixedDeduction) || 0, effectiveDate: form.effectiveDate, currency: form.currency,
      });
      onSaved();
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: { message?: string } } })?.response?.data?.message;
      setError(msg ?? 'Save failed.'); setSaving(false);
    }
  };

  const gross = ['basicSalary', 'housingAllowance', 'transportAllowance', 'foodAllowance', 'mobileAllowance', 'otherAllowance']
    .reduce((sum, k) => sum + (Number(form[k as keyof typeof form]) || 0), 0);

  return (
    <Modal title="Assign Salary to Employee" onClose={onClose} wide>
      <div className="space-y-4">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Employee ID *"><input type="number" aria-label="Employee ID" className={inp} value={form.employeeId} onChange={e => set('employeeId', e.target.value)} /></Field>
          <Field label="Salary Structure *">
            <select aria-label="Salary structure" className={sel} value={form.salaryStructureId} onChange={e => set('salaryStructureId', e.target.value)}>
              <option value="">Select…</option>
              {structures.map(s => <option key={s.id} value={s.id}>{s.name} ({s.currency})</option>)}
            </select>
          </Field>
        </div>

        <div className="rounded-lg border border-slate-200 p-4 dark:border-white/10">
          <p className="mb-3 text-xs font-semibold uppercase tracking-wide text-slate-500">Earnings</p>
          <div className="grid grid-cols-3 gap-3">
            {[
              ['basicSalary', 'Basic Salary *'], ['housingAllowance', 'Housing Allowance'],
              ['transportAllowance', 'Transport Allowance'], ['foodAllowance', 'Food Allowance'],
              ['mobileAllowance', 'Mobile Allowance'], ['otherAllowance', 'Other Allowances'],
            ].map(([k, label]) => (
              <Field key={k} label={label}><input type="number" step="0.01" aria-label={label} className={inp} value={form[k as keyof typeof form]} onChange={e => set(k as keyof typeof form, e.target.value)} /></Field>
            ))}
          </div>
          <div className="mt-3 rounded-lg bg-slate-50 px-4 py-2.5 dark:bg-white/5">
            <p className="text-sm font-semibold text-sapphire dark:text-cyanAccent">Gross Total: {form.currency} {gross.toLocaleString('en-AE', { minimumFractionDigits: 2 })}</p>
          </div>
        </div>

        <div className="grid grid-cols-3 gap-3">
          <Field label="Fixed Deduction"><input type="number" step="0.01" aria-label="Fixed deduction" className={inp} value={form.fixedDeduction} onChange={e => set('fixedDeduction', e.target.value)} /></Field>
          <Field label="Effective Date"><input type="date" aria-label="Effective date" className={inp} value={form.effectiveDate} onChange={e => set('effectiveDate', e.target.value)} /></Field>
          <Field label="Currency">
            <select aria-label="Currency" className={sel} value={form.currency} onChange={e => set('currency', e.target.value)}>
              {['AED', 'SAR', 'QAR', 'KWD', 'BHD', 'OMR', 'USD', 'EUR', 'GBP', 'INR', 'PKR', 'PHP', 'EGP', 'JOD', 'LBP', 'ZAR', 'NGN', 'KES', 'SGD', 'AUD', 'CAD'].map(c => <option key={c}>{c}</option>)}
            </select>
          </Field>
        </div>

        {error && <p className="text-xs text-rose-500">{error}</p>}
        <div className="flex justify-end gap-2 pt-2">
          <button type="button" className={btn.ghost} onClick={onClose}>Cancel</button>
          <button type="button" className={btn.primary} onClick={save} disabled={saving}>{saving ? 'Saving…' : 'Assign Salary'}</button>
        </div>
      </div>
    </Modal>
  );
}

function EmployeeSalaryTab() {
  const [assignments, setAssignments] = useState<EmployeeSalaryStructure[]>([]);
  const [structures, setStructures] = useState<SalaryStructure[]>([]);
  const [loading, setLoading] = useState(true);
  const [empIdFilter, setEmpIdFilter] = useState('');
  const [showAssign, setShowAssign] = useState(false);

  const load = () => {
    setLoading(true);
    Promise.all([
      payrollApi.listEmployeeSalaryStructures(empIdFilter ? Number(empIdFilter) : undefined).then(setAssignments),
      payrollApi.listSalaryStructures().then(setStructures),
    ]).finally(() => setLoading(false));
  };
  useEffect(load, []);

  const structName = (id: string) => structures.find(s => s.id === id)?.name ?? id.slice(0, 8);

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-3">
        <input type="number" aria-label="Employee ID filter" placeholder="Filter by Employee ID" className={`${inp} w-52`} value={empIdFilter} onChange={e => setEmpIdFilter(e.target.value)} />
        <button type="button" className={btn.ghost} onClick={load}>Search</button>
        <button type="button" className={`ml-auto ${btn.primary}`} onClick={() => setShowAssign(true)}><Plus className="h-4 w-4" /> Assign Salary</button>
      </div>
      {loading ? <p className="text-sm text-slate-400">Loading…</p> : assignments.length === 0 ? (
        <div className="surface flex flex-col items-center py-16 text-center">
          <Users className="mb-3 h-8 w-8 text-slate-300 dark:text-slate-600" />
          <p className="text-sm font-medium text-slate-600 dark:text-slate-400">No salary assignments found</p>
        </div>
      ) : (
        <div className="surface overflow-hidden">
          <table className="w-full min-w-[640px] text-sm">
            <thead>
              <tr className="border-b border-slate-100 dark:border-white/[0.07]">
                {['Employee', 'Structure', 'Basic', 'Housing', 'Transport', 'Gross', 'Deduction', 'Effective', 'Status'].map(h => (
                  <th key={h} className="px-3 py-2 text-left text-xs font-bold uppercase text-slate-400">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 dark:divide-white/[0.05]">
              {assignments.map(a => {
                const gross = a.basicSalary + a.housingAllowance + a.transportAllowance + a.foodAllowance + a.mobileAllowance + a.otherAllowance;
                return (
                  <tr key={a.id} className="hover:bg-slate-50 dark:hover:bg-white/[0.03]">
                    <td className="px-3 py-2 font-medium text-slate-900 dark:text-white">Emp #{a.employeeId}</td>
                    <td className="px-3 py-2 text-slate-500">{structName(a.salaryStructureId)}</td>
                    <td className="px-3 py-2 text-right text-slate-700 dark:text-slate-300">{a.basicSalary.toLocaleString()}</td>
                    <td className="px-3 py-2 text-right text-slate-700 dark:text-slate-300">{a.housingAllowance.toLocaleString()}</td>
                    <td className="px-3 py-2 text-right text-slate-700 dark:text-slate-300">{a.transportAllowance.toLocaleString()}</td>
                    <td className="px-3 py-2 text-right font-semibold text-slate-900 dark:text-white">{gross.toLocaleString()}</td>
                    <td className="px-3 py-2 text-right text-rose-500">{a.fixedDeduction > 0 ? `(${a.fixedDeduction.toLocaleString()})` : '—'}</td>
                    <td className="px-3 py-2 text-xs text-slate-400">{fmtDate(a.effectiveDate)}</td>
                    <td className="px-3 py-2"><span className={`rounded px-1.5 py-0.5 text-[10px] font-medium ${a.isActive ? 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400' : 'bg-slate-100 text-slate-500'}`}>{a.isActive ? 'Active' : 'Inactive'}</span></td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
      {showAssign && <AssignSalaryModal structures={structures} onClose={() => setShowAssign(false)} onSaved={() => { setShowAssign(false); load(); }} />}
    </div>
  );
}

// ── Payroll Runs Tab ────────────────────────────────────────────────────────────

function RunsTab({ onSelectRun }: { onSelectRun: (run: PayrollRun, tab: Tab) => void }) {
  const [runs, setRuns] = useState<PayrollRun[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [selectedRun, setSelectedRun] = useState<PayrollRun | null>(null);
  const [slips, setSlips] = useState<PayrollSlip[]>([]);
  const [slipsLoading, setSlipsLoading] = useState(false);
  const [showCreate, setShowCreate] = useState(false);
  const [createYear, setCreateYear] = useState(new Date().getFullYear());
  const [createMonth, setCreateMonth] = useState(new Date().getMonth() + 1);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const load = () => {
    setLoading(true);
    payrollApi.listRuns({ pageSize: 50 }).then(r => { setRuns(r.items); setTotal(r.total); }).catch(() => {}).finally(() => setLoading(false));
  };
  useEffect(load, []);

  const openSlips = async (run: PayrollRun) => {
    setSelectedRun(run);
    setSlipsLoading(true);
    payrollApi.slips(run.id, { pageSize: 200 }).then(r => setSlips(r.items)).catch(() => {}).finally(() => setSlipsLoading(false));
  };

  const createRun = async () => {
    setSaving(true); setError('');
    try { await payrollApi.createRun(createYear, createMonth); setShowCreate(false); load(); } catch (e: unknown) {
      const msg = (e as { response?: { data?: { message?: string } } })?.response?.data?.message;
      setError(msg ?? 'Failed to create payroll run.');
    } finally { setSaving(false); }
  };

  const processRun = async (id: string) => {
    const updated = await payrollApi.processRun(id).catch(() => null);
    if (updated) { setRuns(rs => rs.map(r => r.id === id ? updated : r)); if (selectedRun?.id === id) { setSelectedRun(updated); openSlips(updated); } }
  };

  const lockRun = async (id: string) => {
    const updated = await payrollApi.lockRun(id).catch(() => null);
    if (updated) { setRuns(rs => rs.map(r => r.id === id ? updated : r)); if (selectedRun?.id === id) setSelectedRun(updated); }
  };

  const fmt = (n: number) => n.toLocaleString('en-AE', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <p className="text-sm text-slate-500">{total} payroll run{total !== 1 ? 's' : ''}</p>
        <button type="button" className={btn.primary} onClick={() => { setShowCreate(true); setError(''); }}><Plus className="h-4 w-4" /> New Payroll Run</button>
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <div className="surface overflow-hidden">
          <div className="border-b border-slate-100 px-4 py-3 dark:border-white/[0.07]">
            <p className="text-sm font-semibold text-slate-900 dark:text-white">Payroll Runs</p>
          </div>
          {loading && <div className="flex justify-center py-10"><div className="h-6 w-6 animate-spin rounded-full border-2 border-sapphire border-t-transparent" /></div>}
          {!loading && runs.length === 0 && <p className="py-10 text-center text-sm text-slate-400">No runs yet. Create one above.</p>}
          <div className="divide-y divide-slate-100 dark:divide-white/[0.05]">
            {runs.map(run => (
              <button key={run.id} type="button" onClick={() => openSlips(run)}
                className={`w-full px-4 py-3 text-left transition hover:bg-slate-50 dark:hover:bg-white/[0.03] ${selectedRun?.id === run.id ? 'bg-sapphire/5 dark:bg-sapphire/10' : ''}`}>
                <div className="flex items-center justify-between">
                  <p className="font-semibold text-slate-900 dark:text-white">{MONTHS[run.month - 1]} {run.year}</p>
                  <StatusBadge status={run.status} />
                </div>
                <p className="mt-1 text-xs text-slate-500">{run.employeeCount} employees · Net {fmt(run.totalNetSalary)}</p>
                {run.status !== 'Locked' && (
                  <div className="mt-2 flex gap-1">
                    <button type="button" onClick={e => { e.stopPropagation(); processRun(run.id); }} className={`${btn.sm} h-6 px-2 text-xs`}>
                      <Play className="h-3 w-3" /> Process
                    </button>
                    {run.status === 'Processed' && (
                      <button type="button" onClick={e => { e.stopPropagation(); onSelectRun(run, 'approvals'); }} className={`${btn.sm} h-6 px-2 text-xs`}>
                        <CheckCircle2 className="h-3 w-3" /> Approve
                      </button>
                    )}
                    {run.status === 'Approved' && (
                      <button type="button" onClick={e => { e.stopPropagation(); lockRun(run.id); }} className={`${btn.sm} h-6 px-2 text-xs`}>
                        <Lock className="h-3 w-3" /> Lock
                      </button>
                    )}
                    <button type="button" onClick={e => { e.stopPropagation(); onSelectRun(run, 'validation'); }} className={`${btn.sm} h-6 px-2 text-xs`}>
                      <AlertTriangle className="h-3 w-3" /> Validate
                    </button>
                  </div>
                )}
              </button>
            ))}
          </div>
        </div>

        <div className="surface overflow-hidden lg:col-span-2">
          {!selectedRun ? (
            <div className="flex flex-col items-center justify-center py-24 text-center">
              <FileText className="mb-3 h-10 w-10 text-slate-200 dark:text-slate-700" />
              <p className="text-sm text-slate-400">Select a payroll run to view salary slips</p>
            </div>
          ) : (
            <>
              <div className="flex items-center justify-between border-b border-slate-100 px-4 py-3 dark:border-white/[0.07]">
                <p className="text-sm font-semibold text-slate-900 dark:text-white">Slips — {MONTHS[selectedRun.month - 1]} {selectedRun.year}</p>
                <div className="flex items-center gap-3 text-xs text-slate-500">
                  <span>Gross: <span className="font-semibold text-slate-900 dark:text-white">{fmt(selectedRun.totalGrossSalary)}</span></span>
                  <span>Net: <span className="font-bold text-emerald-600 dark:text-emerald-400">{fmt(selectedRun.totalNetSalary)}</span></span>
                </div>
              </div>
              {slipsLoading ? <div className="flex justify-center py-12"><div className="h-6 w-6 animate-spin rounded-full border-2 border-sapphire border-t-transparent" /></div> : (
                <div className="overflow-x-auto">
                  <table className="w-full min-w-[560px] text-sm">
                    <thead>
                      <tr className="border-b border-slate-100 dark:border-white/[0.07]">
                        {['Employee', 'Dept', 'Basic', 'Gross', 'Deductions', 'Net'].map(h => (
                          <th key={h} className="px-3 py-3 text-left text-xs font-bold uppercase text-slate-400">{h}</th>
                        ))}
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-100 dark:divide-white/[0.05]">
                      {slips.length === 0 && <tr><td colSpan={6} className="py-10 text-center text-sm text-slate-400">No slips. Click Process to generate.</td></tr>}
                      {slips.map(s => (
                        <tr key={s.id} className="hover:bg-slate-50 dark:hover:bg-white/[0.03]">
                          <td className="px-3 py-2.5">
                            <p className="font-medium text-slate-900 dark:text-white">{s.employeeName}</p>
                            <p className="text-xs text-slate-400">{s.employeeCode}</p>
                          </td>
                          <td className="px-3 py-2.5 text-xs text-slate-500">{s.department || '—'}</td>
                          <td className="px-3 py-2.5 text-right text-slate-700 dark:text-slate-300">{fmt(s.basicSalary)}</td>
                          <td className="px-3 py-2.5 text-right font-semibold text-slate-900 dark:text-white">{fmt(s.grossSalary)}</td>
                          <td className="px-3 py-2.5 text-right text-rose-500">({fmt(s.deductions)})</td>
                          <td className="px-3 py-2.5 text-right font-bold text-emerald-600 dark:text-emerald-400">{fmt(s.netSalary)}</td>
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

      {showCreate && (
        <Modal title="New Payroll Run" onClose={() => setShowCreate(false)}>
          <div className="space-y-4">
            {error && <p className="rounded-lg bg-rose-50 px-3 py-2 text-sm text-rose-600 dark:bg-rose-500/10 dark:text-rose-400">{error}</p>}
            <div className="grid grid-cols-2 gap-3">
              <Field label="Year">
                <input type="number" aria-label="Year" className={inp} value={createYear} onChange={e => setCreateYear(Number(e.target.value))} min={2020} max={2035} />
              </Field>
              <Field label="Month">
                <select aria-label="Month" className={sel} value={createMonth} onChange={e => setCreateMonth(Number(e.target.value))}>
                  {MONTHS.map((m, i) => <option key={m} value={i + 1}>{m}</option>)}
                </select>
              </Field>
            </div>
            <div className="flex justify-end gap-2">
              <button type="button" className={btn.ghost} onClick={() => setShowCreate(false)}>Cancel</button>
              <button type="button" className={btn.primary} onClick={createRun} disabled={saving}>{saving ? 'Creating…' : 'Create Run'}</button>
            </div>
          </div>
        </Modal>
      )}
    </div>
  );
}

// ── Validation Tab ──────────────────────────────────────────────────────────────

function ValidationTab({ selectedRunId }: { selectedRunId?: string }) {
  const [runId, setRunId] = useState(selectedRunId ?? '');
  const [runs, setRuns] = useState<PayrollRun[]>([]);
  const [results, setResults] = useState<PayrollValidationResult[]>([]);
  const [loading, setLoading] = useState(false);

  useEffect(() => { payrollApi.listRuns({ pageSize: 50 }).then(r => setRuns(r.items)).catch(() => {}); }, []);

  const validate = async () => {
    if (!runId) return;
    setLoading(true);
    payrollApi.validateRun(runId).then(r => {
      setResults(Array.isArray(r) ? r : (r as { warnings?: PayrollValidationResult[] }).warnings ?? []);
    }).catch(() => {}).finally(() => setLoading(false));
  };

  useEffect(() => { if (selectedRunId) { setRunId(selectedRunId); } }, [selectedRunId]);

  const warnings = results.filter(r => r.severity === 'Warning');
  const errors = results.filter(r => r.severity === 'Error');

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-3">
        <select aria-label="Select payroll run" className={`${sel} flex-1 max-w-xs`} value={runId} onChange={e => setRunId(e.target.value)}>
          <option value="">Select a payroll run…</option>
          {runs.map(r => <option key={r.id} value={r.id}>{MONTHS[r.month - 1]} {r.year} — {r.status}</option>)}
        </select>
        <button type="button" className={btn.primary} onClick={validate} disabled={!runId || loading}>
          {loading ? 'Validating…' : 'Run Validation'}
        </button>
      </div>

      {results.length > 0 && (
        <div className="grid grid-cols-3 gap-4">
          <KpiCard label="Total Issues" value={results.length} icon={AlertTriangle} color="bg-amber-100 text-amber-600 dark:bg-amber-500/20 dark:text-amber-400" />
          <KpiCard label="Warnings" value={warnings.length} icon={AlertTriangle} color="bg-amber-100 text-amber-600 dark:bg-amber-500/20 dark:text-amber-400" />
          <KpiCard label="Errors" value={errors.length} icon={AlertTriangle} color="bg-rose-100 text-rose-600 dark:bg-rose-500/20 dark:text-rose-400" />
        </div>
      )}

      {results.length === 0 && !loading ? (
        <div className="surface flex flex-col items-center py-16 text-center">
          <CheckCircle2 className="mb-3 h-8 w-8 text-slate-300 dark:text-slate-600" />
          <p className="text-sm font-medium text-slate-600 dark:text-slate-400">
            {runId ? 'No validation results — click "Run Validation" to check' : 'Select a run and click validate'}
          </p>
        </div>
      ) : (
        <div className="surface divide-y divide-slate-100 dark:divide-white/5">
          {results.map(r => (
            <div key={r.id} className="flex items-start gap-3 px-5 py-4">
              <div className={`mt-0.5 h-2 w-2 shrink-0 rounded-full ${r.severity === 'Error' ? 'bg-rose-500' : r.severity === 'Warning' ? 'bg-amber-500' : 'bg-sky-400'}`} />
              <div className="min-w-0">
                <div className="flex items-center gap-2">
                  <StatusBadge status={r.severity} />
                  <span className="font-mono text-xs text-slate-500">{r.code}</span>
                  {r.employeeId && <span className="text-xs text-slate-400">Emp #{r.employeeId}</span>}
                </div>
                <p className="mt-1 text-sm text-slate-700 dark:text-slate-300">{r.message}</p>
              </div>
              {r.isResolved && <span className="ml-auto shrink-0 rounded bg-emerald-50 px-1.5 py-0.5 text-[10px] font-medium text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400">Resolved</span>}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

// ── Approvals Tab ───────────────────────────────────────────────────────────────

function ApprovalsTab({ selectedRunId }: { selectedRunId?: string }) {
  const [runs, setRuns] = useState<PayrollRun[]>([]);
  const [runId, setRunId] = useState(selectedRunId ?? '');
  const [approvals, setApprovals] = useState<PayrollApproval[]>([]);
  const [notes, setNotes] = useState('');
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    payrollApi.listRuns({ status: 'Processed', pageSize: 50 }).then(r => setRuns(r.items)).catch(() => {});
  }, []);

  useEffect(() => {
    if (!runId) return;
    payrollApi.runApprovals(runId).then(setApprovals).catch(() => {});
  }, [runId]);

  useEffect(() => { if (selectedRunId) setRunId(selectedRunId); }, [selectedRunId]);

  const approve = async () => {
    if (!runId) return;
    setSaving(true);
    await payrollApi.approveRun(runId, notes).catch(() => {});
    payrollApi.runApprovals(runId).then(setApprovals).catch(() => {});
    setSaving(false);
  };

  const selectedRun = runs.find(r => r.id === runId);

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-3">
        <select aria-label="Select payroll run to approve" className={`${sel} flex-1 max-w-xs`} value={runId} onChange={e => setRunId(e.target.value)}>
          <option value="">Select run to approve…</option>
          {runs.map(r => <option key={r.id} value={r.id}>{MONTHS[r.month - 1]} {r.year} — {r.status}</option>)}
        </select>
      </div>

      {selectedRun && (
        <div className="surface p-5">
          <div className="mb-4 flex items-start justify-between gap-4">
            <div>
              <p className="text-sm font-semibold text-slate-900 dark:text-white">Payroll Run — {MONTHS[selectedRun.month - 1]} {selectedRun.year}</p>
              <p className="text-xs text-slate-400">{selectedRun.employeeCount} employees · Gross {fmtAmt(selectedRun.totalGrossSalary)} · Net {fmtAmt(selectedRun.totalNetSalary)}</p>
            </div>
            <StatusBadge status={selectedRun.status} />
          </div>
          {selectedRun.status === 'Processed' && (
            <div className="space-y-3">
              <Field label="Approval Notes">
                <textarea aria-label="Approval notes" className={inp} rows={3} value={notes} onChange={e => setNotes(e.target.value)} placeholder="Optional comments…" />
              </Field>
              <div className="flex gap-2">
                <button type="button" className={btn.primary} onClick={approve} disabled={saving}>{saving ? 'Approving…' : 'Approve Payroll Run'}</button>
              </div>
            </div>
          )}
          {selectedRun.status !== 'Processed' && (
            <p className="text-sm text-slate-400">
              {selectedRun.status === 'Approved' ? 'Already approved.' : selectedRun.status === 'Locked' ? 'Run is locked and finalised.' : 'Run must be in Processed status to approve.'}
            </p>
          )}
        </div>
      )}

      {approvals.length > 0 && (
        <div className="surface divide-y divide-slate-100 dark:divide-white/5">
          <div className="px-5 py-3"><p className="text-sm font-semibold text-slate-800 dark:text-white">Approval History</p></div>
          {approvals.map(a => (
            <div key={a.id} className="flex items-start gap-3 px-5 py-4">
              <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-emerald-500" />
              <div className="min-w-0">
                <div className="flex items-center gap-2">
                  <StatusBadge status={a.decision} />
                  <span className="text-xs text-slate-400">{a.approvalLevel}</span>
                </div>
                {a.notes && <p className="mt-1 text-xs text-slate-500">{a.notes}</p>}
                <p className="mt-1 text-xs text-slate-400">{fmtDate(a.decidedAtUtc)}</p>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

// ── Payslips Tab ────────────────────────────────────────────────────────────────

function PayslipsTab() {
  const [runs, setRuns] = useState<PayrollRun[]>([]);
  const [runId, setRunId] = useState('');
  const [payslips, setPayslips] = useState<Payslip[]>([]);
  const [loading, setLoading] = useState(false);
  const [generating, setGenerating] = useState(false);

  useEffect(() => { payrollApi.listRuns({ pageSize: 50 }).then(r => setRuns(r.items)).catch(() => {}); }, []);

  const loadPayslips = () => {
    if (!runId) return;
    setLoading(true);
    payrollApi.listPayslips(runId, { pageSize: 200 }).then(r => setPayslips(r.items)).catch(() => {}).finally(() => setLoading(false));
  };
  useEffect(() => { if (runId) loadPayslips(); }, [runId]);

  const generate = async () => {
    if (!runId) return;
    setGenerating(true);
    await payrollApi.generatePayslips(runId).catch(() => {});
    loadPayslips();
    setGenerating(false);
  };

  const published = payslips.filter(p => p.isPublishedToEss).length;

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-3">
        <select aria-label="Select payroll run" className={`${sel} flex-1 max-w-xs`} value={runId} onChange={e => setRunId(e.target.value)}>
          <option value="">Select a payroll run…</option>
          {runs.map(r => <option key={r.id} value={r.id}>{MONTHS[r.month - 1]} {r.year} — {r.status}</option>)}
        </select>
        {runId && (
          <button type="button" className={btn.primary} onClick={generate} disabled={generating}>
            {generating ? 'Generating…' : 'Generate Payslips'}
          </button>
        )}
      </div>

      {payslips.length > 0 && (
        <div className="grid grid-cols-3 gap-4">
          <KpiCard label="Total Payslips" value={payslips.length} icon={FileText} color="bg-sapphire/10 text-sapphire dark:bg-sapphire/20" />
          <KpiCard label="Published to ESS" value={published} icon={CheckCircle2} color="bg-emerald-100 text-emerald-600 dark:bg-emerald-500/20 dark:text-emerald-400" />
          <KpiCard label="Pending Publish" value={payslips.length - published} icon={AlertTriangle} color="bg-amber-100 text-amber-600 dark:bg-amber-500/20 dark:text-amber-400" />
        </div>
      )}

      {loading ? <p className="text-sm text-slate-400">Loading…</p> : payslips.length === 0 && runId ? (
        <div className="surface flex flex-col items-center py-16 text-center">
          <FileText className="mb-3 h-8 w-8 text-slate-300 dark:text-slate-600" />
          <p className="text-sm font-medium text-slate-600 dark:text-slate-400">No payslips yet — click Generate to create payslips for this run</p>
        </div>
      ) : (
        <div className="surface overflow-hidden">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-100 dark:border-white/[0.07]">
                {['Employee', 'Payslip #', 'Language', 'ESS Published', 'Published At', 'Generated'].map(h => (
                  <th key={h} className="px-4 py-2 text-left text-xs font-bold uppercase text-slate-400">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 dark:divide-white/[0.05]">
              {payslips.map(p => (
                <tr key={p.id} className="hover:bg-slate-50 dark:hover:bg-white/[0.03]">
                  <td className="px-4 py-2 font-medium text-slate-900 dark:text-white">Emp #{p.employeeId}</td>
                  <td className="px-4 py-2 font-mono text-xs text-slate-500">{p.payslipNumber}</td>
                  <td className="px-4 py-2 text-slate-500">{p.language.toUpperCase()}</td>
                  <td className="px-4 py-2">
                    <span className={`rounded px-1.5 py-0.5 text-[10px] font-medium ${p.isPublishedToEss ? 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400' : 'bg-slate-100 text-slate-500 dark:bg-white/10'}`}>
                      {p.isPublishedToEss ? 'Published' : 'Not published'}
                    </span>
                  </td>
                  <td className="px-4 py-2 text-xs text-slate-400">{p.publishedAtUtc ? fmtDate(p.publishedAtUtc) : '—'}</td>
                  <td className="px-4 py-2 text-xs text-slate-400">{fmtDate(p.createdAtUtc)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

// ── Bank / WPS Files Tab ────────────────────────────────────────────────────────

function BankWpsTab() {
  const [runs, setRuns] = useState<PayrollRun[]>([]);
  const [runId, setRunId] = useState('');
  const [batches, setBatches] = useState<PayrollPaymentBatch[]>([]);
  const [selectedBatch, setSelectedBatch] = useState<PayrollPaymentBatch | null>(null);
  const [records, setRecords] = useState<PayrollPaymentRecord[]>([]);
  const [creating, setCreating] = useState(false);
  const [paymentMethod, setPaymentMethod] = useState('WPS');

  useEffect(() => { payrollApi.listRuns({ pageSize: 50 }).then(r => setRuns(r.items)).catch(() => {}); }, []);

  useEffect(() => {
    if (!runId) { setBatches([]); return; }
    payrollApi.listPaymentBatches(runId).then(setBatches).catch(() => {});
  }, [runId]);

  const openBatch = (batch: PayrollPaymentBatch) => {
    setSelectedBatch(batch);
    payrollApi.paymentRecords(batch.id).then(setRecords).catch(() => {});
  };

  const createBatch = async () => {
    if (!runId) return;
    setCreating(true);
    const batch = await payrollApi.createPaymentBatch(runId, paymentMethod).catch(() => null);
    if (batch) { setBatches(b => [batch, ...b]); setSelectedBatch(batch); payrollApi.paymentRecords(batch.id).then(setRecords).catch(() => {}); }
    setCreating(false);
  };

  const generateWps = async (batchId: string) => {
    await payrollApi.generateWpsFile(batchId).catch(() => alert('WPS generation failed.'));
    payrollApi.listPaymentBatches(runId).then(setBatches).catch(() => {});
  };

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <select aria-label="Select payroll run" className={`${sel} max-w-xs flex-1`} value={runId} onChange={e => setRunId(e.target.value)}>
          <option value="">Select payroll run…</option>
          {runs.map(r => <option key={r.id} value={r.id}>{MONTHS[r.month - 1]} {r.year} — {r.status}</option>)}
        </select>
        {runId && (
          <>
            <select aria-label="Payment method" className={`${sel} w-40`} value={paymentMethod} onChange={e => setPaymentMethod(e.target.value)}>
              {['WPS', 'BankTransfer', 'Cash'].map(m => <option key={m}>{m}</option>)}
            </select>
            <button type="button" className={btn.primary} onClick={createBatch} disabled={creating}>
              {creating ? 'Creating…' : 'Create Payment Batch'}
            </button>
          </>
        )}
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <div className="surface overflow-hidden">
          <div className="border-b border-slate-100 px-4 py-3 dark:border-white/5">
            <p className="text-sm font-semibold text-slate-800 dark:text-white">Payment Batches</p>
          </div>
          {batches.length === 0 ? <p className="p-5 text-sm text-slate-400">No batches yet.</p> : (
            <div className="divide-y divide-slate-100 dark:divide-white/5">
              {batches.map(b => (
                <button key={b.id} type="button" onClick={() => openBatch(b)}
                  className={`w-full px-4 py-3 text-left hover:bg-slate-50 dark:hover:bg-white/5 ${selectedBatch?.id === b.id ? 'bg-sapphire/5 dark:bg-sapphire/10' : ''}`}>
                  <div className="flex items-center justify-between">
                    <p className="text-sm font-medium text-slate-800 dark:text-white">{b.batchNumber}</p>
                    <StatusBadge status={b.status} />
                  </div>
                  <p className="mt-1 text-xs text-slate-400">{b.paymentMethod} · {fmtAmt(b.totalAmount, b.currency)}</p>
                  {b.status !== 'FileGenerated' && (
                    <button type="button" onClick={e => { e.stopPropagation(); generateWps(b.id); }} className={`mt-1.5 ${btn.sm} h-6 px-2 text-xs`}>
                      Generate WPS/SIF
                    </button>
                  )}
                </button>
              ))}
            </div>
          )}
        </div>

        <div className="surface overflow-hidden lg:col-span-2">
          {!selectedBatch ? (
            <div className="flex flex-col items-center justify-center py-20 text-center">
              <Landmark className="mb-3 h-8 w-8 text-slate-300 dark:text-slate-600" />
              <p className="text-sm text-slate-400">Select a batch to view payment records</p>
            </div>
          ) : (
            <>
              <div className="border-b border-slate-100 px-4 py-3 dark:border-white/5">
                <p className="text-sm font-semibold text-slate-800 dark:text-white">Records — {selectedBatch.batchNumber}</p>
              </div>
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-slate-100 dark:border-white/[0.07]">
                    {['Employee', 'Amount', 'IBAN', 'WPS Reference', 'Status'].map(h => (
                      <th key={h} className="px-4 py-2 text-left text-xs font-bold uppercase text-slate-400">{h}</th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100 dark:divide-white/[0.05]">
                  {records.length === 0 && <tr><td colSpan={5} className="py-8 text-center text-sm text-slate-400">No records.</td></tr>}
                  {records.map(r => (
                    <tr key={r.id} className="hover:bg-slate-50 dark:hover:bg-white/[0.03]">
                      <td className="px-4 py-2 font-medium text-slate-900 dark:text-white">Emp #{r.employeeId}</td>
                      <td className="px-4 py-2 font-semibold text-slate-700 dark:text-slate-300">{r.amount.toLocaleString('en-AE', { minimumFractionDigits: 2 })}</td>
                      <td className="px-4 py-2 font-mono text-xs text-slate-500">{r.iban || <span className="text-rose-500">Missing IBAN</span>}</td>
                      <td className="px-4 py-2 text-xs text-slate-400">{r.wpsReference}</td>
                      <td className="px-4 py-2"><StatusBadge status={r.status} /></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}
        </div>
      </div>
    </div>
  );
}

// ── Payment Tracking Tab ────────────────────────────────────────────────────────

function PaymentTrackingTab() {
  const [batches, setBatches] = useState<PayrollPaymentBatch[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    setLoading(true);
    payrollApi.listPaymentBatches().then(setBatches).catch(() => {}).finally(() => setLoading(false));
  }, []);

  const total = batches.reduce((sum, b) => sum + b.totalAmount, 0);
  const fileGenerated = batches.filter(b => b.status === 'FileGenerated').length;

  return (
    <div className="space-y-4">
      {batches.length > 0 && (
        <div className="grid grid-cols-3 gap-4">
          <KpiCard label="Total Batches" value={batches.length} icon={WalletCards} color="bg-sapphire/10 text-sapphire dark:bg-sapphire/20" />
          <KpiCard label="WPS Files Generated" value={fileGenerated} icon={CheckCircle2} color="bg-emerald-100 text-emerald-600 dark:bg-emerald-500/20 dark:text-emerald-400" />
          <KpiCard label="Total Amount" value={fmtAmt(total)} icon={TrendingUp} color="bg-cyan-100 text-cyan-600 dark:bg-cyan-500/20 dark:text-cyan-400" />
        </div>
      )}
      {loading ? <p className="text-sm text-slate-400">Loading…</p> : batches.length === 0 ? (
        <div className="surface flex flex-col items-center py-16 text-center">
          <TrendingUp className="mb-3 h-8 w-8 text-slate-300 dark:text-slate-600" />
          <p className="text-sm font-medium text-slate-600 dark:text-slate-400">No payment batches yet</p>
          <p className="text-xs text-slate-400">Create payment batches from the Bank/WPS Files tab after processing a payroll run.</p>
        </div>
      ) : (
        <div className="surface overflow-hidden">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-100 dark:border-white/[0.07]">
                {['Batch', 'Method', 'Total Amount', 'Currency', 'Status', 'Created'].map(h => (
                  <th key={h} className="px-4 py-2 text-left text-xs font-bold uppercase text-slate-400">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 dark:divide-white/[0.05]">
              {batches.map(b => (
                <tr key={b.id} className="hover:bg-slate-50 dark:hover:bg-white/[0.03]">
                  <td className="px-4 py-2 font-mono text-xs font-medium text-slate-900 dark:text-white">{b.batchNumber}</td>
                  <td className="px-4 py-2 text-slate-500">{b.paymentMethod}</td>
                  <td className="px-4 py-2 font-semibold text-slate-700 dark:text-slate-300">{b.totalAmount.toLocaleString('en-AE', { minimumFractionDigits: 2 })}</td>
                  <td className="px-4 py-2 text-slate-500">{b.currency}</td>
                  <td className="px-4 py-2"><StatusBadge status={b.status} /></td>
                  <td className="px-4 py-2 text-xs text-slate-400">{fmtDate(b.createdAtUtc)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

// ── Reports Tab ─────────────────────────────────────────────────────────────────

function ReportsTab() {
  const [runs, setRuns] = useState<PayrollRun[]>([]);
  const [runId, setRunId] = useState('');
  const [slips, setSlips] = useState<PayrollSlip[]>([]);
  const [summary, setSummary] = useState<PayrollSummary | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    payrollApi.listRuns({ pageSize: 50 }).then(r => setRuns(r.items)).catch(() => {});
    payrollApi.reportSummary().then(setSummary).catch(() => {});
  }, []);

  const loadRegister = () => {
    if (!runId) return;
    setLoading(true);
    payrollApi.reportRegister(runId).then(setSlips).catch(() => {}).finally(() => setLoading(false));
  };

  const fmt = (n: number) => n.toLocaleString('en-AE', { minimumFractionDigits: 2 });

  return (
    <div className="space-y-6">
      {summary && (
        <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
          <KpiCard label="Total Runs" value={summary.totalRuns} icon={FileText} color="bg-sapphire/10 text-sapphire dark:bg-sapphire/20" />
          <KpiCard label="Locked Runs" value={summary.lockedRuns} icon={Lock} color="bg-emerald-100 text-emerald-600 dark:bg-emerald-500/20 dark:text-emerald-400" />
          <KpiCard label="Gross YTD" value={fmtAmt(summary.totalGrossYtd)} icon={WalletCards} color="bg-cyan-100 text-cyan-600 dark:bg-cyan-500/20 dark:text-cyan-400" />
          <KpiCard label="Net YTD" value={fmtAmt(summary.totalNetYtd)} icon={TrendingUp} color="bg-amber-100 text-amber-600 dark:bg-amber-500/20 dark:text-amber-400" />
        </div>
      )}

      <div className="surface p-5">
        <p className="mb-4 text-sm font-semibold text-slate-800 dark:text-white">Payroll Register</p>
        <div className="flex items-center gap-3">
          <select aria-label="Select payroll run for register" className={`${sel} max-w-xs flex-1`} value={runId} onChange={e => setRunId(e.target.value)}>
            <option value="">Select a payroll run…</option>
            {runs.map(r => <option key={r.id} value={r.id}>{MONTHS[r.month - 1]} {r.year} — {r.status}</option>)}
          </select>
          <button type="button" className={btn.primary} onClick={loadRegister} disabled={!runId || loading}>
            {loading ? 'Loading…' : 'Load Register'}
          </button>
        </div>

        {slips.length > 0 && (
          <>
            <div className="mt-4 rounded-lg bg-slate-50 px-4 py-3 dark:bg-white/5">
              <p className="text-sm text-slate-600 dark:text-slate-400">
                {slips.length} employees · Gross {fmtAmt(slips.reduce((s, x) => s + x.grossSalary, 0))} · Net {fmtAmt(slips.reduce((s, x) => s + x.netSalary, 0))}
              </p>
            </div>
            <div className="mt-4 overflow-x-auto">
              <table className="w-full min-w-[640px] text-sm">
                <thead>
                  <tr className="border-b border-slate-100 dark:border-white/[0.07]">
                    {['Code', 'Employee', 'Department', 'Basic', 'Housing', 'Transport', 'Other', 'Gross', 'Deductions', 'Net'].map(h => (
                      <th key={h} className="px-3 py-2 text-left text-xs font-bold uppercase text-slate-400">{h}</th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100 dark:divide-white/[0.05]">
                  {slips.map(s => (
                    <tr key={s.id} className="hover:bg-slate-50 dark:hover:bg-white/[0.03]">
                      <td className="px-3 py-2 font-mono text-xs text-slate-500">{s.employeeCode}</td>
                      <td className="px-3 py-2 font-medium text-slate-900 dark:text-white">{s.employeeName}</td>
                      <td className="px-3 py-2 text-xs text-slate-500">{s.department || '—'}</td>
                      <td className="px-3 py-2 text-right text-slate-700 dark:text-slate-300">{fmt(s.basicSalary)}</td>
                      <td className="px-3 py-2 text-right text-slate-700 dark:text-slate-300">{fmt(s.housingAllowance)}</td>
                      <td className="px-3 py-2 text-right text-slate-700 dark:text-slate-300">{fmt(s.transportAllowance)}</td>
                      <td className="px-3 py-2 text-right text-slate-700 dark:text-slate-300">{fmt(s.otherAllowances)}</td>
                      <td className="px-3 py-2 text-right font-semibold text-slate-900 dark:text-white">{fmt(s.grossSalary)}</td>
                      <td className="px-3 py-2 text-right text-rose-500">({fmt(s.deductions)})</td>
                      <td className="px-3 py-2 text-right font-bold text-emerald-600 dark:text-emerald-400">{fmt(s.netSalary)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </div>
    </div>
  );
}

// ── AI Validation Tab ───────────────────────────────────────────────────────────

function AIValidationTab() {
  const [runs, setRuns] = useState<PayrollRun[]>([]);
  const [runId, setRunId] = useState('');
  const [result, setResult] = useState<{ advisoryOnly: boolean; warnings: PayrollValidationResult[]; summary: string } | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => { payrollApi.listRuns({ pageSize: 50 }).then(r => setRuns(r.items)).catch(() => {}); }, []);

  const runAI = async () => {
    if (!runId) return;
    setLoading(true);
    payrollApi.aiValidation(runId).then(r => setResult(r as typeof result)).catch(() => {}).finally(() => setLoading(false));
  };

  return (
    <div className="space-y-4">
      <div className="surface p-4">
        <div className="flex items-start gap-3">
          <Bot className="mt-0.5 h-5 w-5 shrink-0 text-sapphire dark:text-cyanAccent" />
          <div>
            <p className="text-sm font-semibold text-slate-800 dark:text-white">AI Payroll Validation — Advisory Only</p>
            <p className="mt-1 text-xs text-slate-500 dark:text-slate-400">
              AI checks flag anomalies like salary variance, unusual overtime, missing IBANs, and duplicate entries.
              AI does not approve payroll or change salaries automatically.
            </p>
          </div>
        </div>
      </div>

      <div className="flex items-center gap-3">
        <select aria-label="Select payroll run for AI validation" className={`${sel} max-w-xs flex-1`} value={runId} onChange={e => setRunId(e.target.value)}>
          <option value="">Select a payroll run…</option>
          {runs.map(r => <option key={r.id} value={r.id}>{MONTHS[r.month - 1]} {r.year} — {r.status}</option>)}
        </select>
        <button type="button" className={btn.primary} onClick={runAI} disabled={!runId || loading}>
          <Zap className="h-4 w-4" /> {loading ? 'Analyzing…' : 'Run AI Checks'}
        </button>
      </div>

      {result && (
        <>
          <div className="surface p-4">
            <div className="flex items-center gap-2">
              <Shield className="h-4 w-4 text-sapphire dark:text-cyanAccent" />
              <p className="text-sm font-semibold text-slate-800 dark:text-white">{result.summary}</p>
            </div>
            <p className="mt-1 text-xs text-amber-600 dark:text-amber-400">Advisory: AI findings are recommendations only — HR/Finance must review and act.</p>
          </div>

          {result.warnings.length === 0 ? (
            <div className="surface flex flex-col items-center py-12 text-center">
              <CheckCircle2 className="mb-3 h-8 w-8 text-emerald-500" />
              <p className="text-sm font-medium text-slate-700 dark:text-slate-300">No AI anomalies detected for this payroll run.</p>
            </div>
          ) : (
            <div className="space-y-3">
              {result.warnings.map(w => (
                <div key={w.id} className="surface p-4">
                  <div className="flex items-start gap-3">
                    <AlertTriangle className={`mt-0.5 h-4 w-4 shrink-0 ${w.severity === 'Error' ? 'text-rose-500' : 'text-amber-500'}`} />
                    <div className="min-w-0">
                      <div className="flex items-center gap-2">
                        <StatusBadge status={w.severity} />
                        <span className="font-mono text-xs text-slate-500">{w.code}</span>
                        {w.employeeId && <span className="text-xs text-slate-400">Emp #{w.employeeId}</span>}
                      </div>
                      <p className="mt-1 text-sm text-slate-700 dark:text-slate-300">{w.message}</p>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
        </>
      )}
    </div>
  );
}

// ── Main Page ───────────────────────────────────────────────────────────────────

export function PayrollPage() {
  const [activeTab, setActiveTab] = useState<Tab>('dashboard');
  const [contextRunId, setContextRunId] = useState<string | undefined>(undefined);

  const handleSelectRun = (run: PayrollRun, tab: Tab) => {
    setContextRunId(run.id);
    setActiveTab(tab);
  };

  const renderTab = () => {
    switch (activeTab) {
      case 'dashboard': return <DashboardTab onNavigate={setActiveTab} />;
      case 'salary-structures': return <SalaryStructuresTab />;
      case 'employee-salary': return <EmployeeSalaryTab />;
      case 'runs': return <RunsTab onSelectRun={handleSelectRun} />;
      case 'validation': return <ValidationTab selectedRunId={contextRunId} />;
      case 'approvals': return <ApprovalsTab selectedRunId={contextRunId} />;
      case 'payslips': return <PayslipsTab />;
      case 'bank-wps': return <BankWpsTab />;
      case 'payment-tracking': return <PaymentTrackingTab />;
      case 'reports': return <ReportsTab />;
      case 'ai-validation': return <AIValidationTab />;
    }
  };

  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-xl font-bold text-slate-900 dark:text-white">Payroll Management</h1>
        <p className="mt-0.5 text-sm text-slate-500 dark:text-slate-400">End-to-end payroll lifecycle — salary structures, runs, WPS/bank files, payslips, and AI validation.</p>
      </div>

      <div className="flex gap-1 overflow-x-auto rounded-xl border border-slate-200 bg-slate-50 p-1 dark:border-white/10 dark:bg-white/5">
        {TABS.map(t => (
          <button
            key={t.key}
            type="button"
            onClick={() => setActiveTab(t.key)}
            className={`flex shrink-0 items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs font-medium transition-colors ${activeTab === t.key ? 'bg-white shadow-sm text-sapphire dark:bg-slate-800 dark:text-cyanAccent' : 'text-slate-500 hover:text-slate-700 dark:text-slate-400 dark:hover:text-slate-200'}`}
          >
            <t.icon className="h-3.5 w-3.5" />
            {t.label}
          </button>
        ))}
      </div>

      <div>{renderTab()}</div>
    </div>
  );
}

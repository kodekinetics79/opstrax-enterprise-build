import { useCallback, useEffect, useRef, useState } from 'react';
import {
  Plus, ChevronLeft, Users, Briefcase, ClipboardList, UserPlus,
  CheckCircle, Clock, XCircle, ChevronRight, X, Star, Send,
  FileText, AlertCircle, ArrowRight, Calendar,
} from 'lucide-react';
import {
  requisitionsApi, openingsApi, candidatesApi, applicationsApi,
} from '../api/recruitment';
import type {
  ManpowerRequisition, JobOpening, Candidate, JobApplication,
  ApplicationDetail, KanbanStage, OfferLetter,
  RecruitmentStats, RequisitionStats,
} from '../api/recruitment';
import { StatusChip } from '../components/StatusChip';

// ── Helpers ───────────────────────────────────────────────────────────────────

function ago(utc: string) {
  const d = Math.floor((Date.now() - new Date(utc).getTime()) / 86400000);
  if (d === 0) return 'Today';
  if (d === 1) return 'Yesterday';
  return `${d}d ago`;
}

function fmt(n: number | null | undefined) {
  if (n == null) return '—';
  return n.toLocaleString('en-AE', { minimumFractionDigits: 0, maximumFractionDigits: 0 });
}

const PRIORITY_COLORS: Record<string, string> = {
  Low: 'bg-slate-100 text-slate-600 dark:bg-slate-700 dark:text-slate-300',
  Medium: 'bg-amber-50 text-amber-700 dark:bg-amber-500/10 dark:text-amber-400',
  High: 'bg-orange-50 text-orange-700 dark:bg-orange-500/10 dark:text-orange-400',
  Critical: 'bg-rose-50 text-rose-700 dark:bg-rose-500/10 dark:text-rose-400',
};

const STAGE_COLORS: Record<string, string> = {
  Applied: '#64748b',
  Screening: '#2F6BFF',
  Assessment: '#f59e0b',
  Interview: '#8b5cf6',
  Offer: '#00C896',
  Hired: '#10b981',
};

const reqStatusTone = (s: string) => {
  switch (s) {
    case 'Approved': return { label: 'Approved', tone: 'emerald' as const };
    case 'Rejected': return { label: 'Rejected', tone: 'rose' as const };
    case 'Submitted':
    case 'PendingApproval': return { label: 'Pending', tone: 'amber' as const };
    case 'Converted': return { label: 'Converted', tone: 'blue' as const };
    default: return { label: s, tone: 'slate' as const };
  }
};

const openingStatusTone = (s: string) => {
  switch (s) {
    case 'Open': return { label: 'Open', tone: 'emerald' as const };
    case 'InProgress': return { label: 'In Progress', tone: 'blue' as const };
    case 'OnHold': return { label: 'On Hold', tone: 'amber' as const };
    case 'Closed': return { label: 'Closed', tone: 'slate' as const };
    default: return { label: s, tone: 'slate' as const };
  }
};

// ── KPI Card ──────────────────────────────────────────────────────────────────

function KpiCard({ label, value, sub, icon: Icon, color }: {
  label: string; value: number | string; sub?: string;
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

// ── Overview Tab ───────────────────────────────────────────────────────────────

function OverviewTab({ onNavigate }: { onNavigate: (tab: Tab) => void }) {
  const [stats, setStats] = useState<RecruitmentStats | null>(null);
  const [reqStats, setReqStats] = useState<RequisitionStats | null>(null);
  const [recentOpenings, setRecentOpenings] = useState<JobOpening[]>([]);

  useEffect(() => {
    openingsApi.stats().then(setStats).catch(() => {});
    requisitionsApi.stats().then(setReqStats).catch(() => {});
    openingsApi.list({ status: 'Open' }).then(r => setRecentOpenings(r.items.slice(0, 5))).catch(() => {});
  }, []);

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-2 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <KpiCard label="Open Positions" value={stats?.openPositions ?? '—'} icon={Briefcase} color="bg-sapphire/10 text-sapphire dark:bg-sapphire/20" />
        <KpiCard label="Active Applications" value={stats?.activeApplications ?? '—'} icon={Users} color="bg-emeraldZ/10 text-emeraldZ dark:bg-emeraldZ/20" />
        <KpiCard label="Offers Pending" value={stats?.offersPending ?? '—'} icon={FileText} color="bg-amber-100 text-amber-600 dark:bg-amber-500/20 dark:text-amber-400" />
        <KpiCard label="Hired This Month" value={stats?.hiredThisMonth ?? '—'} icon={CheckCircle} color="bg-emerald-100 text-emerald-600 dark:bg-emerald-500/20 dark:text-emerald-400" />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        {/* Requisition summary */}
        <div className="surface p-5">
          <div className="mb-4 flex items-center justify-between">
            <h3 className="text-sm font-semibold text-slate-800 dark:text-white">Requisition Pipeline</h3>
            <button type="button" onClick={() => onNavigate('requisitions')} className="text-xs text-sapphire hover:underline dark:text-cyanAccent">View all</button>
          </div>
          <div className="space-y-3">
            {[
              { label: 'Draft', val: reqStats?.draft ?? 0, color: 'bg-slate-200 dark:bg-slate-700' },
              { label: 'Pending Approval', val: reqStats?.pending ?? 0, color: 'bg-amber-400' },
              { label: 'Approved', val: reqStats?.approved ?? 0, color: 'bg-emeraldZ' },
              { label: 'Converted to Openings', val: reqStats?.converted ?? 0, color: 'bg-sapphire' },
            ].map(row => (
              <div key={row.label} className="flex items-center gap-3">
                <div className={`h-2 w-2 shrink-0 rounded-full ${row.color}`} />
                <span className="flex-1 text-sm text-slate-700 dark:text-slate-300">{row.label}</span>
                <span className="text-sm font-semibold tabular-nums text-slate-900 dark:text-white">{row.val}</span>
              </div>
            ))}
          </div>
        </div>

        {/* Active openings */}
        <div className="surface p-5">
          <div className="mb-4 flex items-center justify-between">
            <h3 className="text-sm font-semibold text-slate-800 dark:text-white">Active Job Openings</h3>
            <button type="button" onClick={() => onNavigate('openings')} className="text-xs text-sapphire hover:underline dark:text-cyanAccent">View all</button>
          </div>
          {recentOpenings.length === 0 && <p className="text-sm text-slate-400 dark:text-slate-500">No open positions yet.</p>}
          <div className="space-y-3">
            {recentOpenings.map(o => (
              <div key={o.id} className="flex items-center justify-between">
                <div className="min-w-0">
                  <p className="truncate text-sm font-medium text-slate-800 dark:text-slate-200">{o.title}</p>
                  <p className="text-xs text-slate-400 dark:text-slate-500">{o.departmentName} · {o.activeApplications ?? 0} applicants</p>
                </div>
                <span className="ml-3 shrink-0 text-xs font-semibold text-sapphire dark:text-cyanAccent">{o.remaining ?? o.headCount - o.filledCount} left</span>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

// ── Create Requisition Modal ───────────────────────────────────────────────────

interface CreateReqModalProps { onClose: () => void; onSaved: () => void; }

function CreateReqModal({ onClose, onSaved }: CreateReqModalProps) {
  const [form, setForm] = useState({
    departmentName: '', designationTitle: '', headCount: 1, employmentType: 'Full-Time',
    priority: 'Medium', justification: '', requiredSkills: '', minExperienceYears: '',
    maxExperienceYears: '', budgetFrom: '', budgetTo: '', targetJoiningDate: '', requestedByName: '',
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const set = (k: keyof typeof form, v: string | number) => setForm(f => ({ ...f, [k]: v }));

  const save = async () => {
    if (!form.departmentName || !form.designationTitle || !form.justification) {
      setError('Department, Designation and Justification are required.');
      return;
    }
    setSaving(true);
    setError('');
    try {
      await requisitionsApi.create({
        departmentName: form.departmentName,
        designationTitle: form.designationTitle,
        headCount: form.headCount,
        employmentType: form.employmentType,
        priority: form.priority,
        justification: form.justification,
        requiredSkills: form.requiredSkills,
        minExperienceYears: form.minExperienceYears ? Number(form.minExperienceYears) : undefined,
        maxExperienceYears: form.maxExperienceYears ? Number(form.maxExperienceYears) : undefined,
        budgetFrom: form.budgetFrom ? Number(form.budgetFrom) : undefined,
        budgetTo: form.budgetTo ? Number(form.budgetTo) : undefined,
        targetJoiningDate: form.targetJoiningDate || undefined,
        requestedByName: form.requestedByName,
      });
      onSaved();
    } catch {
      setError('Failed to create requisition.');
      setSaving(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-black/40 p-4 pt-12">
      <div className="w-full max-w-lg rounded-2xl border border-slate-200 bg-white p-6 shadow-2xl dark:border-white/10 dark:bg-[#0D1221]">
        <div className="mb-5 flex items-center justify-between">
          <h3 className="font-semibold text-slate-900 dark:text-white">New Manpower Requisition</h3>
          <button type="button" onClick={onClose} className="grid h-7 w-7 place-items-center rounded-lg text-slate-400 hover:bg-slate-100 dark:hover:bg-white/10"><X className="h-4 w-4" /></button>
        </div>

        <div className="space-y-3">
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Department *</label>
              <input className="input w-full" placeholder="e.g. Engineering" value={form.departmentName} onChange={e => set('departmentName', e.target.value)} />
            </div>
            <div>
              <label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Designation *</label>
              <input className="input w-full" placeholder="e.g. Software Engineer" value={form.designationTitle} onChange={e => set('designationTitle', e.target.value)} />
            </div>
          </div>
          <div className="grid grid-cols-3 gap-3">
            <div>
              <label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Head Count</label>
              <input type="number" className="input w-full" min={1} value={form.headCount} onChange={e => set('headCount', Number(e.target.value))} aria-label="Head count" />
            </div>
            <div>
              <label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Type</label>
              <select className="select w-full" value={form.employmentType} onChange={e => set('employmentType', e.target.value)} aria-label="Employment type">
                {['Full-Time', 'Part-Time', 'Contract', 'Internship'].map(t => <option key={t}>{t}</option>)}
              </select>
            </div>
            <div>
              <label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Priority</label>
              <select className="select w-full" value={form.priority} onChange={e => set('priority', e.target.value)} aria-label="Priority">
                {['Low', 'Medium', 'High', 'Critical'].map(p => <option key={p}>{p}</option>)}
              </select>
            </div>
          </div>
          <div>
            <label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Justification *</label>
            <textarea className="input w-full resize-none" rows={3} placeholder="Business need and justification…" value={form.justification} onChange={e => set('justification', e.target.value)} />
          </div>
          <div>
            <label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Required Skills</label>
            <textarea className="input w-full resize-none" rows={2} placeholder="e.g. React, TypeScript, Node.js…" value={form.requiredSkills} onChange={e => set('requiredSkills', e.target.value)} />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Budget Range (AED/mo)</label>
              <div className="flex items-center gap-1">
                <input className="input w-full" type="number" placeholder="From" value={form.budgetFrom} onChange={e => set('budgetFrom', e.target.value)} aria-label="Budget from" />
                <span className="text-slate-400">–</span>
                <input className="input w-full" type="number" placeholder="To" value={form.budgetTo} onChange={e => set('budgetTo', e.target.value)} aria-label="Budget to" />
              </div>
            </div>
            <div>
              <label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Target Join Date</label>
              <input type="date" className="input w-full" value={form.targetJoiningDate} onChange={e => set('targetJoiningDate', e.target.value)} aria-label="Target joining date" />
            </div>
          </div>
          <div>
            <label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Requested By</label>
            <input className="input w-full" placeholder="Your name" value={form.requestedByName} onChange={e => set('requestedByName', e.target.value)} />
          </div>
          {error && <p className="text-xs text-rose-500">{error}</p>}
        </div>

        <div className="mt-5 flex justify-end gap-2">
          <button type="button" className="btn-secondary text-sm" onClick={onClose}>Cancel</button>
          <button type="button" className="btn-primary text-sm" onClick={save} disabled={saving}>{saving ? 'Saving…' : 'Create Requisition'}</button>
        </div>
      </div>
    </div>
  );
}

// ── Requisitions Tab ───────────────────────────────────────────────────────────

function RequisitionsTab({ onCreateOpening }: { onCreateOpening: (req: ManpowerRequisition) => void }) {
  const [items, setItems] = useState<ManpowerRequisition[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [statusFilter, setStatusFilter] = useState('');
  const [createOpen, setCreateOpen] = useState(false);
  const [acting, setActing] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const r = await requisitionsApi.list({ status: statusFilter || undefined });
      setItems(r.items); setTotal(r.total);
    } catch { /**/ }
    finally { setLoading(false); }
  }, [statusFilter]);

  useEffect(() => { load(); }, [load]);

  const submit = async (id: string) => {
    setActing(id);
    try { await requisitionsApi.submit(id); load(); } catch { /**/ }
    finally { setActing(null); }
  };

  const approve = async (id: string) => {
    setActing(id);
    try { await requisitionsApi.approve(id); load(); } catch { /**/ }
    finally { setActing(null); }
  };

  const reject = async (id: string) => {
    const reason = window.prompt('Rejection reason:');
    if (!reason) return;
    setActing(id);
    try { await requisitionsApi.reject(id, reason); load(); } catch { /**/ }
    finally { setActing(null); }
  };

  return (
    <div>
      <div className="mb-4 flex items-center gap-3">
        <select className="select w-44" value={statusFilter} onChange={e => setStatusFilter(e.target.value)} aria-label="Filter by status">
          <option value="">All Statuses</option>
          {['Draft', 'Submitted', 'PendingApproval', 'Approved', 'Rejected', 'Converted'].map(s => <option key={s}>{s}</option>)}
        </select>
        <span className="ml-auto text-xs text-slate-500 dark:text-slate-400">{total} requisitions</span>
        <button type="button" className="btn-primary flex items-center gap-1.5 text-sm" onClick={() => setCreateOpen(true)}>
          <Plus className="h-3.5 w-3.5" />New Requisition
        </button>
      </div>

      <div className="overflow-hidden rounded-xl border border-slate-200 dark:border-white/10">
        <table className="min-w-full text-sm">
          <thead className="border-b border-slate-200 bg-slate-50 dark:border-white/10 dark:bg-white/[0.03]">
            <tr>
              {['Number', 'Position', 'Department', 'Count', 'Priority', 'Status', 'Actions'].map(h => (
                <th key={h} className="px-4 py-3 text-left text-xs font-semibold text-slate-500 dark:text-slate-400">{h}</th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100 dark:divide-white/[0.06]">
            {loading && <tr><td colSpan={7} className="py-10 text-center"><div className="mx-auto h-5 w-5 animate-spin rounded-full border-2 border-sapphire border-t-transparent" /></td></tr>}
            {!loading && items.length === 0 && <tr><td colSpan={7} className="py-10 text-center text-sm text-slate-400 dark:text-slate-500">No requisitions found.</td></tr>}
            {!loading && items.map(r => (
              <tr key={r.id} className="hover:bg-slate-50/60 dark:hover:bg-white/[0.02]">
                <td className="px-4 py-3 font-mono text-xs text-slate-600 dark:text-slate-400">{r.requisitionNumber}</td>
                <td className="px-4 py-3">
                  <p className="font-medium text-slate-800 dark:text-slate-200">{r.designationTitle}</p>
                  <p className="text-xs text-slate-400 dark:text-slate-500">{ago(r.createdAtUtc)}</p>
                </td>
                <td className="px-4 py-3 text-slate-600 dark:text-slate-400">{r.departmentName}</td>
                <td className="px-4 py-3 text-center font-semibold text-slate-800 dark:text-slate-200">{r.headCount}</td>
                <td className="px-4 py-3">
                  <span className={`rounded-md px-2 py-0.5 text-xs font-medium ${PRIORITY_COLORS[r.priority] ?? ''}`}>{r.priority}</span>
                </td>
                <td className="px-4 py-3"><StatusChip {...reqStatusTone(r.status)} /></td>
                <td className="px-4 py-3">
                  <div className="flex items-center gap-1">
                    {r.status === 'Draft' && (
                      <button type="button" disabled={acting === r.id} onClick={() => submit(r.id)} className="rounded-lg bg-sapphire/10 px-2.5 py-1 text-xs font-medium text-sapphire hover:bg-sapphire/20 disabled:opacity-50 dark:bg-sapphire/20 dark:text-cyanAccent">
                        Submit
                      </button>
                    )}
                    {(r.status === 'Submitted' || r.status === 'PendingApproval') && (
                      <>
                        <button type="button" disabled={acting === r.id} onClick={() => approve(r.id)} className="rounded-lg bg-emerald-50 px-2.5 py-1 text-xs font-medium text-emerald-600 hover:bg-emerald-100 disabled:opacity-50 dark:bg-emerald-500/10 dark:text-emerald-400">Approve</button>
                        <button type="button" disabled={acting === r.id} onClick={() => reject(r.id)} className="rounded-lg bg-rose-50 px-2.5 py-1 text-xs font-medium text-rose-600 hover:bg-rose-100 disabled:opacity-50 dark:bg-rose-500/10 dark:text-rose-400">Reject</button>
                      </>
                    )}
                    {r.status === 'Approved' && (
                      <button type="button" onClick={() => onCreateOpening(r)} className="rounded-lg bg-blue-50 px-2.5 py-1 text-xs font-medium text-blue-600 hover:bg-blue-100 dark:bg-blue-500/10 dark:text-blue-400">
                        Open Job
                      </button>
                    )}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {createOpen && <CreateReqModal onClose={() => setCreateOpen(false)} onSaved={() => { setCreateOpen(false); load(); }} />}
    </div>
  );
}

// ── Create Job Opening Modal ───────────────────────────────────────────────────

function CreateOpeningModal({ requisition, onClose, onSaved }: {
  requisition: ManpowerRequisition | null; onClose: () => void; onSaved: () => void;
}) {
  const [form, setForm] = useState({
    title: requisition?.designationTitle ?? '',
    departmentName: requisition?.departmentName ?? '',
    designationTitle: requisition?.designationTitle ?? '',
    employmentType: requisition?.employmentType ?? 'Full-Time',
    headCount: requisition?.headCount ?? 1,
    description: '', requirements: '', responsibilities: '',
    salaryFrom: '', salaryTo: '', location: '', assignedHrName: '',
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const set = (k: keyof typeof form, v: string | number) => setForm(f => ({ ...f, [k]: v }));

  const save = async () => {
    if (!form.title || !form.departmentName) { setError('Title and Department are required.'); return; }
    setSaving(true); setError('');
    try {
      await openingsApi.create({
        requisitionId: requisition?.id,
        title: form.title, departmentName: form.departmentName,
        designationTitle: form.designationTitle, employmentType: form.employmentType,
        headCount: Number(form.headCount), description: form.description,
        requirements: form.requirements, responsibilities: form.responsibilities,
        salaryFrom: form.salaryFrom ? Number(form.salaryFrom) : undefined,
        salaryTo: form.salaryTo ? Number(form.salaryTo) : undefined,
        location: form.location, assignedHrName: form.assignedHrName,
      });
      onSaved();
    } catch { setError('Failed to create opening.'); setSaving(false); }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-black/40 p-4 pt-12">
      <div className="w-full max-w-lg rounded-2xl border border-slate-200 bg-white p-6 shadow-2xl dark:border-white/10 dark:bg-[#0D1221]">
        <div className="mb-4 flex items-center justify-between">
          <h3 className="font-semibold text-slate-900 dark:text-white">New Job Opening{requisition ? ` from ${requisition.requisitionNumber}` : ''}</h3>
          <button type="button" onClick={onClose} className="grid h-7 w-7 place-items-center rounded-lg text-slate-400 hover:bg-slate-100 dark:hover:bg-white/10"><X className="h-4 w-4" /></button>
        </div>
        <div className="space-y-3">
          <div>
            <label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Job Title *</label>
            <input className="input w-full" value={form.title} onChange={e => set('title', e.target.value)} />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Department *</label>
              <input className="input w-full" value={form.departmentName} onChange={e => set('departmentName', e.target.value)} />
            </div>
            <div>
              <label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Head Count</label>
              <input type="number" className="input w-full" min={1} value={form.headCount} onChange={e => set('headCount', Number(e.target.value))} aria-label="Head count" />
            </div>
          </div>
          <div>
            <label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Job Description</label>
            <textarea className="input w-full resize-none" rows={3} value={form.description} onChange={e => set('description', e.target.value)} />
          </div>
          <div>
            <label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Requirements (one per line)</label>
            <textarea className="input w-full resize-none" rows={3} placeholder="5+ years of experience&#10;Strong TypeScript skills…" value={form.requirements} onChange={e => set('requirements', e.target.value)} />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Salary From (AED)</label>
              <input type="number" className="input w-full" value={form.salaryFrom} onChange={e => set('salaryFrom', e.target.value)} aria-label="Salary from" />
            </div>
            <div>
              <label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Salary To (AED)</label>
              <input type="number" className="input w-full" value={form.salaryTo} onChange={e => set('salaryTo', e.target.value)} aria-label="Salary to" />
            </div>
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Location</label>
              <input className="input w-full" placeholder="e.g. Dubai, UAE" value={form.location} onChange={e => set('location', e.target.value)} />
            </div>
            <div>
              <label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Assigned HR</label>
              <input className="input w-full" placeholder="HR name" value={form.assignedHrName} onChange={e => set('assignedHrName', e.target.value)} />
            </div>
          </div>
          {error && <p className="text-xs text-rose-500">{error}</p>}
        </div>
        <div className="mt-5 flex justify-end gap-2">
          <button type="button" className="btn-secondary text-sm" onClick={onClose}>Cancel</button>
          <button type="button" className="btn-primary text-sm" onClick={save} disabled={saving}>{saving ? 'Saving…' : 'Create Opening'}</button>
        </div>
      </div>
    </div>
  );
}

// ── Job Openings Tab ───────────────────────────────────────────────────────────

function OpeningsTab({ onSelectOpening, requisitionToOpen, onOpeningCreated }: {
  onSelectOpening: (o: JobOpening) => void;
  requisitionToOpen: ManpowerRequisition | null;
  onOpeningCreated: () => void;
}) {
  const [items, setItems] = useState<JobOpening[]>([]);
  const [loading, setLoading] = useState(true);
  const [createOpen, setCreateOpen] = useState(!!requisitionToOpen);

  const load = () => {
    setLoading(true);
    openingsApi.list().then(r => { setItems(r.items); }).catch(() => {}).finally(() => setLoading(false));
  };

  useEffect(() => { load(); }, []);
  useEffect(() => { if (requisitionToOpen) setCreateOpen(true); }, [requisitionToOpen]);

  return (
    <div>
      <div className="mb-4 flex items-center justify-between">
        <p className="text-sm text-slate-500 dark:text-slate-400">{items.length} opening{items.length !== 1 ? 's' : ''}</p>
        <button type="button" className="btn-primary flex items-center gap-1.5 text-sm" onClick={() => setCreateOpen(true)}>
          <Plus className="h-3.5 w-3.5" />New Opening
        </button>
      </div>

      {loading && <div className="flex justify-center py-12"><div className="h-5 w-5 animate-spin rounded-full border-2 border-sapphire border-t-transparent" /></div>}

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {!loading && items.map(o => (
          <button
            key={o.id}
            type="button"
            onClick={() => onSelectOpening(o)}
            className="surface group flex flex-col gap-3 p-5 text-left transition hover:border-sapphire/30 hover:shadow-md dark:hover:border-cyanAccent/20"
          >
            <div className="flex items-start justify-between gap-2">
              <div className="min-w-0">
                <p className="truncate font-semibold text-slate-800 group-hover:text-sapphire dark:text-white dark:group-hover:text-cyanAccent">{o.title}</p>
                <p className="text-xs text-slate-400 dark:text-slate-500">{o.jobCode}</p>
              </div>
              <StatusChip {...openingStatusTone(o.status)} />
            </div>
            <div className="space-y-1 text-xs text-slate-500 dark:text-slate-400">
              <p>{o.departmentName}</p>
              {o.salaryFrom && <p>AED {fmt(o.salaryFrom)}–{fmt(o.salaryTo)}/mo</p>}
              {o.location && <p>{o.location}</p>}
            </div>
            <div className="flex items-center justify-between border-t border-slate-100 pt-3 dark:border-white/[0.06]">
              <div className="flex items-center gap-3 text-xs">
                <span className="font-semibold text-sapphire dark:text-cyanAccent">{o.activeApplications ?? 0}</span>
                <span className="text-slate-400">applicants</span>
              </div>
              <div className="flex items-center gap-1 text-xs text-slate-400">
                <span>{o.headCount - o.filledCount} position{o.headCount - o.filledCount !== 1 ? 's' : ''} left</span>
                <ChevronRight className="h-3.5 w-3.5" />
              </div>
            </div>
          </button>
        ))}
      </div>

      {createOpen && (
        <CreateOpeningModal
          requisition={requisitionToOpen}
          onClose={() => setCreateOpen(false)}
          onSaved={() => { setCreateOpen(false); load(); onOpeningCreated(); }}
        />
      )}
    </div>
  );
}

// ── Application Detail Drawer ──────────────────────────────────────────────────

function ApplicationDrawer({ id, onClose, onRefresh }: { id: string; onClose: () => void; onRefresh: () => void }) {
  const [detail, setDetail] = useState<ApplicationDetail | null>(null);
  const [tab, setTab] = useState<'timeline' | 'interviews' | 'offer'>('timeline');
  const [acting, setActing] = useState('');
  const [offerForm, setOfferForm] = useState({ department: '', startDate: '', basicSalary: '', housingAllowance: '', transportAllowance: '', otherAllowances: '', probationMonths: 3 });
  const [interviewForm, setInterviewForm] = useState({ interviewType: 'HR Screening', interviewerNames: '', scheduledAt: '', durationMinutes: 60, mode: 'Video', meetingLink: '', location: '' });
  const [noteText, setNoteText] = useState('');
  const [rejectReason, setRejectReason] = useState('');

  const load = () => {
    applicationsApi.get(id).then(setDetail).catch(() => {});
  };
  useEffect(() => { load(); }, [id]);

  const app = detail?.application;
  if (!app) return null;

  const nextStage = (() => {
    const stages = ['Applied', 'Screening', 'Assessment', 'Interview', 'Offer', 'Hired'];
    const idx = stages.indexOf(app.stage);
    return idx >= 0 && idx < stages.length - 1 ? stages[idx + 1] : null;
  })();

  const doAdvance = async () => {
    setActing('advance');
    try { await applicationsApi.advance(id); load(); onRefresh(); } catch { /**/ }
    finally { setActing(''); }
  };

  const doReject = async () => {
    if (!rejectReason.trim()) return;
    setActing('reject');
    try { await applicationsApi.reject(id, rejectReason); load(); onRefresh(); } catch { /**/ }
    finally { setActing(''); }
  };

  const doNote = async () => {
    if (!noteText.trim()) return;
    setActing('note');
    try { await applicationsApi.addNote(id, noteText); setNoteText(''); load(); } catch { /**/ }
    finally { setActing(''); }
  };

  const doScheduleInterview = async () => {
    setActing('interview');
    try {
      await applicationsApi.scheduleInterview(id, {
        interviewType: interviewForm.interviewType,
        interviewerNames: interviewForm.interviewerNames,
        scheduledAt: interviewForm.scheduledAt,
        durationMinutes: interviewForm.durationMinutes,
        mode: interviewForm.mode,
        meetingLink: interviewForm.meetingLink,
        location: interviewForm.location,
      });
      load();
    } catch { /**/ }
    finally { setActing(''); }
  };

  const doGenerateOffer = async () => {
    setActing('offer');
    try {
      await applicationsApi.generateOffer(id, {
        department: offerForm.department,
        startDate: offerForm.startDate,
        basicSalary: Number(offerForm.basicSalary),
        housingAllowance: Number(offerForm.housingAllowance),
        transportAllowance: Number(offerForm.transportAllowance),
        otherAllowances: Number(offerForm.otherAllowances) || 0,
        probationMonths: offerForm.probationMonths,
      });
      setTab('offer'); load();
    } catch { /**/ }
    finally { setActing(''); }
  };

  const offer = detail.offer as OfferLetter | null;

  const offerAction = async (action: 'send' | 'accept' | 'decline') => {
    if (!offer) return;
    setActing(action);
    try {
      if (action === 'send') await applicationsApi.sendOffer(offer.id);
      else if (action === 'accept') await applicationsApi.acceptOffer(offer.id);
      else {
        const reason = window.prompt('Decline reason:') ?? '';
        await applicationsApi.declineOffer(offer.id, reason);
      }
      load(); onRefresh();
    } catch { /**/ }
    finally { setActing(''); }
  };

  return (
    <div className="fixed inset-0 z-50 flex">
      <div className="flex-1 bg-black/40" onClick={onClose} />
      <div className="flex h-full w-full max-w-lg flex-col overflow-hidden border-l border-slate-200 bg-white dark:border-white/10 dark:bg-[#0B1020]">
        {/* Header */}
        <div className="flex items-start gap-3 border-b border-slate-200 p-5 dark:border-white/10">
          <div className="flex-1 min-w-0">
            <p className="truncate font-semibold text-slate-900 dark:text-white">{app.candidateName}</p>
            <p className="text-xs text-slate-500 dark:text-slate-400">{app.jobTitle}</p>
            <div className="mt-2 flex items-center gap-2">
              <span className="inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-xs font-semibold text-white" style={{ backgroundColor: STAGE_COLORS[app.stage] ?? '#64748b' }}>
                {app.stage}
              </span>
              {app.status !== 'Active' && (
                <span className="rounded-full bg-rose-50 px-2.5 py-0.5 text-xs font-semibold text-rose-600 dark:bg-rose-500/10 dark:text-rose-400">{app.status}</span>
              )}
            </div>
          </div>
          <button type="button" onClick={onClose} className="grid h-8 w-8 shrink-0 place-items-center rounded-lg text-slate-400 hover:bg-slate-100 dark:hover:bg-white/10"><X className="h-4 w-4" /></button>
        </div>

        {/* Stage advance/reject actions */}
        {app.status === 'Active' && app.stage !== 'Hired' && (
          <div className="flex gap-2 border-b border-slate-100 px-5 py-3 dark:border-white/[0.06]">
            {nextStage && (
              <button type="button" disabled={!!acting} onClick={doAdvance}
                className="flex items-center gap-1.5 rounded-lg bg-sapphire px-3 py-1.5 text-xs font-semibold text-white hover:bg-sapphire/90 disabled:opacity-50">
                <ArrowRight className="h-3.5 w-3.5" />Move to {nextStage}
              </button>
            )}
            <div className="flex items-center gap-1.5 rounded-lg border border-rose-200 px-2 py-1 dark:border-rose-500/30">
              <input className="w-28 bg-transparent text-xs text-slate-600 outline-none placeholder:text-slate-400 dark:text-slate-300" placeholder="Reject reason…" value={rejectReason} onChange={e => setRejectReason(e.target.value)} />
              <button type="button" disabled={!rejectReason.trim() || !!acting} onClick={doReject} className="text-xs font-medium text-rose-500 hover:text-rose-600 disabled:opacity-40">Reject</button>
            </div>
          </div>
        )}

        {/* Candidate info */}
        {detail.candidate && (
          <div className="border-b border-slate-100 px-5 py-3 dark:border-white/[0.06]">
            <div className="grid grid-cols-2 gap-x-4 gap-y-1 text-xs">
              <div><span className="text-slate-400">Email </span><span className="text-slate-700 dark:text-slate-300">{detail.candidate.email}</span></div>
              <div><span className="text-slate-400">Phone </span><span className="text-slate-700 dark:text-slate-300">{detail.candidate.phone || '—'}</span></div>
              <div><span className="text-slate-400">Experience </span><span className="text-slate-700 dark:text-slate-300">{detail.candidate.totalExperienceYears}y</span></div>
              <div><span className="text-slate-400">Source </span><span className="text-slate-700 dark:text-slate-300">{detail.candidate.source}</span></div>
            </div>
          </div>
        )}

        {/* Sub-tabs */}
        <div className="flex gap-1 border-b border-slate-200 px-5 dark:border-white/[0.06]">
          {(['timeline', 'interviews', 'offer'] as const).map(t => (
            <button key={t} type="button" onClick={() => setTab(t)}
              className={`py-2.5 text-xs font-medium capitalize transition ${tab === t ? 'border-b-2 border-sapphire text-sapphire dark:border-cyanAccent dark:text-cyanAccent' : 'text-slate-500 hover:text-slate-800 dark:text-slate-400'}`}>
              {t}
              {t === 'interviews' && detail.interviews.length > 0 && <span className="ml-1 rounded-full bg-slate-100 px-1.5 py-0.5 text-[10px] dark:bg-white/10">{detail.interviews.length}</span>}
            </button>
          ))}
        </div>

        {/* Content */}
        <div className="flex-1 overflow-y-auto px-5 py-4">
          {/* Timeline */}
          {tab === 'timeline' && (
            <div>
              <div className="space-y-3">
                {detail.events.map(ev => (
                  <div key={ev.id} className="flex gap-3">
                    <div className="mt-1 h-2 w-2 shrink-0 rounded-full bg-sapphire" />
                    <div className="flex-1">
                      <div className="flex items-baseline justify-between">
                        <p className="text-xs font-semibold text-slate-800 dark:text-slate-200">{ev.eventType.replace(/([A-Z])/g, ' $1').trim()}</p>
                        <p className="text-[10px] text-slate-400 dark:text-slate-500">{ago(ev.createdAtUtc)}</p>
                      </div>
                      {ev.notes && <p className="mt-0.5 text-xs text-slate-500 dark:text-slate-400">{ev.notes}</p>}
                      {ev.performedByName && <p className="text-[10px] text-slate-400">by {ev.performedByName}</p>}
                    </div>
                  </div>
                ))}
                {detail.events.length === 0 && <p className="text-xs text-slate-400 dark:text-slate-500">No events yet.</p>}
              </div>
              <div className="mt-4 flex gap-2">
                <input className="input flex-1" placeholder="Add a note…" value={noteText} onChange={e => setNoteText(e.target.value)} />
                <button type="button" disabled={!noteText.trim() || acting === 'note'} onClick={doNote} className="btn-secondary text-xs">Add</button>
              </div>
            </div>
          )}

          {/* Interviews */}
          {tab === 'interviews' && (
            <div className="space-y-4">
              {detail.interviews.map(iv => (
                <div key={iv.id} className="rounded-lg border border-slate-200 p-3 dark:border-white/10">
                  <div className="flex items-center justify-between">
                    <p className="text-xs font-semibold text-slate-800 dark:text-slate-200">{iv.interviewType}</p>
                    <span className={`rounded-full px-2 py-0.5 text-[10px] font-medium ${iv.status === 'Completed' ? 'bg-emerald-50 text-emerald-600 dark:bg-emerald-500/10 dark:text-emerald-400' : 'bg-blue-50 text-blue-600 dark:bg-blue-500/10 dark:text-blue-400'}`}>{iv.status}</span>
                  </div>
                  <p className="mt-1 text-xs text-slate-500 dark:text-slate-400">{new Date(iv.scheduledAt).toLocaleString('en-GB', { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' })} · {iv.mode}</p>
                  {iv.interviewerNames && <p className="text-xs text-slate-400">With: {iv.interviewerNames}</p>}
                  {iv.overallRating && (
                    <div className="mt-2 flex items-center gap-1">
                      {Array.from({ length: 5 }, (_, i) => (
                        <Star key={i} className={`h-3.5 w-3.5 ${i < iv.overallRating! ? 'fill-amber-400 text-amber-400' : 'text-slate-200 dark:text-slate-700'}`} />
                      ))}
                      <span className="ml-1 text-xs font-medium text-slate-600 dark:text-slate-400">{iv.recommendation}</span>
                    </div>
                  )}
                  {iv.feedbackNotes && <p className="mt-1 text-xs text-slate-500 dark:text-slate-400">{iv.feedbackNotes}</p>}
                </div>
              ))}

              {/* Schedule new interview */}
              {app.status === 'Active' && (
                <div className="rounded-lg border border-dashed border-slate-200 p-4 dark:border-white/10">
                  <p className="mb-3 text-xs font-semibold text-slate-700 dark:text-slate-300">Schedule Interview</p>
                  <div className="space-y-2">
                    <select className="select w-full" value={interviewForm.interviewType} onChange={e => setInterviewForm(f => ({ ...f, interviewType: e.target.value }))} aria-label="Interview type">
                      {['HR Screening', 'Technical', 'Manager', 'Panel', 'Final'].map(t => <option key={t}>{t}</option>)}
                    </select>
                    <input className="input w-full" placeholder="Interviewer names" value={interviewForm.interviewerNames} onChange={e => setInterviewForm(f => ({ ...f, interviewerNames: e.target.value }))} />
                    <div className="grid grid-cols-2 gap-2">
                      <input type="datetime-local" className="input w-full" value={interviewForm.scheduledAt} onChange={e => setInterviewForm(f => ({ ...f, scheduledAt: e.target.value }))} aria-label="Scheduled at" />
                      <select className="select w-full" value={interviewForm.mode} onChange={e => setInterviewForm(f => ({ ...f, mode: e.target.value }))} aria-label="Interview mode">
                        {['Video', 'InPerson', 'Phone'].map(m => <option key={m}>{m}</option>)}
                      </select>
                    </div>
                    {interviewForm.mode === 'Video' && (
                      <input className="input w-full" placeholder="Meeting link" value={interviewForm.meetingLink} onChange={e => setInterviewForm(f => ({ ...f, meetingLink: e.target.value }))} />
                    )}
                    <button type="button" disabled={acting === 'interview' || !interviewForm.scheduledAt} onClick={doScheduleInterview}
                      className="btn-primary w-full text-xs">
                      {acting === 'interview' ? 'Scheduling…' : 'Schedule Interview'}
                    </button>
                  </div>
                </div>
              )}
            </div>
          )}

          {/* Offer */}
          {tab === 'offer' && (
            <div className="space-y-4">
              {offer ? (
                <div className="rounded-lg border border-slate-200 p-4 dark:border-white/10">
                  <div className="mb-3 flex items-center justify-between">
                    <p className="text-sm font-semibold text-slate-800 dark:text-white">Offer Letter</p>
                    <span className={`rounded-full px-2 py-0.5 text-[10px] font-medium ${offer.status === 'Accepted' ? 'bg-emerald-50 text-emerald-600 dark:bg-emerald-500/10' : offer.status === 'Declined' ? 'bg-rose-50 text-rose-500' : 'bg-blue-50 text-blue-600 dark:bg-blue-500/10'}`}>{offer.status}</span>
                  </div>
                  <div className="space-y-1 text-xs text-slate-600 dark:text-slate-400">
                    <div className="flex justify-between"><span>Basic Salary</span><span className="font-medium">AED {fmt(offer.basicSalary)}</span></div>
                    <div className="flex justify-between"><span>Housing</span><span>AED {fmt(offer.housingAllowance)}</span></div>
                    <div className="flex justify-between"><span>Transport</span><span>AED {fmt(offer.transportAllowance)}</span></div>
                    <div className="flex justify-between border-t border-slate-100 pt-1 font-semibold text-slate-800 dark:border-white/10 dark:text-white">
                      <span>Gross Total</span><span>AED {fmt(offer.grossSalary)}</span>
                    </div>
                  </div>
                  <div className="mt-3 flex gap-2">
                    <a href={`/api/recruitment/applications/offers/${offer.id}/html`} target="_blank" rel="noreferrer"
                      className="flex items-center gap-1 rounded-lg bg-slate-100 px-2.5 py-1.5 text-xs font-medium text-slate-600 hover:bg-slate-200 dark:bg-white/10 dark:text-slate-300">
                      <FileText className="h-3.5 w-3.5" />Preview
                    </a>
                    {offer.status === 'Draft' && (
                      <button type="button" disabled={acting === 'send'} onClick={() => offerAction('send')}
                        className="flex items-center gap-1 rounded-lg bg-sapphire px-2.5 py-1.5 text-xs font-semibold text-white hover:bg-sapphire/90 disabled:opacity-50">
                        <Send className="h-3.5 w-3.5" />Send to Candidate
                      </button>
                    )}
                    {offer.status === 'Sent' && (
                      <>
                        <button type="button" disabled={!!acting} onClick={() => offerAction('accept')}
                          className="flex items-center gap-1 rounded-lg bg-emerald-500 px-2.5 py-1.5 text-xs font-semibold text-white hover:bg-emerald-600 disabled:opacity-50">
                          <CheckCircle className="h-3.5 w-3.5" />Accept
                        </button>
                        <button type="button" disabled={!!acting} onClick={() => offerAction('decline')}
                          className="flex items-center gap-1 rounded-lg border border-rose-300 px-2.5 py-1.5 text-xs font-medium text-rose-500 hover:bg-rose-50 disabled:opacity-50">
                          <XCircle className="h-3.5 w-3.5" />Decline
                        </button>
                      </>
                    )}
                  </div>
                </div>
              ) : (
                app.stage === 'Offer' && app.status === 'Active' ? (
                  <div className="rounded-lg border border-dashed border-slate-200 p-4 dark:border-white/10">
                    <p className="mb-3 text-xs font-semibold text-slate-700 dark:text-slate-300">Generate Offer Letter</p>
                    <div className="space-y-2">
                      <input className="input w-full" placeholder="Department" value={offerForm.department} onChange={e => setOfferForm(f => ({ ...f, department: e.target.value }))} />
                      <input type="date" className="input w-full" value={offerForm.startDate} onChange={e => setOfferForm(f => ({ ...f, startDate: e.target.value }))} aria-label="Start date" />
                      <div className="grid grid-cols-2 gap-2">
                        <input type="number" className="input w-full" placeholder="Basic Salary (AED)" value={offerForm.basicSalary} onChange={e => setOfferForm(f => ({ ...f, basicSalary: e.target.value }))} aria-label="Basic salary" />
                        <input type="number" className="input w-full" placeholder="Housing Allowance" value={offerForm.housingAllowance} onChange={e => setOfferForm(f => ({ ...f, housingAllowance: e.target.value }))} aria-label="Housing allowance" />
                      </div>
                      <div className="grid grid-cols-2 gap-2">
                        <input type="number" className="input w-full" placeholder="Transport Allowance" value={offerForm.transportAllowance} onChange={e => setOfferForm(f => ({ ...f, transportAllowance: e.target.value }))} aria-label="Transport allowance" />
                        <input type="number" className="input w-full" placeholder="Other Allowances" value={offerForm.otherAllowances} onChange={e => setOfferForm(f => ({ ...f, otherAllowances: e.target.value }))} aria-label="Other allowances" />
                      </div>
                      <button type="button" disabled={acting === 'offer' || !offerForm.startDate || !offerForm.basicSalary} onClick={doGenerateOffer}
                        className="btn-primary w-full text-xs">
                        {acting === 'offer' ? 'Generating…' : 'Generate Offer Letter'}
                      </button>
                    </div>
                  </div>
                ) : (
                  <p className="text-xs text-slate-400 dark:text-slate-500">No offer letter yet. Advance application to Offer stage first.</p>
                )
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

// ── Pipeline (Kanban) View ─────────────────────────────────────────────────────

function PipelineView({ opening, onBack }: { opening: JobOpening; onBack: () => void }) {
  const [kanban, setKanban] = useState<KanbanStage[]>([]);
  const [rejected, setRejected] = useState<JobApplication[]>([]);
  const [loading, setLoading] = useState(true);
  const [selectedAppId, setSelectedAppId] = useState<string | null>(null);
  const [addModalOpen, setAddModalOpen] = useState(false);
  const [candidates, setCandidates] = useState<Candidate[]>([]);
  const [selectedCandidateId, setSelectedCandidateId] = useState('');
  const [adding, setAdding] = useState(false);

  const load = () => {
    setLoading(true);
    applicationsApi.kanban(opening.id)
      .then(r => { setKanban(r.stages); setRejected(r.rejected); })
      .catch(() => {})
      .finally(() => setLoading(false));
  };

  useEffect(() => { load(); }, [opening.id]);

  const openAddModal = async () => {
    const res = await candidatesApi.list({ status: 'Active' }).catch(() => ({ items: [] as Candidate[] }));
    setCandidates(res.items);
    setSelectedCandidateId(res.items[0]?.id ?? '');
    setAddModalOpen(true);
  };

  const addCandidate = async () => {
    if (!selectedCandidateId) return;
    setAdding(true);
    try { await applicationsApi.apply(opening.id, selectedCandidateId); setAddModalOpen(false); load(); } catch { /**/ }
    finally { setAdding(false); }
  };

  const totalActive = kanban.reduce((sum, s) => sum + s.applications.length, 0);

  return (
    <div className="flex h-full flex-col">
      {/* Breadcrumb header */}
      <div className="mb-4 flex items-center gap-3">
        <button type="button" onClick={onBack} className="flex items-center gap-1.5 text-sm text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-slate-200">
          <ChevronLeft className="h-4 w-4" />Job Openings
        </button>
        <span className="text-slate-300 dark:text-slate-600">/</span>
        <span className="text-sm font-semibold text-slate-800 dark:text-slate-200">{opening.title}</span>
        <StatusChip {...openingStatusTone(opening.status)} />
        <span className="ml-auto text-xs text-slate-400 dark:text-slate-500">{totalActive} active</span>
        <button type="button" className="btn-primary flex items-center gap-1.5 text-sm" onClick={openAddModal}>
          <UserPlus className="h-3.5 w-3.5" />Add Candidate
        </button>
      </div>

      {loading ? (
        <div className="flex justify-center py-12"><div className="h-5 w-5 animate-spin rounded-full border-2 border-sapphire border-t-transparent" /></div>
      ) : (
        <div className="flex gap-3 overflow-x-auto pb-4">
          {kanban.map(col => (
            <div key={col.stage} className="flex w-56 shrink-0 flex-col gap-2">
              <div className="flex items-center gap-2 px-1">
                <div className="h-2.5 w-2.5 rounded-full" style={{ backgroundColor: STAGE_COLORS[col.stage] ?? '#64748b' }} />
                <p className="text-xs font-semibold text-slate-700 dark:text-slate-300">{col.stage}</p>
                <span className="ml-auto rounded-full bg-slate-100 px-1.5 py-0.5 text-[10px] font-medium text-slate-500 dark:bg-white/10 dark:text-slate-400">{col.applications.length}</span>
              </div>
              <div className="flex flex-col gap-2 rounded-xl border border-slate-200/70 bg-slate-50 p-2 dark:border-white/[0.06] dark:bg-white/[0.02]" style={{ minHeight: 120 }}>
                {col.applications.map(app => (
                  <button
                    key={app.id}
                    type="button"
                    onClick={() => setSelectedAppId(app.id)}
                    className="rounded-lg border border-slate-200 bg-white p-3 text-left shadow-sm transition hover:border-sapphire/30 hover:shadow-md dark:border-white/10 dark:bg-white/[0.04] dark:hover:border-cyanAccent/20"
                  >
                    <p className="text-xs font-semibold text-slate-800 dark:text-slate-200">{app.candidateName}</p>
                    <p className="mt-0.5 text-[10px] text-slate-400 dark:text-slate-500">{app.candidateEmail}</p>
                    <p className="mt-1.5 text-[10px] text-slate-400 dark:text-slate-500">{ago(app.stageChangedAtUtc ?? app.appliedAtUtc)}</p>
                  </button>
                ))}
                {col.applications.length === 0 && (
                  <p className="py-4 text-center text-[10px] text-slate-300 dark:text-slate-700">No candidates</p>
                )}
              </div>
            </div>
          ))}

          {/* Rejected column */}
          {rejected.length > 0 && (
            <div className="flex w-44 shrink-0 flex-col gap-2">
              <div className="flex items-center gap-2 px-1">
                <div className="h-2.5 w-2.5 rounded-full bg-rose-400" />
                <p className="text-xs font-semibold text-slate-700 dark:text-slate-300">Rejected</p>
                <span className="ml-auto rounded-full bg-slate-100 px-1.5 py-0.5 text-[10px] font-medium text-slate-500 dark:bg-white/10">{rejected.length}</span>
              </div>
              <div className="flex flex-col gap-2 rounded-xl border border-rose-100 bg-rose-50/50 p-2 dark:border-rose-500/10 dark:bg-rose-500/[0.03]" style={{ minHeight: 60 }}>
                {rejected.map(app => (
                  <div key={app.id} className="rounded-lg border border-rose-100 bg-white px-3 py-2 dark:border-rose-500/10 dark:bg-white/[0.03]">
                    <p className="text-[10px] font-medium text-rose-600 dark:text-rose-400">{app.candidateName}</p>
                    <p className="text-[10px] text-slate-400">{app.stage}</p>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      )}

      {/* Add candidate modal */}
      {addModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="w-96 rounded-2xl border border-slate-200 bg-white p-6 shadow-2xl dark:border-white/10 dark:bg-[#0D1221]">
            <div className="mb-4 flex items-center justify-between">
              <h3 className="text-sm font-semibold text-slate-900 dark:text-white">Add Candidate to Pipeline</h3>
              <button type="button" onClick={() => setAddModalOpen(false)} className="grid h-7 w-7 place-items-center rounded-lg text-slate-400 hover:bg-slate-100 dark:hover:bg-white/10"><X className="h-4 w-4" /></button>
            </div>
            <label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Select Candidate</label>
            <select className="select w-full" value={selectedCandidateId} onChange={e => setSelectedCandidateId(e.target.value)} aria-label="Select candidate">
              {candidates.map(c => <option key={c.id} value={c.id}>{c.fullName || `${c.firstName} ${c.lastName}`} — {c.currentJobTitle}</option>)}
            </select>
            {candidates.length === 0 && <p className="mt-2 text-xs text-rose-500">No candidates in talent pool. Add candidates first.</p>}
            <div className="mt-4 flex justify-end gap-2">
              <button type="button" className="btn-secondary text-sm" onClick={() => setAddModalOpen(false)}>Cancel</button>
              <button type="button" className="btn-primary text-sm" disabled={adding || !selectedCandidateId} onClick={addCandidate}>{adding ? 'Adding…' : 'Add to Pipeline'}</button>
            </div>
          </div>
        </div>
      )}

      {selectedAppId && (
        <ApplicationDrawer
          id={selectedAppId}
          onClose={() => setSelectedAppId(null)}
          onRefresh={load}
        />
      )}
    </div>
  );
}

// ── Add Candidate Modal ────────────────────────────────────────────────────────

function AddCandidateModal({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({
    firstName: '', lastName: '', email: '', phone: '', currentJobTitle: '',
    currentCompany: '', totalExperienceYears: 0, educationLevel: 'Bachelor',
    nationality: '', linkedInUrl: '', source: 'Direct', tags: '',
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const set = (k: keyof typeof form, v: string | number) => setForm(f => ({ ...f, [k]: v }));

  const save = async () => {
    if (!form.firstName || !form.email) { setError('First name and email are required.'); return; }
    setSaving(true); setError('');
    try {
      await candidatesApi.create({ ...form });
      onSaved();
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: { message?: string } } })?.response?.data?.message;
      setError(msg ?? 'Failed to add candidate.');
      setSaving(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-black/40 p-4 pt-12">
      <div className="w-full max-w-md rounded-2xl border border-slate-200 bg-white p-6 shadow-2xl dark:border-white/10 dark:bg-[#0D1221]">
        <div className="mb-4 flex items-center justify-between">
          <h3 className="font-semibold text-slate-900 dark:text-white">Add to Talent Pool</h3>
          <button type="button" onClick={onClose} className="grid h-7 w-7 place-items-center rounded-lg text-slate-400 hover:bg-slate-100 dark:hover:bg-white/10"><X className="h-4 w-4" /></button>
        </div>
        <div className="space-y-3">
          <div className="grid grid-cols-2 gap-3">
            <div><label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">First Name *</label><input className="input w-full" value={form.firstName} onChange={e => set('firstName', e.target.value)} /></div>
            <div><label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Last Name</label><input className="input w-full" value={form.lastName} onChange={e => set('lastName', e.target.value)} /></div>
          </div>
          <div><label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Email *</label><input type="email" className="input w-full" value={form.email} onChange={e => set('email', e.target.value)} /></div>
          <div className="grid grid-cols-2 gap-3">
            <div><label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Phone</label><input className="input w-full" value={form.phone} onChange={e => set('phone', e.target.value)} /></div>
            <div><label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Experience (years)</label><input type="number" className="input w-full" min={0} value={form.totalExperienceYears} onChange={e => set('totalExperienceYears', Number(e.target.value))} aria-label="Experience years" /></div>
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div><label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Current Title</label><input className="input w-full" value={form.currentJobTitle} onChange={e => set('currentJobTitle', e.target.value)} /></div>
            <div><label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Current Company</label><input className="input w-full" value={form.currentCompany} onChange={e => set('currentCompany', e.target.value)} /></div>
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Education</label>
              <select className="select w-full" value={form.educationLevel} onChange={e => set('educationLevel', e.target.value)} aria-label="Education level">
                {['High School', 'Diploma', 'Bachelor', 'Master', 'PhD'].map(l => <option key={l}>{l}</option>)}
              </select>
            </div>
            <div>
              <label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Source</label>
              <select className="select w-full" value={form.source} onChange={e => set('source', e.target.value)} aria-label="Source">
                {['Direct', 'LinkedIn', 'Referral', 'JobBoard', 'Walk-In', 'Agency'].map(s => <option key={s}>{s}</option>)}
              </select>
            </div>
          </div>
          <div><label className="mb-1 block text-xs font-medium text-slate-600 dark:text-slate-400">Nationality</label><input className="input w-full" value={form.nationality} onChange={e => set('nationality', e.target.value)} /></div>
          {error && <p className="text-xs text-rose-500">{error}</p>}
        </div>
        <div className="mt-5 flex justify-end gap-2">
          <button type="button" className="btn-secondary text-sm" onClick={onClose}>Cancel</button>
          <button type="button" className="btn-primary text-sm" disabled={saving} onClick={save}>{saving ? 'Adding…' : 'Add Candidate'}</button>
        </div>
      </div>
    </div>
  );
}

// ── Candidates Tab ─────────────────────────────────────────────────────────────

function CandidatesTab() {
  const [items, setItems] = useState<Candidate[]>([]);
  const [total, setTotal] = useState(0);
  const [search, setSearch] = useState('');
  const [loading, setLoading] = useState(true);
  const [addOpen, setAddOpen] = useState(false);
  const searchRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const load = (q = search) => {
    setLoading(true);
    candidatesApi.list({ search: q || undefined }).then(r => { setItems(r.items); setTotal(r.total); }).catch(() => {}).finally(() => setLoading(false));
  };

  useEffect(() => { load(); }, []);

  const onSearch = (q: string) => {
    setSearch(q);
    if (searchRef.current) clearTimeout(searchRef.current);
    searchRef.current = setTimeout(() => load(q), 300);
  };

  return (
    <div>
      <div className="mb-4 flex items-center gap-3">
        <input className="input w-72" placeholder="Search name, email, title…" value={search} onChange={e => onSearch(e.target.value)} />
        <span className="ml-auto text-xs text-slate-500 dark:text-slate-400">{total} candidates</span>
        <button type="button" className="btn-primary flex items-center gap-1.5 text-sm" onClick={() => setAddOpen(true)}>
          <Plus className="h-3.5 w-3.5" />Add Candidate
        </button>
      </div>

      <div className="overflow-hidden rounded-xl border border-slate-200 dark:border-white/10">
        <table className="min-w-full text-sm">
          <thead className="border-b border-slate-200 bg-slate-50 dark:border-white/10 dark:bg-white/[0.03]">
            <tr>
              {['Name', 'Title / Company', 'Experience', 'Education', 'Source', 'Applied'].map(h => (
                <th key={h} className="px-4 py-3 text-left text-xs font-semibold text-slate-500 dark:text-slate-400">{h}</th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100 dark:divide-white/[0.06]">
            {loading && <tr><td colSpan={6} className="py-10 text-center"><div className="mx-auto h-5 w-5 animate-spin rounded-full border-2 border-sapphire border-t-transparent" /></td></tr>}
            {!loading && items.length === 0 && <tr><td colSpan={6} className="py-10 text-center text-sm text-slate-400 dark:text-slate-500">No candidates yet. Add your first candidate to the talent pool.</td></tr>}
            {!loading && items.map(c => (
              <tr key={c.id} className="hover:bg-slate-50/60 dark:hover:bg-white/[0.02]">
                <td className="px-4 py-3">
                  <p className="font-medium text-slate-800 dark:text-slate-200">{c.fullName || `${c.firstName} ${c.lastName}`}</p>
                  <p className="text-xs text-slate-400 dark:text-slate-500">{c.email}</p>
                </td>
                <td className="px-4 py-3">
                  <p className="text-slate-700 dark:text-slate-300">{c.currentJobTitle || '—'}</p>
                  <p className="text-xs text-slate-400 dark:text-slate-500">{c.currentCompany || ''}</p>
                </td>
                <td className="px-4 py-3 text-slate-600 dark:text-slate-400">{c.totalExperienceYears}y</td>
                <td className="px-4 py-3 text-slate-600 dark:text-slate-400">{c.educationLevel || '—'}</td>
                <td className="px-4 py-3">
                  <span className="rounded-md bg-slate-100 px-2 py-0.5 text-xs text-slate-600 dark:bg-white/10 dark:text-slate-400">{c.source}</span>
                </td>
                <td className="px-4 py-3 text-slate-500 dark:text-slate-400">{c.applicationCount ?? 0}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {addOpen && <AddCandidateModal onClose={() => setAddOpen(false)} onSaved={() => { setAddOpen(false); load(); }} />}
    </div>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────

type Tab = 'overview' | 'requisitions' | 'openings' | 'candidates';

export function RecruitmentPage() {
  const [tab, setTab] = useState<Tab>('overview');
  const [selectedOpening, setSelectedOpening] = useState<JobOpening | null>(null);
  const [requisitionToOpen, setRequisitionToOpen] = useState<ManpowerRequisition | null>(null);

  const tabs: { id: Tab; label: string; icon: React.ComponentType<{ className?: string }> }[] = [
    { id: 'overview', label: 'Overview', icon: ClipboardList },
    { id: 'requisitions', label: 'Requisitions', icon: AlertCircle },
    { id: 'openings', label: 'Job Openings', icon: Briefcase },
    { id: 'candidates', label: 'Talent Pool', icon: Users },
  ];

  const handleCreateOpening = (req: ManpowerRequisition) => {
    setRequisitionToOpen(req);
    setTab('openings');
  };

  return (
    <div className="space-y-5 p-4 sm:p-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900 dark:text-white">Recruitment & Requisitions</h1>
        <p className="mt-0.5 text-sm text-slate-500 dark:text-slate-400">
          Manage manpower requisitions, job openings, and candidate pipeline end-to-end.
        </p>
      </div>

      {/* Tab bar */}
      <div className="flex gap-1 rounded-xl border border-slate-200 bg-slate-50 p-1 dark:border-white/10 dark:bg-white/[0.03]" style={{ width: 'fit-content' }}>
        {tabs.map(t => (
          <button
            key={t.id}
            type="button"
            onClick={() => { setTab(t.id); if (t.id !== 'openings') setSelectedOpening(null); }}
            className={`flex items-center gap-1.5 rounded-lg px-4 py-1.5 text-sm font-medium transition ${
              tab === t.id
                ? 'bg-white text-sapphire shadow-sm dark:bg-white/10 dark:text-cyanAccent'
                : 'text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-slate-200'
            }`}
          >
            <t.icon className="h-3.5 w-3.5" />
            {t.label}
          </button>
        ))}
      </div>

      {/* Content */}
      {tab === 'overview' && <OverviewTab onNavigate={setTab} />}
      {tab === 'requisitions' && <RequisitionsTab onCreateOpening={handleCreateOpening} />}
      {tab === 'openings' && !selectedOpening && (
        <OpeningsTab
          onSelectOpening={setSelectedOpening}
          requisitionToOpen={requisitionToOpen}
          onOpeningCreated={() => setRequisitionToOpen(null)}
        />
      )}
      {tab === 'openings' && selectedOpening && (
        <PipelineView
          opening={selectedOpening}
          onBack={() => setSelectedOpening(null)}
        />
      )}
      {tab === 'candidates' && <CandidatesTab />}
    </div>
  );
}

import { useEffect, useState } from 'react';
import { ChevronLeft, ChevronRight, Plus, Pencil, Trash2, Clock, X } from 'lucide-react';
import { shiftsApi } from '../api/shifts';
import type { ShiftDefinition, RosterEmployee, RosterAssignment } from '../api/shifts';

// ── helpers ──────────────────────────────────────────────────────────────────

function toDateString(d: Date) {
  return d.toISOString().slice(0, 10);
}

function getWeekStart(d: Date) {
  const day = d.getDay();
  const diff = day === 0 ? -6 : 1 - day; // Monday
  const start = new Date(d);
  start.setDate(d.getDate() + diff);
  start.setHours(0, 0, 0, 0);
  return start;
}

function addDays(d: Date, n: number) {
  const r = new Date(d);
  r.setDate(r.getDate() + n);
  return r;
}

const DAY_LABELS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
const DAY_FULL = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

const PRESET_COLORS = ['#2F6BFF', '#00C896', '#5EEBFF', '#f59e0b', '#ef4444', '#8b5cf6', '#ec4899', '#64748b'];

function fmt12(t: string) {
  if (!t) return '';
  const [h, m] = t.split(':').map(Number);
  const ampm = h < 12 ? 'AM' : 'PM';
  const hr = h % 12 || 12;
  return `${hr}:${String(m).padStart(2, '0')} ${ampm}`;
}

// ── Assign Shift Modal ────────────────────────────────────────────────────────

interface AssignModalProps {
  employee: RosterEmployee;
  date: string;
  definitions: ShiftDefinition[];
  existing: RosterAssignment | undefined;
  onClose: () => void;
  onSaved: () => void;
}

function AssignModal({ employee, date, definitions, existing, onClose, onSaved }: AssignModalProps) {
  const [selectedId, setSelectedId] = useState(existing?.shiftDefinitionId ?? definitions[0]?.id ?? '');
  const [notes, setNotes] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const label = new Date(date + 'T00:00:00').toLocaleDateString('en-GB', { weekday: 'long', day: 'numeric', month: 'long' });

  const save = async () => {
    if (!selectedId) return;
    setSaving(true);
    setError('');
    try {
      await shiftsApi.assign({ employeeId: employee.id, shiftDefinitionId: selectedId, date, notes });
      onSaved();
    } catch {
      setError('Failed to assign shift.');
      setSaving(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4">
      <div className="w-full max-w-sm rounded-2xl border border-slate-200 bg-white p-6 shadow-2xl dark:border-white/10 dark:bg-[#0D1221]">
        <div className="mb-4 flex items-start justify-between">
          <div>
            <h3 className="font-semibold text-slate-900 dark:text-white">Assign Shift</h3>
            <p className="mt-0.5 text-xs text-slate-500 dark:text-slate-400">{employee.fullName} · {label}</p>
          </div>
          <button type="button" onClick={onClose} className="grid h-7 w-7 place-items-center rounded-lg text-slate-400 hover:bg-slate-100 dark:hover:bg-white/10">
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="space-y-3">
          <div>
            <label className="mb-1.5 block text-xs font-medium text-slate-700 dark:text-slate-300">Shift</label>
            <select
              className="select w-full"
              value={selectedId}
              onChange={(e) => setSelectedId(e.target.value)}
              aria-label="Select shift"
            >
              {definitions.filter((d) => d.isActive).map((d) => (
                <option key={d.id} value={d.id}>{d.name} ({fmt12(d.startTime)} – {fmt12(d.endTime)})</option>
              ))}
            </select>
          </div>
          <div>
            <label className="mb-1.5 block text-xs font-medium text-slate-700 dark:text-slate-300">Notes (optional)</label>
            <input
              className="input w-full"
              placeholder="Any notes…"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
            />
          </div>
          {error && <p className="text-xs text-red-500">{error}</p>}
        </div>

        <div className="mt-5 flex justify-end gap-2">
          <button type="button" className="btn-secondary text-sm" onClick={onClose}>Cancel</button>
          <button type="button" className="btn-primary text-sm" onClick={save} disabled={saving || !selectedId}>
            {saving ? 'Saving…' : 'Assign'}
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Definition Modal ──────────────────────────────────────────────────────────

interface DefinitionModalProps {
  existing: ShiftDefinition | null;
  onClose: () => void;
  onSaved: () => void;
}

function DefinitionModal({ existing, onClose, onSaved }: DefinitionModalProps) {
  const [form, setForm] = useState({
    code: existing?.code ?? '',
    name: existing?.name ?? '',
    startTime: existing?.startTime?.slice(0, 5) ?? '08:00',
    endTime: existing?.endTime?.slice(0, 5) ?? '16:00',
    breakMinutes: existing?.breakMinutes ?? 60,
    color: existing?.color ?? '#2F6BFF',
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const set = (k: keyof typeof form, v: string | number) => setForm((f) => ({ ...f, [k]: v }));

  const save = async () => {
    if (!form.code || !form.name) { setError('Code and Name are required.'); return; }
    setSaving(true);
    setError('');
    try {
      if (existing) {
        await shiftsApi.updateDefinition(existing.id, form);
      } else {
        await shiftsApi.createDefinition(form);
      }
      onSaved();
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: { message?: string } } })?.response?.data?.message;
      setError(msg ?? 'Failed to save shift definition.');
      setSaving(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4">
      <div className="w-full max-w-md rounded-2xl border border-slate-200 bg-white p-6 shadow-2xl dark:border-white/10 dark:bg-[#0D1221]">
        <div className="mb-5 flex items-start justify-between">
          <h3 className="font-semibold text-slate-900 dark:text-white">{existing ? 'Edit Shift' : 'New Shift Definition'}</h3>
          <button type="button" onClick={onClose} className="grid h-7 w-7 place-items-center rounded-lg text-slate-400 hover:bg-slate-100 dark:hover:bg-white/10">
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="space-y-3">
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="mb-1.5 block text-xs font-medium text-slate-700 dark:text-slate-300">Code</label>
              <input className="input w-full uppercase" placeholder="e.g. MRN" value={form.code} onChange={(e) => set('code', e.target.value.toUpperCase())} maxLength={10} />
            </div>
            <div>
              <label className="mb-1.5 block text-xs font-medium text-slate-700 dark:text-slate-300">Name</label>
              <input className="input w-full" placeholder="e.g. Morning" value={form.name} onChange={(e) => set('name', e.target.value)} />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="mb-1.5 block text-xs font-medium text-slate-700 dark:text-slate-300">Start Time</label>
              <input type="time" className="input w-full" value={form.startTime} onChange={(e) => set('startTime', e.target.value)} aria-label="Start time" />
            </div>
            <div>
              <label className="mb-1.5 block text-xs font-medium text-slate-700 dark:text-slate-300">End Time</label>
              <input type="time" className="input w-full" value={form.endTime} onChange={(e) => set('endTime', e.target.value)} aria-label="End time" />
            </div>
          </div>

          <div>
            <label className="mb-1.5 block text-xs font-medium text-slate-700 dark:text-slate-300">Break (minutes)</label>
            <input type="number" className="input w-full" min={0} max={120} value={form.breakMinutes} onChange={(e) => set('breakMinutes', Number(e.target.value))} aria-label="Break minutes" />
          </div>

          <div>
            <label className="mb-1.5 block text-xs font-medium text-slate-700 dark:text-slate-300">Color</label>
            <div className="flex flex-wrap gap-2">
              {PRESET_COLORS.map((c) => (
                <button
                  key={c}
                  type="button"
                  aria-label={`Select color ${c}`}
                  onClick={() => set('color', c)}
                  className={`h-7 w-7 rounded-full border-2 transition ${form.color === c ? 'border-white scale-110 shadow-lg' : 'border-transparent'}`}
                  style={{ backgroundColor: c }}
                />
              ))}
            </div>
          </div>
          {error && <p className="text-xs text-red-500">{error}</p>}
        </div>

        <div className="mt-5 flex justify-end gap-2">
          <button type="button" className="btn-secondary text-sm" onClick={onClose}>Cancel</button>
          <button type="button" className="btn-primary text-sm" onClick={save} disabled={saving}>
            {saving ? 'Saving…' : existing ? 'Save Changes' : 'Create Shift'}
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Roster Tab ────────────────────────────────────────────────────────────────

interface RosterTabProps {
  definitions: ShiftDefinition[];
}

function RosterTab({ definitions }: RosterTabProps) {
  const [weekStart, setWeekStart] = useState(() => getWeekStart(new Date()));
  const [employees, setEmployees] = useState<RosterEmployee[]>([]);
  const [assignments, setAssignments] = useState<RosterAssignment[]>([]);
  const [loading, setLoading] = useState(true);
  const [assignTarget, setAssignTarget] = useState<{ emp: RosterEmployee; date: string } | null>(null);

  const from = toDateString(weekStart);
  const to = toDateString(addDays(weekStart, 6));
  const days = Array.from({ length: 7 }, (_, i) => addDays(weekStart, i));

  const load = () => {
    setLoading(true);
    shiftsApi.getRoster(from, to)
      .then((r) => { setEmployees(r.employees); setAssignments(r.assignments); })
      .catch(() => {})
      .finally(() => setLoading(false));
  };

  useEffect(() => { load(); }, [from]);

  const assignmentMap = new Map<string, RosterAssignment>();
  for (const a of assignments) {
    assignmentMap.set(`${a.employeeId}|${a.date}`, a);
  }

  const removeAssignment = async (id: string) => {
    await shiftsApi.removeAssignment(id).catch(() => {});
    load();
  };

  const today = toDateString(new Date());
  const weekLabel = `${weekStart.toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })} – ${addDays(weekStart, 6).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })}`;

  return (
    <div>
      {/* Week navigation */}
      <div className="mb-4 flex items-center justify-between">
        <div className="flex items-center gap-3">
          <button
            type="button"
            aria-label="Previous week"
            onClick={() => setWeekStart((w) => addDays(w, -7))}
            className="grid h-8 w-8 place-items-center rounded-lg border border-slate-200 text-slate-500 hover:bg-slate-50 dark:border-white/10 dark:text-slate-400 dark:hover:bg-white/10"
          >
            <ChevronLeft className="h-4 w-4" />
          </button>
          <span className="text-sm font-semibold text-slate-800 dark:text-slate-200">{weekLabel}</span>
          <button
            type="button"
            aria-label="Next week"
            onClick={() => setWeekStart((w) => addDays(w, 7))}
            className="grid h-8 w-8 place-items-center rounded-lg border border-slate-200 text-slate-500 hover:bg-slate-50 dark:border-white/10 dark:text-slate-400 dark:hover:bg-white/10"
          >
            <ChevronRight className="h-4 w-4" />
          </button>
          <button
            type="button"
            onClick={() => setWeekStart(getWeekStart(new Date()))}
            className="rounded-lg border border-slate-200 px-3 py-1 text-xs text-slate-500 hover:bg-slate-50 dark:border-white/10 dark:text-slate-400 dark:hover:bg-white/10"
          >
            This week
          </button>
        </div>

        {/* Legend */}
        <div className="hidden items-center gap-3 md:flex">
          {definitions.filter((d) => d.isActive).map((d) => (
            <div key={d.id} className="flex items-center gap-1.5">
              <span className="h-2.5 w-2.5 rounded-full" style={{ backgroundColor: d.color }} />
              <span className="text-xs text-slate-500 dark:text-slate-400">{d.name}</span>
            </div>
          ))}
        </div>
      </div>

      {/* Grid */}
      <div className="overflow-x-auto rounded-xl border border-slate-200 dark:border-white/10">
        <table className="min-w-full border-collapse text-sm">
          <thead>
            <tr className="border-b border-slate-200 bg-slate-50 dark:border-white/10 dark:bg-white/[0.03]">
              <th className="w-48 px-4 py-3 text-left text-xs font-semibold text-slate-500 dark:text-slate-400">Employee</th>
              {days.map((d, i) => {
                const ds = toDateString(d);
                const isToday = ds === today;
                return (
                  <th
                    key={ds}
                    className={`min-w-[110px] px-3 py-3 text-center text-xs font-semibold ${isToday ? 'text-sapphire dark:text-cyanAccent' : 'text-slate-500 dark:text-slate-400'}`}
                  >
                    <div>{DAY_LABELS[i]}</div>
                    <div className={`mt-0.5 text-[10px] font-normal ${isToday ? 'opacity-100' : 'opacity-60'}`}>
                      {d.toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })}
                    </div>
                  </th>
                );
              })}
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100 dark:divide-white/[0.06]">
            {loading && (
              <tr>
                <td colSpan={8} className="py-12 text-center text-sm text-slate-400 dark:text-slate-500">
                  <div className="mx-auto h-5 w-5 animate-spin rounded-full border-2 border-sapphire border-t-transparent" />
                </td>
              </tr>
            )}
            {!loading && employees.length === 0 && (
              <tr>
                <td colSpan={8} className="py-12 text-center text-sm text-slate-400 dark:text-slate-500">No active employees found.</td>
              </tr>
            )}
            {!loading && employees.map((emp) => (
              <tr key={emp.id} className="group hover:bg-slate-50/60 dark:hover:bg-white/[0.02]">
                <td className="px-4 py-3">
                  <p className="text-xs font-semibold text-slate-800 dark:text-white">{emp.fullName}</p>
                  <p className="text-[10px] text-slate-400 dark:text-slate-500">{emp.department}</p>
                </td>
                {days.map((d) => {
                  const ds = toDateString(d);
                  const a = assignmentMap.get(`${emp.id}|${ds}`);
                  return (
                    <td key={ds} className="px-2 py-2 text-center">
                      {a ? (
                        <div
                          className="group/cell relative inline-flex items-center gap-1 rounded-lg px-2 py-1 text-[11px] font-semibold text-white"
                          style={{ backgroundColor: a.shiftColor }}
                        >
                          {a.shiftCode}
                          <button
                            type="button"
                            aria-label="Remove shift assignment"
                            onClick={() => removeAssignment(a.id)}
                            className="ml-0.5 hidden rounded-full bg-white/20 p-0.5 hover:bg-white/40 group-hover/cell:inline-flex"
                          >
                            <X className="h-2.5 w-2.5" />
                          </button>
                        </div>
                      ) : (
                        <button
                          type="button"
                          aria-label={`Assign shift to ${emp.fullName} on ${DAY_FULL[days.indexOf(d)]}`}
                          onClick={() => definitions.length > 0 && setAssignTarget({ emp, date: ds })}
                          className="inline-flex h-7 w-7 items-center justify-center rounded-lg border border-dashed border-slate-200 text-slate-300 opacity-0 transition hover:border-sapphire hover:text-sapphire group-hover:opacity-100 dark:border-white/10 dark:text-slate-600 dark:hover:border-cyanAccent dark:hover:text-cyanAccent"
                        >
                          <Plus className="h-3 w-3" />
                        </button>
                      )}
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {assignTarget && (
        <AssignModal
          employee={assignTarget.emp}
          date={assignTarget.date}
          definitions={definitions}
          existing={assignmentMap.get(`${assignTarget.emp.id}|${assignTarget.date}`)}
          onClose={() => setAssignTarget(null)}
          onSaved={() => { setAssignTarget(null); load(); }}
        />
      )}
    </div>
  );
}

// ── Definitions Tab ───────────────────────────────────────────────────────────

interface DefinitionsTabProps {
  definitions: ShiftDefinition[];
  onRefresh: () => void;
}

function DefinitionsTab({ definitions, onRefresh }: DefinitionsTabProps) {
  const [modal, setModal] = useState<{ open: boolean; editing: ShiftDefinition | null }>({ open: false, editing: null });
  const [deleting, setDeleting] = useState<string | null>(null);

  const deleteDefinition = async (id: string) => {
    setDeleting(id);
    await shiftsApi.deleteDefinition(id).catch(() => {});
    setDeleting(null);
    onRefresh();
  };

  return (
    <div>
      <div className="mb-4 flex items-center justify-between">
        <p className="text-sm text-slate-500 dark:text-slate-400">{definitions.length} shift type{definitions.length !== 1 ? 's' : ''} defined</p>
        <button
          type="button"
          className="btn-primary flex items-center gap-1.5 text-sm"
          onClick={() => setModal({ open: true, editing: null })}
        >
          <Plus className="h-3.5 w-3.5" />
          New Shift
        </button>
      </div>

      <div className="space-y-3">
        {definitions.length === 0 && (
          <div className="rounded-xl border border-dashed border-slate-200 py-12 text-center dark:border-white/10">
            <Clock className="mx-auto mb-2 h-8 w-8 text-slate-200 dark:text-slate-700" />
            <p className="text-sm text-slate-400 dark:text-slate-500">No shift definitions yet. Create your first shift type.</p>
          </div>
        )}
        {definitions.map((d) => (
          <div key={d.id} className="flex items-center gap-4 rounded-xl border border-slate-200 bg-white px-4 py-3.5 dark:border-white/10 dark:bg-white/[0.03]">
            <div className="h-10 w-10 shrink-0 rounded-xl" style={{ backgroundColor: d.color }} />
            <div className="flex-1 min-w-0">
              <div className="flex items-center gap-2">
                <p className="font-semibold text-slate-800 dark:text-white">{d.name}</p>
                <span className="rounded-md bg-slate-100 px-1.5 py-0.5 font-mono text-[10px] text-slate-500 dark:bg-white/10 dark:text-slate-400">{d.code}</span>
                {!d.isActive && <span className="rounded-full bg-rose-50 px-2 py-0.5 text-[10px] text-rose-500 dark:bg-rose-500/10">Inactive</span>}
              </div>
              <p className="mt-0.5 text-xs text-slate-500 dark:text-slate-400">
                {fmt12(d.startTime)} – {fmt12(d.endTime)} · {d.breakMinutes}m break
              </p>
            </div>
            <div className="flex shrink-0 items-center gap-1">
              <button
                type="button"
                aria-label="Edit shift definition"
                onClick={() => setModal({ open: true, editing: d })}
                className="grid h-8 w-8 place-items-center rounded-lg border border-slate-200 text-slate-500 hover:bg-slate-50 dark:border-white/10 dark:text-slate-400 dark:hover:bg-white/10"
              >
                <Pencil className="h-3.5 w-3.5" />
              </button>
              <button
                type="button"
                aria-label="Delete shift definition"
                onClick={() => deleteDefinition(d.id)}
                disabled={deleting === d.id}
                className="grid h-8 w-8 place-items-center rounded-lg border border-slate-200 text-slate-500 hover:border-rose-300 hover:bg-rose-50 hover:text-rose-500 disabled:opacity-40 dark:border-white/10 dark:text-slate-400 dark:hover:border-rose-500/30 dark:hover:bg-rose-500/10 dark:hover:text-rose-400"
              >
                <Trash2 className="h-3.5 w-3.5" />
              </button>
            </div>
          </div>
        ))}
      </div>

      {modal.open && (
        <DefinitionModal
          existing={modal.editing}
          onClose={() => setModal({ open: false, editing: null })}
          onSaved={() => { setModal({ open: false, editing: null }); onRefresh(); }}
        />
      )}
    </div>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────

type Tab = 'roster' | 'definitions';

export function ShiftsPage() {
  const [tab, setTab] = useState<Tab>('roster');
  const [definitions, setDefinitions] = useState<ShiftDefinition[]>([]);
  const [defsLoading, setDefsLoading] = useState(true);

  const loadDefinitions = () => {
    setDefsLoading(true);
    shiftsApi.listDefinitions()
      .then(setDefinitions)
      .catch(() => {})
      .finally(() => setDefsLoading(false));
  };

  useEffect(() => { loadDefinitions(); }, []);

  return (
    <div className="space-y-5 p-4 sm:p-6">
      {/* Header */}
      <div>
        <h1 className="text-xl font-bold text-slate-900 dark:text-white">Shifts & Rosters</h1>
        <p className="mt-0.5 text-sm text-slate-500 dark:text-slate-400">Manage shift definitions and assign employees to weekly schedules.</p>
      </div>

      {/* Tabs */}
      <div className="flex gap-1 rounded-xl border border-slate-200 bg-slate-50 p-1 dark:border-white/10 dark:bg-white/[0.03]" style={{ width: 'fit-content' }}>
        {(['roster', 'definitions'] as Tab[]).map((t) => (
          <button
            key={t}
            type="button"
            onClick={() => setTab(t)}
            className={`rounded-lg px-4 py-1.5 text-sm font-medium transition ${
              tab === t
                ? 'bg-white text-sapphire shadow-sm dark:bg-white/10 dark:text-cyanAccent'
                : 'text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-slate-200'
            }`}
          >
            {t === 'roster' ? 'Weekly Roster' : 'Shift Definitions'}
          </button>
        ))}
      </div>

      {/* Content */}
      {defsLoading ? (
        <div className="flex justify-center py-12">
          <div className="h-5 w-5 animate-spin rounded-full border-2 border-sapphire border-t-transparent" />
        </div>
      ) : tab === 'roster' ? (
        <RosterTab definitions={definitions} />
      ) : (
        <DefinitionsTab definitions={definitions} onRefresh={loadDefinitions} />
      )}
    </div>
  );
}

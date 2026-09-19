import { ArrowRight, Pencil } from 'lucide-react';
import type { LogisticsOrder, LogisticsRoute, LogisticsStop } from '@/services/logisticsApi';
import { StatusBadge } from '@/components/ui';

const terminalOrder = (status: string) => status === 'Delivered' || status === 'Returned';
const terminalRoute = (status: string) => status === 'Closed' || status === 'Completed';
const terminalStop = (status: string) => status === 'Delivered';

export function DispatchOrderRow({ order, onDispatch, onEdit, saving, canAssign, canUpdate }: { order: LogisticsOrder; onDispatch: () => void; onEdit: () => void; saving: boolean; canAssign: boolean; canUpdate: boolean }) {
  const dispatchable = order.status === 'Queued' && Boolean(order.routeCode?.trim() && order.driverName?.trim() && order.vehicleNumber?.trim());
  const promisedDate = order.promisedAtUtc ? new Date(order.promisedAtUtc) : null;
  const promisedAt = promisedDate && !Number.isNaN(promisedDate.getTime())
    ? promisedDate.toLocaleString([], { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' })
    : 'No promised time';

  return (
    <article className="grid min-w-0 gap-2 px-3 py-2.5 hover:bg-slate-50 lg:grid-cols-[minmax(180px,1.35fr)_110px_minmax(180px,1fr)_150px_auto] lg:items-center">
      <div className="min-w-0">
        <p className="truncate text-sm font-bold text-slate-950">{order.orderNumber}</p>
        <p className="truncate text-xs text-slate-500">{order.customerName}{order.city ? ` · ${order.city}` : ''}</p>
      </div>
      <div className="flex items-center gap-2 lg:block">
        <StatusBadge status={order.status} />
        <p className="text-[11px] font-semibold text-slate-500 lg:mt-1">{order.priority} priority</p>
      </div>
      <div className="min-w-0 text-xs text-slate-600">
        <p className="truncate font-semibold">{order.routeCode?.trim() || 'Route unassigned'}</p>
        <p className="truncate text-slate-500">{order.driverName?.trim() || 'Driver pending'} · {order.vehicleNumber?.trim() || 'Vehicle pending'}</p>
      </div>
      <p className="text-xs text-slate-500">{promisedAt}</p>
      {(canAssign || canUpdate) && !terminalOrder(order.status) ? (
        <div className="flex flex-wrap gap-2 lg:justify-end">
          {canUpdate && <button type="button" className="btn-ghost btn-compact" disabled={saving} onClick={onEdit}><Pencil className="h-3.5 w-3.5" /> Edit</button>}
          {canAssign && (
            <button
              type="button"
              className="btn-primary btn-compact"
              disabled={saving || order.status !== 'Queued'}
              title={!dispatchable && order.status === 'Queued' ? 'Complete route, driver, and vehicle assignment first' : undefined}
              onClick={dispatchable ? onDispatch : onEdit}
            >
              {saving ? 'Dispatching…' : dispatchable ? 'Dispatch' : 'Assign'}
            </button>
          )}
        </div>
      ) : <span className="text-right text-xs text-slate-400">No action</span>}
    </article>
  );
}

export function ActionOrderCard({ order, onDispatch, onEdit, saving, canAssign, canUpdate }: { order: LogisticsOrder; onDispatch: () => void; onEdit: () => void; saving: boolean; canAssign: boolean; canUpdate: boolean }) {
  const dispatchable = order.status === 'Queued' && Boolean(order.routeCode?.trim() && order.driverName?.trim() && order.vehicleNumber?.trim());
  return (
    <div className="rounded-[24px] border border-slate-200/70 bg-[linear-gradient(180deg,rgba(255,255,255,0.92),rgba(245,248,255,0.78))] p-4 shadow-[0_10px_24px_rgba(37,99,235,0.05)] dark:border-white/10 dark:bg-white/[0.04]">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-[12px] font-black tracking-tight text-slate-950 dark:text-white">{order.orderNumber}</p>
          <p className="text-[11px] text-slate-500 dark:text-slate-400">{order.customerName} · {order.city}</p>
        </div>
        <span className="rounded-full border border-slate-200/70 bg-white px-2.5 py-1 text-[9px] font-semibold uppercase tracking-[0.18em] text-slate-500 dark:border-white/10 dark:bg-white/[0.05] dark:text-slate-300">
          {order.status}
        </span>
      </div>
      <div className="mt-3 flex flex-wrap items-center gap-2 text-[10px] font-semibold uppercase tracking-[0.18em] text-slate-400">
        <span>{order.routeCode}</span>
        <span>•</span>
        <span>{order.priority}</span>
        <span>•</span>
        <span>{order.driverName}</span>
      </div>
      <p className="mt-3 text-[11px] leading-relaxed text-slate-500 dark:text-slate-400">
        {order.priority === 'High' || order.priority === 'Critical'
          ? 'Priority order with visible service impact if dispatch slips.'
          : 'Ready for operational assignment and route ownership.'}
      </p>
      {(canAssign || canUpdate) && !terminalOrder(order.status) && (
        <div className={`mt-4 grid gap-2 ${canAssign && canUpdate ? 'grid-cols-2' : 'grid-cols-1'}`}>
          {canUpdate && <button type="button" onClick={onEdit} disabled={saving} className="inline-flex items-center justify-center gap-2 rounded-2xl border border-slate-200 bg-white px-4 py-3 text-xs font-bold text-slate-700 disabled:opacity-50 dark:border-white/10 dark:bg-white/[0.05] dark:text-white"><Pencil className="h-3.5 w-3.5" /> Edit</button>}
          {canAssign && <button type="button" onClick={dispatchable ? onDispatch : onEdit} disabled={saving || order.status !== 'Queued'} title={!dispatchable && order.status === 'Queued' ? 'Complete route, driver, and vehicle assignment first' : undefined} className="inline-flex items-center justify-center gap-2 rounded-2xl bg-gradient-to-r from-blue-600 via-sky-500 to-cyan-400 px-4 py-3 text-xs font-bold text-white disabled:cursor-not-allowed disabled:opacity-50">
            {saving ? 'Dispatching...' : dispatchable ? 'Dispatch' : 'Complete assignment'} <ArrowRight className="h-4 w-4" />
          </button>}
        </div>
      )}
    </div>
  );
}

export function ActionRouteCard({ route, onAdvance, onInspect, onEdit, saving, canUpdate }: { route: LogisticsRoute; onAdvance: () => void; onInspect: () => void; onEdit: () => void; saving: boolean; canUpdate: boolean }) {
  const canProgress = canUpdate && ['Ready', 'Active', 'Delayed'].includes(route.status) && route.plannedStops > route.completedStops;
  return (
    <div className="rounded-[24px] border border-slate-200/70 bg-[linear-gradient(180deg,rgba(255,255,255,0.92),rgba(245,248,255,0.78))] p-4 shadow-[0_10px_24px_rgba(37,99,235,0.05)] dark:border-white/10 dark:bg-white/[0.04]">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-[12px] font-black tracking-tight text-slate-950 dark:text-white">{route.routeCode}</p>
          <p className="text-[11px] text-slate-500 dark:text-slate-400">{route.hub} · {route.territory}</p>
        </div>
        <span className="rounded-full border border-slate-200/70 bg-white px-2.5 py-1 text-[9px] font-semibold uppercase tracking-[0.18em] text-slate-500 dark:border-white/10 dark:bg-white/[0.05] dark:text-slate-300">
          {route.status}
        </span>
      </div>
      <div className="mt-3 grid grid-cols-3 gap-2 text-[10px] text-slate-400">
        <span>{route.completedStops}/{route.plannedStops} stops</span>
        <span>{route.distanceKm.toFixed(1)} km</span>
        <span>{route.driverName}</span>
      </div>
      <p className="mt-3 text-[11px] leading-relaxed text-slate-500 dark:text-slate-400">
        {route.nextStop
          ? `Next operational handoff is ${route.nextStop}.`
          : 'Planner view is ready for next-stop progression and route recovery.'}
      </p>
      <div className={`mt-4 grid gap-2 ${canUpdate ? 'grid-cols-3' : 'grid-cols-1'}`}>
        <button
          type="button"
          onClick={onInspect}
          className="inline-flex w-full items-center justify-center gap-2 rounded-2xl border border-slate-200/70 bg-white px-4 py-3 text-[12px] font-bold text-slate-700 transition hover:border-sky-300 hover:text-sky-700 dark:border-white/10 dark:bg-white/[0.05] dark:text-white"
        >
          Inspect stops
        </button>
        {canUpdate && !terminalRoute(route.status) && <button type="button" onClick={onEdit} disabled={saving} className="inline-flex items-center justify-center gap-2 rounded-2xl border border-slate-200 bg-white px-3 py-3 text-[11px] font-bold text-slate-700 disabled:opacity-50 dark:border-white/10 dark:bg-white/[0.05] dark:text-white"><Pencil className="h-3.5 w-3.5" /> Edit</button>}
        {canUpdate && <button
          type="button"
          onClick={onAdvance}
          disabled={saving || !canProgress}
          className="inline-flex w-full items-center justify-center gap-2 rounded-2xl bg-gradient-to-r from-sky-600 via-cyan-500 to-teal-400 px-4 py-3 text-[12px] font-bold text-white shadow-[0_14px_30px_rgba(47,107,255,0.26)] transition hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-60"
        >
          {saving ? 'Advancing...' : 'Advance route'}
          <ArrowRight className="h-4 w-4" />
        </button>}
      </div>
    </div>
  );
}

export function ActionStopCard({ stop, onConfirm, onAttempt, onReschedule, saving, canUpdate, canDeliver }: { stop: LogisticsStop; onConfirm: () => void; onAttempt: () => void; onReschedule: () => void; saving: boolean; canUpdate: boolean; canDeliver: boolean }) {
  const terminal = terminalStop(stop.status);
  return (
    <div className="rounded-[24px] border border-slate-200/70 bg-[linear-gradient(180deg,rgba(255,255,255,0.92),rgba(245,248,255,0.78))] p-4 shadow-[0_10px_24px_rgba(37,99,235,0.05)] dark:border-white/10 dark:bg-white/[0.04]">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-[12px] font-black tracking-tight text-slate-950 dark:text-white">{stop.customerName}</p>
          <p className="text-[11px] text-slate-500 dark:text-slate-400">{stop.addressLine}</p>
        </div>
        <span className="rounded-full border border-slate-200/70 bg-white px-2.5 py-1 text-[9px] font-semibold uppercase tracking-[0.18em] text-slate-500 dark:border-white/10 dark:bg-white/[0.05] dark:text-slate-300">
          {stop.status}
        </span>
      </div>
      <div className="mt-3 flex flex-wrap gap-2 text-[10px] font-semibold uppercase tracking-[0.18em] text-slate-400">
        <span>{stop.routeCode}</span>
        <span>•</span>
        <span>{stop.timeWindow}</span>
        <span>•</span>
        <span>{stop.proofStatus}</span>
      </div>
      <p className="mt-3 text-[11px] leading-relaxed text-slate-500 dark:text-slate-400">
        {stop.status === 'Attempted' || stop.status === 'Rescheduled'
          ? 'This stop has an open exception — record an attempt, deliver or reschedule.'
          : 'Use proof, attempt, or reschedule actions to keep customer visibility current.'}
      </p>
      {!terminal && (canUpdate || canDeliver) && <div className={`mt-4 grid gap-2 ${canUpdate && canDeliver ? 'grid-cols-3' : canUpdate ? 'grid-cols-2' : 'grid-cols-1'}`}>
        {canUpdate && <button
          type="button"
          onClick={onAttempt}
          disabled={saving}
          className="inline-flex w-full items-center justify-center rounded-2xl border border-amber-200/70 bg-amber-50 px-3 py-3 text-[11px] font-bold text-amber-700 transition hover:border-amber-300 disabled:opacity-60"
        >
          Attempt
        </button>}
        {canUpdate && <button
          type="button"
          onClick={onReschedule}
          disabled={saving}
          className="inline-flex w-full items-center justify-center rounded-2xl border border-slate-200/70 bg-white px-3 py-3 text-[11px] font-bold text-slate-700 transition hover:border-sky-300 disabled:opacity-60 dark:border-white/10 dark:bg-white/[0.05] dark:text-white"
        >
          Reschedule
        </button>}
        {canDeliver && <button
          type="button"
          onClick={onConfirm}
          disabled={saving}
          className="inline-flex w-full items-center justify-center gap-2 rounded-2xl bg-gradient-to-r from-emerald-600 via-teal-500 to-cyan-400 px-3 py-3 text-[11px] font-bold text-white shadow-[0_14px_30px_rgba(47,107,255,0.22)] transition hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-60"
        >
          {saving ? 'Saving...' : 'Deliver'}
        </button>}
      </div>}
    </div>
  );
}

export function DarkField({ label, value, onChange, type = 'text', min, max, step, required = false, disabled = false }: { label: string; value: string; onChange: (value: string) => void; type?: string; min?: string; max?: string; step?: string; required?: boolean; disabled?: boolean }) {
  return (
    <label className="block text-[11px] font-semibold text-white/70">
      {label}{required ? ' *' : ''}
      <input type={type} min={min} max={max} step={step} required={required} disabled={disabled} value={value} onChange={(event) => onChange(event.target.value)} className="mt-1 w-full rounded-2xl border border-white/10 bg-white/[0.05] px-3 py-2.5 text-sm text-white outline-none disabled:cursor-not-allowed disabled:opacity-55" />
    </label>
  );
}

export function LightField({ label, value, onChange, type = 'text', min, max, step, required = false, disabled = false }: { label: string; value: string; onChange: (value: string) => void; type?: string; min?: string; max?: string; step?: string; required?: boolean; disabled?: boolean }) {
  return (
    <label className="form-field flex flex-col text-xs font-semibold text-slate-700">
      {label}{required ? ' *' : ''}
      <input type={type} min={min} max={max} step={step} required={required} disabled={disabled} value={value} onChange={(event) => onChange(event.target.value)} className="field disabled:cursor-not-allowed disabled:opacity-55" />
    </label>
  );
}

export function ReadOnlyMessage() {
  return <p className="rounded-2xl border border-white/10 bg-white/[0.04] p-3 text-xs leading-relaxed text-white/65">This is a read-only view. Ask an administrator for the required dispatch create or update permission to change records.</p>;
}

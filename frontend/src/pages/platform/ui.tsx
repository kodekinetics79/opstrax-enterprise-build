import { useState, useRef, type ReactNode } from "react";
import { Loader2 } from "lucide-react";

// Dark, executive-grade primitives for the Platform Admin control plane.
// Kept separate from the tenant app's light ui.tsx so the two surfaces never
// visually bleed into each other.

export function PHeader({ title, eyebrow, description, actions }: {
  title: string; eyebrow?: string; description?: string; actions?: ReactNode;
}) {
  return (
    <div className="panel relative overflow-hidden px-5 py-6 lg:px-6">
      <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(circle_at_top_right,rgba(45,212,191,.12),transparent_34%),radial-gradient(circle_at_bottom_left,rgba(59,130,246,.09),transparent_30%),linear-gradient(180deg,rgba(255,255,255,.18),transparent_26%)]" />
      <div className="relative flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
      <div className="max-w-3xl">
        {eyebrow && (
          <span className="inline-flex items-center gap-2 rounded-full border border-teal-200 bg-teal-50 px-3 py-1 text-[10px] font-black uppercase tracking-[0.26em] text-teal-700">
            {eyebrow}
          </span>
        )}
        <h1 className="mt-3 text-[28px] font-black tracking-tight text-slate-950 md:text-[34px]">{title}</h1>
        {description && <p className="mt-2 max-w-3xl text-[14px] leading-7 text-slate-500">{description}</p>}
      </div>
      {actions && <div className="flex shrink-0 flex-wrap items-center gap-2.5 rounded-[20px] border border-slate-200/80 bg-white/80 p-2 shadow-sm backdrop-blur">{actions}</div>}
      </div>
    </div>
  );
}

// `panel` scoping matters: it applies the light-surface text corrections from
// index.css, so any legacy dark-theme utility (text-slate-100/200/300,
// text-white) nested in platform pages renders readable on the white card.
export function PCard({ children, className = "" }: { children: ReactNode; className?: string }) {
  return (
    <div className={`panel rounded-[20px] ${className}`}>{children}</div>
  );
}

export function PKpi({ label, value, sub, tone = "default" }: {
  label: string; value: ReactNode; sub?: string; tone?: "default" | "good" | "warn" | "bad";
}) {
  // WCAG AA on the light card: 600-level semantic colors, slate-950 default.
  const toneCls = {
    default: "text-slate-950",
    good: "text-emerald-600",
    warn: "text-amber-600",
    bad: "text-red-600",
  }[tone];
  return (
    <PCard className="p-5">
      <p className="text-[10px] font-black uppercase tracking-[0.24em] text-slate-400">{label}</p>
      <p className={`mt-3 text-[30px] font-black tracking-tight ${toneCls}`}>{value}</p>
      {sub && <p className="mt-2 text-xs leading-5 text-slate-500">{sub}</p>}
    </PCard>
  );
}

// Light-surface tone ramps (700-level text on 50-tint) — WCAG AA on PCard.
const TONES: Record<string, string> = {
  active: "border-emerald-200 bg-emerald-50 text-emerald-700",
  paid: "border-emerald-200 bg-emerald-50 text-emerald-700",
  green: "border-emerald-200 bg-emerald-50 text-emerald-700",
  trial: "border-sky-200 bg-sky-50 text-sky-700",
  sent: "border-sky-200 bg-sky-50 text-sky-700",
  draft: "border-slate-200 bg-slate-100 text-slate-600",
  yellow: "border-amber-200 bg-amber-50 text-amber-700",
  past_due: "border-amber-200 bg-amber-50 text-amber-700",
  overdue: "border-red-200 bg-red-50 text-red-700",
  suspended: "border-red-200 bg-red-50 text-red-700",
  cancelled: "border-slate-200 bg-slate-100 text-slate-500",
  red: "border-red-200 bg-red-50 text-red-700",
  manual_contract: "border-violet-200 bg-violet-50 text-violet-700",
  invited: "border-sky-200 bg-sky-50 text-sky-700",
  disabled: "border-red-200 bg-red-50 text-red-700",
};

export function PBadge({ value }: { value?: unknown }) {
  const raw = String(value ?? "").toLowerCase();
  const cls = TONES[raw] ?? "border-slate-200 bg-slate-100 text-slate-600";
  const label = String(value ?? "—").replace(/_/g, " ").replace(/\b\w/g, (m) => m.toUpperCase());
  return (
    <span className={`inline-flex items-center rounded-full border px-2.5 py-[3px] text-[10px] font-bold shadow-sm ${cls}`}>
      {label}
    </span>
  );
}

export function PButton({ children, onClick, variant = "primary", type = "button", disabled }: {
  children: ReactNode; onClick?: () => void; variant?: "primary" | "ghost" | "danger"; type?: "button" | "submit"; disabled?: boolean;
}) {
  const base = "inline-flex items-center justify-center gap-2 rounded-[14px] px-4 py-2 text-sm font-semibold transition disabled:opacity-50";
  const cls = {
    primary: "bg-gradient-to-r from-teal-400 to-cyan-400 text-slate-950 hover:brightness-105 shadow-sm",
    ghost: "border border-slate-300 bg-white text-slate-700 hover:border-slate-400 hover:bg-slate-50",
    danger: "border border-red-500/30 bg-red-50 text-red-700 hover:bg-red-100",
  }[variant];
  return (
    <button type={type} onClick={onClick} disabled={disabled} className={`${base} ${cls}`}>{children}</button>
  );
}

export function PField({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1.5 block text-xs font-semibold uppercase tracking-wider text-slate-500">{label}</span>
      {children}
    </label>
  );
}

export function PInput(props: React.InputHTMLAttributes<HTMLInputElement>) {
  return (
    <input
      {...props}
      className="w-full rounded-[14px] border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-900 placeholder:text-slate-400 outline-none shadow-sm focus:border-teal-400 focus:ring-2 focus:ring-teal-400/15"
    />
  );
}

export function PSelect(props: React.SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <select
      {...props}
      className="w-full rounded-[14px] border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-900 outline-none shadow-sm focus:border-teal-400 focus:ring-2 focus:ring-teal-400/15"
    />
  );
}

export function PLoading() {
  return (
    <div className="flex items-center justify-center gap-2 py-20 text-slate-500">
      <Loader2 className="h-5 w-5 animate-spin" /> Loading…
    </div>
  );
}

export function PEmpty({ title, subtitle }: { title: string; subtitle?: string }) {
  return (
    <PCard className="flex flex-col items-center justify-center p-14 text-center">
      <p className="font-semibold text-slate-800">{title}</p>
      {subtitle && <p className="mt-1.5 max-w-md text-sm leading-6 text-slate-500">{subtitle}</p>}
    </PCard>
  );
}

export function PError({ message }: { message?: string }) {
  return (
    <PCard className="border-red-200 bg-red-50 p-6 text-red-700">
      <p className="font-semibold">Unable to load</p>
      <p className="mt-1 text-sm text-red-600/90">{message ?? "Please try again."}</p>
    </PCard>
  );
}

// Destructive-action confirmation. When `confirmText` is provided the user must
// type it exactly (e.g. the tenant code) before the confirm button enables —
// required by the control-plane standard for cancel/offboard-class actions.
export function PConfirm({ open, title, body, confirmLabel = "Confirm", confirmText, danger = true, busy, onConfirm, onClose }: {
  open: boolean; title: string; body?: ReactNode; confirmLabel?: string; confirmText?: string;
  danger?: boolean; busy?: boolean; onConfirm: () => void; onClose: () => void;
}) {
  const [typed, setTyped] = useState("");
  if (!open) return null;
  const blocked = Boolean(confirmText) && typed !== confirmText;
  return (
    <div className="fixed inset-0 z-60 flex items-center justify-center bg-slate-950/60 backdrop-blur-sm" onClick={onClose}>
      <div
        role="alertdialog"
        aria-modal="true"
        className="w-full max-w-md rounded-[20px] border border-slate-200 bg-white p-6 shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <h2 className="text-lg font-bold text-slate-950">{title}</h2>
        {body && <div className="mt-2 text-sm leading-6 text-slate-600">{body}</div>}
        {confirmText && (
          <div className="mt-4">
            <p className="mb-1.5 text-xs font-semibold uppercase tracking-wider text-slate-500">
              Type <span className="font-mono text-slate-800">{confirmText}</span> to confirm
            </p>
            <PInput value={typed} onChange={(e) => setTyped(e.target.value)} placeholder={confirmText} autoFocus />
          </div>
        )}
        <div className="mt-5 flex justify-end gap-2">
          <PButton variant="ghost" onClick={onClose} disabled={busy}>Cancel</PButton>
          <PButton variant={danger ? "danger" : "primary"} onClick={onConfirm} disabled={busy || blocked}>
            {confirmLabel}
          </PButton>
        </div>
      </div>
    </div>
  );
}

// ─── Bulk selection primitives ────────────────────────────────────────────────
// Shared across every platform table that supports multi-select CRUD (Tenants,
// Invoices, …) so the selection model and action bar behave identically product-
// wide. Selection is keyed by string id. Callers pass the CURRENTLY VISIBLE
// (filtered) ids, so "select all" only ever targets rows the operator can see,
// while any selected id that scrolls out of the active filter is preserved.
export function useRowSelection(visibleIds: unknown[]) {
  const [selected, setSelected] = useState<Set<string>>(new Set());
  // Anchor for shift-click / Shift+Space range selection (the last row toggled).
  const anchorRef = useRef<string | null>(null);
  const ids = visibleIds.map(String);
  const allVisibleSelected = ids.length > 0 && ids.every((id) => selected.has(id));
  const someVisibleSelected = ids.some((id) => selected.has(id));

  // toggle(id, shiftKey): a plain toggle flips one row and sets the anchor. A
  // shift-toggle selects the whole contiguous range between the anchor and this
  // row (Gmail/Finder semantics), so you can pick many rows in two clicks/keys.
  const toggle = (id: unknown, shiftKey = false) => {
    const key = String(id);
    const idx = ids.indexOf(key);
    setSelected((prev) => {
      const next = new Set(prev);
      const anchor = anchorRef.current;
      if (shiftKey && anchor !== null && idx !== -1) {
        const aIdx = ids.indexOf(anchor);
        if (aIdx !== -1) {
          const [lo, hi] = aIdx <= idx ? [aIdx, idx] : [idx, aIdx];
          for (let i = lo; i <= hi; i++) next.add(ids[i]);
          return next;
        }
      }
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
    anchorRef.current = key;
  };

  const toggleAllVisible = () =>
    setSelected((prev) => {
      const next = new Set(prev);
      if (ids.every((id) => prev.has(id))) ids.forEach((id) => next.delete(id));
      else ids.forEach((id) => next.add(id));
      return next;
    });

  return {
    selectedIds: [...selected],
    count: selected.size,
    isSelected: (id: unknown) => selected.has(String(id)),
    toggle,
    toggleAllVisible,
    allVisibleSelected,
    someVisibleSelected,
    clear: () => { anchorRef.current = null; setSelected(new Set()); },
  };
}

// Native checkbox: fully keyboard-operable out of the box (Tab to focus, Space to
// toggle, Shift+Space to range-select). We handle the toggle in onClick so we can
// read `shiftKey` — which is set for both mouse clicks and keyboard Space — and
// keep onChange as a no-op to satisfy React's controlled-input contract.
export function PCheckbox({ checked, indeterminate, onToggle, ariaLabel }: {
  checked: boolean; indeterminate?: boolean; onToggle: (shiftKey: boolean) => void; ariaLabel: string;
}) {
  return (
    <input
      type="checkbox"
      checked={checked}
      ref={(el) => { if (el) el.indeterminate = Boolean(indeterminate) && !checked; }}
      onChange={() => {}}
      onClick={(e) => { e.stopPropagation(); onToggle(e.shiftKey); }}
      aria-label={ariaLabel}
      className="h-4 w-4 cursor-pointer rounded border-slate-300 text-teal-500 focus:ring-2 focus:ring-teal-400/30"
    />
  );
}

// Sticky action bar shown while ≥1 row is selected. `children` are the action
// buttons, already permission-filtered by the caller.
export function PBulkBar({ count, onClear, children }: {
  count: number; onClear: () => void; children: ReactNode;
}) {
  if (count === 0) return null;
  return (
    <div className="sticky bottom-4 z-40 flex flex-wrap items-center gap-3 rounded-[16px] border border-slate-300 bg-white/95 px-4 py-3 shadow-lg backdrop-blur">
      <span className="text-sm font-semibold text-slate-700">{count} selected</span>
      <div className="flex flex-wrap items-center gap-2">{children}</div>
      <button type="button" onClick={onClear} className="ml-auto text-sm font-medium text-slate-500 hover:text-slate-700">Clear</button>
    </div>
  );
}

export function PDrawer({ open, onClose, title, children }: {
  open: boolean; onClose: () => void; title: string; children: ReactNode;
}) {
  if (!open) return null;
  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-slate-950/50 backdrop-blur-sm" onClick={onClose}>
      <aside
        className="panel panel-flush h-full w-full max-w-xl overflow-y-auto border-l border-slate-200 p-6 shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between border-b border-slate-200 pb-4">
          <h2 className="text-lg font-bold text-slate-950">{title}</h2>
          <button type="button" aria-label="Close" onClick={onClose} className="text-slate-500 hover:text-slate-700">✕</button>
        </div>
        <div className="mt-5">{children}</div>
      </aside>
    </div>
  );
}

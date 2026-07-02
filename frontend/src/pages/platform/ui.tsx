import { useState, type ReactNode } from "react";
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
          <span className="inline-flex items-center gap-2 rounded-full border border-teal-400/20 bg-teal-500/10 px-3 py-1 text-[10px] font-black uppercase tracking-[0.26em] text-teal-300">
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

export function PCard({ children, className = "" }: { children: ReactNode; className?: string }) {
  return (
    <div className={`rounded-[20px] border border-slate-200 bg-[linear-gradient(180deg,#ffffff_0%,#f8fbff_100%)] shadow-[0_10px_30px_rgba(15,23,42,.07)] ${className}`}>{children}</div>
  );
}

export function PKpi({ label, value, sub, tone = "default" }: {
  label: string; value: ReactNode; sub?: string; tone?: "default" | "good" | "warn" | "bad";
}) {
  const toneCls = {
    default: "text-white",
    good: "text-emerald-400",
    warn: "text-amber-400",
    bad: "text-red-400",
  }[tone];
  return (
    <PCard className="p-5">
      <p className="text-[10px] font-black uppercase tracking-[0.24em] text-slate-400">{label}</p>
      <p className={`mt-3 text-[30px] font-black tracking-tight ${toneCls}`}>{value}</p>
      {sub && <p className="mt-2 text-xs leading-5 text-slate-500">{sub}</p>}
    </PCard>
  );
}

const TONES: Record<string, string> = {
  active: "border-emerald-400/30 bg-emerald-500/10 text-emerald-300",
  paid: "border-emerald-400/30 bg-emerald-500/10 text-emerald-300",
  green: "border-emerald-400/30 bg-emerald-500/10 text-emerald-300",
  trial: "border-sky-400/30 bg-sky-500/10 text-sky-300",
  sent: "border-sky-400/30 bg-sky-500/10 text-sky-300",
  draft: "border-slate-600 bg-slate-700/40 text-slate-300",
  yellow: "border-amber-400/30 bg-amber-500/10 text-amber-300",
  past_due: "border-amber-400/30 bg-amber-500/10 text-amber-300",
  overdue: "border-red-400/30 bg-red-500/10 text-red-300",
  suspended: "border-red-400/30 bg-red-500/10 text-red-300",
  cancelled: "border-slate-600 bg-slate-700/40 text-slate-400",
  red: "border-red-400/30 bg-red-500/10 text-red-300",
  manual_contract: "border-violet-400/30 bg-violet-500/10 text-violet-300",
};

export function PBadge({ value }: { value?: unknown }) {
  const raw = String(value ?? "").toLowerCase();
  const cls = TONES[raw] ?? "border-slate-600 bg-slate-700/40 text-slate-300";
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

export function PDrawer({ open, onClose, title, children }: {
  open: boolean; onClose: () => void; title: string; children: ReactNode;
}) {
  if (!open) return null;
  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-slate-950/50 backdrop-blur-sm" onClick={onClose}>
      <aside
        className="h-full w-full max-w-xl overflow-y-auto border-l border-slate-200 bg-gradient-to-b from-white to-slate-50 p-6 shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between border-b border-slate-200 pb-4">
          <h2 className="text-lg font-bold text-slate-950">{title}</h2>
          <button onClick={onClose} className="text-slate-500 hover:text-slate-700">✕</button>
        </div>
        <div className="mt-5">{children}</div>
      </aside>
    </div>
  );
}

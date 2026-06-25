import type { ReactNode } from "react";
import { Loader2 } from "lucide-react";

// Dark, executive-grade primitives for the Platform Admin control plane.
// Kept separate from the tenant app's light ui.tsx so the two surfaces never
// visually bleed into each other.

export function PHeader({ title, eyebrow, description, actions }: {
  title: string; eyebrow?: string; description?: string; actions?: ReactNode;
}) {
  return (
    <div className="flex flex-col gap-4 border-b border-slate-800 pb-6 lg:flex-row lg:items-end lg:justify-between">
      <div className="max-w-3xl">
        {eyebrow && (
          <span className="inline-flex items-center gap-2 rounded-full border border-teal-400/25 bg-teal-400/10 px-3 py-1 text-[11px] font-bold uppercase tracking-[0.22em] text-teal-300">
            {eyebrow}
          </span>
        )}
        <h1 className="mt-3 text-2xl font-bold tracking-tight text-white md:text-3xl">{title}</h1>
        {description && <p className="mt-2 text-sm leading-6 text-slate-400">{description}</p>}
      </div>
      {actions && <div className="flex shrink-0 flex-wrap items-center gap-2.5">{actions}</div>}
    </div>
  );
}

export function PCard({ children, className = "" }: { children: ReactNode; className?: string }) {
  return (
    <div className={`rounded-2xl border border-slate-800 bg-slate-900/60 ${className}`}>{children}</div>
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
      <p className="text-xs font-medium uppercase tracking-wider text-slate-500">{label}</p>
      <p className={`mt-2.5 text-3xl font-bold tracking-tight ${toneCls}`}>{value}</p>
      {sub && <p className="mt-2 text-xs text-slate-500">{sub}</p>}
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
    <span className={`inline-flex items-center rounded-full border px-2.5 py-[3px] text-[10px] font-bold ${cls}`}>
      {label}
    </span>
  );
}

export function PButton({ children, onClick, variant = "primary", type = "button", disabled }: {
  children: ReactNode; onClick?: () => void; variant?: "primary" | "ghost" | "danger"; type?: "button" | "submit"; disabled?: boolean;
}) {
  const base = "inline-flex items-center justify-center gap-2 rounded-xl px-4 py-2 text-sm font-semibold transition disabled:opacity-50";
  const cls = {
    primary: "bg-teal-400 text-slate-950 hover:bg-teal-300",
    ghost: "border border-slate-700 bg-slate-800/60 text-slate-200 hover:border-slate-600",
    danger: "border border-red-500/40 bg-red-500/10 text-red-300 hover:bg-red-500/20",
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
      className="w-full rounded-xl border border-slate-700 bg-slate-800/60 px-3 py-2 text-sm text-slate-100 placeholder:text-slate-500 outline-none focus:border-teal-400/60"
    />
  );
}

export function PSelect(props: React.SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <select
      {...props}
      className="w-full rounded-xl border border-slate-700 bg-slate-800/60 px-3 py-2 text-sm text-slate-100 outline-none focus:border-teal-400/60"
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
      <p className="font-semibold text-slate-300">{title}</p>
      {subtitle && <p className="mt-1.5 max-w-md text-sm text-slate-500">{subtitle}</p>}
    </PCard>
  );
}

export function PError({ message }: { message?: string }) {
  return (
    <PCard className="border-red-500/30 bg-red-500/5 p-6 text-red-300">
      <p className="font-semibold">Unable to load</p>
      <p className="mt-1 text-sm text-red-400/80">{message ?? "Please try again."}</p>
    </PCard>
  );
}

export function PDrawer({ open, onClose, title, children }: {
  open: boolean; onClose: () => void; title: string; children: ReactNode;
}) {
  if (!open) return null;
  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/60 backdrop-blur-sm" onClick={onClose}>
      <aside
        className="h-full w-full max-w-xl overflow-y-auto border-l border-slate-800 bg-slate-900 p-6 shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between border-b border-slate-800 pb-4">
          <h2 className="text-lg font-bold text-white">{title}</h2>
          <button onClick={onClose} className="text-slate-500 hover:text-slate-300">✕</button>
        </div>
        <div className="mt-5">{children}</div>
      </aside>
    </div>
  );
}

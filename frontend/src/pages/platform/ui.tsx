import { type ReactNode } from "react";
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

// ─── Bulk selection + confirmation primitives ─────────────────────────────────
// These now live in the shared components/bulk.tsx so the tenant app and the
// platform control plane use ONE selection model (shift-click range select,
// indeterminate header checkbox, sticky action bar, type-to-confirm dialog).
// Re-exported under their original P-names so PlatformTenantsPage,
// PlatformOperatorsPage and PlatformBillingPage keep working unchanged.
export {
  useRowSelection,
  BulkCheckbox as PCheckbox,
  BulkBar as PBulkBar,
  ConfirmDialog as PConfirm,
} from '@/components/bulk';

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

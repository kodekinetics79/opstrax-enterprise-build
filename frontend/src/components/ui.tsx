import { useMemo, useState } from "react";
import type { ReactNode } from "react";
import {
  AlertTriangle, ArrowDownRight, ArrowUpRight,
  Bot, ChevronUp, ChevronDown as ChevronDownIcon,
  Loader2, Minus, Search, SlidersHorizontal,
  Sparkles, TrendingUp, X,
} from "lucide-react";
import type { AnyRecord } from "@/types";

/* ============================================================
   UTILITY
   ============================================================ */
export function exportCsv(name: string, rows: AnyRecord[]) {
  if (!rows.length) return;
  const cols = Array.from(new Set(rows.flatMap((row) => Object.keys(row)))).slice(0, 24);
  const csv = [cols.join(","), ...rows.map((row) => cols.map((c) => JSON.stringify(row[c] ?? "")).join(","))].join("\n");
  const a = document.createElement("a");
  a.href = URL.createObjectURL(new Blob([csv], { type: "text/csv" }));
  const now = new Date();
  const ts = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}_${String(now.getHours()).padStart(2, "0")}-${String(now.getMinutes()).padStart(2, "0")}`;
  a.download = `${name}_${ts}.csv`;
  a.click();
}

export function labelize(value: string) {
  return value
    .replace(/([A-Z])/g, " $1")
    .replace(/_/g, " ")
    .replace(/\b\w/g, (m) => m.toUpperCase())
    .trim();
}

/* ============================================================
   PAGE HEADER
   ============================================================ */
export function PageHeader({
  title, eyebrow, description, actions,
}: {
  title: string; eyebrow?: string; description: string; actions?: ReactNode;
}) {
  return (
    <div className="flex flex-col gap-4 border-b border-slate-200 pb-6 lg:flex-row lg:items-end lg:justify-between">
      <div className="max-w-3xl">
        {eyebrow && (
          <span className="inline-flex items-center gap-2 rounded-full border border-teal-400/25 bg-teal-400/8 px-3 py-1 text-[11px] font-bold uppercase tracking-[0.22em] text-teal-300">
            <span className="live-dot h-1.5 w-1.5" />
            {eyebrow}
          </span>
        )}
        <h1 className="mt-3 text-3xl font-bold tracking-tight text-slate-900 md:text-4xl">{title}</h1>
        <p className="mt-2.5 text-sm leading-6 text-slate-400">{description}</p>
      </div>
      {actions && (
        <div className="flex shrink-0 flex-wrap items-center gap-2.5">{actions}</div>
      )}
    </div>
  );
}

/* ============================================================
   KPI CARD
   ============================================================ */
export function KpiCard({
  label, value, trend, status, delta,
}: {
  label: string; value: ReactNode; trend?: string; status?: string; icon?: ReactNode; delta?: string;
}) {
  const isCritical = /critical|overdue|breach|rejected/i.test(String(label) + String(status));
  const isWarning  = !isCritical && /missing|anomal|unusual|pending|risk/i.test(String(label) + String(status));
  const isUp   = delta?.startsWith("+") || /up|increase|improv/i.test(String(trend));
  const isDown = delta?.startsWith("-") || /down|decreas|drop/i.test(String(trend));
  const valueColor = isCritical && Number(value) > 0
    ? "text-red-600"
    : isWarning && Number(value) > 0
    ? "text-amber-700"
    : "text-slate-900";

  return (
    <div className="panel p-6">
      <p className="text-sm font-medium text-slate-500">{label}</p>
      <p className={`mt-3 text-3xl font-bold tracking-tight ${valueColor}`}>{value}</p>
      {(delta || trend) && (
        <p className="mt-3 flex items-center gap-1 text-xs text-slate-400">
          {isDown ? <ArrowDownRight className="h-3 w-3 text-red-400" /> : isUp ? <ArrowUpRight className="h-3 w-3 text-emerald-500" /> : null}
          {delta ?? trend}
        </p>
      )}
    </div>
  );
}

/* ============================================================
   SKELETON CARD  (drop-in KpiCard loader)
   ============================================================ */
export function SkeletonCard() {
  return (
    <div className="panel flex flex-col justify-between p-5">
      <div className="skeleton h-3 w-20 rounded" />
      <div className="mt-4 skeleton h-8 w-28 rounded-lg" />
      <div className="mt-3 flex items-center justify-between">
        <div className="skeleton h-3 w-16 rounded" />
        <div className="skeleton h-5 w-12 rounded-full" />
      </div>
    </div>
  );
}

/* ============================================================
   STATUS BADGE
   ============================================================ */
export function StatusBadge({ status }: { status?: unknown }) {
  const text = String(status ?? "Open");
  let cls: string;
  let pulse = false;

  if (/critical|failed|breach|expired/i.test(text)) {
    cls = "border-red-400/30 bg-red-500/10 text-red-300"; pulse = true;
  } else if (/risk|anomaly|overdue|missing|rejected/i.test(text)) {
    cls = "border-red-400/20 bg-red-500/8 text-red-300";
  } else if (/warning|review|pending|near|expiring|at.risk/i.test(text)) {
    cls = "border-amber-400/28 bg-amber-500/10 text-amber-300";
  } else if (/complete|healthy|active|valid|sent|passed|available|connected|approved|compliant/i.test(text)) {
    cls = "border-emerald-400/28 bg-emerald-500/10 text-emerald-300";
  } else if (/ai|intelligence|predict/i.test(text)) {
    cls = "border-violet-400/28 bg-violet-500/10 text-violet-300";
  } else {
    cls = "border-slate-300 bg-slate-50 text-slate-600";
  }

  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-[3px] text-[10px] font-bold ${cls}`}>
      {pulse && <span className="live-dot h-1.5 w-1.5" />}
      {text}
    </span>
  );
}

/* ============================================================
   RISK BADGE
   ============================================================ */
export function RiskBadge({ risk }: { risk?: unknown }) {
  const text = String(risk ?? "Low");
  const cls = /critical/i.test(text)
    ? "border-red-400/35 bg-red-500/12 text-red-300 font-extrabold"
    : /high/i.test(text)
    ? "border-red-400/25 bg-red-500/8 text-red-300"
    : /medium|warning/i.test(text)
    ? "border-amber-400/30 bg-amber-500/10 text-amber-300"
    : "border-emerald-400/25 bg-emerald-500/8 text-emerald-300";
  return (
    <span className={`inline-flex items-center rounded-full border px-2.5 py-[3px] text-[10px] font-bold ${cls}`}>
      {text}
    </span>
  );
}

/* ============================================================
   SCORE RING  (SVG donut)
   ============================================================ */
export function ScoreRing({
  score, size = 64, strokeWidth = 5, color = "#2dd4bf", label,
}: {
  score: number; size?: number; strokeWidth?: number; color?: string; label?: string;
}) {
  const r = (size - strokeWidth * 2) / 2;
  const circ = 2 * Math.PI * r;
  const offset = circ - (Math.min(Math.max(score, 0), 100) / 100) * circ;
  return (
    <div className="relative inline-flex items-center justify-center" style={{ width: size, height: size }}>
      <svg width={size} height={size} style={{ transform: "rotate(-90deg)" }}>
        <circle cx={size / 2} cy={size / 2} r={r} strokeWidth={strokeWidth} className="score-ring-track" />
        <circle
          cx={size / 2} cy={size / 2} r={r}
          strokeWidth={strokeWidth}
          className="score-ring-fill"
          stroke={color}
          strokeDasharray={circ}
          strokeDashoffset={offset}
        />
      </svg>
      <div className="absolute flex flex-col items-center">
        <span className="text-xs font-bold text-slate-800">{score}</span>
        {label && <span className="text-[9px] text-slate-500 mt-0.5">{label}</span>}
      </div>
    </div>
  );
}

/* ============================================================
   PROGRESS BAR
   ============================================================ */
export function ProgressBar({
  value, max = 100, label, color = "var(--teal)",
}: {
  value: number; max?: number; label?: string; color?: string;
}) {
  const pct = Math.min((value / max) * 100, 100);
  return (
    <div className="w-full">
      {label && (
        <div className="mb-1.5 flex items-center justify-between">
          <span className="text-xs text-slate-400">{label}</span>
          <span className="text-xs font-bold text-slate-800">{Math.round(pct)}%</span>
        </div>
      )}
      <div className="progress-track">
        <div className="progress-fill" style={{ width: `${pct}%`, background: color }} />
      </div>
    </div>
  );
}

/* ============================================================
   DATA TABLE  (sortable, count badge)
   ============================================================ */
export function DataTable({
  rows, columns, onSelect,
}: {
  rows: AnyRecord[]; columns: string[]; onSelect?: (row: AnyRecord) => void;
}) {
  const [sortKey, setSortKey] = useState<string | null>(null);
  const [sortDir, setSortDir] = useState<"asc" | "desc">("asc");
  const [search, setSearch] = useState("");

  const filtered = useMemo(() => {
    if (!search.trim()) return rows;
    const q = search.toLowerCase();
    return rows.filter((row) => columns.some((col) => String(row[col] ?? "").toLowerCase().includes(q)));
  }, [rows, columns, search]);

  const sorted = useMemo(() => {
    if (!sortKey) return filtered;
    return [...filtered].sort((a, b) => {
      const va = String(a[sortKey] ?? "");
      const vb = String(b[sortKey] ?? "");
      const cmp = va.localeCompare(vb, undefined, { numeric: true, sensitivity: "base" });
      return sortDir === "asc" ? cmp : -cmp;
    });
  }, [filtered, sortKey, sortDir]);

  const handleSort = (col: string) => {
    if (sortKey === col) setSortDir((d) => (d === "asc" ? "desc" : "asc"));
    else { setSortKey(col); setSortDir("asc"); }
  };

  return (
    <div className="panel overflow-hidden">
      {/* Table toolbar */}
      <div className="flex flex-col gap-3 border-b border-slate-100 px-5 py-3.5 md:flex-row md:items-center md:justify-between">
        <div className="relative max-w-xs flex-1">
          <Search className="absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-slate-500" />
          <input
            className="field h-9 py-0 pl-9 pr-3 text-sm"
            placeholder="Search records..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
        <div className="flex items-center gap-2">
          <span className="rounded-full border border-slate-200 bg-slate-50 px-2.5 py-1 text-[11px] font-bold text-slate-500">
            {filtered.length === rows.length ? `${rows.length} records` : `${filtered.length} of ${rows.length}`}
          </span>
          <button className="btn-ghost h-9 py-0"><SlidersHorizontal className="h-3.5 w-3.5" /> Filters</button>
        </div>
      </div>

      <div className="overflow-x-auto">
        <table className="w-full min-w-[760px] text-left text-sm">
          <thead className="border-b border-slate-100 bg-slate-50">
            <tr>
              {columns.map((col) => {
                const isActive = sortKey === col;
                return (
                  <th
                    key={col}
                    onClick={() => handleSort(col)}
                    className={`sortable px-5 py-3.5 text-xs font-semibold uppercase tracking-wider transition ${
                      isActive ? "sort-active text-slate-700" : "text-slate-500"
                    }`}
                  >
                    <span className="flex items-center gap-1.5">
                      {labelize(col)}
                      <span className="sort-icon">
                        {isActive
                          ? sortDir === "asc"
                            ? <ChevronUp className="h-3 w-3" />
                            : <ChevronDownIcon className="h-3 w-3" />
                          : <ChevronUp className="h-3 w-3 opacity-0 group-hover:opacity-40" />}
                      </span>
                    </span>
                  </th>
                );
              })}
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {sorted.length === 0 ? (
              <tr>
                <td colSpan={columns.length} className="px-5 py-12 text-center text-sm text-slate-500">
                  No records found
                </td>
              </tr>
            ) : (
              sorted.map((row, index) => (
                <tr
                  key={String(row.id ?? index)}
                  onClick={() => onSelect?.(row)}
                  className="group cursor-pointer transition-colors hover:bg-slate-50"
                >
                  {columns.map((col) => (
                    <td key={col} className="px-5 py-3.5 text-slate-600">
                      {renderCell(col, row[col])}
                    </td>
                  ))}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {sorted.length > 0 && (
        <div className="flex items-center justify-between border-t border-slate-100 px-5 py-2.5">
          <span className="text-xs text-slate-600">Showing {sorted.length} of {rows.length} records</span>
          <span className="text-xs text-slate-600">Click a row to view details · Click column headers to sort</span>
        </div>
      )}
    </div>
  );
}

/* ============================================================
   FILTER BAR
   ============================================================ */
export function FilterBar({ children }: { children?: ReactNode }) {
  return (
    <div className="panel flex flex-wrap items-center gap-2.5 p-3.5">
      {children ||
        ["All", "Active", "At Risk", "Completed", "Pending"].map((x) => (
          <button key={x} className="btn-ghost py-1.5 text-xs">{x}</button>
        ))}
    </div>
  );
}

/* ============================================================
   DETAIL DRAWER  (generic slot, used outside Batch pages)
   ============================================================ */
export function DetailDrawer({ record, onClose }: { record: AnyRecord | null; onClose: () => void }) {
  if (!record) return null;
  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/55 backdrop-blur-sm anim-fade-in">
      <aside className="anim-slide-right h-full w-full max-w-lg overflow-y-auto border-l border-slate-200 bg-white p-6 shadow-2xl">
        <button className="float-right icon-btn" onClick={onClose}><X className="h-4 w-4" /></button>
        <p className="section-title text-teal-700">OpsTrax Detail</p>
        <h2 className="mt-3 text-2xl font-bold text-slate-900">
          {String(record.title || record.name || record.vehicleCode || record.driverCode || record.jobCode || `Record ${record.id}`)}
        </h2>
        <div className="mt-6 space-y-2">
          {Object.entries(record).slice(0, 24).map(([key, value]) => (
            <div key={key} className="flex items-start justify-between gap-3 rounded-xl border border-slate-100 bg-slate-50 px-4 py-2.5">
              <p className="text-[11px] uppercase tracking-[0.16em] text-slate-500 mt-0.5">{labelize(key)}</p>
              <p className="text-sm text-slate-700 text-right break-all">{String(value ?? "--")}</p>
            </div>
          ))}
        </div>
      </aside>
    </div>
  );
}

/* ============================================================
   LOADING STATE  (skeleton rows)
   ============================================================ */
export function LoadingState() {
  return (
    <div className="space-y-3">
      {/* KPI skeleton row */}
      <div className="grid gap-3 sm:grid-cols-2 md:grid-cols-4">
        {[...Array(4)].map((_, i) => <SkeletonCard key={i} />)}
      </div>
      {/* Table skeleton */}
      <div className="panel p-5 space-y-3">
        <div className="flex items-center gap-3">
          <div className="skeleton h-9 w-64 rounded-xl" />
          <div className="skeleton h-9 w-24 rounded-xl ml-auto" />
        </div>
        {[...Array(6)].map((_, i) => (
          <div key={i} className="flex items-center gap-4 rounded-xl border border-slate-100 bg-slate-50/50 px-4 py-3">
            <div className="skeleton h-4 w-24 shrink-0" />
            <div className="skeleton h-4 flex-1" />
            <div className="skeleton h-4 w-16" />
            <div className="skeleton h-5 w-14 rounded-full" />
          </div>
        ))}
      </div>
    </div>
  );
}

/* ============================================================
   ERROR / EMPTY STATES
   ============================================================ */
export function ErrorState({ message }: { message?: string }) {
  return (
    <div className="panel flex items-center gap-3 border-red-400/25 bg-red-500/5 p-6 text-red-300">
      <AlertTriangle className="h-5 w-5 shrink-0" />
      <div>
        <p className="font-semibold">Unable to load data</p>
        <p className="text-sm text-red-400/80">{message || "Check your connection and try again."}</p>
      </div>
    </div>
  );
}

export function EmptyState({ title = "No records found", subtitle }: { title?: string; subtitle?: string }) {
  return (
    <div className="panel flex flex-col items-center justify-center p-14 text-center">
      <div className="mb-4 flex h-14 w-14 items-center justify-center rounded-2xl border border-slate-200 bg-slate-50 text-slate-400">
        <Search className="h-6 w-6" />
      </div>
      <p className="font-semibold text-slate-600">{title}</p>
      {subtitle && <p className="mt-1.5 max-w-xs text-sm text-slate-500">{subtitle}</p>}
    </div>
  );
}

/* ============================================================
   AI INSIGHT CARD
   ============================================================ */
export function AiInsightCard({ insight }: { insight: AnyRecord }) {
  const score = Number(insight.score || insight.confidence || 0);
  return (
    <div className="relative overflow-hidden rounded-2xl border border-violet-200 bg-violet-50/60 p-4">
      {/* Glow blob */}
      <div className="pointer-events-none absolute -right-8 -top-8 h-24 w-24 rounded-full bg-violet-500/12 blur-2xl" />
      <div className="relative">
        <div className="flex items-center justify-between gap-2">
          <div className="flex items-center gap-2">
            <div className="flex h-7 w-7 items-center justify-center rounded-lg bg-violet-100 border border-violet-200">
              <Sparkles className="h-3.5 w-3.5 text-violet-600" />
            </div>
            <span className="text-[10px] font-extrabold uppercase tracking-[0.2em] text-violet-600">System Fleet Insight</span>
          </div>
          {score > 0 && (
            <span className="text-[10px] font-bold text-violet-400/70">{score}% confidence</span>
          )}
        </div>
        <h3 className="mt-2.5 text-sm font-bold text-slate-800 leading-snug">
          {String(insight.title || insight.recommendation || "Recommended action")}
        </h3>
        <p className="mt-1.5 text-xs leading-5 text-slate-600">
          {String(insight.body || insight.recommendation || insight.description || "Review the available data and assign an action owner.")}
        </p>
        {!!insight.moduleKey && (
          <span className="mt-2.5 inline-block rounded-full border border-violet-200 bg-violet-50 px-2 py-0.5 text-[10px] text-violet-600">
            {String(insight.moduleKey)}
          </span>
        )}
      </div>
    </div>
  );
}

/* ============================================================
   ACTION QUEUE
   ============================================================ */
const priorityDot: Record<string, string> = {
  Critical: "bg-red-500",
  High: "bg-amber-400",
  Medium: "bg-yellow-500",
  Low: "bg-emerald-500",
};

export function ActionQueue({ actions }: { actions: AnyRecord[] }) {
  return (
    <div className="panel p-5">
      <div className="flex items-center justify-between mb-4">
        <h2 className="section-title">Priority Action Queue</h2>
        <span className="rounded-full border border-slate-200 bg-slate-50 px-2 py-0.5 text-[10px] font-bold text-slate-500">
          {actions.length}
        </span>
      </div>
      <div className="space-y-2">
        {actions.slice(0, 6).map((action, i) => {
          const priority = String(action.priority || action.riskLevel || "Medium");
          const dot = priorityDot[priority] || "bg-slate-500";
          return (
            <div key={String(action.id || i)} className="flex items-center gap-3 rounded-xl border border-slate-100 bg-white px-4 py-3 transition hover:border-slate-200 hover:bg-slate-50">
              <span className={`h-2 w-2 shrink-0 rounded-full ${dot}`} />
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-semibold text-slate-800">{String(action.title)}</p>
                <p className="text-xs text-slate-500">{String(action.moduleKey || action.module_key || "operations")}</p>
              </div>
              <RiskBadge risk={priority} />
            </div>
          );
        })}
      </div>
    </div>
  );
}

/* ============================================================
   TIMELINE
   ============================================================ */
const timelineDot: Record<string, string> = {
  safety: "bg-red-400",
  maintenance: "bg-amber-400",
  dispatch: "bg-teal-400",
  fuel: "bg-emerald-400",
  ai: "bg-violet-400",
};

export function Timeline({ items }: { items: AnyRecord[] }) {
  return (
    <div className="panel p-5">
      <div className="flex items-center gap-2 mb-5">
        <TrendingUp className="h-4 w-4 text-teal-400" />
        <h2 className="section-title">Mission Control Timeline</h2>
      </div>
      <div className="space-y-0">
        {items.slice(0, 10).map((item, i) => {
          const type = String(item.eventType || item.type || "").toLowerCase();
          const dot = Object.entries(timelineDot).find(([k]) => type.includes(k))?.[1] || "bg-slate-500";
          const isLast = i === Math.min(items.length, 10) - 1;
          return (
            <div key={String(item.id || i)} className="flex gap-3">
              <div className="flex flex-col items-center">
                <div className={`mt-0.5 h-2.5 w-2.5 shrink-0 rounded-full ${dot} ring-2 ring-white`} />
                {!isLast && <div className="mt-1 w-px flex-1 bg-slate-200 min-h-[20px]" />}
              </div>
              <div className="pb-4 min-w-0">
                <p className="text-sm font-semibold text-slate-800">{String(item.title || item.eventType || "Event")}</p>
                <p className="text-xs text-slate-500">{String(item.eventTime || item.createdAt || "Live")}</p>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

/* ============================================================
   LIVE COUNT  (animated number display)
   ============================================================ */
export function LiveCount({ value, label, color = "text-teal-300" }: { value: number | string; label: string; color?: string }) {
  return (
    <div className="flex flex-col items-center gap-1 anim-count">
      <span className={`text-3xl font-extrabold tabular-nums tracking-tight ${color}`}>{value}</span>
      <span className="text-[10px] font-bold uppercase tracking-[0.2em] text-slate-500">{label}</span>
    </div>
  );
}

/* ============================================================
   CELL RENDERER
   ============================================================ */
function renderCell(column: string, value: unknown) {
  if (/\bstatus\b/i.test(column) || /approval_status|reviewStatus/i.test(column))
    return <StatusBadge status={value} />;
  if (/risk|priority|severity|anomaly/i.test(column))
    return <RiskBadge risk={value} />;
  if (value === null || value === undefined || value === "")
    return <span className="text-slate-600">—</span>;
  const str = String(value);
  if (/^\$[\d,]+/.test(str))
    return <span className="font-semibold text-emerald-700">{str}</span>;
  if (/^\d{4}-\d{2}-\d{2}/.test(str))
    return <span className="text-slate-400 font-mono text-xs">{str.slice(0, 10)}</span>;
  return <span>{str}</span>;
}

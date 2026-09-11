import type { ReactNode } from "react";

/* ============================================================
   FLEET CONSOLE PRIMITIVES — shared clay/neumo building blocks
   for module surfaces (styles live in styles/index.css: .fc-*,
   .deck-*). Every value rendered through these comes from the
   caller's live data; these are presentation only.
   ============================================================ */

export function ClayStat({ Icon, tone, iconCls, label, value, caption, alert, active, onClick }: {
  Icon: React.ElementType;
  tone: string;
  iconCls: string;
  label: string;
  value: ReactNode;
  caption?: string;
  alert?: boolean;
  active?: boolean;
  onClick?: () => void;
}) {
  const n = Number(value);
  const valueColor = alert && Number.isFinite(n) && n > 0
    ? (tone.includes("red") ? "text-rose-600" : "text-amber-600")
    : "text-slate-900";
  const body = (
    <>
      <div className="flex items-center justify-between">
        <span className="text-[12px] font-bold text-slate-600">{label}</span>
        <span className="fc-blob"><Icon className={`h-4 w-4 ${iconCls}`} /></span>
      </div>
      <div className={`mt-1 text-[24px] font-black leading-none tracking-tight tabular-nums ${valueColor}`}>{value}</div>
      {caption ? <p className="mt-1 text-[11px] font-medium text-slate-500">{caption}</p> : null}
    </>
  );
  if (onClick) {
    return (
      <button type="button" onClick={onClick} aria-pressed={active}
        className={`fc-clay ${tone} ${active ? "deck-clay-pressed" : ""} p-3 text-left`}>
        {body}
      </button>
    );
  }
  return <div className={`fc-clay ${tone} p-3`}>{body}</div>;
}

export function ConsoleNav<K extends string>({ sections, active, onSelect }: {
  sections: ReadonlyArray<{ key: K; label: string; description: string }>;
  active: K;
  onSelect: (key: K) => void;
}) {
  return (
    <nav className="fc-neumo sticky top-2 z-20 p-1.5">
      <div className={`grid gap-1 ${sections.length >= 5 ? "sm:grid-cols-5" : "sm:grid-cols-4"}`}>
        {sections.map((item) => (
          <button
            key={item.key}
            type="button"
            onClick={() => onSelect(item.key)}
            className={`rounded-lg px-2.5 py-1.5 text-left transition ${
              active === item.key ? "fc-seg-btn-active rounded-lg" : "hover:bg-white/60"
            }`}
          >
            <div className={`text-xs font-bold uppercase tracking-[0.14em] ${active === item.key ? "text-teal-800" : "text-slate-700"}`}>{item.label}</div>
            <div className="mt-0.5 hidden text-[11px] text-slate-500 lg:block">{item.description}</div>
          </button>
        ))}
      </div>
    </nav>
  );
}

export function ConsoleRail({ eyebrow, icon, title, meta, actions }: {
  eyebrow: string;
  icon: ReactNode;
  title: string;
  meta: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <header className="fc-rail relative px-4 py-3">
      <div className="relative flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
        <div className="min-w-0">
          <span className="section-title inline-flex items-center gap-2">{icon} {eyebrow}</span>
          <h1 className="mt-1 text-[22px] font-black leading-none tracking-tight text-slate-950">{title}</h1>
          <p className="mt-1.5 text-[12.5px] font-medium text-slate-500">{meta}</p>
        </div>
        {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
      </div>
    </header>
  );
}

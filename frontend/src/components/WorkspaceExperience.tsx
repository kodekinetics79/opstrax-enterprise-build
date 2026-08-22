import { useMemo } from "react";
import { ArrowRight } from "lucide-react";
import { useNavigate } from "react-router";

type Shortcut = {
  label: string;
  route: string;
};

export function WorkspaceExperience({
  pageTitle,
  shortcuts = [],
}: {
  pageTitle: string;
  // Kept for call-site compatibility. Outcome/maintenance commentary is deliberately
  // not rendered here: the page itself owns user-facing context and hierarchy.
  clientOutcome?: string;
  maintenanceOutcome?: string;
  shortcuts?: Shortcut[];
}) {
  const navigate = useNavigate();
  const topShortcuts = useMemo(() => shortcuts.slice(0, 4), [shortcuts]);

  if (topShortcuts.length === 0) return null;

  return (
    <nav
      aria-label={`${pageTitle} quick access`}
      className="flex min-h-10 flex-wrap items-center gap-2 rounded-xl border border-slate-200/80 bg-white/75 px-3 py-2 shadow-sm backdrop-blur-md"
    >
      <span className="mr-1 text-[10px] font-bold uppercase tracking-[0.16em] text-slate-400">
        Quick access
      </span>
      {topShortcuts.map((item) => (
        <button
          key={item.route}
          type="button"
          onClick={() => navigate(item.route)}
          className="group inline-flex h-8 items-center gap-1.5 rounded-lg px-2.5 text-xs font-semibold text-slate-600 transition hover:bg-slate-100 hover:text-slate-950"
        >
          {item.label}
          <ArrowRight className="h-3 w-3 text-slate-300 transition group-hover:translate-x-0.5 group-hover:text-slate-500" />
        </button>
      ))}
    </nav>
  );
}

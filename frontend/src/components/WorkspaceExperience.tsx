import { useMemo } from "react";
import { ArrowRight, CheckCircle2, Sparkles } from "lucide-react";
import { useNavigate } from "react-router-dom";

type Shortcut = {
  label: string;
  route: string;
};

export function WorkspaceExperience({
  pageTitle,
  clientOutcome,
  shortcuts = [],
}: {
  pageTitle: string;
  clientOutcome: string;
  // maintenanceOutcome accepted for call-site compatibility but intentionally NOT rendered
  // (it was internal dev commentary — must not appear in the customer UI).
  maintenanceOutcome?: string;
  shortcuts?: Shortcut[];
}) {
  const navigate = useNavigate();
  const topShortcuts = useMemo(() => shortcuts.slice(0, 4), [shortcuts]);

  if (!clientOutcome && topShortcuts.length === 0) return null;

  return (
    <section className="panel overflow-hidden px-4 py-4 md:px-5">
      <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(circle_at_top_right,rgba(45,212,191,.10),transparent_36%),radial-gradient(circle_at_bottom_left,rgba(37,99,235,.06),transparent_34%)]" />
      <div className="relative flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
        <div className="flex items-start gap-3">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl border border-teal-200 bg-teal-50 text-teal-700 shadow-sm">
            <Sparkles className="h-4 w-4" />
          </div>
          <div className="min-w-0">
            <h2 className="text-base font-bold text-slate-950">{pageTitle}</h2>
            <p className="mt-1 text-sm leading-6 text-slate-500">{clientOutcome}</p>
          </div>
        </div>

        <div className="flex flex-wrap gap-2 md:justify-end">
          {topShortcuts.map((item) => (
            <button
              key={item.route}
              type="button"
              onClick={() => navigate(item.route)}
              className="btn-ghost h-10 gap-1.5 px-3 text-xs"
            >
              <CheckCircle2 className="h-3.5 w-3.5 text-teal-600" />
              {item.label}
              <ArrowRight className="h-3.5 w-3.5" />
            </button>
          ))}
        </div>
      </div>
    </section>
  );
}

import { ArrowRight } from 'lucide-react';
import type { ReactNode } from 'react';

interface DataPanelProps {
  title: string;
  description?: string;
  children: ReactNode;
  action?: ReactNode;
  viewAll?: boolean;
  onViewAll?: () => void;
  className?: string;
}

export function DataPanel({
  title,
  description,
  children,
  action,
  viewAll,
  onViewAll,
  className = '',
}: DataPanelProps) {
  return (
    <section className={`surface rounded-xl p-5 ${className}`}>
      <div className="mb-4 flex items-start justify-between gap-4">
        <div className="min-w-0">
          <h2 className="text-sm font-bold text-slate-950 dark:text-white">{title}</h2>
          {description ? (
            <p className="mt-0.5 text-xs text-slate-500 dark:text-slate-400">{description}</p>
          ) : null}
        </div>
        {action ? (
          <div className="shrink-0">{action}</div>
        ) : viewAll ? (
          <button
            type="button"
            onClick={onViewAll}
            className="flex shrink-0 items-center gap-1 text-xs font-semibold text-sapphire hover:underline dark:text-cyanAccent"
          >
            View all
            <ArrowRight className="h-3 w-3" />
          </button>
        ) : null}
      </div>
      {children}
    </section>
  );
}

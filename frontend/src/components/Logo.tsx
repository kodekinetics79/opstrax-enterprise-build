interface LogoProps {
  collapsed?: boolean;
}

export function Logo({ collapsed }: LogoProps) {
  return (
    <div className="flex items-center gap-2.5">
      <div className="grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-gradient-to-br from-cyanAccent to-sapphire text-sm font-black text-midnight shadow-glow">
        K
      </div>
      {!collapsed && (
        <div className="min-w-0">
          <p className="truncate text-sm font-black tracking-[0.16em] text-slate-950 dark:text-white">KYNEXONE</p>
          <p className="truncate text-[10px] font-medium text-slate-400 dark:text-slate-500">Workforce Command Center</p>
        </div>
      )}
    </div>
  );
}

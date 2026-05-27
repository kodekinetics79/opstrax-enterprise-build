import { useEffect, useRef, useState } from "react";
import { NavLink, Outlet, useLocation } from "react-router-dom";
import {
  Bell, Bot, Building2, ChevronDown, LogOut,
  Menu, Search, Sparkles, X, Zap,
} from "lucide-react";
import { modules, moduleIcons } from "@/modules/moduleConfig";
import { useAuth } from "@/hooks/useAuth";

const GROUPS = [
  "Command", "Fleet", "Dispatch", "Safety",
  "Maintenance", "Compliance", "Finance", "Intelligence", "Platform",
] as const;

type Group = typeof GROUPS[number];

const GROUP_META: Record<Group, { emoji: string; color: string }> = {
  Command:     { emoji: "⚡", color: "text-teal-400" },
  Fleet:       { emoji: "🚛", color: "text-sky-400" },
  Dispatch:    { emoji: "📡", color: "text-blue-400" },
  Safety:      { emoji: "🛡", color: "text-red-400" },
  Maintenance: { emoji: "🔧", color: "text-amber-400" },
  Compliance:  { emoji: "✅", color: "text-emerald-400" },
  Finance:     { emoji: "💰", color: "text-yellow-400" },
  Intelligence:{ emoji: "🧠", color: "text-violet-400" },
  Platform:    { emoji: "⚙️", color: "text-slate-400" },
};

const NOTIFS = [
  { text: "Safety event — TRK-104 harsh braking",     time: "2m ago",  type: "danger" },
  { text: "Maintenance overdue — VAN-112",             time: "8m ago",  type: "warning" },
  { text: "Carrier compliance risk — FastLine LLC",    time: "14m ago", type: "warning" },
  { text: "Contract expiring in 7 days — CON-0082",   time: "1h ago",  type: "info" },
];

export function AppShell() {
  const { session, logout } = useAuth();
  const location = useLocation();

  // navigation collapse — persist across renders, default all open
  const [collapsed, setCollapsed] = useState<Set<Group>>(new Set());
  const [mobileOpen, setMobileOpen] = useState(false);
  const [notifOpen, setNotifOpen] = useState(false);
  const notifRef = useRef<HTMLDivElement>(null);

  // close mobile sidebar & notif panel on route change
  useEffect(() => {
    setMobileOpen(false);
    setNotifOpen(false);
  }, [location.pathname]);

  // close notif panel on outside click
  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (notifRef.current && !notifRef.current.contains(e.target as Node)) {
        setNotifOpen(false);
      }
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, []);

  const toggleGroup = (g: Group) =>
    setCollapsed((prev) => {
      const next = new Set(prev);
      if (next.has(g)) next.delete(g); else next.add(g);
      return next;
    });

  const initials = (session?.role || "A")[0].toUpperCase();

  /* ── Sidebar nav content (shared between desktop + mobile) ── */
  const navContent = (
    <div className="flex h-full flex-col gap-4">

      {/* Brand */}
      <div className="relative overflow-hidden rounded-2xl border border-teal-400/20 bg-gradient-to-br from-teal-400/10 via-teal-400/5 to-transparent p-4">
        <div className="pointer-events-none absolute -right-6 -top-6 h-20 w-20 rounded-full bg-teal-400/12 blur-3xl" />
        <div className="relative flex items-center gap-3">
          <div className="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-xl bg-gradient-to-br from-teal-400 to-blue-500 shadow-lg shadow-teal-400/25">
            <Zap className="h-5 w-5 text-slate-950" />
          </div>
          <div>
            <p className="text-base font-extrabold tracking-tight text-white">OpsTrax</p>
            <p className="text-[10px] font-bold uppercase tracking-[0.22em] text-teal-300/70">Enterprise TMS</p>
          </div>
        </div>
        <div className="relative mt-3 flex items-center gap-2">
          <span className="live-dot" />
          <span className="text-[11px] font-semibold text-emerald-300/80">Live Simulation Active</span>
        </div>
      </div>

      {/* Navigation */}
      <nav className="flex-1 space-y-0.5 overflow-y-auto pb-2">
        {GROUPS.map((group) => {
          const items = modules.filter((m) => m.group === group);
          if (!items.length) return null;
          const isOpen = !collapsed.has(group);
          const meta  = GROUP_META[group];

          return (
            <div key={group}>
              {/* Group header */}
              <button
                className="flex w-full items-center justify-between rounded-lg px-2.5 py-1.5 transition-colors hover:bg-white/[0.03]"
                onClick={() => toggleGroup(group)}
              >
                <span className="flex items-center gap-2">
                  <span className="text-sm leading-none">{meta.emoji}</span>
                  <span className={`text-[10px] font-bold uppercase tracking-[0.22em] ${meta.color} opacity-70`}>
                    {group}
                  </span>
                </span>
                <ChevronDown
                  className="h-3.5 w-3.5 text-slate-600 transition-transform duration-200"
                  style={{ transform: isOpen ? "rotate(0deg)" : "rotate(-90deg)" }}
                />
              </button>

              {/* Group items */}
              {isOpen && (
                <div className="mt-0.5 space-y-px pb-1">
                  {items.map((module) => {
                    const Icon = moduleIcons[module.key];
                    return (
                      <NavLink
                        key={module.key}
                        to={module.route}
                        className={({ isActive }) =>
                          `relative flex items-center gap-2.5 rounded-xl px-3 py-2 text-sm transition-all duration-150 ${
                            isActive
                              ? "bg-gradient-to-r from-teal-400/12 to-blue-500/5 text-white ring-1 ring-teal-400/18"
                              : "text-slate-400 hover:bg-white/[0.04] hover:text-slate-100"
                          }`
                        }
                      >
                        {({ isActive }) => (
                          <>
                            {isActive && <span className="nav-active-bar" />}
                            <Icon
                              className={`h-4 w-4 flex-shrink-0 transition-colors ${
                                isActive ? meta.color : "text-slate-500"
                              }`}
                            />
                            <span className="truncate font-medium">{module.title}</span>
                          </>
                        )}
                      </NavLink>
                    );
                  })}
                </div>
              )}
            </div>
          );
        })}
      </nav>

      {/* User footer */}
      <div className="border-t border-white/[0.07] pt-3">
        <div className="flex items-center gap-3 rounded-xl px-3 py-2.5 transition hover:bg-white/[0.03]">
          <div className="flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-lg bg-gradient-to-br from-teal-400/25 to-blue-500/15 border border-teal-400/20 text-sm font-extrabold text-teal-200">
            {initials}
          </div>
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm font-semibold text-white">{session?.role || "Company Admin"}</p>
            <p className="truncate text-xs text-slate-500">{String(session?.company?.name || "OpsTrax Demo")}</p>
          </div>
          <button
            className="icon-btn flex-shrink-0"
            title="Sign out"
            onClick={logout}
          >
            <LogOut className="h-3.5 w-3.5" />
          </button>
        </div>
      </div>
    </div>
  );

  return (
    <div className="min-h-screen bg-slate-950 text-slate-100">

      {/* ── Desktop Sidebar ── */}
      <aside className="fixed inset-y-0 left-0 z-30 hidden w-[264px] border-r border-white/[0.07] bg-slate-950/98 p-3 backdrop-blur-xl xl:flex xl:flex-col">
        {navContent}
      </aside>

      {/* ── Mobile Sidebar ── */}
      {mobileOpen && (
        <div className="fixed inset-0 z-50 xl:hidden anim-fade-in">
          <div
            className="absolute inset-0 bg-black/65 backdrop-blur-sm"
            onClick={() => setMobileOpen(false)}
          />
          <aside className="anim-slide-left absolute inset-y-0 left-0 w-[264px] overflow-y-auto border-r border-white/[0.09] bg-slate-950 p-3 shadow-2xl">
            <button
              className="absolute right-3 top-3 icon-btn"
              onClick={() => setMobileOpen(false)}
            >
              <X className="h-4 w-4" />
            </button>
            {navContent}
          </aside>
        </div>
      )}

      {/* ── Main ── */}
      <div className="xl:pl-[264px]">

        {/* ── Header ── */}
        <header className="sticky top-0 z-20 border-b border-white/[0.07] bg-slate-950/88 backdrop-blur-xl">
          <div className="mx-auto max-w-[1800px] px-4 md:px-6">
            <div className="flex h-[54px] items-center gap-3">

              {/* Mobile menu button */}
              <button className="icon-btn xl:hidden flex-shrink-0" onClick={() => setMobileOpen(true)}>
                <Menu className="h-4 w-4" />
              </button>

              {/* Search */}
              <div className="relative w-full max-w-xs">
                <Search className="absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-slate-500" />
                <input
                  className="field h-8 rounded-lg py-0 pl-9 pr-10 text-[13px]"
                  placeholder="Search anything..."
                />
                <span className="absolute right-2 top-1/2 -translate-y-1/2 hidden items-center gap-0.5 rounded border border-white/10 bg-white/[0.04] px-1 py-0.5 text-[10px] text-slate-500 md:flex">
                  ⌘K
                </span>
              </div>

              {/* Right section */}
              <div className="ml-auto flex items-center gap-2">

                {/* Live */}
                <div className="hidden sm:flex items-center gap-1.5 rounded-full border border-emerald-400/20 bg-emerald-400/7 px-2.5 py-1 text-[11px] font-bold text-emerald-300">
                  <span className="live-dot h-[6px] w-[6px]" />
                  Live
                </div>

                {/* AI */}
                <div className="hidden md:flex items-center gap-1.5 rounded-full border border-violet-400/20 bg-violet-400/7 px-2.5 py-1 text-[11px] font-bold text-violet-300">
                  <Sparkles className="h-3 w-3" />
                  AI
                </div>

                {/* AI Copilot icon */}
                <div className="hidden lg:flex items-center gap-1.5 rounded-full border border-white/[0.09] bg-white/[0.03] px-2.5 py-1 text-[11px] text-slate-400">
                  <Bot className="h-3 w-3 text-violet-400" />
                  <span className="max-w-[100px] truncate">{String(session?.company?.name || "OpsTrax Demo")}</span>
                </div>

                {/* Notifications */}
                <div className="relative" ref={notifRef}>
                  <button
                    className="icon-btn relative h-8 w-8"
                    onClick={() => setNotifOpen((v) => !v)}
                  >
                    <Bell className="h-3.5 w-3.5" />
                    <span className="notif-badge">{NOTIFS.length}</span>
                  </button>

                  {notifOpen && (
                    <div className="panel anim-slide-right absolute right-0 top-full z-50 mt-2 w-[300px] overflow-hidden p-0">
                      <div className="flex items-center justify-between border-b border-white/[0.07] px-4 py-3">
                        <p className="section-title">Notifications</p>
                        <span className="rounded-full border border-red-400/25 bg-red-500/10 px-2 py-0.5 text-[10px] font-bold text-red-300">
                          {NOTIFS.length} new
                        </span>
                      </div>
                      <div className="max-h-[320px] overflow-y-auto divide-y divide-white/[0.05]">
                        {NOTIFS.map((n, i) => (
                          <div key={i} className="flex items-start gap-3 px-4 py-3 transition hover:bg-white/[0.03] cursor-pointer">
                            <span
                              className={`mt-0.5 h-2 w-2 flex-shrink-0 rounded-full ${
                                n.type === "danger" ? "bg-red-400" : n.type === "warning" ? "bg-amber-400" : "bg-sky-400"
                              }`}
                            />
                            <div className="min-w-0 flex-1">
                              <p className="text-[13px] text-slate-200 leading-snug">{n.text}</p>
                              <p className="mt-0.5 text-xs text-slate-500">{n.time}</p>
                            </div>
                          </div>
                        ))}
                      </div>
                      <div className="border-t border-white/[0.06] px-4 py-2.5">
                        <button className="text-xs font-semibold text-teal-400 hover:text-teal-300 transition">
                          View all notifications
                        </button>
                      </div>
                    </div>
                  )}
                </div>

                {/* Avatar / sign-out */}
                <button
                  className="flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-lg bg-gradient-to-br from-teal-400/22 to-blue-500/12 border border-teal-400/18 text-[13px] font-extrabold text-teal-200 transition hover:border-teal-400/35 hover:from-teal-400/30"
                  title={`Signed in as ${session?.role || "Admin"} — click to sign out`}
                  onClick={logout}
                >
                  {initials}
                </button>
              </div>
            </div>
          </div>
        </header>

        {/* ── Content ── */}
        <main className="mx-auto max-w-[1800px] px-4 py-6 md:px-6">
          <Outlet />
        </main>
      </div>
    </div>
  );
}

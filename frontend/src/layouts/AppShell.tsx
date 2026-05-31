import { useEffect, useMemo, useRef, useState } from "react";
import { NavLink, Outlet, useLocation } from "react-router-dom";
import {
  Bell, Bot, ChevronDown, Languages, LogOut,
  Menu, Search, Sparkles, X, Zap,
} from "lucide-react";
import { modules, moduleIcons } from "@/modules/moduleConfig";
import { useAuth } from "@/hooks/useAuth";
import { useHasPermission } from "@/hooks/usePermission";
import { useI18n, LOCALES } from "@/i18n";
import type { LocaleCode } from "@/i18n";

const GROUPS = [
  "Control Tower", "CRM & Growth", "Commercial", "Transport Operations",
  "Fleet", "Telematics & IoT", "Safety & Compliance", "Maintenance",
  "Financials", "Governance", "Intelligence",
] as const;

type Group = typeof GROUPS[number];

const GROUP_META: Record<Group, { color: string }> = {
  "Control Tower":       { color: "text-blue-700" },
  "CRM & Growth":        { color: "text-emerald-700" },
  Commercial:            { color: "text-teal-700" },
  "Transport Operations":{ color: "text-sky-700" },
  Fleet:                 { color: "text-indigo-700" },
  "Telematics & IoT":    { color: "text-cyan-700" },
  "Safety & Compliance": { color: "text-red-700" },
  Maintenance:           { color: "text-amber-700" },
  Financials:            { color: "text-yellow-700" },
  Governance:            { color: "text-slate-600" },
  Intelligence:          { color: "text-violet-700" },
};

const NOTIFS = [
  { text: "Safety event — TRK-104 harsh braking",     time: "2m ago",  type: "danger" },
  { text: "Maintenance overdue — VAN-112",             time: "8m ago",  type: "warning" },
  { text: "Carrier compliance risk — FastLine LLC",    time: "14m ago", type: "warning" },
  { text: "Contract expiring in 7 days — CON-0082",   time: "1h ago",  type: "info" },
];

export function AppShell() {
  const { session, logout } = useAuth();
  const { locale, setLocale } = useI18n();
  const location = useLocation();
  const hasPermission = useHasPermission();

  // Modules visible to this user based on their permissions
  const visibleModules = useMemo(
    () => modules.filter((m) => !m.requiredPermission || hasPermission(m.requiredPermission)),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [session?.permissions],
  );

  // navigation collapse — persist across renders, default all open
  const [collapsed, setCollapsed] = useState<Set<Group>>(new Set());
  const [mobileOpen, setMobileOpen] = useState(false);
  const [notifOpen, setNotifOpen] = useState(false);
  const [langOpen, setLangOpen] = useState(false);
  const notifRef = useRef<HTMLDivElement>(null);
  const langRef = useRef<HTMLDivElement>(null);

  // close mobile sidebar & notif panel on route change
  useEffect(() => {
    setMobileOpen(false);
    setNotifOpen(false);
  }, [location.pathname]);

  // close notif + lang panels on outside click
  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (notifRef.current && !notifRef.current.contains(e.target as Node)) setNotifOpen(false);
      if (langRef.current && !langRef.current.contains(e.target as Node)) setLangOpen(false);
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
      <div className="relative overflow-hidden rounded-2xl border border-blue-200 bg-gradient-to-br from-blue-50 via-white to-teal-50 p-4 shadow-sm">
        <div className="pointer-events-none absolute -right-6 -top-6 h-20 w-20 rounded-full bg-blue-100/80 blur-3xl" />
        <div className="relative flex items-center gap-3">
          <div className="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-xl bg-gradient-to-br from-teal-500 to-blue-600 shadow-sm">
            <Zap className="h-5 w-5 text-white" />
          </div>
          <div>
            <p className="text-base font-extrabold tracking-tight text-slate-950">OpsTrax</p>
            <p className="text-[10px] font-bold uppercase tracking-[0.22em] text-blue-700/70">Enterprise TMS</p>
          </div>
        </div>
        <div className="relative mt-3 flex items-center gap-2">
          <span className="live-dot" />
          <span className="text-[11px] font-semibold text-emerald-700">Live Simulation Active</span>
        </div>
      </div>

      {/* Navigation */}
      <nav className="flex-1 space-y-0.5 overflow-y-auto pb-2">
        {GROUPS.map((group) => {
          const items = visibleModules.filter((m) => m.group === group);
          if (!items.length) return null;
          const isOpen = !collapsed.has(group);
          const meta  = GROUP_META[group];

          return (
            <div key={group}>
              {/* Group header */}
              <button
                className="flex w-full items-center justify-between rounded-lg px-2.5 py-1.5 transition-colors hover:bg-slate-100"
                onClick={() => toggleGroup(group)}
              >
                <span className="flex items-center gap-2">
                  <span className={`text-[10px] font-bold uppercase tracking-[0.22em] ${meta.color} opacity-70`}>
                    {group}
                  </span>
                </span>
                <ChevronDown
                  className="h-3.5 w-3.5 text-slate-400 transition-transform duration-200"
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
                              ? "bg-gradient-to-r from-blue-50 to-teal-50 text-slate-950 ring-1 ring-blue-200"
                              : "text-slate-600 hover:bg-slate-100 hover:text-slate-950"
                          }`
                        }
                      >
                        {({ isActive }) => (
                          <>
                            {isActive && <span className="nav-active-bar" />}
                            <Icon
                              className={`h-4 w-4 flex-shrink-0 transition-colors ${
                                isActive ? meta.color : "text-slate-400"
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
      <div className="border-t border-slate-200 pt-3 space-y-1">
        <div className="flex items-center gap-3 rounded-xl px-3 py-2.5 transition hover:bg-slate-100">
          <div className="flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-lg bg-gradient-to-br from-teal-50 to-blue-50 border border-blue-200 text-sm font-extrabold text-blue-700">
            {initials}
          </div>
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm font-semibold text-slate-900">{session?.role || "Company Admin"}</p>
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
        {/* Kode Kinetics attribution */}
        <div className="rounded-lg px-3 py-2 text-center">
          <p className="text-[10px] font-semibold text-slate-500">OpsTrax by Kode Kinetics</p>
          <p className="text-[9px] text-slate-400 mt-0.5">Enterprise software · AI automation · connected operations</p>
        </div>
      </div>
    </div>
  );

  return (
    <div className="min-h-screen bg-[#f3f6fb] text-slate-800">

      {/* ── Desktop Sidebar ── */}
      <aside className="fixed inset-y-0 left-0 z-30 hidden w-[264px] border-r border-slate-200 bg-white p-3 xl:flex xl:flex-col">
        {navContent}
      </aside>

      {/* ── Mobile Sidebar ── */}
      {mobileOpen && (
        <div className="fixed inset-0 z-50 xl:hidden anim-fade-in">
          <div
            className="absolute inset-0 bg-slate-900/30 backdrop-blur-sm"
            onClick={() => setMobileOpen(false)}
          />
          <aside className="anim-slide-left absolute inset-y-0 left-0 w-[264px] overflow-y-auto border-r border-slate-200 bg-white p-3 shadow-2xl">
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
        <header className="sticky top-0 z-20 border-b border-slate-200 bg-white/95 shadow-sm backdrop-blur-xl">
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
                <span className="absolute right-2 top-1/2 -translate-y-1/2 hidden items-center gap-0.5 rounded border border-slate-200 bg-slate-100 px-1 py-0.5 text-[10px] text-slate-400 md:flex">
                  ⌘K
                </span>
              </div>

              {/* Right section */}
              <div className="ml-auto flex items-center gap-2">

                {/* Live */}
                <div className="hidden sm:flex items-center gap-1.5 rounded-full border border-emerald-500/30 bg-emerald-50 px-2.5 py-1 text-[11px] font-bold text-emerald-700">
                  <span className="live-dot h-[6px] w-[6px]" />
                  Live
                </div>

                {/* AI */}
                <div className="hidden md:flex items-center gap-1.5 rounded-full border border-violet-500/30 bg-violet-50 px-2.5 py-1 text-[11px] font-bold text-violet-700">
                  <Sparkles className="h-3 w-3" />
                  AI
                </div>

                {/* AI Copilot icon */}
                <div className="hidden lg:flex items-center gap-1.5 rounded-full border border-slate-200 bg-slate-50 px-2.5 py-1 text-[11px] text-slate-600">
                  <Bot className="h-3 w-3 text-violet-400" />
                  <span className="max-w-[100px] truncate">{String(session?.company?.name || "OpsTrax Demo")}</span>
                </div>

                {/* Language selector */}
                <div className="relative" ref={langRef}>
                  <button
                    type="button"
                    title="Switch language"
                    className="icon-btn relative h-8 w-8"
                    onClick={() => setLangOpen((v) => !v)}
                  >
                    <Languages className="h-3.5 w-3.5" />
                  </button>
                  {langOpen && (
                    <div className="panel anim-slide-right absolute right-0 top-full z-50 mt-2 w-[220px] overflow-hidden p-1">
                      {(Object.entries(LOCALES) as [LocaleCode, typeof LOCALES[LocaleCode]][]).map(([code, meta]) => (
                        <button
                          key={code}
                          type="button"
                          onClick={() => { setLocale(code); setLangOpen(false); }}
                          className={`flex w-full items-center justify-between rounded-lg px-3 py-2 text-[12px] transition hover:bg-slate-50 ${locale === code ? "text-teal-700 font-semibold" : "text-slate-500"}`}
                        >
                          <span>{meta.nativeLabel}</span>
                          {meta.rtl && <span className="text-[9px] font-bold text-amber-700 border border-amber-300 rounded px-1">RTL</span>}
                        </button>
                      ))}
                    </div>
                  )}
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
                      <div className="flex items-center justify-between border-b border-slate-200 px-4 py-3">
                        <p className="section-title">Notifications</p>
                        <span className="rounded-full border border-red-300/50 bg-red-50 px-2 py-0.5 text-[10px] font-bold text-red-600">
                          {NOTIFS.length} new
                        </span>
                      </div>
                      <div className="max-h-[320px] overflow-y-auto divide-y divide-slate-100">
                        {NOTIFS.map((n, i) => (
                          <div key={i} className="flex items-start gap-3 px-4 py-3 transition hover:bg-slate-50 cursor-pointer">
                            <span
                              className={`mt-0.5 h-2 w-2 flex-shrink-0 rounded-full ${
                                n.type === "danger" ? "bg-red-400" : n.type === "warning" ? "bg-amber-400" : "bg-sky-400"
                              }`}
                            />
                            <div className="min-w-0 flex-1">
                              <p className="text-[13px] text-slate-700 leading-snug">{n.text}</p>
                              <p className="mt-0.5 text-xs text-slate-500">{n.time}</p>
                            </div>
                          </div>
                        ))}
                      </div>
                      <div className="border-t border-slate-200 px-4 py-2.5">
                        <button className="text-xs font-semibold text-teal-700 hover:text-teal-600 transition">
                          View all notifications
                        </button>
                      </div>
                    </div>
                  )}
                </div>

                {/* Avatar / sign-out */}
                <button
                  className="flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-lg bg-gradient-to-br from-teal-500/15 to-blue-500/10 border border-teal-500/25 text-[13px] font-extrabold text-teal-700 transition hover:border-teal-500/40 hover:from-teal-500/25"
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

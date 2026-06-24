import { useEffect, useMemo, useRef, useState } from "react";
import { NavLink, Outlet, useLocation, useNavigate } from "react-router-dom";
import {
  Bell, ChevronDown, Languages, LogOut,
  Menu, Search, Settings, User, X,
} from "lucide-react";
import { OpsTraxLogo } from "@/components/OpsTraxLogo";
import { modules, moduleIcons } from "@/modules/moduleConfig";
import { useAuth } from "@/hooks/useAuth";
import { useHasPermission } from "@/hooks/usePermission";
import { useI18n, LOCALES } from "@/i18n";
import type { LocaleCode } from "@/i18n";

const NAV_SECTIONS = [
  {
    label: "Operations",
    color: "text-teal-600",
    items: ["command-center", "fleet-health", "live-dashboard", "map-view", "alerts"],
  },
  {
    label: "Fleet",
    color: "text-blue-600",
    items: ["vehicles", "drivers", "fleet-utilization", "assignments"],
  },
  {
    label: "Dispatch",
    color: "text-cyan-700",
    items: ["dispatch-board", "jobs", "route-plans", "last-mile-delivery"],
  },
  {
    label: "Shipments",
    color: "text-teal-700",
    items: ["active-shipments", "shipments", "proof-of-delivery"],
  },
  {
    label: "Safety",
    color: "text-red-600",
    items: ["incidents", "coaching", "driver-scorecards", "dvir-inspections", "hos-eld"],
  },
  {
    label: "Maintenance",
    color: "text-amber-700",
    items: ["work-orders", "preventive-maintenance", "service-history", "downtime"],
  },
  {
    label: "Telematics",
    color: "text-violet-700",
    items: ["iot-devices", "gps-tracking", "obd-j1939"],
  },
  {
    label: "Customers",
    color: "text-indigo-600",
    items: ["customers"],
  },
  {
    label: "Reports",
    color: "text-purple-600",
    items: ["reports-analytics", "predictive-analytics", "sla-kpi", "carbon-tracking"],
  },
  {
    label: "Admin",
    color: "text-slate-500",
    items: ["user-management", "audit-logs", "integrations", "settings", "about"],
  },
] as const;

type Group = typeof NAV_SECTIONS[number]["label"];

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
  const navigate = useNavigate();
  const hasPermission = useHasPermission();

  // navigation collapse — persist across renders, default all open
  const [collapsed, setCollapsed] = useState<Set<Group>>(new Set());
  const [mobileOpen, setMobileOpen] = useState(false);
  const [notifOpen, setNotifOpen] = useState(false);
  const [langOpen, setLangOpen] = useState(false);
  const [profileOpen, setProfileOpen] = useState(false);
  const [searchQuery, setSearchQuery] = useState("");
  const notifRef = useRef<HTMLDivElement>(null);
  const langRef = useRef<HTMLDivElement>(null);
  const profileRef = useRef<HTMLDivElement>(null);

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
      if (profileRef.current && !profileRef.current.contains(e.target as Node)) setProfileOpen(false);
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

  const visibleSections = useMemo(
    () => NAV_SECTIONS.map((section) => ({
      ...section,
      items: section.items
        .map((key) => modules.find((module) => module.key === key || module.route === key || module.route === `/${key}`))
        .filter((module): module is (typeof modules)[number] => Boolean(module && (!module.requiredPermission || hasPermission(module.requiredPermission)))),
    })).filter((section) => section.items.length > 0),
    [hasPermission, session?.permissions],
  );

  /* ── Sidebar nav content (shared between desktop + mobile) ── */
  const navContent = (
    <div className="flex h-full flex-col gap-4">

      {/* Brand */}
      <div className="flex items-center gap-3 px-1 py-2">
        <div className="shrink-0">
          <OpsTraxLogo size={36} />
        </div>
        <div className="min-w-0 flex-1">
          <p className="text-[15px] font-extrabold tracking-tight text-slate-950">OpsTrax</p>
          <p className="truncate text-[10px] font-semibold uppercase tracking-widest text-slate-400">
            {String(session?.company?.name || "Fleet Platform")}
          </p>
        </div>
        <div className="flex items-center gap-1.5">
          <span className="live-dot" />
        </div>
      </div>

      {/* Navigation */}
      <nav className="flex-1 space-y-0.5 overflow-y-auto pb-2">
        {visibleSections.map((section) => {
          const isOpen = !collapsed.has(section.label);

          return (
            <div key={section.label}>
              {/* Group header */}
              <button
                type="button"
                className="flex w-full items-center justify-between rounded-lg px-2.5 py-1.5 transition-colors hover:bg-slate-100"
                onClick={() => toggleGroup(section.label)}
              >
                <span className="flex items-center gap-2">
                  <span className={`text-[10px] font-bold uppercase tracking-[0.22em] ${section.color} opacity-70`}>
                    {section.label}
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
                  {section.items.map((module) => {
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
                                className={`h-4 w-4 shrink-0 transition-colors ${
                                isActive ? section.color : "text-slate-400"
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
          <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-gradient-to-br from-teal-50 to-blue-50 border border-blue-200 text-sm font-extrabold text-blue-700">
            {initials}
          </div>
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm font-semibold text-slate-900">{session?.role || "Company Admin"}</p>
            <p className="truncate text-xs text-slate-500">{String(session?.company?.name || "OpsTrax Tenant")}</p>
          </div>
          <button
            type="button"
            className="icon-btn shrink-0"
            title="Sign out"
            onClick={logout}
          >
            <LogOut className="h-3.5 w-3.5" />
          </button>
        </div>
        <div className="px-3 py-1.5 text-center">
          <p className="text-[10px] text-slate-400">OpsTrax · Kode Kinetics</p>
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
              type="button"
              aria-label="Close navigation"
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
              <button type="button" aria-label="Open navigation" className="icon-btn xl:hidden shrink-0" onClick={() => setMobileOpen(true)}>
                <Menu className="h-4 w-4" />
              </button>

              {/* Search */}
              <div className="relative w-full max-w-xs">
                <Search className="pointer-events-none absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-slate-400" />
                <input
                  className="field h-8 w-full rounded-lg py-0 pl-9 pr-10 text-[13px]"
                  placeholder="Search vehicles, drivers, jobs…"
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" && searchQuery.trim()) {
                      const q = searchQuery.trim().toLowerCase();
                      if (q.includes("vehicle") || q.match(/^trk|^van|^box/)) navigate("/vehicles");
                      else if (q.includes("driver")) navigate("/drivers");
                      else if (q.includes("job") || q.includes("dispatch")) navigate("/dispatch-board");
                      else if (q.includes("safety") || q.includes("incident")) navigate("/incidents");
                      else if (q.includes("maint") || q.includes("work order")) navigate("/work-orders");
                      else if (q.includes("alert")) navigate("/alerts");
                      else navigate("/command-center");
                      setSearchQuery("");
                    }
                  }}
                />
                <span className="absolute right-2 top-1/2 -translate-y-1/2 hidden items-center gap-0.5 rounded border border-slate-200 bg-slate-100 px-1 py-0.5 text-[10px] text-slate-400 md:flex">
                  ↵
                </span>
              </div>

              {/* Right section */}
              <div className="ml-auto flex items-center gap-2">

                {/* Live status */}
                <div className="hidden sm:flex items-center gap-1.5 rounded-full border border-emerald-500/30 bg-emerald-50 px-2.5 py-1 text-[11px] font-semibold text-emerald-700">
                  <span className="live-dot h-1.5 w-1.5" />
                  Live
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
                    type="button"
                    aria-label="Notifications"
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
                              className={`mt-0.5 h-2 w-2 shrink-0 rounded-full ${
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
                        <button
                          type="button"
                          className="text-xs font-semibold text-teal-700 hover:text-teal-600 transition"
                          onClick={() => navigate("/audit-logs")}
                        >
                          View all notifications
                        </button>
                      </div>
                    </div>
                  )}
                </div>

                {/* Avatar / profile dropdown */}
                <div className="relative" ref={profileRef}>
                  <button
                    type="button"
                    className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-linear-to-br from-teal-500/15 to-blue-500/10 border border-teal-500/25 text-[13px] font-extrabold text-teal-700 transition hover:border-teal-500/40 hover:from-teal-500/25"
                    title="My profile"
                    onClick={() => setProfileOpen((v) => !v)}
                  >
                    {initials}
                  </button>
                  {profileOpen && (
                    <div className="panel absolute right-0 top-full z-50 mt-2 w-55 overflow-hidden p-0 shadow-lg">
                      {/* User info */}
                      <div className="flex items-center gap-3 px-4 py-3 border-b border-slate-100 bg-slate-50">
                        <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-linear-to-br from-teal-100 to-blue-100 text-[14px] font-extrabold text-teal-700">
                          {initials}
                        </div>
                        <div className="min-w-0">
                          <p className="text-sm font-bold text-slate-900 truncate">{session?.user?.name || session?.role || "User"}</p>
                          <p className="text-[11px] text-slate-500 truncate">{session?.role || "Admin"}</p>
                        </div>
                      </div>
                      {/* Actions */}
                      <div className="py-1">
                        <button
                          type="button"
                          className="flex w-full items-center gap-3 px-4 py-2.5 text-[13px] text-slate-700 hover:bg-slate-50 transition"
                          onClick={() => { navigate("/settings"); setProfileOpen(false); }}
                        >
                          <Settings className="h-4 w-4 text-slate-400" />
                          Settings
                        </button>
                        <button
                          type="button"
                          className="flex w-full items-center gap-3 px-4 py-2.5 text-[13px] text-slate-700 hover:bg-slate-50 transition"
                          onClick={() => { navigate("/user-management"); setProfileOpen(false); }}
                        >
                          <User className="h-4 w-4 text-slate-400" />
                          User Management
                        </button>
                      </div>
                      <div className="border-t border-slate-100 py-1">
                        <button
                          type="button"
                          className="flex w-full items-center gap-3 px-4 py-2.5 text-[13px] text-red-600 hover:bg-red-50 transition"
                          onClick={() => { logout(); setProfileOpen(false); }}
                        >
                          <LogOut className="h-4 w-4" />
                          Sign out
                        </button>
                      </div>
                    </div>
                  )}
                </div>
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

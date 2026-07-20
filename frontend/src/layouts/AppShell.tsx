import { useEffect, useMemo, useRef, useState } from "react";
import { NavLink, Outlet, useLocation, useNavigate } from "react-router-dom";
import {
  Bell, ChevronDown, ChevronRight, LogOut,
  Menu, Search, Settings, User, X,
} from "lucide-react";
import { OpsTraxLogo } from "@/components/OpsTraxLogo";
import { modules, moduleIcons } from "@/modules/moduleConfig";
import { useAuth } from "@/hooks/useAuth";
import { useHasPermission } from "@/hooks/usePermission";

const NAV_SECTIONS = [
  {
    label: "Operations",
    color: "text-teal-600",
    items: ["command-center", "fleet-health", "live-dashboard", "map-view", "alerts"],
  },
  {
    label: "Fleet",
    color: "text-blue-600",
    items: ["vehicles", "drivers", "fleet-utilization", "fleet-workspace", "fleet-cold-chain", "fleet-assets", "fleet-saudi-readiness", "fleet-compliance", "assignments"],
  },
  {
    label: "Dispatch",
    color: "text-cyan-700",
    items: ["dispatch-board", "jobs", "route-plans", "last-mile-delivery", "logistics-workspace"],
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
  const location = useLocation();
  const navigate = useNavigate();
  const hasPermission = useHasPermission();

  // navigation collapse — persist across renders, default all open
  const [collapsed, setCollapsed] = useState<Set<Group>>(new Set());
  const [mobileOpen, setMobileOpen] = useState(false);
  const [notifOpen, setNotifOpen] = useState(false);
  const [profileOpen, setProfileOpen] = useState(false);
  const [searchQuery, setSearchQuery] = useState("");
  const [now, setNow] = useState(() => new Date());
  const notifRef = useRef<HTMLDivElement>(null);
  const profileRef = useRef<HTMLDivElement>(null);

  // live header clock
  useEffect(() => {
    const t = setInterval(() => setNow(new Date()), 1000);
    return () => clearInterval(t);
  }, []);

  // current page context (group + title) for the header breadcrumb
  const pageContext = useMemo(() => {
    const active = modules.find((m) => m.route === location.pathname);
    const group = NAV_SECTIONS.find((s) => active && (s.items as readonly string[]).includes(active.key));
    return { group: group?.label ?? "Workspace", title: active?.title ?? "Dashboard" };
  }, [location.pathname]);

  // close mobile sidebar & notif panel on route change
  useEffect(() => {
    setMobileOpen(false);
    setNotifOpen(false);
  }, [location.pathname]);

  // close notif + lang panels on outside click
  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (notifRef.current && !notifRef.current.contains(e.target as Node)) setNotifOpen(false);
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
    <div className="relative flex h-full flex-col gap-4">

      {/* Brand */}
      <div className="sb-brand">
        <span className="sb-brand-glow" />
        <div className="shrink-0">
          <OpsTraxLogo size={32} />
        </div>
        <div className="min-w-0 flex-1">
          <p className="sb-brand-name">OpsTrax</p>
          <p className="sb-brand-sub truncate">
            {String(session?.company?.name || "Fleet Platform")}
          </p>
        </div>
        <span className="sb-brand-live">
          <span className="sb-brand-live-dot" /> Live
        </span>
      </div>

      {/* Navigation */}
      <nav className="sb-scroll flex-1 space-y-1 overflow-y-auto overflow-x-hidden pb-2">
        {visibleSections.map((section) => {
          const isOpen = !collapsed.has(section.label);

          return (
            <div key={section.label}>
              {/* Group header */}
              <button
                type="button"
                className="sb-section-btn"
                onClick={() => toggleGroup(section.label)}
              >
                <span className={`sb-section-label ${section.color}`}>
                  {section.label}
                </span>
                <ChevronDown
                  className="sb-section-chevron"
                  style={{ transform: isOpen ? "rotate(0deg)" : "rotate(-90deg)" }}
                />
              </button>

              {/* Group items */}
              {isOpen && (
                <div className="mt-0.5 space-y-0.5 pb-1.5">
                  {section.items.map((module) => {
                    const Icon = moduleIcons[module.key];
                    return (
                      <NavLink
                        key={module.key}
                        to={module.route}
                        className={({ isActive }) =>
                          `sb-nav-item ${isActive ? "sb-nav-active" : ""}`
                        }
                      >
                        {({ isActive }) => (
                          <>
                            {isActive && <span className="sb-nav-active-bar" />}
                            <span className="sb-nav-icon">
                              <Icon />
                            </span>
                            <span className="truncate">{module.title}</span>
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
      <div className="space-y-2 border-t border-white/[0.06] pt-3">
        <div className="sb-user">
          <div className="sb-user-avatar">
            {initials}
          </div>
          <div className="min-w-0 flex-1">
            <p className="sb-user-name truncate">{session?.role || "Company Admin"}</p>
            <p className="sb-user-role truncate">{String(session?.company?.name || "OpsTrax Tenant")}</p>
          </div>
          <button
            type="button"
            className="sb-logout-btn"
            title="Sign out"
            onClick={logout}
          >
            <LogOut className="h-3.5 w-3.5" />
          </button>
        </div>
        <p className="sb-footer-text">OpsTrax · Kode Kinetics</p>
      </div>
    </div>
  );

  return (
    <div className="min-h-screen bg-[#f3f6fb] text-slate-800">

      {/* ── Desktop Sidebar ── */}
      <aside className="sb-shell fixed inset-y-0 left-0 z-30 hidden w-[260px] xl:flex xl:flex-col">
        {navContent}
      </aside>

      {/* ── Mobile Sidebar ── */}
      {mobileOpen && (
        <div className="fixed inset-0 z-50 xl:hidden anim-fade-in">
          <div
            className="absolute inset-0 bg-slate-900/50 backdrop-blur-sm"
            onClick={() => setMobileOpen(false)}
          />
          <aside className="sb-mobile anim-slide-left absolute inset-y-0 left-0 w-[260px] overflow-y-auto p-3 shadow-2xl">
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
      <div className="xl:pl-[260px]">

        {/* ── Header ── */}
        <header className="hd-shell sticky top-0 z-20">
          <div className="mx-auto max-w-[1800px] px-4 md:px-6">
            <div className="flex h-[56px] items-center gap-3">

              {/* Mobile menu button */}
              <button type="button" aria-label="Open navigation" className="hd-icon-btn xl:hidden shrink-0" onClick={() => setMobileOpen(true)}>
                <Menu />
              </button>

              {/* Search */}
              <div className="hd-search-wrap">
                <Search className="hd-search-icon" />
                <input
                  className="hd-search-input"
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
                <span className="hd-search-hint">↵</span>
              </div>

              {/* Page context breadcrumb */}
              <div className="hd-breadcrumb">
                <span className="hd-breadcrumb-group">{pageContext.group}</span>
                <ChevronRight className="hd-breadcrumb-chevron" />
                <span className="hd-breadcrumb-title">{pageContext.title}</span>
              </div>

              {/* Live clock */}
              <div className="hd-clock">
                <span className="hd-clock-time">
                  {now.toLocaleTimeString("en-GB", { hour12: false })}
                </span>
                <span className="hd-clock-date">
                  {now.toLocaleDateString("en-GB", { weekday: "short", day: "2-digit", month: "short" })}
                </span>
              </div>

              {/* Right section */}
              <div className="hd-right">

                {/* Live status */}
                <div className="hd-live">
                  <span className="hd-live-dot" />
                  Live
                </div>

                {/* Notifications */}
                <div className="relative" ref={notifRef}>
                  <button
                    type="button"
                    aria-label="Notifications"
                    className="hd-icon-btn"
                    onClick={() => setNotifOpen((v) => !v)}
                  >
                    <Bell />
                    <span className="hd-notif-badge">{NOTIFS.length}</span>
                  </button>

                  {notifOpen && (
                    <div className="hd-panel anim-slide-right">
                      <div className="hd-panel-header">
                        <p className="hd-panel-title">Notifications</p>
                        <span className="hd-panel-badge">
                          {NOTIFS.length} new
                        </span>
                      </div>
                      <div className="hd-panel-body">
                        {NOTIFS.map((n, i) => (
                          <div key={i} className="hd-notif-item">
                            <span
                              className={`hd-notif-dot ${
                                n.type === "danger" ? "hd-notif-dot-danger" : n.type === "warning" ? "hd-notif-dot-warning" : "hd-notif-dot-info"
                              }`}
                            />
                            <div className="min-w-0 flex-1">
                              <p className="hd-notif-text">{n.text}</p>
                              <p className="hd-notif-time">{n.time}</p>
                            </div>
                          </div>
                        ))}
                      </div>
                      <div className="hd-panel-footer">
                        <button
                          type="button"
                          className="hd-panel-footer-btn"
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
                    className="hd-profile-btn"
                    title="My profile"
                    onClick={() => setProfileOpen((v) => !v)}
                  >
                    {initials}
                  </button>
                  {profileOpen && (
                    <div className="hd-profile-panel">
                      {/* User info */}
                      <div className="hd-profile-info">
                        <div className="hd-profile-info-avatar">
                          {initials}
                        </div>
                        <div className="min-w-0">
                          <p className="hd-profile-info-name">{String(session?.user?.["name"] ?? session?.role ?? "User")}</p>
                          <p className="hd-profile-info-role">{session?.role || "Admin"}</p>
                        </div>
                      </div>
                      {/* Actions */}
                      <div className="hd-profile-actions">
                        <button
                          type="button"
                          className="hd-profile-action"
                          onClick={() => { navigate("/settings"); setProfileOpen(false); }}
                        >
                          <Settings />
                          Settings
                        </button>
                        <button
                          type="button"
                          className="hd-profile-action"
                          onClick={() => { navigate("/user-management"); setProfileOpen(false); }}
                        >
                          <User />
                          User Management
                        </button>
                      </div>
                      <div className="hd-profile-divider">
                        <button
                          type="button"
                          className="hd-profile-logout"
                          onClick={() => { logout(); setProfileOpen(false); }}
                        >
                          <LogOut />
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

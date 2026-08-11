import { useEffect, useState } from "react";
import { NavLink, Outlet, useLocation, useNavigate } from "react-router";
import {
  LayoutDashboard, Building2, Package, Receipt, HeartPulse, ScrollText, LogOut, Gauge, BriefcaseBusiness, Activity, UserCog, KeyRound, Menu, X,
} from "lucide-react";
import { OpsTraxLogo } from "@/components/OpsTraxLogo";
import { usePlatformAuth } from "@/hooks/usePlatformAuth";
import { useDialogFocus } from "@/hooks/useDialogFocus";

type NavItem = { to: string; label: string; icon: typeof LayoutDashboard; permission: string };

const NAV: NavItem[] = [
  { to: "/platform", label: "Dashboard", icon: LayoutDashboard, permission: "platform:dashboard:view" },
  { to: "/platform/commercial-ops", label: "Commercial Ops", icon: BriefcaseBusiness, permission: "platform:dashboard:view" },
  { to: "/platform/tenants", label: "Tenants", icon: Building2, permission: "platform:tenants:view" },
  { to: "/platform/packages", label: "Packages & Pricing", icon: Package, permission: "platform:packages:view" },
  { to: "/platform/revenue", label: "Revenue & Usage", icon: Gauge, permission: "platform:packages:view" },
  { to: "/platform/billing", label: "Billing & Invoices", icon: Receipt, permission: "platform:billing:view" },
  { to: "/platform/health", label: "Customer Success", icon: HeartPulse, permission: "platform:health:view" },
  { to: "/platform/reliability", label: "Reliability Center", icon: Activity, permission: "platform:health:view" },
  { to: "/platform/audit", label: "Security & Audit", icon: ScrollText, permission: "platform:audit:view" },
  { to: "/platform/operators", label: "Operators", icon: UserCog, permission: "platform:admins:view" },
  // Self-service — every signed-in admin holds dashboard:view, so this is always visible.
  { to: "/platform/account", label: "My Account", icon: KeyRound, permission: "platform:dashboard:view" },
];

export function PlatformShell() {
  const { session, logout, can } = usePlatformAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [mobileOpen, setMobileOpen] = useState(false);
  const mobileNavRef = useDialogFocus<HTMLElement>(mobileOpen, () => setMobileOpen(false));
  const visibleNav = NAV.filter((item) => can(item.permission));

  useEffect(() => setMobileOpen(false), [location.pathname]);

  const handleLogout = async () => {
    await logout();
    navigate("/platform/login", { replace: true });
  };

  return (
    <div className="platform-shell flex min-h-screen text-slate-100">
      {/* Sidebar */}
      <aside className="glass-nav-dark hidden w-64 shrink-0 flex-col border-r px-4 py-6 shadow-[0_24px_80px_rgba(2,6,23,.35)] lg:flex">
        <div className="flex items-center gap-2.5 rounded-[18px] border border-slate-800/80 bg-white/5 px-3 py-2">
          <OpsTraxLogo size={34} />
          <div>
            <p className="text-sm font-bold tracking-tight">OpsTrax</p>
            <p className="text-[10px] font-semibold uppercase tracking-[0.2em] text-teal-400">Platform Admin</p>
          </div>
        </div>

        <nav className="mt-8 flex flex-1 flex-col gap-1">
          {visibleNav.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.to === "/platform"}
              className={({ isActive }) =>
                `flex items-center gap-3 rounded-[14px] px-3 py-2.5 text-sm font-medium transition ${
                  isActive
                    ? "bg-teal-400/10 text-teal-300 ring-1 ring-inset ring-teal-400/20"
                    : "text-slate-400 hover:bg-slate-800/60 hover:text-slate-100"
                }`
              }
            >
              <item.icon className="h-4 w-4 shrink-0" />
              {item.label}
            </NavLink>
          ))}
        </nav>

        <div className="mt-auto rounded-[18px] border border-slate-800/80 bg-slate-900/70 p-3">
          <p className="truncate text-sm font-semibold text-slate-100">{session?.admin.name}</p>
          <p className="truncate text-xs text-slate-500">{session?.role.name}</p>
          <button
            onClick={handleLogout}
            className="mt-3 flex w-full items-center justify-center gap-2 rounded-lg border border-slate-700 bg-slate-800/60 px-3 py-2 text-xs font-semibold text-slate-200 transition hover:border-red-500/40 hover:text-red-300"
          >
            <LogOut className="h-3.5 w-3.5" /> Sign out
          </button>
        </div>
      </aside>

      {mobileOpen && (
        <div className="fixed inset-0 z-50 lg:hidden">
          <button type="button" className="absolute inset-0 h-full w-full bg-slate-950/70" aria-label="Close platform navigation" onClick={() => setMobileOpen(false)} />
          <aside ref={mobileNavRef} id="platform-mobile-navigation" className="glass-nav-dark absolute inset-y-0 left-0 flex w-[min(20rem,88vw)] flex-col border-r px-4 py-5 shadow-2xl" role="dialog" aria-modal="true" aria-label="Platform navigation">
            <div className="flex items-center justify-between gap-3">
              <div className="flex items-center gap-2.5"><OpsTraxLogo size={30} /><div><p className="text-sm font-bold">OpsTrax</p><p className="text-[10px] font-semibold uppercase tracking-[0.2em] text-teal-400">Platform Admin</p></div></div>
              <button type="button" className="rounded-lg border border-slate-700 p-2 text-slate-200" onClick={() => setMobileOpen(false)} aria-label="Close platform navigation"><X className="h-4 w-4" /></button>
            </div>
            <nav className="mt-6 flex flex-1 flex-col gap-1" aria-label="Platform">
              {visibleNav.map((item) => (
                <NavLink key={item.to} to={item.to} end={item.to === "/platform"} onClick={() => setMobileOpen(false)} className={({ isActive }) => `flex min-h-11 items-center gap-3 rounded-[14px] px-3 py-2.5 text-sm font-medium transition ${isActive ? "bg-teal-400/10 text-teal-300 ring-1 ring-inset ring-teal-400/20" : "text-slate-300 hover:bg-slate-800/60 hover:text-white"}`}>
                  <item.icon className="h-4 w-4 shrink-0" />{item.label}
                </NavLink>
              ))}
            </nav>
            <div className="rounded-[18px] border border-slate-800/80 bg-slate-900/70 p-3"><p className="truncate text-sm font-semibold text-slate-100">{session?.admin.name}</p><p className="truncate text-xs text-slate-500">{session?.role.name}</p><button type="button" onClick={handleLogout} className="mt-3 flex min-h-11 w-full items-center justify-center gap-2 rounded-lg border border-slate-700 bg-slate-800/60 px-3 py-2 text-xs font-semibold text-slate-200"><LogOut className="h-3.5 w-3.5" /> Sign out</button></div>
          </aside>
        </div>
      )}

      {/* Main */}
      <div className="flex min-w-0 flex-1 flex-col">
        {/* Mobile top bar */}
        <header className="glass-nav-dark flex items-center justify-between border-b px-4 py-3 lg:hidden">
          <div className="flex min-w-0 items-center gap-2">
            <button type="button" className="rounded-lg border border-slate-700 p-2 text-slate-200" onClick={() => setMobileOpen(true)} aria-label="Open platform navigation" aria-expanded={mobileOpen} aria-controls="platform-mobile-navigation"><Menu className="h-4 w-4" /></button>
            <OpsTraxLogo size={22} />
            <span className="truncate text-sm font-bold">Platform Admin</span>
          </div>
          <button
            type="button"
            onClick={handleLogout}
            className="flex items-center gap-1.5 rounded-lg border border-slate-700 bg-slate-800/60 px-3 py-1.5 text-xs font-semibold text-slate-200 transition hover:border-red-500/40 hover:text-red-300"
          >
            <LogOut className="h-3.5 w-3.5" /> Sign out
          </button>
        </header>
        <main className="flex-1 overflow-y-auto px-5 py-7 lg:px-10 lg:py-9">
          <div className="mx-auto max-w-7xl">
            <Outlet />
          </div>
        </main>
      </div>
    </div>
  );
}

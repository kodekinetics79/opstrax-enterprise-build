import { NavLink, Outlet, useNavigate } from "react-router-dom";
import {
  LayoutDashboard, Building2, Package, Receipt, HeartPulse, ScrollText, LogOut, Gauge, BriefcaseBusiness, Activity, UserCog,
} from "lucide-react";
import { OpsTraxLogo } from "@/components/OpsTraxLogo";
import { usePlatformAuth } from "@/hooks/usePlatformAuth";

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
];

export function PlatformShell() {
  const { session, logout, can } = usePlatformAuth();
  const navigate = useNavigate();

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
          {NAV.filter((item) => can(item.permission)).map((item) => (
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

      {/* Main */}
      <div className="flex min-w-0 flex-1 flex-col">
        {/* Mobile top bar */}
        <header className="glass-nav-dark flex items-center justify-between border-b px-5 py-3 lg:hidden">
          <div className="flex items-center gap-2">
            <OpsTraxLogo size={22} />
            <span className="text-sm font-bold">Platform Admin</span>
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

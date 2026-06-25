import { NavLink, Outlet, useNavigate } from "react-router-dom";
import {
  LayoutDashboard, Building2, Package, Receipt, HeartPulse, ScrollText, LogOut,
} from "lucide-react";
import { OpsTraxLogo } from "@/components/OpsTraxLogo";
import { usePlatformAuth } from "@/hooks/usePlatformAuth";

type NavItem = { to: string; label: string; icon: typeof LayoutDashboard; permission: string };

const NAV: NavItem[] = [
  { to: "/platform", label: "Command Center", icon: LayoutDashboard, permission: "platform:dashboard:view" },
  { to: "/platform/tenants", label: "Tenants", icon: Building2, permission: "platform:tenants:view" },
  { to: "/platform/packages", label: "Packages & Pricing", icon: Package, permission: "platform:packages:view" },
  { to: "/platform/billing", label: "Billing & Invoices", icon: Receipt, permission: "platform:billing:view" },
  { to: "/platform/health", label: "Customer Success", icon: HeartPulse, permission: "platform:health:view" },
  { to: "/platform/audit", label: "Security & Audit", icon: ScrollText, permission: "platform:audit:view" },
];

export function PlatformShell() {
  const { session, logout, can } = usePlatformAuth();
  const navigate = useNavigate();

  const handleLogout = async () => {
    await logout();
    navigate("/platform/login", { replace: true });
  };

  return (
    <div className="flex min-h-screen bg-slate-950 text-slate-100" style={{ backgroundColor: "#020617" }}>
      {/* Sidebar */}
      <aside className="hidden w-64 shrink-0 flex-col border-r border-slate-800 px-4 py-6 lg:flex" style={{ backgroundColor: "#0b1220" }}>
        <div className="flex items-center gap-2.5 px-2">
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
                `flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-medium transition ${
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

        <div className="mt-auto rounded-xl border border-slate-800 bg-slate-900/80 p-3">
          <p className="truncate text-sm font-semibold text-slate-100">{session?.admin.name}</p>
          <p className="truncate text-xs text-slate-500">{session?.role.name}</p>
          <button
            onClick={handleLogout}
            className="mt-3 flex w-full items-center justify-center gap-2 rounded-lg border border-slate-700 bg-slate-800/60 px-3 py-2 text-xs font-semibold text-slate-300 transition hover:border-red-500/40 hover:text-red-300"
          >
            <LogOut className="h-3.5 w-3.5" /> Sign out
          </button>
        </div>
      </aside>

      {/* Main */}
      <div className="flex min-w-0 flex-1 flex-col">
        {/* Mobile top bar */}
        <header className="flex items-center justify-between border-b border-slate-800 bg-slate-900/60 px-5 py-3 lg:hidden">
          <div className="flex items-center gap-2">
            <OpsTraxLogo size={22} />
            <span className="text-sm font-bold">Platform Admin</span>
          </div>
          <button onClick={handleLogout} className="text-xs text-slate-400">Sign out</button>
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

import { Outlet } from "react-router";
import { LogOut } from "lucide-react";
import { OpsTraxLogo } from "@/components/OpsTraxLogo";
import { useAuth } from "@/hooks/useAuth";

/**
 * Customer Portal shell — the portal-user counterpart of DriverLayout.
 *
 * Customer-bound sessions (customer_portal:view without a direct dashboard
 * grant) never render the internal AppShell: ProtectedShell bounces them here,
 * exactly like the driver bounce (UAT DEF-024). This shell deliberately
 * carries NO internal navigation — no sidebar, no module search, no links into
 * back-office surfaces — because everything a portal user may do lives on the
 * portal page itself. Internal supervisors who preview /customer-portal see
 * the same customer chrome the customer sees.
 */
export function CustomerLayout() {
  const { session, logout } = useAuth();
  const displayName = String(session?.user?.name ?? session?.user?.fullName ?? session?.user?.email ?? "Customer");
  const companyLabel = String(session?.company?.name ?? "OpsTrax");

  return (
    <div className="flex min-h-screen flex-col bg-slate-50">
      {/* Header — identity and sign-out only. Shared-desk customer contacts need
          to see who is signed in and be able to end the session. */}
      <header className="sticky top-0 z-30 border-b border-slate-200 bg-white">
        <div className="mx-auto flex max-w-6xl items-center justify-between gap-3 px-4 py-3 md:px-6">
          <div className="flex min-w-0 items-center gap-2.5">
            <OpsTraxLogo size={28} />
            <div className="min-w-0">
              <p className="truncate text-sm font-black tracking-tight text-slate-950">{companyLabel}</p>
              <p className="text-[11px] font-medium tracking-wide text-slate-500">Customer Portal</p>
            </div>
          </div>
          <div className="flex shrink-0 items-center gap-3">
            <div className="hidden min-w-0 flex-col items-end leading-tight sm:flex">
              <span className="max-w-[180px] truncate text-[12px] font-semibold text-slate-900">{displayName}</span>
              <span className="text-[10px] font-medium uppercase tracking-wider text-slate-400">Customer</span>
            </div>
            <button
              type="button"
              onClick={() => { void logout(); }}
              className="flex items-center gap-1.5 rounded-xl border border-slate-200 px-3 py-2 text-xs font-semibold text-slate-600 transition hover:border-slate-300 hover:text-slate-900 active:bg-slate-100"
            >
              <LogOut className="h-4 w-4" />
              Sign out
            </button>
          </div>
        </div>
      </header>

      {/* Portal content */}
      <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-6 md:px-6">
        <Outlet />
      </main>
    </div>
  );
}

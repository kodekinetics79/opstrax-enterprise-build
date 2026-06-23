import { NavLink, Outlet } from "react-router-dom";
import { AlertTriangle, Bell, BookOpen, ClipboardList, Clock, Package, Truck } from "lucide-react";
import { useOfflineQueue } from "@/hooks/useOfflineQueue";
import { useQuery } from "@tanstack/react-query";
import { notificationsApi } from "@/services/notificationsApi";

const NAV = [
  { to: "/driver",               icon: Truck,         label: "Home",     end: true,  badge: false },
  { to: "/driver/assignments",   icon: Package,       label: "Trip",     end: false, badge: false },
  { to: "/driver/dvir",          icon: ClipboardList, label: "DVIR",     end: false, badge: false },
  { to: "/driver/coaching",      icon: BookOpen,      label: "Coaching", end: false, badge: false },
  { to: "/driver/hos",           icon: Clock,         label: "HOS",      end: false, badge: false },
  { to: "/driver/notifications", icon: Bell,          label: "Alerts",   end: false, badge: true  },
];

export function DriverLayout() {
  const { isOnline, pendingCount } = useOfflineQueue();
  const { data: unreadData } = useQuery({
    queryKey: ["notifications", "unread-count"],
    queryFn:  notificationsApi.unreadCount,
    refetchInterval: 30_000,
  });
  const unreadCount = (unreadData as { count?: number } | null)?.count ?? 0;

  return (
    <div className="flex flex-col min-h-screen bg-slate-50">
      {/* Offline banner */}
      {!isOnline && (
        <div className="flex items-center justify-center gap-2 bg-amber-500 px-4 py-2 text-sm font-semibold text-white">
          <AlertTriangle className="h-4 w-4" />
          Offline — drafts saved locally, sync pending
          {pendingCount > 0 ? ` (${pendingCount})` : ""}
        </div>
      )}
      {isOnline && pendingCount > 0 && (
        <div className="flex items-center justify-center gap-2 bg-teal-500 px-4 py-2 text-sm font-semibold text-white">
          Syncing {pendingCount} pending action(s)…
        </div>
      )}

      {/* Page content */}
      <main className="flex-1 overflow-y-auto pb-20">
        <Outlet />
      </main>

      {/* Mobile bottom navigation */}
      <nav className="fixed bottom-0 left-0 right-0 z-40 border-t border-slate-200 bg-white">
        <div className="flex">
          {NAV.map(({ to, icon: Icon, label, end, badge }) => (
            <NavLink
              key={to}
              to={to}
              end={end}
              className={({ isActive }) =>
                `relative flex flex-1 flex-col items-center justify-center py-3 text-xs font-medium transition ${
                  isActive ? "text-teal-600" : "text-slate-400 hover:text-slate-600"
                }`
              }
            >
              {({ isActive }) => (
                <>
                  {isActive && (
                    <span className="absolute top-0 left-2 right-2 h-0.5 rounded-b-full bg-teal-500" />
                  )}
                  <div className="relative mb-1">
                    <Icon className="h-5 w-5" />
                    {badge && unreadCount > 0 && (
                      <span className="absolute -top-1 -right-1 flex h-4 w-4 items-center justify-center rounded-full bg-red-500 text-[9px] font-bold text-white">
                        {unreadCount > 9 ? "9+" : unreadCount}
                      </span>
                    )}
                  </div>
                  {label}
                </>
              )}
            </NavLink>
          ))}
        </div>
      </nav>
    </div>
  );
}

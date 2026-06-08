import { type ReactNode } from "react";
import { Navigate, Outlet, useLocation } from "react-router-dom";
import { ShieldAlert } from "lucide-react";
import { hasPermission, getPermissionVariants, PERMISSIONS } from "@/auth/rbacConfig";
import { useAuth } from "./useAuth";

export function usePermissions(): string[] {
  const { session } = useAuth();
  return session?.permissions ?? [];
}

export function useHasPermission(): (permission: string) => boolean {
  const permissions = usePermissions();
  return (permission: string) => hasPermission(permissions, permission);
}

export function usePermission(permission: string): boolean {
  const has = useHasPermission();
  return has(permission);
}

export function useAnyPermission(permissions: string[]): boolean {
  const has = useHasPermission();
  return permissions.some((permission) => has(permission));
}

export function useHasAnyPermission(permissions: string[]): boolean {
  return useAnyPermission(permissions);
}

export function useHasAllPermissions(permissions: string[]): boolean {
  const has = useHasPermission();
  return permissions.every((permission) => has(permission));
}

export function ProtectedRoute({ redirectTo = "/login" }: { redirectTo?: string }) {
  const { session } = useAuth();
  const location = useLocation();

  if (!session) {
    return <Navigate to={redirectTo} replace state={{ from: location }} />;
  }

  return <Outlet />;
}

export function RequirePermission({
  permission,
  permissions,
  children,
  fallback,
}: {
  permission?: string;
  permissions?: string[];
  children: ReactNode;
  fallback?: ReactNode;
}) {
  const has = useHasPermission();
  const granted = permission ? has(permission) : (permissions ?? []).some((item) => has(item));

  if (!granted) {
    return fallback ?? <PermissionDenied permission={permission ?? permissions?.[0] ?? "unknown"} />;
  }

  return <>{children}</>;
}

export function PermissionGate({
  permission,
  permissions,
  children,
  fallback = null,
}: {
  permission?: string;
  permissions?: string[];
  children: ReactNode;
  fallback?: ReactNode;
}) {
  const has = useHasPermission();
  const granted = permission ? has(permission) : (permissions ?? []).some((item) => has(item));
  return granted ? <>{children}</> : <>{fallback}</>;
}

export function PermissionDenied({ permission }: { permission: string }) {
  return (
    <div className="grid min-h-[60vh] place-items-center px-4">
      <div className="panel max-w-xl p-8 text-center">
        <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl border border-rose-400/20 bg-rose-400/10 text-rose-300">
          <ShieldAlert className="h-7 w-7" />
        </div>
        <p className="mt-5 text-[11px] font-bold uppercase tracking-[0.22em] text-rose-400/90">
          Permission denied
        </p>
        <h1 className="mt-2 text-2xl font-bold tracking-tight text-slate-100">
          You do not have permission to perform this action
        </h1>
        <p className="mt-3 text-sm leading-6 text-slate-400">
          The current session does not include <span className="font-semibold text-slate-200">{permission}</span>.
        </p>
      </div>
    </div>
  );
}

export { getPermissionVariants, PERMISSIONS };

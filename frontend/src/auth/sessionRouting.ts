import { hasPermission } from "@/auth/rbacConfig";
import type { UserSession } from "@/types";

export function getLandingRouteForSession(session: UserSession | null): string {
  const permissions = session?.permissions ?? [];
  const normalize = (permission: string) => permission.trim().toLowerCase().replace(/\./g, ":");
  const direct = new Set(permissions.map(normalize));
  const ownsDirectly = (permission: string) => direct.has("*") || direct.has(normalize(permission));

  // driver:self is an identity boundary, not a semantic navigation alias. It appears
  // in several operation permission groups because driver endpoints may perform those
  // narrow actions. Expanding those aliases here made a Driver look dashboard-capable
  // and rendered the entire back-office shell before the APIs correctly rejected it.
  if (ownsDirectly("driver:self") && !ownsDirectly("dashboard:view")) {
    return "/driver";
  }
  if (hasPermission(permissions, "dashboard:view")) {
    return "/live-dashboard";
  }
  if (hasPermission(permissions, "customer_portal:view")) {
    return "/customer-portal";
  }
  if (hasPermission(permissions, "shipments:view")) {
    return "/shipments";
  }
  if (hasPermission(permissions, "drivers:view")) {
    return "/drivers";
  }

  return "/live-dashboard";
}

import { hasPermission } from "@/auth/rbacConfig";
import type { UserSession } from "@/types";

export function getLandingRouteForSession(session: UserSession | null): string {
  const permissions = session?.permissions ?? [];

  if (hasPermission(permissions, "driver:self") && !hasPermission(permissions, "dashboard:view")) {
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

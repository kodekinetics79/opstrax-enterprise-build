import { hasPermission } from "@/auth/rbacConfig";
import type { UserSession } from "@/types";

type SessionLike = Pick<UserSession, "permissions" | "user"> | null | undefined;

/**
 * Canonical token form for DIRECT (non-semantic) comparisons.
 *
 * The four permission copies (backend RolePermissionDefaults, the DB role seed,
 * the frontend catalogue and the login payload) each spell tokens slightly
 * differently. `customer-portal:view` (DB roles 9/10) and `customer_portal:view`
 * (backend defaults) are the SAME grant; a normalizer that only folded "." into
 * ":" treated them as different and left the seeded Customer Portal User
 * matching no rung at all. Fold "." → ":" and "-" → "_".
 */
function normalizeToken(permission: string) {
  return permission.trim().toLowerCase().replace(/\./g, ":").replace(/-/g, "_");
}

/**
 * Grants that ONLY an internal back-office user is ever given.
 *
 * Deliberately excludes `shipments:view` and `alerts:view`: real portal roles
 * (Customer = shipments:view + customer_portal:view + alerts:view, Customer
 * Viewer, Customer Portal User) hold those, and treating them as internal would
 * put a genuine customer back inside the 35-module shell.
 */
const INTERNAL_DIRECT_GRANTS = [
  "dashboard:view",
  "dispatch:view",
  "customers:view",
  "crm:view",
  "maintenance:view",
  "safety:view",
  "compliance:view",
  "vehicles:view",
  "drivers:view",
  "fleet:view",
  "users:view",
  "roles:view",
  "audit:view",
  "settings:view",
  "reports:view",
  "finance:view",
  "billing:view",
  "carriers:view",
  "fuel:view",
  "ops:view",
  "vendor_portal:view",
];

/** Keys the login payload may carry for a server-side customer binding. */
const CUSTOMER_BINDING_KEYS = ["customerId", "customer_id", "customerid", "portalCustomerId"];

/**
 * The server-side customer binding (`users.customer_id`), if the session carries
 * one. This is the ONLY authoritative "is this session customer-bound?" signal:
 * the middleware sets AuthCustomerIdItemKey from that column and such a principal
 * is locked out of every internal endpoint, so the SPA must confine it too.
 *
 * The login response does NOT expose this field today (it returns only
 * {id, email, name}); this reads it opportunistically so the boundary keys on the
 * real binding the moment the backend adds it, and falls back to the permission
 * shape below until then. See the report note for Packet 8.
 */
function customerBindingId(session: SessionLike): string | null {
  const user = (session?.user ?? null) as Record<string, unknown> | null;
  if (!user) return null;
  for (const key of CUSTOMER_BINDING_KEYS) {
    const raw = user[key];
    if (raw === null || raw === undefined) continue;
    const value = String(raw).trim();
    if (value && value !== "0") return value;
  }
  return null;
}

/**
 * True when the session is a CUSTOMER-PORTAL identity that must never render the
 * internal shell.
 *
 * The previous rule was `customer_portal:view && !dashboard:view`, which reads as
 * "is a portal user" but actually captures INTERNAL staff: the shipped
 * `Customer Service` role (customers:view, customer_portal:view, dispatch:view,
 * crm:view) and the seeded `CRM & Sales Manager` (crm:view, campaigns:*,
 * customer_portal:view, reports:view) both match it. Those accounts were
 * imprisoned in the chrome-less portal shell, could not reach /dispatch,
 * /customers or /crm, and got 403s from the portal APIs because they have no
 * customer binding — an unusable account either way.
 *
 * So: bind-first, and never fire when the session carries any internal grant.
 */
export function isPortalConfinedSession(session: SessionLike): boolean {
  if (!session) return false;
  const permissions = session.permissions ?? [];
  const direct = new Set(permissions.map(normalizeToken));
  if (direct.has("*")) return false;
  const owns = (permission: string) => direct.has(normalizeToken(permission));

  // A server-side binding is authoritative — the API will reject every internal
  // call from this principal regardless of what else the token lists.
  if (customerBindingId(session) !== null) return true;

  if (!owns("customer_portal:view")) return false;
  return !INTERNAL_DIRECT_GRANTS.some(owns);
}

/**
 * A landing candidate, and the guard its route ACTUALLY carries in App.tsx.
 *
 * `selectors` are DIRECT grants (deterministic identity), never semantic ones:
 * semantic selection is how `Mechanic` (maintenance:view, fleet:view) and the
 * seeded `Finance & Billing Manager` (finance:*, fuel:*, reports:view) were sent
 * to /shipments — fleet:view reaches shipments:view through the shared
 * operations.execution_summary.read alias group — where the route's DIRECT
 * shipments:view guard then rendered PermissionDenied. They logged straight into
 * a dead end.
 *
 * `guard` mirrors the route's own <RequirePermission> and is verified before the
 * rung is chosen, so a landing route can never be one the user is denied.
 * KEEP IN LOCKSTEP WITH App.tsx — test-rbac-contract.mjs asserts they match.
 */
export type LandingRung = {
  route: string;
  selectors: string[];
  guard: { permissions: string[]; direct?: boolean };
};

export const LANDING_LADDER: LandingRung[] = [
  { route: "/live-dashboard",    selectors: ["dashboard:view"],                    guard: { permissions: ["dashboard:view"] } },
  { route: "/shipments",         selectors: ["shipments:view"],                    guard: { permissions: ["shipments:view"], direct: true } },
  { route: "/dispatch",          selectors: ["dispatch:view"],                     guard: { permissions: ["dispatch:view"] } },
  { route: "/customers",         selectors: ["customers:view", "crm:view"],        guard: { permissions: ["customers:view"] } },
  // Vendor Service Provider (DB role 16: vendor_portal:view, maintenance:view,
  // pod:view) is an EXTERNAL service partner that holds no dashboard grant and no
  // customer binding, so it matched no rung and dead-ended on /login with a valid
  // session. Maintenance is the surface it actually holds.
  { route: "/maintenance",       selectors: ["vendor_portal:view", "maintenance:view"], guard: { permissions: ["maintenance:view"] } },
  { route: "/safety",            selectors: ["safety:view"],                       guard: { permissions: ["safety:view"] } },
  { route: "/compliance",        selectors: ["compliance:view"],                   guard: { permissions: ["compliance:view"] } },
  { route: "/drivers",           selectors: ["drivers:view"],                      guard: { permissions: ["drivers:view"] } },
  { route: "/vehicles",          selectors: ["vehicles:view", "fleet:view"],       guard: { permissions: ["vehicles:view"] } },
  { route: "/reports-analytics", selectors: ["reports:view"],                      guard: { permissions: ["reports:view"] } },
  // Internal staff whose ONLY surface is the portal preview (they hold
  // customer_portal:view plus some internal grant that has no landing page).
  { route: "/customer-portal",   selectors: ["customer_portal:view"],              guard: { permissions: ["customer_portal:view"] } },
  { route: "/settings",          selectors: ["settings:view"],                     guard: { permissions: ["settings:view"] } },
];

const PORTAL_GUARD: LandingRung["guard"] = { permissions: ["customer_portal:view"] };
// The /driver shell's own guard (App.tsx: <RequirePermission permission="driver:self">).
// Landing on /driver must be verified against THIS, not against the token that selected it.
const DRIVER_GUARD: LandingRung["guard"] = { permissions: ["driver:self"] };

/** Evaluate a route guard exactly the way <RequirePermission> does. */
export function landingGuardPasses(permissions: string[], guard: LandingRung["guard"]): boolean {
  if (guard.direct) {
    // MUST match useHasDirectPermission (hooks/usePermission.tsx), which is what the real
    // `direct` route guard uses — and it folds "." → ":" ONLY. Normalising "-" → "_" here
    // as well made this ladder MORE permissive than the guard it is predicting, so it could
    // certify a direct-guarded landing that the route then refuses. Keep the two spellings
    // of normalisation apart: isPortalConfinedSession genuinely needs the "-" fold
    // (customer-portal:view ≡ customer_portal:view); a direct guard must not have it.
    const normalizeDirect = (permission: string) => permission.trim().toLowerCase().replace(/\./g, ":");
    const owned = new Set(permissions.map(normalizeDirect));
    if (owned.has("*")) return true;
    return guard.permissions.some((permission) => owned.has(normalizeDirect(permission)));
  }
  return guard.permissions.some((permission) => hasPermission(permissions, permission));
}

export function getLandingRouteForSession(session: UserSession | null): string {
  const permissions = session?.permissions ?? [];
  const direct = new Set(permissions.map(normalizeToken));
  const ownsDirectly = (permission: string) => direct.has("*") || direct.has(normalizeToken(permission));

  // ── Identity boundaries first, decided on DIRECT grants only ──
  // driver:self is an identity boundary, not a semantic navigation alias. It appears
  // in several operation permission groups because driver endpoints may perform those
  // narrow actions. Expanding those aliases here made a Driver look dashboard-capable
  // and rendered the entire back-office shell before the APIs correctly rejected it.
  //
  // ROUND-2 FIX — `driver:portal` was accepted here as "the DB seed's spelling of the
  // same boundary" (roles row 5) and this branch was the ONLY landing decision exempt
  // from guard verification. It is not the same boundary: /driver's shell guards on
  // `driver:self`, and the seeded Driver's grants (driver:portal, jobs:view, dvir:manage)
  // do NOT satisfy it. So the session landed on /driver, PermissionDenied rendered, and
  // ProtectedShell (App.tsx) bounced every other URL — including /login — straight back
  // to /driver. An inescapable dead end, recoverable only by signing out.
  // The branch is gone AND the landing is now guard-verified like every other rung: a
  // driver:portal-only session falls through to the fail-closed /login terminal and gets
  // NoWorkspaceScreen — an honest explanation with a working sign-out.
  // (RolePermissionReconciler rewrites is_system roles at boot and would usually repair
  // the seeded row, but it is non-fatal on failure and never touches tenant-authored
  // custom roles, so it cannot be relied on to make this invariant true.)
  if (ownsDirectly("driver:self") && !ownsDirectly("dashboard:view")) {
    if (landingGuardPasses(permissions, DRIVER_GUARD)) return "/driver";
    return "/login";
  }
  if (isPortalConfinedSession(session)) {
    // Still verify the portal's own guard: a session bound to a customer but not
    // granted customer_portal:view would be denied there, so fall through to the
    // no-workspace terminal rather than into a PermissionDenied page.
    if (landingGuardPasses(permissions, PORTAL_GUARD)) return "/customer-portal";
    return "/login";
  }

  // ── Back-office landings — selected by DIRECT grant, then guard-verified ──
  for (const rung of LANDING_LADDER) {
    if (!rung.selectors.some(ownsDirectly)) continue;
    if (!landingGuardPasses(permissions, rung.guard)) continue;
    return rung.route;
  }

  // Fail CLOSED: a session that matches no landing surface goes back to /login,
  // where App.tsx renders the "no assigned workspace" terminal (an explanation and
  // a working sign-out) rather than an ordinary login form that appears to do
  // nothing when valid credentials are submitted.
  return "/login";
}

// RBAC & portal-isolation contract (RETEST-20260821-1035-R1 / DEF-006, DEF-024, DEF-025).
//
// Compiles the REAL auth modules (rbacConfig.ts + sessionRouting.ts + moduleConfig.ts)
// with esbuild and executes them, so these assertions exercise the exact alias-closure,
// landing-route and navigation behaviour the app ships — not a re-implementation of it.
//
// The invariants under test:
//  1. Alias groups are symmetric closures, so no narrow permission's group may carry a
//     broad module token (that is how alerts:view once satisfied dashboard:view). The
//     closure MERGES groups that share a token, so a group must be checked by execution:
//     tightening TELEMETRY_LIVE_STATE_READ alone was a no-op while the untouched
//     TELEMATICS_GPS_VIEW group still listed fleet:view beside the shared canonical.
//  2. Landing routes decide identity boundaries (driver, customer portal) on DIRECT
//     grants, verify the chosen route's OWN guard, and fail CLOSED to /login.
//  3. Governance routes (/admin, /user-management) accept only direct grants.
//  4. Every navigation entry's requiredPermission implies its route guard — a nav item
//     that is visible but whose route denies is a PermissionDenied dead end.
//
// Cases are driven by the SHIPPED BACKEND grant sets (EndpointMappings.RolePermissionDefaults,
// parsed from source) and the DB role seed, not only the idealised frontend catalogue —
// testing the catalogue alone is why real-role regressions passed this script before.
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const repoRoot = resolve(root, "..");
const require = createRequire(import.meta.url);
const esbuild = require("esbuild");

const built = await esbuild.build({
  stdin: {
    contents: [
      'export { hasPermission, getPermissionsForRole, ROLE_PERMISSIONS } from "@/auth/rbacConfig";',
      'export { getLandingRouteForSession, isPortalConfinedSession, LANDING_LADDER, landingGuardPasses } from "@/auth/sessionRouting";',
      'export { modules } from "@/modules/moduleConfig";',
    ].join("\n"),
    resolveDir: root,
    loader: "ts",
  },
  bundle: true,
  format: "esm",
  write: false,
  alias: { "@": resolve(root, "src") },
  logLevel: "silent",
});

const {
  hasPermission, ROLE_PERMISSIONS,
  getLandingRouteForSession, isPortalConfinedSession, LANDING_LADDER, landingGuardPasses,
  modules,
} = await import(
  "data:text/javascript;base64," + Buffer.from(built.outputFiles[0].text).toString("base64")
);

const landing = (permissions) => getLandingRouteForSession({ permissions });
const normalize = (permission) => permission.trim().toLowerCase().replace(/\./g, ":").replace(/-/g, "_");

// ── 0. The SHIPPED backend grant sets, parsed from source ─────────────────────
// Reading these instead of restating them means a backend grant change that breaks a
// real role's landing route fails HERE, not in production. EndpointMappings.cs is owned
// by the backend packets; this script only reads it.
const endpointMappings = readFileSync(resolve(repoRoot, "backend-dotnet/Controllers/EndpointMappings.cs"), "utf8");
const defaultsBlock = endpointMappings.slice(
  endpointMappings.indexOf("RolePermissionDefaults = new(StringComparer.OrdinalIgnoreCase)"),
);
const BACKEND_ROLES = {};
for (const match of defaultsBlock.matchAll(/\["([^"]+)"\]\s*=\s*\[([^\]]*)\],/g)) {
  BACKEND_ROLES[match[1]] = [...match[2].matchAll(/"([^"]*)"/g)].map((token) => token[1]);
  if (Object.keys(BACKEND_ROLES).length >= 40) break;
}
assert.ok(
  Object.keys(BACKEND_ROLES).length >= 15,
  `RolePermissionDefaults must be parsable from EndpointMappings.cs (found ${Object.keys(BACKEND_ROLES).length} roles) — if the shape changed, fix this parser rather than letting the real-role cases silently vanish`,
);
for (const required of ["Dispatcher", "Driver", "Customer", "Customer Service", "Mechanic", "Customer Viewer"]) {
  assert.ok(BACKEND_ROLES[required], `RolePermissionDefaults must still define "${required}"`);
}

// database/init/002_seed.sql — a THIRD token vocabulary again (customer-portal:view,
// driver:portal). Real seeded sessions carry these spellings.
const DB_SEED_ROLES = {
  "db:Fleet Manager": ["dashboard:view", "fleet:view", "fleet:manage", "maintenance:view", "maintenance:manage", "telematics:view", "dispatch:view", "intelligence:view", "map:view"],
  "db:Mechanic": ["maintenance:view", "maintenance:manage", "dvir:review", "fleet:view"],
  "db:Customer Service": ["customers:view", "customer-portal:view", "dispatch:view", "crm:view"],
  "db:Customer Portal User": ["customer-portal:view"],
  "db:Finance & Billing Manager": ["finance:view", "finance:manage", "fuel:view", "fuel:manage", "reports:view", "settings:view"],
  "db:CRM & Sales Manager": ["crm:view", "crm:manage", "campaigns:view", "campaigns:manage", "customer_portal:view", "reports:view"],
  "db:Vendor Service Provider": ["vendor_portal:view", "maintenance:view", "pod:view"],
  "db:Driver": ["driver:portal", "jobs:view", "dvir:manage"],
  "db:Read-only Auditor": ["audit:view", "fleet:view", "dashboard:view"],
  "db:Operations Manager": ["dashboard:view", "map:view", "fleet:view", "dispatch:view", "dispatch:manage", "orders:view", "orders:manage", "shipments:view", "shipments:manage", "pod:view", "pod:upload", "maintenance:view", "safety:view", "dashcam:view", "compliance:view", "reports:view", "settings:view"],
};

// ── 1. Portal roles must not satisfy internal module permissions ──
for (const role of ["customer", "customer_portal_user", "customer_viewer"]) {
  const owned = ROLE_PERMISSIONS[role];
  assert.ok(Array.isArray(owned) && owned.length > 0, `${role} must exist in ROLE_PERMISSIONS`);
  for (const forbidden of ["dashboard:view", "users:view", "roles:view", "audit:view", "telemetry.devices.read", "telemetry.live_state.read", "settings:view"]) {
    assert.equal(hasPermission(owned, forbidden), false, `${role} must NOT satisfy ${forbidden}`);
  }
}
for (const [name, owned] of Object.entries(BACKEND_ROLES)) {
  if (!owned.includes("customer_portal:view") || owned.includes("dashboard:view") || owned.includes("*")) continue;
  if (owned.some((token) => ["dispatch:view", "customers:view", "crm:view", "maintenance:view"].includes(token))) continue;
  for (const forbidden of ["dashboard:view", "users:view", "audit:view", "telemetry.devices.read", "telemetry.live_state.read"]) {
    assert.equal(hasPermission(owned, forbidden), false, `backend role "${name}" must NOT satisfy ${forbidden}`);
  }
}

// ── 2. Dispatcher must not reach governance surfaces (DEF-025) ──
for (const forbidden of ["audit:view", "users:view", "roles:view", "settings:view"]) {
  assert.equal(hasPermission(ROLE_PERMISSIONS.dispatcher, forbidden), false, `dispatcher must NOT satisfy ${forbidden}`);
  assert.equal(hasPermission(BACKEND_ROLES.Dispatcher, forbidden), false, `backend Dispatcher must NOT satisfy ${forbidden}`);
}

// ── 3. The measured poisonous-alias witnesses stay dead ──
assert.equal(hasPermission(["alerts:view"], "dashboard:view"), false, "alerts:view must NOT satisfy dashboard:view");
assert.equal(hasPermission(["shipments:view"], "telemetry.devices.read"), false, "shipments:view must NOT satisfy telemetry.devices.read (DEF-006)");
assert.equal(hasPermission(["reports:view"], "audit:view"), false, "reports:view must NOT satisfy audit:view (DEF-025 enabler)");
assert.equal(hasPermission(["reports:view"], "dashboard:view"), false, "reports:view must NOT satisfy dashboard:view");
assert.equal(hasPermission(["fleet:view"], "dashboard:view"), false, "fleet:view must NOT satisfy dashboard:view");

// The three that the earlier tightening MISSED: TELEMATICS_GPS_VIEW / TELEMATICS_DEVICES_VIEW
// still listed the broad tokens beside the canonical that TELEMETRY_* shares, so the
// closure merged them straight back in and the "fix" was a no-op. Assert by execution.
assert.equal(hasPermission(["fleet:view"], "telemetry.live_state.read"), false, "fleet:view must NOT satisfy telemetry.live_state.read (alias closure re-entry)");
assert.equal(hasPermission(["fleet:view"], "telemetry.devices.read"), false, "fleet:view must NOT satisfy telemetry.devices.read (alias closure re-entry)");
assert.equal(hasPermission(["telematics:view"], "telemetry.live_state.read"), false, "telematics:view must NOT satisfy telemetry.live_state.read");
assert.equal(hasPermission(["telematics:view"], "telemetry.devices.read"), false, "telematics:view must NOT satisfy telemetry.devices.read");
assert.equal(hasPermission(["fleet:view"], "telematics:gps:view"), false, "fleet:view must NOT satisfy telematics:gps:view");
assert.equal(hasPermission(["fleet:view"], "telematics:devices:view"), false, "fleet:view must NOT satisfy telematics:devices:view");
assert.equal(hasPermission(["fleet:view"], "map:view"), false, "fleet:view must NOT satisfy map:view");
assert.equal(hasPermission(["vehicles:view"], "telemetry.live_state.read"), false, "vehicles:view must NOT satisfy telemetry.live_state.read");
// These four groups are the frontend mirror of the backend satisfy-sets. Keep them equal.
assert.equal(hasPermission(["map:view"], "telemetry.live_state.read"), true, "map:view must still satisfy telemetry.live_state.read (backend mirror)");
assert.equal(hasPermission(["telematics:gps:view"], "telemetry.live_state.read"), true, "telematics:gps:view must still satisfy telemetry.live_state.read (backend mirror)");
assert.equal(hasPermission(["telematics:devices:view"], "telemetry.devices.read"), true, "telematics:devices:view must still satisfy telemetry.devices.read (backend mirror)");

// ── 3b. Governance view tiers are ONE-WAY (round-2 release blocker) ───────────
// Measured on the shipped closure BEFORE the fix: every assertion below was TRUE.
// The three view groups listed their own manage token, and because groups are
// symmetric and merge on shared tokens, `settings:view` was EQUIVALENT to
// `settings:manage` — which gates POST /api/settings/api-keys, PUT /api/settings/webhook
// and POST /api/settings/webhook/rotate-secret — while `users:view` was equivalent to
// `users:manage`, which gates POST/PUT/DELETE /api/feature-flags.
// Enumerated, not hand-picked: every (view, write) pair in the governance triad.
const GOVERNANCE_VIEW_TIERS = {
  "settings:view": ["settings:manage", "settings:update"],
  "users:view": ["users:manage", "users:create", "users:update", "users:delete"],
  "roles:view": ["users:manage", "roles:manage", "roles:create", "roles:update"],
};
for (const [view, writes] of Object.entries(GOVERNANCE_VIEW_TIERS)) {
  for (const write of writes) {
    assert.equal(hasPermission([view], write), false, `${view} must NOT satisfy ${write} — a view tier is one-way`);
  }
  // The dot spelling must not be a back door around the colon spelling.
  assert.equal(
    hasPermission([view.replace(":", ".")], writes[0].replace(":", ".")),
    false,
    `${view.replace(":", ".")} must NOT satisfy ${writes[0].replace(":", ".")}`,
  );
}
// settings:view also reached the telematics provider-management token, because
// SETTINGS_VIEW carried settings:manage and TELEMATICS_PROVIDERS_MANAGE lists it too.
assert.equal(hasPermission(["settings:view"], "telematics:providers:manage"), false, "settings:view must NOT satisfy telematics:providers:manage");
assert.equal(hasPermission(["settings:view"], "telemetry.devices.manage"), false, "settings:view must NOT satisfy telemetry.devices.manage");

// ── 3c. AUD-003: action inheritance is directed, never a sibling closure ─────
// Every pair below evaluated TRUE on b982ef8. A narrow/read/export grant must not
// acquire another mutation merely because both were historically listed together.
for (const [held, required] of [
  ["drivers:create", "drivers:delete"],
  ["vehicles:update", "vehicles:delete"],
  ["shipments:create", "shipments:delete"],
  ["dispatch:create", "dispatch:cancel"],
  ["customers:update", "customers:delete"],
  ["safety:create", "safety:review"],
  ["maintenance:update", "maintenance:close"],
  ["compliance:export", "compliance:manage"],
  ["alerts:acknowledge", "alerts:close"],
  ["reports:export", "reports:manage"],
  ["users:create", "users:delete"],
  ["roles:create", "roles:update"],
  ["telematics:devices:update", "telemetry.devices.manage"],
  ["dispatch.smart_assign.read", "dispatch.smart_assign.accept"],
  ["dispatch:assign", "dispatch.smart_assign.recommend"],
  ["dispatch:assign", "dispatch.smart_assign.accept"],
  ["reports:view", "reports:export"],
  ["dispatch:manage", "shipments:delete"],
  ["operations.site_access.read", "operations.site_access.create"],
  ["operations.access_document.read", "operations.access_document.verify"],
  ["operations.access_document.update", "operations.access_document.verify"],
  ["operations.pickup_authorization.update", "operations.pickup_authorization.verify"],
  ["operations.proof.read", "operations.proof.validate"],
  ["operations.proof.create", "operations.proof.update"],
  ["operations.proof.create", "operations.proof.submit"],
  ["finance.invoice.issue", "finance.invoice.approve"],
  ["settlement.create", "settlement.pay"],
  ["tax.update", "tax.publish"],
  ["revrec.update", "revrec.period.close"],
]) {
  assert.equal(hasPermission([held], required), false, `${held} must NOT satisfy sibling mutation ${required}`);
}

for (const [held, required] of [
  ["fleet:manage", "vehicles:delete"],
  ["drivers:manage", "drivers:assign"],
  ["shipments:manage", "shipments:delete"],
  ["dispatch:manage", "dispatch:cancel"],
  ["maintenance:manage", "maintenance:close"],
  ["compliance:manage", "compliance:export"],
  ["alerts:manage", "alerts:close"],
  ["reports:manage", "reports:export"],
  ["users:manage", "users:delete"],
  ["roles:manage", "roles:update"],
  ["settings:manage", "settings:update"],
  ["finance:manage", "finance.invoice.approve"],
]) {
  assert.equal(hasPermission([held], required), true, `${held} must retain intentional implication to ${required}`);
}

// The Read-only Auditor is the role this defect was reported against. Drive the
// SHIPPED backend grant set, not the frontend catalogue.
const READ_ONLY_AUDITOR = BACKEND_ROLES["Read-Only Auditor"];
assert.ok(READ_ONLY_AUDITOR?.includes("settings:view"), "Read-Only Auditor must still hold settings:view by direct grant");
for (const forbidden of [
  "settings:manage", "settings:update",
  "users:manage", "users:create", "users:update", "users:delete",
  "roles:manage", "roles:create", "roles:update",
  "telemetry.devices.manage", "telematics:providers:manage",
]) {
  assert.equal(hasPermission(READ_ONLY_AUDITOR, forbidden), false, `Read-Only Auditor must NOT reach ${forbidden}`);
}
// …and the seeded Operations Manager / Finance & Billing Manager, which hold
// settings:view and nothing else in the settings family.
for (const seeded of ["db:Operations Manager", "db:Finance & Billing Manager"]) {
  const owned = DB_SEED_ROLES[seeded];
  assert.ok(owned?.includes("settings:view"), `${seeded} must still hold settings:view by direct grant`);
  for (const forbidden of ["settings:update", "users:manage", "roles:update"]) {
    assert.equal(hasPermission(owned, forbidden), false, `${seeded} must NOT reach ${forbidden}`);
  }
}
// The genuine write tiers must survive the tightening — a one-way edge, not a deletion.
assert.equal(hasPermission(["settings:update"], "settings:manage"), false, "settings:update must not become settings:manage");
assert.equal(hasPermission(["settings:manage"], "settings:update"), true, "settings:manage must still satisfy settings:update");
assert.equal(hasPermission(["users:create"], "users:manage"), false, "users:create must not become users:manage");
assert.equal(hasPermission(["roles:update"], "roles:manage"), false, "roles:update must not become roles:manage");
assert.equal(hasPermission(BACKEND_ROLES["Tenant Admin"], "settings:manage"), true, "Tenant Admin must keep settings:manage (settings:update)");
assert.equal(hasPermission(BACKEND_ROLES["Tenant Admin"], "users:manage"), true, "Tenant Admin must keep users:manage (users:create/update/delete)");
assert.equal(hasPermission(BACKEND_ROLES["Fleet Owner"], "settings:manage"), true, "Fleet Owner must keep settings:manage (settings:update)");

// ── 3c. The four telemetry groups the enforcing engine had never mirrored ─────
// EndpointMappings declared these satisfy-sets, the frontend mirrored them, and the
// ENGINE (FoundationServices) did not — so the SPA rendered the device kill switch and
// the API 403'd. Both sides now agree; assert the frontend half by execution.
for (const [held, required] of [
  ["fleet:manage", "telemetry.devices.manage"],
  ["fleet.manage", "telemetry.devices.manage"],
  ["telematics:providers:manage", "telemetry.devices.manage"],
  ["alerts:view", "telemetry.alerts.read"],
  ["safety:view", "telemetry.alerts.read"],
  ["maintenance:view", "telemetry.alerts.read"],
  ["alerts:manage", "telemetry.alerts.manage"],
  ["fleet:manage", "telemetry.rules.manage"],
  ["devices:manage", "telemetry.rules.manage"],
]) {
  assert.equal(hasPermission([held], required), true, `${held} must satisfy ${required} (backend mirror)`);
}
// A read grant still never reaches the device write tier.
assert.equal(hasPermission(["telematics:devices:view"], "telemetry.devices.manage"), false, "telematics:devices:view must NOT satisfy telemetry.devices.manage");
// Broad fleet read alone is not an alert grant on either side.
assert.equal(hasPermission(["fleet:view"], "telemetry.alerts.read"), false, "fleet:view must not reach telemetry.alerts.read");

// ── 4. Internal roles keep the telemetry surfaces they hold legitimately ──
assert.equal(hasPermission(ROLE_PERMISSIONS.fleet_manager, "telemetry.live_state.read"), true, "fleet_manager must keep the live map");
assert.equal(hasPermission(ROLE_PERMISSIONS.fleet_manager, "telemetry.devices.read"), true, "fleet_manager must keep device surfaces");
assert.equal(hasPermission(ROLE_PERMISSIONS.dispatcher, "telemetry.devices.read"), true, "dispatcher must keep device surfaces (telematics:devices:view)");
assert.equal(hasPermission(ROLE_PERMISSIONS.tenant_admin, "audit:view"), true, "tenant_admin must keep audit:view (direct grant)");
// No role may lose a permission it holds by DIRECT grant.
for (const [name, owned] of Object.entries({ ...ROLE_PERMISSIONS, ...BACKEND_ROLES, ...DB_SEED_ROLES })) {
  for (const token of owned) {
    if (token === "*") continue;
    assert.equal(hasPermission(owned, token), true, `${name} must still satisfy its own direct grant ${token}`);
  }
}

// ── 5. Landing routes: identity boundaries first, guard-verified, fail closed (DEF-024) ──
assert.equal(landing(BACKEND_ROLES.Driver), "/driver", "backend Driver session must land on /driver");
// ROUND-2 FIX: this used to assert the seeded Driver lands on /driver. It DID — and then
// /driver's own guard (driver:self) denied it, ProtectedShell bounced every other URL back
// to /driver, and the session was stuck on PermissionDenied with no way out but signing out.
// The `driver:portal` landing branch was the only one exempt from guard verification, and
// this assertion certified the dead end. A landing the session cannot render is worse than
// no landing: fail closed to the /login terminal, which renders NoWorkspaceScreen.
assert.equal(
  landing(DB_SEED_ROLES["db:Driver"]), "/login",
  "the seeded Driver (driver:portal, no driver:self) must fail closed to the no-workspace terminal — /driver's guard would deny it and ProtectedShell would trap the session there",
);
assert.equal(landing(["driver:self"]), "/driver", "driver:self is the real identity boundary and must still land on /driver");
assert.equal(landing(["driver:self", "dashboard:view"]), "/live-dashboard", "a driver who also holds dashboard:view is back-office, not portal-confined");
assert.equal(landing(ROLE_PERMISSIONS.customer_portal_user), "/customer-portal", "customer_portal_user must land on /customer-portal");
assert.equal(landing(ROLE_PERMISSIONS.customer), "/customer-portal", "customer must land on /customer-portal");
assert.equal(landing(ROLE_PERMISSIONS.customer_viewer), "/customer-portal", "customer_viewer must land on /customer-portal");
assert.equal(landing(BACKEND_ROLES.Customer), "/customer-portal", "backend Customer must land on /customer-portal");
assert.equal(landing(BACKEND_ROLES["Customer Viewer"]), "/customer-portal", "backend Customer Viewer must land on /customer-portal");
assert.equal(landing(DB_SEED_ROLES["db:Customer Portal User"]), "/customer-portal", "the seeded Customer Portal User (customer-portal:view) must land on /customer-portal");
assert.equal(landing([]), "/login", "an empty permission set must fail closed to /login");
assert.equal(getLandingRouteForSession(null), "/login", "a null session must fail closed to /login");
for (const role of ["super_admin", "tenant_admin", "company_admin", "fleet_manager", "dispatcher"]) {
  assert.equal(landing(ROLE_PERMISSIONS[role]), "/live-dashboard", `${role} must still land on /live-dashboard`);
}

// INTERNAL staff that hold customer_portal:view must NOT be imprisoned in the portal.
// "Customer Service" and "CRM & Sales Manager" match the old
// `customer_portal:view && !dashboard:view` rule exactly, and both are internal.
for (const [name, owned] of [
  ["Customer Service", BACKEND_ROLES["Customer Service"]],
  ["db:Customer Service", DB_SEED_ROLES["db:Customer Service"]],
  ["db:CRM & Sales Manager", DB_SEED_ROLES["db:CRM & Sales Manager"]],
]) {
  assert.equal(isPortalConfinedSession({ permissions: owned }), false, `${name} is INTERNAL staff and must not be confined to the customer portal`);
  assert.notEqual(landing(owned), "/customer-portal", `${name} must not land in the customer portal`);
  assert.notEqual(landing(owned), "/login", `${name} must have a usable landing route`);
}
// Vendor sessions must match a rung rather than dead-ending on /login.
assert.notEqual(landing(DB_SEED_ROLES["db:Vendor Service Provider"]), "/login", "the seeded Vendor Service Provider must match a landing rung");
assert.equal(landing(DB_SEED_ROLES["db:Vendor Service Provider"]), "/maintenance", "vendor_portal:view + maintenance:view lands on the maintenance surface it holds");
// A server-side customer binding is authoritative even beside internal grants.
assert.equal(
  isPortalConfinedSession({ permissions: BACKEND_ROLES["Customer Service"], user: { customerId: 42 } }),
  true,
  "a session carrying a server-side customer binding must be confined to the portal",
);

// EVERY role must land on a route whose own guard it passes — no login straight into
// PermissionDenied. This is what sent Mechanic and the seeded Finance & Billing Manager
// to /shipments, a DIRECT shipments:view route neither of them holds.
// ROUND-2 FIX: the skip list used to read
//   if (route === "/login" || route === "/driver" || route === "/customer-portal") continue;
// which excused exactly the two identity-boundary landings — and /driver was the one that
// bypassed guard verification in sessionRouting. The sweep therefore proved the property
// only where the code already enforced it. /driver and /customer-portal are now asserted
// like every other landing; only the /login terminal (which renders NoWorkspaceScreen, not
// a guarded route) is exempt.
const ROUTE_GUARDS = parseRouteGuards();
const ALL_ROLES = { ...ROLE_PERMISSIONS, ...BACKEND_ROLES, ...DB_SEED_ROLES };
for (const [name, owned] of Object.entries(ALL_ROLES)) {
  const route = landing(owned);
  if (route === "/login") continue;
  const guard = resolveGuard(route, ROUTE_GUARDS);
  assert.ok(guard, `landing route ${route} (from ${name}) must exist in App.tsx`);
  assert.equal(
    guardPasses(owned, guard.guard), true,
    `${name} lands on ${route} but its guard (${guard.guard.permissions.join("|")}${guard.guard.direct ? " direct" : ""}) DENIES it — that is a login straight into PermissionDenied`,
  );
}
// The ladder's own declared guards must match what App.tsx really carries.
for (const rung of LANDING_LADDER) {
  const actual = resolveGuard(rung.route, ROUTE_GUARDS);
  assert.ok(actual, `landing ladder route ${rung.route} must exist in App.tsx`);
  assert.deepEqual(
    [...rung.guard.permissions].sort(), [...actual.guard.permissions].sort(),
    `landing ladder guard for ${rung.route} is out of lockstep with App.tsx`,
  );
  assert.equal(
    Boolean(rung.guard.direct), Boolean(actual.guard.direct),
    `landing ladder direct-match flag for ${rung.route} is out of lockstep with App.tsx`,
  );
  // At least one selector must be a grant that actually opens the route. Some
  // selectors are pure IDENTITY markers (vendor_portal:view names the persona but
  // grants nothing), which is safe only because getLandingRouteForSession verifies
  // the guard before returning the rung — assert that structure holds too.
  assert.ok(
    rung.selectors.some((selector) => landingGuardPasses([selector], rung.guard)),
    `no selector of the ${rung.route} rung can actually pass its guard — the rung is unreachable`,
  );
  for (const selector of rung.selectors) {
    if (landingGuardPasses([selector], rung.guard)) continue;
    assert.equal(
      landing([selector]), "/login",
      `${selector} selects ${rung.route} but cannot pass its guard, so a session holding ONLY it must fall through to the no-workspace terminal, never into PermissionDenied`,
    );
  }
}

// ── 6. Navigation visibility must imply route-guard pass, for EVERY module ──
// moduleConfig:143 gated "Audit Logs" on reports.manage while /audit-logs is guarded by
// audit:view; once the reports.manage → audit:view alias was severed, Dispatcher, Fleet
// Manager and Safety Manager saw the item and hit a dead end. Sweep all of them.
//
// ROUND-2 FIX: this sweep used to evaluate a SYNTHETIC session holding exactly the nav
// token (`const owned = [module.requiredPermission]`). That proves a weaker property than
// it appears to: real sessions reach a nav token through the ALIAS CLOSURE, and the closure
// is not transitive through the route's token — a role can satisfy the nav token and still
// fail the route token. The synthetic sweep reported 0/91 while 11 real role × nav dead
// ends existed (all on `evidence-packages`: nav asked safety.view, /evidence-graphs guards
// safety:evidence:view). It now runs every module against every REAL grant set.
const navMismatches = [];
for (const module of modules) {
  if (!module.requiredPermission) continue;
  const resolved = resolveGuard(module.route, ROUTE_GUARDS);
  assert.ok(resolved, `module "${module.key}" points at ${module.route}, which has no route in App.tsx`);

  // The synthetic case still matters — a nav token that cannot open its own route is
  // broken for everyone — so keep it, then add every real role on top.
  for (const [roleName, roleGrants] of [["<token itself>", [module.requiredPermission]], ...Object.entries(ALL_ROLES)]) {
    const navVisible = module.permissionMatch === "direct"
      ? new Set(roleGrants.map(normalize)).has(normalize(module.requiredPermission))
      : hasPermission(roleGrants, module.requiredPermission);
    if (!navVisible) continue;
    if (guardPasses(roleGrants, resolved.guard)) continue;
    navMismatches.push(
      `  ${module.key} [${roleName}]: nav requires ${module.requiredPermission}${module.permissionMatch === "direct" ? " (direct)" : ""}`
      + ` but ${resolved.route} guards on ${resolved.guard.permissions.join("|")}${resolved.guard.direct ? " (direct)" : ""}`,
    );
  }
}
assert.equal(
  navMismatches.length, 0,
  `every visible nav item must pass its route guard, for every REAL role — these are PermissionDenied dead ends:\n${navMismatches.join("\n")}`,
);

// ── 7. Route-shape tripwires in App.tsx (harness-style source assertions) ──
const app = readFileSync(resolve(root, "src/App.tsx"), "utf8");
const adminGuards = app.match(/path="\/(?:admin|user-management)" element=\{<RequirePermission[^>]*?>/g) ?? [];
assert.equal(adminGuards.length, 2, "/admin and /user-management guards must remain identifiable");
for (const guard of adminGuards) {
  assert.match(guard, /\bdirect\b/, "governance routes must use DIRECT permission matching");
  assert.doesNotMatch(guard, /audit:view/, "audit:view must not unlock /admin or /user-management");
}
assert.match(app, /isPortalConfinedSession\(session\)/, "ProtectedShell must bounce CONFINED portal sessions, not merely sessions whose landing route is the portal");
assert.match(app, /<CustomerLayout \/>/, "the customer portal must render inside CustomerLayout, not the internal AppShell");
assert.doesNotMatch(
  app.slice(app.indexOf("<Route element={<ProtectedShell />}")),
  /path="\/customer-portal"/,
  "/customer-portal must not be routed inside ProtectedShell",
);
assert.match(app, /<NoWorkspaceScreen \/>/, "an authenticated zero-landing session must get the no-workspace terminal, not the login form");
assert.match(app, /function NoWorkspaceScreen/, "the no-workspace terminal must exist");
assert.match(
  app.slice(app.indexOf("function NoWorkspaceScreen")).slice(0, 2000),
  /logout\(\)/,
  "the no-workspace terminal must offer a working sign-out",
);

console.log("RBAC contract OK: alias closure, portal identity boundary, governance guards, guard-verified landing ladder and nav/route lockstep all hold.");

// ── helpers ──────────────────────────────────────────────────────────────────

/** Extract every `path=... element={<RequirePermission ...>}` guard (and Navigate redirect) from App.tsx. */
function parseRouteGuards() {
  const source = readFileSync(resolve(root, "src/App.tsx"), "utf8");
  const guards = new Map();
  const redirects = new Map();
  // Layout routes guard their children: <Route element={<RequirePermission …><Layout/></…>}>
  // wraps <Route path="/customer-portal" …/>, which carries no guard of its own.
  const layoutGuards = [...source.matchAll(/<Route element=\{<RequirePermission\s+([^>]*)>/g)]
    .map((match) => ({ index: match.index, attributes: match[1] }));
  for (const match of source.matchAll(/<Route\s+path="([^"]+)"\s+element=\{([\s\S]*?)\}\s*\/>/g)) {
    const [, path, element] = match;
    const navigate = element.match(/^<Navigate to="([^"]+)"/);
    if (navigate) { redirects.set(path, navigate[1]); continue; }
    const require_ = element.match(/<RequirePermission\s+([^>]*)>/);
    let attributes = require_?.[1];
    if (!attributes) {
      // Inherit the nearest enclosing layout guard, if any.
      const enclosing = layoutGuards.filter((layout) => layout.index < match.index).at(-1);
      attributes = enclosing?.attributes;
    }
    if (!attributes) { guards.set(path, { permissions: [], open: true }); continue; }
    const single = attributes.match(/\bpermission="([^"]+)"/);
    const many = attributes.match(/\bpermissions=\{\[([^\]]*)\]\}/);
    guards.set(path, {
      permissions: single ? [single[1]] : many ? [...many[1].matchAll(/"([^"]+)"/g)].map((x) => x[1]) : [],
      direct: /\bdirect\b/.test(attributes),
    });
  }
  assert.ok(guards.size > 50, `App.tsx route guards must be parsable (found ${guards.size}) — fix this parser rather than letting the sweep silently pass`);
  return { guards, redirects };
}

/** Follow <Navigate> redirects and /* splats to the guard that actually protects a route. */
function resolveGuard(route, table, seen = new Set()) {
  if (seen.has(route)) return null;
  seen.add(route);
  if (table.redirects.has(route)) return resolveGuard(table.redirects.get(route), table, seen);
  if (table.guards.has(route)) return { route, guard: table.guards.get(route) };
  for (const [path, guard] of table.guards) {
    if (path.endsWith("/*") && route.startsWith(path.slice(0, -2))) return { route: path, guard };
  }
  return null;
}

/** Evaluate a parsed App.tsx guard the way <RequirePermission> does. */
function guardPasses(owned, guard) {
  if (!guard || guard.open) return true;
  if (guard.direct) {
    const set = new Set(owned.map(normalize));
    return set.has("*") || guard.permissions.some((permission) => set.has(normalize(permission)));
  }
  return guard.permissions.some((permission) => hasPermission(owned, permission));
}

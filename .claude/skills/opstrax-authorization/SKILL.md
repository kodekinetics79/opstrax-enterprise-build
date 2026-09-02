---
name: opstrax-authorization
description: >
  Use before changing ANY permission, role, route guard, navigation entry, or
  endpoint gate, and when diagnosing wrong-page/403/PermissionDenied behaviour.
  Triggers: "RBAC", "permission", "role", "route guard", "RequirePermission",
  "direct URL", "landing page", "lands on the wrong page", "customer portal",
  "internal shell", "403 after login", "PermissionDenied", "alias",
  "hasPermission", "sidebar shows but page denies", "who can see this",
  "least privilege", "cross-tenant". ALWAYS use before tightening a permission
  alias — a partial tightening is worse than none.
---

# OpsTrax Authorization

## Permissions live in FOUR copies that drift

| Copy | File | Role |
|---|---|---|
| Backend role defaults | `EndpointMappings.cs` `RolePermissionDefaults` | what a role actually gets |
| Backend satisfy-sets | `EndpointMappings.cs` (~:2779) | **often dead** — see below |
| **Enforcing engine** | `Foundation/FoundationServices.cs` `SemanticPermissionAliases` | what `RequirePermission` really consults |
| Frontend mirror | `frontend/src/auth/rbacConfig.ts` | drives nav + landing route |
| DB seed | `database/init/002_seed.sql` | a *different* token vocabulary again |

`RequirePermission` delegates required-side expansion to **FoundationServices**.
A satisfy-set added only to `EndpointMappings` is **dead in enforcement** —
granting the documented token does nothing. Change all copies or make them
consume one shared table.

## Alias closure: tightening one group is usually a no-op

`addPermissionGroup` in `rbacConfig.ts` **merges any groups that share a token**.
Removing `fleet:view` from group A does nothing if untouched group B still lists
`fleet:view` beside a canonical token that is also in A — it flows straight back.

Proven case (Aug 2026): three witness assertions passed while
`hasPermission(["fleet:view"], "telemetry.live_state.read")` was still **true**.
The contract test asserted only the three cases that were fixed.

**Always verify by executing the shipped closure, never by reading the diff:**

```bash
cd frontend && npx esbuild src/auth/rbacConfig.ts --bundle --format=esm --outfile=/tmp/r.mjs
node -e 'import("/tmp/r.mjs").then(m=>console.log(m.hasPermission(["fleet:view"],"telemetry.live_state.read")))'
```

Then add the negative assertion to `scripts/test-rbac-contract.mjs` so it stays dead.

## Enforce at four layers — all of them

1. **Post-login destination** (`sessionRouting.ts`) — identity boundaries first.
2. **Route guard** (`App.tsx`) — governance routes need `direct` exact-match.
3. **Navigation metadata** (`modules/moduleConfig.ts`) — must stay in lockstep
   with the route guard. A nav item whose `requiredPermission` no longer implies
   its route guard is a PermissionDenied dead end.
4. **API permission + tenant boundary** — plus `company_id` scoping and RLS.

Disabled buttons are not authorization. A rendered internal shell that only
403s later is still a leak.

## Identity boundaries: key on binding, not permission shape

A rule like `customer_portal:view && !dashboard:view` looks like "is a portal
user" but captures **internal staff**: `Customer Service` holds
`customer_portal:view` with no `dashboard:view` and gets imprisoned in the
portal shell. `Vendor Service Provider` holds `vendor_portal:view` and matches
no rung at all, landing on `/login` with a valid session.

Key portal identity on the **actual customer binding**, and never on a role-NAME
substring — `Customer` and `Customer Viewer` contain no "Portal", so a
`/portal/i` test creates unbindable dead accounts. Routing and binding must use
the SAME criterion; today one uses permissions and the other uses the name.

## Fail closed, and check what the user then sees

`?? "Company Admin"` on a null role granted a **wildcard admin session**. Fail
closed to zero permissions — but a fail-closed terminal that renders an ordinary
login form to an authenticated user is a dead end, not a fix. Give it an
explanatory screen and a sign-out.

## Verification gates

`npm run lint` is **worthless on `frontend/src`** — eslint's flat config ignores
it. Use `npx tsc --noEmit`, `npm run build`, and the contract scripts.

Test against the **shipped backend grant sets**, not the idealised frontend
catalogue — that gap is why real-role regressions passed the contract script.

Re-measure the per-role sidebar blast radius after any alias change: no internal
role may lose a module it holds by DIRECT grant.

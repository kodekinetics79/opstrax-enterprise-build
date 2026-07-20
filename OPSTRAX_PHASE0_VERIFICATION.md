# OpsTrax — Phase 0 Verification & RLS Activation Plan

Verification-only pass. No code changed except this document. Every claim below
is backed by a file:line quote.

---

## PART A — Verify the self-correction

### A.3 first (it reframes everything): what does the global middleware actually provide?

**It provides AUTHENTICATION + tenant-context resolution + module-entitlement
gating. It does NOT provide per-route RBAC permission, and it does NOT scope any
query by tenant.** These are three different things, and the distinction is the
whole point.

**Pipeline ordering** (`backend-dotnet/Program.cs`): the auth middleware is
registered at line 187 and runs on every `/api/*` request *before* any endpoint
handler executes; endpoints are mapped later at line 491+:

```
185 app.UseCors("OpsTraxCors");
187 app.UseWhen(context => context.Request.Path.StartsWithSegments("/api", …),
        branch => branch.Use(async (context, next) => { … auth middleware … }));
…
491 app.MapOpsTraxEndpoints();
```

So the stages are: **(1) global middleware (authentication)** → **(2) endpoint
handler**, and the per-route `RequirePermission(...)` call lives *inside* (2).
They are NOT the same stage.

**What the middleware does** (`Program.cs:263-302`):
```
263 if (string.IsNullOrWhiteSpace(authHeader) ||
264     !authHeader.StartsWith("Bearer ", …)) {
266     context.Response.StatusCode = 401; … return;     // rejects anonymous
    }
279 var session = await db.QuerySingleAsync("SELECT s.user_id, s.company_id, … FROM user_sessions s JOIN users u … WHERE s.session_token=@token AND s.expires_at > NOW() …");
288 if (session is null) { 401; return; }                // rejects invalid/expired
297 context.Items[AuthUserIdItemKey]      = userId;
298 context.Items[AuthCompanyIdItemKey]   = companyId;   // tenant CONTEXT only
299 context.Items[AuthRoleItemKey]        = roleName;
300 context.Items[AuthPermissionsItemKey] = permissions.ToArray();
```
It then does **module-entitlement** gating (`Program.cs:332`: "SELECT COUNT(*)
FROM tenant_entitlements WHERE company_id=@cid AND module_key=@mk AND
enabled=false"). It never calls `RequirePermission` and never adds a `company_id`
predicate to the handler's own SQL.

**Therefore: a logged-in Tenant A user hitting a route whose handler omits a
`company_id` predicate CAN read or mutate Tenant B's data**, because the
middleware only proved "you are a valid logged-in user of *some* tenant", resolved
your company into `Items`, and confirmed the module isn't disabled for you. It did
**not** confine the query to your tenant. Resolved tenant *context* ≠ tenant
*isolation*. This is demonstrated concretely in A.1 (#3, #4, #5, #6, #8).

> Correction to the Step-2 report: its phrase "still requiring an authenticated,
> tenant-resolved session" was accurate but could be read as reassuring. It is
> **not** sufficient for isolation. Authentication ≠ authorization ≠ data
> tenant-scoping. The routes below prove real cross-tenant exposure survives the
> middleware.

### A.1 — 10 routes marked N/N/N in v2, re-judged by following the handler body

For each: the public allowlist (`Program.cs:194-212`) is `(/api/auth/login,
/api/health, /api/ready, /api/platform*, /health*, /api/telemetry/ingest, GET
/api/customer-eta/track/*, GET /api/customer-visibility/tracking/*, GET
/api/public/shipments/track/*)`. **None of the 10 below are in it**, so the
authentication middleware applies to all 10.

| # | Route (v2 = N/N/N) | Handler evidence | Authn (middleware)? | Per-route RBAC? | Tenant-scoped query? | True verdict |
|---|---|---|---|---|---|---|
| 1 | `GET /api/control-tower/events` | `EndpointMappings.cs:44` `RequirePermission(http,"dashboard:view")`; `:46` `WHERE company_id=@companyId` | Yes | **Yes** | **Yes** | **v2 FALSE NEGATIVE (both present)** |
| 2 | `POST /api/control-tower/actions/send-eta-update` | `:49` → `SimpleAction(...)` `:4689-4693` only writes an `audit.LogAsync(...)`, no business query | Yes | No | N/A (no tenant data touched) | Authn-only; no data exposure |
| 3 | `POST /api/vehicles/{id}/assign-driver` | `:62` → `ChangeEntityStatus(...)` `:4718` `UPDATE vehicles SET assigned_driver_id=@targetId WHERE id=@id` — **no company_id** | Yes | No | **No** | **GENUINE cross-tenant WRITE gap** |
| 4 | `POST /api/vehicles/{id}/change-status` | `:63` → `ChangeStatus(...)` `:4705` `UPDATE vehicles SET status=@status WHERE id=@id` — **no company_id** | Yes | No | **No** | **GENUINE cross-tenant WRITE gap** |
| 5 | `GET /api/routes/summary` | `:3500` `RequirePermission(http,"dispatch:view")`; query `:3515` `FROM routes WHERE deleted_at IS NULL` — **no company_id** | Yes | **Yes** | **No** | Authn + RBAC, **cross-tenant READ leak** |
| 6 | `GET /api/last-mile/deliveries` | `:3523` `RequirePermission(http,"dispatch:view")`; query `FROM routes r LEFT JOIN …` — **no company_id** | Yes | **Yes** | **No** | Authn + RBAC, **cross-tenant READ leak** |
| 7 | `GET /api/profitability` | `:449` `WHERE cm.company_id=@cid`; no `RequirePermission` | Yes | No | **Yes** | Authn + tenant-scoped, no RBAC |
| 8 | `GET /api/carbon-emissions` | `:462` handler signature `(Database db, …)` — **no `HttpContext`**; query `FROM vehicles … WHERE deleted_at IS NULL` — **no company_id** | Yes | No | **No** | **GENUINE cross-tenant READ leak** |
| 9 | `GET /api/feature-flags` | `:516` `WHERE mr.company_id=@cid`; no `RequirePermission` | Yes | No | **Yes** | Authn + tenant-scoped, no RBAC |
| 10 | `GET /api/integrations` | `:532` `WHERE company_id=@cid`; no `RequirePermission` | Yes | No | **Yes** | Authn + tenant-scoped, no RBAC |

**Honest reading of the 10:**
- The "global middleware covers them" claim is true **only for authentication** —
  none can be called anonymously. ✔
- It is **false** to imply the middleware makes them safe. Of the 10:
  - 1 was a v2 false-negative (fully protected): **#1**.
  - 2 are RBAC-checked but **not tenant-scoped** → cross-tenant read leak: **#5, #6**.
  - 3 are tenant-scoped but not RBAC-checked (any role can read own-tenant): **#7, #9, #10**.
  - **3 are genuine cross-tenant gaps the middleware does NOT close: #3 and #4
    (cross-tenant WRITE by `id`), #8 (cross-tenant READ).**
  - 1 touches no tenant data: **#2**.

**New findings beyond original Phase-0 scope.** #3, #4 (`/api/vehicles/{id}/
assign-driver`, `/change-status` — authenticated cross-tenant *writes*) and #8
(`/api/carbon-emissions` — authenticated cross-tenant *read*) were **not** in the
enumerated sensitive prefixes I fixed in Step 2, and remain open. #5/#6 likewise.
These are exactly the class of leak RLS (Part C) is meant to backstop. They should
be added to the remediation backlog; #3/#4 are higher severity (mutation).

### A.2 — Why 619 (v2) vs 615 (this session)

The 4 missing routes are the **dynamic, interpolated-path** registrations
(`EndpointMappings.cs:1606-1609`):
```
1606 app.MapGet ($"/api/{moduleKey}",            …);
1607 app.MapGet ($"/api/{moduleKey}/{{id:long}}",…);
1608 app.MapPost($"/api/{moduleKey}",            …);
1609 app.MapPut ($"/api/{moduleKey}/{{id:long}}",…);
```
v2 listed these as 4 table rows (its rows for `/api/{moduleKey}` …). My counting
regex requires a **string literal** immediately after `(` — `app\.Map\w+\(\s*"` —
which does not match `$"…"` (interpolated). Hence `619 − 4 = 615`. The difference
is purely the counting method, not removed routes; both counts refer to the same
code. (Note: these 4 statements are themselves registered per module key, so the
*physical* route count is higher than either table row count — but as registration
statements they are 4, matching v2's 4 rows.)

---

## PART B — api-dotnet failure

**Confirmed: `api-dotnet/` is dead code, not deployed, safe to ignore.**

Evidence:
- **render.yaml** — only services are `opstrax-api` (`rootDir: backend-dotnet`,
  line 5) and `opstrax-events` (`rootDir: node-services/events`). No `api-dotnet`.
- **docker-compose.yml** — the *service* named `api-dotnet` (line 19) builds
  `context: ./backend-dotnet` (line 21). The other `api-dotnet` strings are the
  `depends_on` reference (line 16) and `API_BASE_URL: http://api-dotnet:8080`
  (line 40) — both point at that compose **service name**, which is built from
  `./backend-dotnet`. Nothing builds the `api-dotnet/` **directory**.
- **CI** (`.github/workflows/ci.yml`) builds/tests only `backend-dotnet/
  Opstrax.Api.csproj` + `backend-dotnet.Tests` (+ frontend, node backend,
  node-services/events). It never restores/builds `api-dotnet/`.
- **No `.sln`** references `api-dotnet`.
- **Frontend** calls `VITE_API_BASE_URL=http://localhost:8088` (docker-compose
  arg) → host port `8088:8080` on the `api-dotnet` compose service → built from
  `./backend-dotnet`. No frontend code references the `api-dotnet/` directory.
- The build failure (`api-dotnet/Program.cs:1,3` — missing `Opstrax.Api.Controllers`
  / `Opstrax.Api.Services` namespaces) is **pre-existing** — identical on the
  unmodified committed revision `3668c08` (verified by stashing my changes and
  rebuilding). The `api-dotnet/` folder has no `Controllers/` or `Services/`
  directories at all.

**Conclusion: api-dotnet is dead code, not deployed, safe to ignore.** It is NOT a
P0. (The Step-1 secret removal there is still worthwhile hygiene since the file is
committed in git history, but it carries no runtime/deploy risk.)

---

## PART C — RLS activation plan (DESIGN ONLY — not implemented)

### The constraint
`backend-dotnet/Data/Database.cs` opens a **new pooled `NpgsqlConnection` per
query** (every `OpenAsync`), with no request-scoped connection or transaction.
A session GUC (`app.current_tenant_id`) set on one connection does not flow to the
next query's connection, and a non-`LOCAL` `SET` left on a pooled physical
connection **leaks to whichever request reuses it**. So tenant context must be
bound to the exact connection each query uses, and cleared deterministically.

### Option A — set `app.current_tenant_id` per request (recommended)
Two concrete shapes:

- **A1 (recommended): request-scoped connection + transaction + `SET LOCAL`.**
  Make the tenant connection a DI-`Scoped` service: open ONE connection per
  request, `BEGIN`, then `SELECT set_config('app.current_tenant_id', @cid, true)`
  (the `is_local := true` form is the parameterizable equivalent of `SET LOCAL`,
  avoiding string interpolation), run all handler queries on that connection,
  `COMMIT` at request end. `SET LOCAL`/`set_config(..., true)` is **transaction-
  scoped**: it is automatically discarded at commit/rollback and therefore **cannot
  leak across pooled connections** — this is what makes A1 safe.
- **A2 (interim, simpler, slower): `AsyncLocal` + set on every `OpenAsync`.**
  Keep per-query connections; the auth middleware populates an
  `AsyncLocal<long?> CurrentTenant`; `Database.OpenAsync` issues `SELECT
  set_config('app.current_tenant_id', @cid, false)` (or `RESET`) immediately after
  every open. Safe *because it overwrites on every checkout* (no stale value
  survives), but costs one extra round-trip per query and relies on every
  acquisition path remembering to set it — one missed path = leak.

`A3` (Npgsql `UsePhysicalConnectionInitializer`) is **not** viable alone: it runs
once per physical connection, so it cannot carry a per-request tenant value.

### Option B — SECURITY DEFINER function tied to the session token
As literally stated this does not map onto Postgres: the database has no knowledge
of the app's bearer token or app-level session, so a SECURITY DEFINER function
still needs the tenant value *delivered* per connection (GUC / temp table /
parameter) — which lands back on the same connection-scoping problem; SECURITY
DEFINER changes *who executes*, not *how the tenant arrives*. The only token-free
variant is **one Postgres role per tenant** with policies on `current_user` and
per-request `SET ROLE` — but `SET ROLE` has the *same* pooled-connection leakage
hazard as a GUC (needs `RESET`/`LOCAL`), adds per-tenant role provisioning, and
scales poorly as tenants grow. So Option B is **more complex and no safer**.

### Recommendation
**Option A1** (request-scoped connection + transaction + `set_config(..., true)`).
`SET LOCAL` semantics directly neutralize the pooling-leak hazard that blocks
Step 3b. A2 is an acceptable lower-effort interim with a per-query round-trip cost.

### Changes required (no code yet)
1. **DB role / migration prereqs (from Step 3):** a dedicated **non-superuser,
   non-`BYPASSRLS`** app role that does not own the tables; `ALTER TABLE … FORCE
   ROW LEVEL SECURITY` on the RLS tables (today RLS is dormant because `zayra` is
   superuser+`BYPASSRLS` and owners bypass without `FORCE`).
2. **`Database.cs` / acquisition path:** introduce a `Scoped` request connection
   accessor that opens once per request, begins a transaction, and sets
   `app.current_tenant_id` via `set_config(...,true)`; route `QueryAsync/
   ExecuteAsync/...` through it within a request; keep the current per-query open
   only for non-request contexts (workers, schema-init/seeders), which run with
   the explicit `app.platform_admin='on'` bypass GUC or as a privileged role.
3. **Middleware (`Program.cs` ~297):** after resolving `companyId`, set it on the
   accessor (or `AsyncLocal`) so it is in place before any handler query runs.
4. **Background/system paths** (OutboxDispatcher, schema services, seeders) must
   set `app.platform_admin='on'` (the separate bypass policy) or use a privileged
   role, else they return/insert nothing once `FORCE` is on.

### Tests that must change / be added (flagged)
- **New isolation test (must run as the restricted role):** seed rows for
  company A and company B in an RLS table (e.g. `dvir_reports`); set tenant
  context to A; assert only A's rows are visible and a write to B's row affects
  0 rows. Requires a **second connection string for the non-superuser role** —
  the existing `zayra` superuser bypasses RLS and cannot validate enforcement.
- **New pool-leakage test:** interleave queries under tenant A then tenant B
  across the pool (forcing physical-connection reuse) and assert no A-rows ever
  appear under B context — validates that `set_config(...,true)`/transaction
  scoping prevents cross-request leakage. (This is the test the prompt asked for:
  two concurrent tenant contexts on the pool, asserting no leakage.)
- **Existing Postgres integration tests (impact):** ~10 fixtures construct
  `Database` directly with the `zayra` connection and do **not** set tenant
  context. Under `FORCE` + restricted role they would see no rows. Decision
  required: keep test setup/teardown on a bypass-capable role, OR have each test
  set tenant context / `app.platform_admin='on'` explicitly. This is the main
  test-suite blast radius to plan for — it must be resolved by *setting context
  correctly*, never by weakening a policy.

---

## Net verification verdict

- The self-correction is **directionally right but was stated too softly**: the
  global middleware is real and makes nothing anonymous, but it is **authentication
  only** — not RBAC, and crucially not tenant isolation. ✔ corrected here.
- The "458→476 auth / 405→433 tenant" resolved counts stand, but they count the
  *presence* of a predicate, not its *correctness/coverage* — verification surfaced
  additional un-scoped routes (#3, #4, #5, #6, #8) outside the original enumerated
  scope that remain genuine cross-tenant gaps until handler fixes and/or RLS
  activation.
- The 619→615 delta is fully explained (4 interpolated `$"/api/{moduleKey}"`
  registrations excluded by the literal-string regex).
- `api-dotnet` is confirmed dead code, not deployed — not a P0.
- RLS activation: **Option A1** recommended; Part C details the connection-path and
  test changes, left unimplemented per instructions.

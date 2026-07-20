# OpsTrax — Architecture Notes & Known Constraints

Grounded in the actual codebase as of 2026-07-02. For engineers and for the SOC2
system description. Complements `OPSTRAX_TENANT_ISOLATION_FINDINGS.md` and
`OPSTRAX_COMPLIANCE_READINESS.md`.

## 1. Runtime shape
- **Frontend:** Vite + React SPA (`frontend/`), served by nginx from prebuilt `dist/`
  (Docker) / Vercel. **The frontend Dockerfile COPYs `dist` — it does NOT build.** Run
  `npm run build` before `docker compose build frontend` or the container serves stale UI.
- **Primary backend:** .NET 8 minimal API `Opstrax.Api` (`backend-dotnet/`), ~557
  endpoints, most mapped in `Controllers/EndpointMappings.cs` (~15k lines).
- **Side service:** Node events/integrations (`services/node-events`, :8090).
- **DB:** Postgres (local :5433 `opstrax_local`; prod Neon).

## 2. Tenant isolation (see findings doc)
RLS is now **actively enforced**: app connects as restricted `opstrax_app`,
`Rls__EnforceTenantContext=true`, per-request tenant-scoped transaction. Postgres
fail-closes any read missing a `company_id` predicate. Platform admin uses a separate
`platform_admin_bypass` GUC for legitimate cross-tenant work.

## 3. CONSTRAINT: RLS concurrency ceiling
Under enforcement, **every HTTP request holds one DB connection + one open transaction
for its full duration**, and queries within a request are serialized through a
per-scope `SemaphoreSlim` (so `Task.WhenAll` fan-out is safe on the single connection).

Implications:
- **Correct** for isolation and for `SET LOCAL app.current_tenant_id` semantics.
- **Caps concurrency:** max in-flight requests ≈ the connection budget; a slow query
  holds its connection the whole time. Fine for a pilot (low concurrency); a known
  ceiling before broad scale.
- Off-pipeline work (background services, SSE, seeding) MUST wrap DB calls in
  `RunInSystemScopeAsync` / `RunInTenantScopeAsync` or it gets 0 rows (fail-closed).

Path past the ceiling (when needed, not now):
1. Connection budget sizing + pooler (PgBouncer / Neon pooler) tuning.
2. Shorten transaction lifetime for read-only requests (scope-per-query rather than
   scope-per-request) where SET LOCAL is re-applied per query.
3. Consider `SET` (session) + explicit reset on read replicas for heavy read paths.

## 4. CONSTRAINT: schema has no single source of truth
- **35 `*SchemaService.EnsureAsync()`** create ~256 tables idempotently at startup
  (`RunSchemaStep` in Program.cs). Plus **13 SQL files** in `database/migrations/` (a
  partial, parallel history). There is **no `schema_migrations` tracking table**.
- **Effect:** schema truth = "whatever the running code creates." Flexible, but:
  - No versioned baseline or rollback.
  - Drift between environments and between the two mechanisms (this session hit two
    column-drift bugs: `latest_vehicle_positions.recorded_at`→`event_time`,
    `fuel_transactions.distance_km` absent).
  - RLS enrollment is point-in-time, so tables added later miss it (closed by Stage 22
    reconciliation + the coverage regression test).

Recommended path (post-pilot, deliberate):
1. Snapshot the current live schema as a single **baseline migration** (source of truth).
2. Introduce a lightweight migration runner + `schema_migrations` ledger; new changes go
   through versioned migrations, not new `EnsureAsync` blocks.
3. Keep `EnsureAsync` only as an idempotent safety net, or retire it once baselined.
4. Add a CI check: live schema == baseline + applied migrations (drift detector).

## 5. Verified this session
- RLS enforcement validated across 223 GET routes (0 broke).
- Platform-admin cross-tenant bypass works under enforcement; tenant users fail-closed.
- Data-subject export/erasure, custom-role creation, carbon trend — all on real data.

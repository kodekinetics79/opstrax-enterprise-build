# OpsTrax — SIT (System Integration Testing) Runbook

Status: dev-phase freeze `3668c08` on `opstrax-product-main`.
Baseline at freeze: API build clean (0/0), frontend build clean, **862/862 tests passing**
(includes DB-backed integration tests).

This runbook describes how to stand up an isolated SIT environment, apply schema,
run the integration suite, and gate sign-off. UAT (business acceptance) is covered
separately.

---

## 1. Architecture facts that shape SIT

- **No EF Core migrations.** Schema is created by ~33 idempotent `*SchemaService`
  classes (`backend-dotnet/Services/*SchemaService.cs`) that run at app startup via
  `RunSchemaStep(... EnsureAsync())` in `backend-dotnet/Program.cs` (ordered, ~lines 125–168).
  `EnsureAsync` is `CREATE ... IF NOT EXISTS`-style, so it is safe to re-run.
- **Committed SQL migrations** mirror the riskier modules in `database/migrations/*.sql`
  for controlled application where startup DDL is gated off.
- **Local seed/init SQL** lives in `database/init/001_…008_*.sql`.
- **Integration tests target a LOCAL Postgres**, never Neon. Hardcoded in the test
  fixtures: `Host=127.0.0.1;Port=5433;Database=opstrax_local;Username=zayra;Password=zayra`.
  They self-bootstrap schema via the SchemaServices, so any disposable Postgres works.

### Production-gated schema modules (IMPORTANT)
These run automatically only when `!IsProduction()` (or when the flag is set):

| Module | Flag (set `=true` to force-on) | Backing SQL migration |
|---|---|---|
| RevenueReadiness | `RevenueReadinessSchema:Enabled` | `2026_06_28_stage7a_revenue_readiness_schema_contract.sql` |
| FinanceActivation | `FinanceActivationSchema:Enabled` | `2026_06_28_stage8_finance_activation.sql` |
| Stage9 | `Stage9Schema:Enabled` | ⚠️ **none committed** |

> **Action before SIT:** decide how Stage9 schema is applied in a Production-mode
> environment — either set `Stage9Schema:Enabled=true` for SIT, or add a committed
> `database/migrations/*_stage9_*.sql` file. Without one of these, Stage9 tables are
> absent in Production mode.

---

## 2. SIT database options

Pick one. The automated integration suite only needs a disposable Postgres.

**Option A — Ephemeral Postgres (recommended for the automated suite / CI):**
- A throwaway Postgres container (or CI `services: postgres`) on `127.0.0.1:5433`,
  db `opstrax_local`, user/password `zayra`/`zayra`.
- No Neon involved → zero risk to the 4 real tenants. Fully repeatable.

**Option B — Neon branch (for a prod-like SIT/UAT environment with realistic data):**
- Create a **Neon branch** off the main DB (instant isolated copy).
- Point the SIT service's `ConnectionStrings:DefaultConnection` + the Neon Auth URLs
  (`NEON_AUTH_BASE_URL`, `VITE_NEON_AUTH_URL`) at the **branch**, never the shared endpoint.
- Use this for end-to-end app testing, not for the write-heavy `*PostgresTests`.

> Never run the write-heavy integration tests against the shared Neon endpoint
> (`ep-ancient-hill-…`) — they create/modify tenant rows.

---

## 3. Stand up SIT and apply schema

1. Start the SIT Postgres (Option A or B).
2. Apply schema. Two equivalent paths:
   - **App-driven:** run `Opstrax.Api` against the SIT connection with a non-Production
     environment (or the three flags above set `=true`). Startup runs every
     `SchemaService.EnsureAsync()` and logs each `RunSchemaStep`.
   - **SQL-driven (prod-like):** apply `database/migrations/*.sql` in filename order,
     then resolve the Stage9 gap (section 1).
3. Seed: apply `database/init/005–008_*.sql` for module/telemetry/Saudi/simulation data.
   Keep demo fleet data behind `ENABLE_FLEET_DEMO_SEED` (off in prod).

---

## 4. Run the integration suite

```bash
# Build once
dotnet build backend-dotnet.Tests/Opstrax.Tests.csproj

# Full suite (needs Postgres on 127.0.0.1:5433)
dotnet test backend-dotnet.Tests/Opstrax.Tests.csproj --no-build

# Just the DB-backed integration tests
dotnet test backend-dotnet.Tests/Opstrax.Tests.csproj --no-build --filter "FullyQualifiedName~Postgres"

# Unit/source/regression only (no DB) — this is what CI runs today
dotnet test backend-dotnet.Tests/Opstrax.Tests.csproj --no-build --filter "FullyQualifiedName!~Postgres"
```

**CI note:** `.github/workflows/ci.yml` (`dotnet-build-test` job) currently runs the
non-Postgres filter. To gate the integration tests in CI, add a `services: postgres`
block (image `postgres:16`, env `POSTGRES_DB=opstrax_local POSTGRES_USER=zayra
POSTGRES_PASSWORD=zayra`, port `5433:5432`) and drop the `!~Postgres` filter in a
dedicated job.

---

## 5. Security checks (must pass for SIT sign-off)

- **Auth:** every internal `/api/*` route requires authentication.
- **RBAC:** fleet endpoints enforce the `fleet.*` taxonomy / fleet personas
  (commit `c7258c8`); customer-tracking management stays supervisor-only (`9ff89ac`).
- **Tenant isolation:** reads are scoped by `company_id`/tenant across jobs, vehicles,
  drivers, customers, dispatch, fleet — verify no cross-tenant leakage with ≥2 SIT tenants.
- **Public tracking:** `/track/[token]` exposes only customer-safe fields; raw POD
  storage URLs are not exposed (`dc4d024`). Confirm no cost, tenant IDs, internal notes,
  driver PII, or unrelated shipments are returned.

---

## 6. SIT exit criteria (go/no-go)

- [ ] API + frontend build clean on the SIT branch.
- [ ] `dotnet test` full suite green against SIT Postgres (target: 862/862).
- [ ] Stage9 schema decision resolved and applied.
- [ ] Schema applied cleanly from a fresh DB (no manual fix-ups).
- [ ] Security checks (section 5) verified with ≥2 tenants.
- [ ] Smoke-test critical flows end-to-end on the deployed SIT app
      (dispatch → shipment lifecycle → POD → public tracking; revenue/platform admin).

---

## 7. Known gaps / open items at freeze

- Stage9 has no committed SQL migration (section 1).
- CI does not yet run the DB-backed integration tests (section 4) — wiring described above.
- Integration tests carry a hardcoded local connection string; consider reading it from
  an env var so SIT/CI can point them at the provisioned Postgres without code changes.

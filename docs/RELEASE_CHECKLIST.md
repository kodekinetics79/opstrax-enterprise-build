# Release checklist — financial platform + telematics completion

Everything below is on branch `fix/driver-portal-p0`. Nothing here is live in production yet: the app
runs as a restricted DB role in prod and **skips schema creation by design**, so these tables do not
exist in prod until the owner migrations are applied out-of-band. Do this deliberately.

## 1. Apply owner migrations (as the Neon DB OWNER), in this order

Each is idempotent (`IF NOT EXISTS`) and additive. Order matters where later stages reference earlier
tables (tax/billing/rev-rec/config reference the invoice chain).

```
database/migrations/2026_07_14_feature_flags.sql
database/migrations/2026_07_14_stage33_gps_gateway_replay.sql
database/migrations/2026_07_15_stage35_job_charges_rating_seam.sql
database/migrations/2026_07_15_stage36_outbox_job_delivered_idempotency.sql
database/migrations/2026_07_15_stage37_settlement_ap.sql
database/migrations/2026_07_16_stage38_tax_engine.sql
database/migrations/2026_07_16_stage39_billing_consolidation.sql
database/migrations/2026_07_16_stage40_revenue_recognition.sql
database/migrations/2026_07_16_stage41_fin_config_envelope.sql
database/migrations/2026_07_16_stage42_telemetry_gateways.sql
database/migrations/2026_07_21_stage43_tenant_mfa.sql
database/migrations/2026_07_21_stage44_maintenance_pm_baseline.sql
database/migrations/2026_07_22_stage45_general_ledger.sql
database/migrations/2026_07_22_stage46_gl_period_close_export.sql
database/migrations/2026_07_22_stage47_detention_recovery.sql
database/migrations/2026_07_22_stage48_driver_detention_pay.sql
```

NOTE: stage47 is the core Detention Recovery owner DDL (detention tables,
evidence-immutability trigger, RLS and grants), mirroring the core of
DetentionSchemaService. Its `job_charges` columns/index and detention outbox indexes
are installed only when the separately owned Business Spine/Foundation predecessor
tables already exist. A Stage47 ledger therefore proves the immutable detention
evidence/offboarding schema, but does **not** by itself prove the complete
detention-to-charge/outbox/invoice path. Before enabling or selling Detention Recovery,
verify `job_charges.detention_dwell_id`, `job_charges.evidence_sha256`,
`uq_job_charges_detention`, `ux_outbox_detention_priced` and
`ux_outbox_detention_warning`, then run the approval/billing/outbox PostgreSQL suites.
The detector health gate and Stage47 core schema are necessary, not sufficient, for
commercial activation. The GL boot step is gated by GeneralLedgerSchema:Enabled
(non-prod default on; prod uses stage45/46).

NOTE: stage48 is the detention→driver-pay policy table (`driver_detention_pay_policy`, one row
per tenant, fail-closed: no enabled policy ⇒ drivers paid no detention). Detention pay lines are
DERIVED during settlement generation keyed on the trigger date (billed/collected), so
delete-and-recompute stays honest and never double-pays. This closes the differentiator: OpsTrax
collects detention AND pays the driver their share on the same evidence chain.

Stage48 remains a separate financial-product migration and is not implied by the
Safety pilot's canonical Stage47/58/59/65–74 owner lane. If driver detention pay is
in release scope, index its migration ledger and restricted-role/RLS evidence
separately; otherwise exclude that capability from the client promise.

Each migration also RLS-enrolls its new tables and grants the restricted `opstrax_app` role, so no
separate RLS step is needed. Verify after: `SELECT tablename FROM pg_policies WHERE policyname='tenant_isolation'`
includes the new tables.

## 2. Deploy the app (backend + frontend)

Merge `fix/driver-portal-p0` and deploy. Confirm the required config in prod:
- `PLATFORM_SUPERADMIN_EMAIL` / `PLATFORM_SUPERADMIN_PASSWORD` — the bootstrap admin no longer falls
  back to a default in Production; without these, no platform admin is seeded (intended).
- The PII envelope key (`Pii`) — required, else `POST /api/telemetry/gateways` refuses to mint gateway
  secrets (fail-closed, they'd otherwise be stored in plaintext).

## 3. Backward-compat is the safety net

Every financial module is additive and fail-closed: with **no** tax profile / billing profile / pay
agreement / rev-rec profile configured, the platform reproduces today's numbers exactly (zero tax, per-
job billing, no recognition, no settlement). So the deploy changes no money until a tenant is configured.

## 4. Per-tenant enablement (controlled rollout, one tenant at a time)

For each of the 4 tenants, in the admin UI:
1. **Tax** (Financials → Tax Configuration): create + publish a tax profile (maker-checker: a different
   user must publish) + seller registration, if the tenant charges tax.
2. **Rev-rec**: run "Recognize historical invoices" ONCE, before closing any period.
3. **Billing / Driver Pay / Rev-rec**: configure billing profiles + pay agreements as needed.
4. **Telematics**: provision per-gateway credentials (`POST /api/telemetry/gateways`); after all
   forwarders are migrated, decommission the legacy `Telemetry:GatewaySecret` (stage42 note).

## 5. Smoke tests (prod, one tenant)

- Issue an invoice with no tax profile → `tax_total=0` (unchanged behavior).
- Publish a tax profile → new invoices carry tax; the tax breakdown shows on the invoice.
- Consolidate a customer's period → invoice draft(s) created; no double-billing of already-invoiced charges.
- Generate + approve + pay a driver statement.
- PT40-Q telematics: see docs/telematics/pt40/pt40-e2e-simulation-results.md.

## 6. Known deferred items (not release-blocking)
- MFA-at-login is DONE (tenant TOTP enroll + two-step login, stage43). Still open from the
  Platform Admin review: entitlement-cap enforcement and real time-limited impersonation
  (post-launch governance hardening; the platform operator is currently the only cross-tenant actor).
- Telematics C3 (GT06 alarm/diagnostics bridge) + H1 (canonical-model unification); GT06 raw-TCP
  gateway deploy needs a Render TCP service (dashboard action).
- Config-envelope is inert scaffolding (no module reads it yet); AI-dashcam and certified ELD go
  via partner programs per the director-panel verdict (do not build ground-up).
- Detention fast-follows after first revenue: Motive connector (needs partner API), QuickBooks
  direct push (needs OAuth app), dual-ring fences, broker mode, suggested-sites bootstrap.
- Notice email delivery requires SMTP config in prod (unconfigured -> notices stay honestly 'logged').

## Verification
- Cross-module chain: `OrderToCashE2EPostgresTests` (rate→bill→tax→issue→recognize→settle) is green in CI.
- Full suite green in CI on every commit (dotnet-build-test + dotnet-integration-tests + frontend-build).
- `tools/verify-dotnet-warning-baseline.sh` performs a clean Release rebuild and fails on a new warning
  code or any increase above the checked-in per-code ceilings. A reduction must be followed by ratcheting
  `tools/dotnet-warning-baseline.tsv` downward; the baseline is debt visibility, not a clean-build claim.
- `tools/validate-ci-supply-chain-pins.sh` requires full commit SHAs for third-party Actions and manifest
  digests for workflow/Docker base images. Dependency updates must deliberately update and revalidate pins.
- The exact-SHA aggregator always uploads `opstrax-mandatory-gates-<sha>-<attempt>`, including failed,
  skipped, or cancelled upstream results. It publishes `opstrax-release-candidate-<sha>` only when all seven
  mandatory jobs succeed and the downloaded provenance resolves to the same commit SHA.

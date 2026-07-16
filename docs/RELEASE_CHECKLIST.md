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
```

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
- Platform Admin P0s not yet done: MFA-at-login enforcement, entitlement-cap enforcement, real
  time-limited impersonation (see the Platform Admin review).
- Telematics C3 (GT06 gateway alarm/diagnostics bridge) + H1 (canonical-model unification).
- Config-envelope is inert scaffolding (no module reads it yet); AI-dashcam + certified ELD are net-new
  programs (largest competitive gaps).

## Verification
- Cross-module chain: `OrderToCashE2EPostgresTests` (rate→bill→tax→issue→recognize→settle) is green in CI.
- Full suite green in CI on every commit (dotnet-build-test + dotnet-integration-tests + frontend-build).

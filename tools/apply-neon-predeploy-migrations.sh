#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Pre-deploy migration runner for the DEPLOYED (Neon) database.
#
# WHY THIS EXISTS
#   The code currently on origin/main (deployed to Render) predates several
#   schema changes that the new code REQUIRES at request time — most critically
#   users.customer_id and users.branch_id, which the auth middleware selects on
#   EVERY authenticated request. Deploying the new backend before these columns
#   exist would 500 every tenant API call. Run this against Neon FIRST, then
#   merge/deploy.
#
# WHAT IT APPLIES (all additive + idempotent; zero destructive statements):
#   stage21  customer portal      (users.customer_id, portal tables)
#   stage23  schema_migrations ledger
#   stage24  compliance tenant scope (company_id columns + backfill)
#   stage25  branches org hierarchy  (branches table, users.branch_id, …)
#   stage26  platform control plane  (platform tables as migration)
#
# WHAT IT DELIBERATELY SKIPS
#   stage19/20/22 (Row-Level Security FORCE + restricted role). Applying RLS to
#   the deployed DB is a separate, planned cutover: it requires the opstrax_app
#   role, PG_CONNECTION_APP on Render, and Rls__EnforceTenantContext=true to be
#   flipped together. Forcing RLS while the API still connects as the owner
#   role WITHOUT tenant GUCs would blank out every query.
#
# USAGE (run from repo root; credentials never leave your shell):
#   export NEON_PG_URI='postgresql://USER:PASSWORD@HOST/DB?sslmode=require'
#   ./tools/apply-neon-predeploy-migrations.sh
#
# The URI is read from the environment only — never passed as an argument
# (arguments show up in `ps` output and shell history).
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

if [ -z "${NEON_PG_URI:-}" ]; then
  echo "ERROR: set NEON_PG_URI in your environment first (see header)." >&2
  exit 1
fi
command -v psql >/dev/null || { echo "ERROR: psql not found. brew install libpq (or run via docker exec)." >&2; exit 1; }

MIGRATIONS=(
  2026_06_30_stage21_customer_portal
  2026_07_02_stage23_schema_migration_ledger
  2026_07_02_stage24_compliance_tenant_scope
  2026_07_02_stage25_branches_org_hierarchy
  2026_07_02_stage26_platform_control_plane
)

echo "Target host: $(printf '%s' "$NEON_PG_URI" | sed -E 's|.*@([^/:?]+).*|\1|')"
echo "Pre-check: read-only connectivity…"
psql "$NEON_PG_URI" -tA -c "SELECT current_database(), version()" | head -1

for m in "${MIGRATIONS[@]}"; do
  f="database/migrations/$m.sql"
  [ -f "$f" ] || { echo "ERROR: missing $f (run from repo root)" >&2; exit 1; }
  # Skip if already registered in the ledger (ledger may not exist before stage23 — treat as not applied).
  applied=$(psql "$NEON_PG_URI" -tA -c "SELECT COUNT(*) FROM schema_migrations WHERE version='$m'" 2>/dev/null || echo 0)
  if [ "$applied" = "1" ]; then
    echo "── $m: already applied (ledger) — skipping"
    continue
  fi
  echo "── applying $m"
  psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q -f "$f"
  # stage21/24/25 don't self-register; record them so reruns are no-ops.
  psql "$NEON_PG_URI" -q -c "INSERT INTO schema_migrations (version, description) VALUES ('$m', 'applied by apply-neon-predeploy-migrations.sh') ON CONFLICT (version) DO NOTHING" 2>/dev/null || true
done

echo
echo "Post-check: auth-critical columns…"
psql "$NEON_PG_URI" -tA -c "
  SELECT
    'users.customer_id: ' || COUNT(*) FILTER (WHERE column_name='customer_id') ||
    ' | users.branch_id: ' || COUNT(*) FILTER (WHERE column_name='branch_id')
  FROM information_schema.columns WHERE table_name='users' AND column_name IN ('customer_id','branch_id')"
psql "$NEON_PG_URI" -tA -c "SELECT 'branches table: ' || COUNT(*) FROM information_schema.tables WHERE table_name='branches'"
echo
echo "Ledger:"
psql "$NEON_PG_URI" -tA -c "SELECT version FROM schema_migrations ORDER BY version"
echo
echo "✅ Neon is ready for the new backend. Safe to merge to main / deploy."

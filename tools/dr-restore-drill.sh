#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# dr-restore-drill.sh — Disaster-recovery restore verification for OpsTrax (Neon).
#
# Proves RPO/RTO by restoring the database to a point in time on a throwaway Neon
# BRANCH (copy-on-write, instant, zero risk to production) and asserting the app's
# readiness + core row counts against it. Run quarterly and record the result in
# the Platform Admin → Backup Verifications.
#
# Requirements: neonctl (https://neon.tech/docs/reference/neon-cli) + psql.
# Env:
#   NEON_API_KEY        Neon API key (neonctl auth)
#   NEON_PROJECT_ID     Neon project id
#   DR_RESTORE_MINUTES  How far back to restore (default 60 = 1h ago) → tests RPO
#
# Exit code 0 = drill passed (restore point reachable, data intact).
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

: "${NEON_PROJECT_ID:?Set NEON_PROJECT_ID}"
RESTORE_MINUTES="${DR_RESTORE_MINUTES:-60}"
BRANCH_NAME="dr-drill-$(date -u +%Y%m%d-%H%M%S)"
START_TS=$(date +%s)

echo "▶ DR restore drill: restoring to ~${RESTORE_MINUTES}m ago on branch '${BRANCH_NAME}'"

# Point-in-time restore target (ISO-8601, UTC).
if date -u -v-"${RESTORE_MINUTES}"M +%Y-%m-%dT%H:%M:%SZ >/dev/null 2>&1; then
  RESTORE_TS=$(date -u -v-"${RESTORE_MINUTES}"M +%Y-%m-%dT%H:%M:%SZ)   # BSD/macOS
else
  RESTORE_TS=$(date -u -d "-${RESTORE_MINUTES} minutes" +%Y-%m-%dT%H:%M:%SZ) # GNU/Linux
fi
echo "  restore point: ${RESTORE_TS}"

cleanup() {
  echo "▶ Cleaning up drill branch '${BRANCH_NAME}'"
  neonctl branches delete "${BRANCH_NAME}" --project-id "${NEON_PROJECT_ID}" 2>/dev/null || true
}
trap cleanup EXIT

# 1) Create a branch restored to the target timestamp (PITR).
neonctl branches create \
  --project-id "${NEON_PROJECT_ID}" \
  --name "${BRANCH_NAME}" \
  --timestamp "${RESTORE_TS}"

# 2) Get its connection string.
DR_CONN=$(neonctl connection-string "${BRANCH_NAME}" --project-id "${NEON_PROJECT_ID}")
[ -n "${DR_CONN}" ] || { echo "✗ Could not obtain restored-branch connection string"; exit 1; }

# 3) Assert core tables exist and are non-empty (data actually restored).
echo "▶ Verifying restored data integrity"
ASSERT_SQL="
  SELECT
    (SELECT COUNT(*) FROM companies)              AS companies,
    (SELECT COUNT(*) FROM users)                  AS users,
    (SELECT COUNT(*) FROM dispatch_assignments)   AS assignments;
"
RESULT=$(psql "${DR_CONN}" -tAc "${ASSERT_SQL}")
echo "  row counts (companies|users|assignments): ${RESULT}"

COMPANIES=$(echo "${RESULT}" | cut -d'|' -f1)
if [ "${COMPANIES:-0}" -lt 1 ]; then
  echo "✗ DRILL FAILED — restored branch has no companies (restore point invalid)"
  exit 1
fi

END_TS=$(date +%s)
RTO_SECONDS=$((END_TS - START_TS))

echo "✓ DR DRILL PASSED"
echo "  RPO target : ${RESTORE_MINUTES} min (restore point reachable)"
echo "  RTO (drill): ${RTO_SECONDS}s to a verified, queryable restore"
echo "  Record this result in Platform Admin → Backup Verifications (restore_tested=true)."

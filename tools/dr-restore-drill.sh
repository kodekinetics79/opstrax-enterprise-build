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
#   DR_PILOT_COMPANY_CODE  Optional Safety pilot tenant code. When supplied,
#                          runs the Safety-specific restored database contract.
#   DR_DATABASE_EVIDENCE_OUTPUT Optional JSON output path for the database-only
#                          phase. The output is PARTIAL evidence, never DR-01 PASS.
#   DR_ENVIRONMENT         Non-secret environment name recorded in JSON output.
#
# Exit code 0 = drill passed (restore point reachable, data intact).
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

: "${NEON_PROJECT_ID:?Set NEON_PROJECT_ID}"
RESTORE_MINUTES="${DR_RESTORE_MINUTES:-60}"
[[ "$RESTORE_MINUTES" =~ ^[0-9]+$ ]] && [ "$RESTORE_MINUTES" -ge 1 ] && [ "$RESTORE_MINUTES" -le 10080 ] || {
  echo "✗ DR_RESTORE_MINUTES must be an integer from 1 through 10080" >&2
  exit 2
}
for command_name in neonctl psql jq git; do
  command -v "$command_name" >/dev/null || { echo "✗ Required command unavailable: $command_name" >&2; exit 2; }
done

BRANCH_NAME="dr-drill-$(date -u +%Y%m%d-%H%M%S)-${$}"
START_TS=$(date +%s)
START_UTC=$(date -u +%Y-%m-%dT%H:%M:%SZ)
CANDIDATE_SHA=$(git rev-parse HEAD)
BRANCH_CREATED=false

echo "▶ DR restore drill: restoring to ~${RESTORE_MINUTES}m ago on branch '${BRANCH_NAME}'"

# Point-in-time restore target (ISO-8601, UTC).
if date -u -v-"${RESTORE_MINUTES}"M +%Y-%m-%dT%H:%M:%SZ >/dev/null 2>&1; then
  RESTORE_TS=$(date -u -v-"${RESTORE_MINUTES}"M +%Y-%m-%dT%H:%M:%SZ)   # BSD/macOS
else
  RESTORE_TS=$(date -u -d "-${RESTORE_MINUTES} minutes" +%Y-%m-%dT%H:%M:%SZ) # GNU/Linux
fi
echo "  restore point: ${RESTORE_TS}"

cleanup() {
  status=$?
  trap - EXIT
  if [ "$BRANCH_CREATED" = true ]; then
    echo "▶ Cleaning up drill branch '${BRANCH_NAME}'"
    if ! neonctl branches delete "${BRANCH_NAME}" --project-id "${NEON_PROJECT_ID}" >/dev/null 2>&1; then
      echo "✗ DRILL FAILED — provider did not accept deletion of throwaway branch '${BRANCH_NAME}'" >&2
      status=1
    fi
  fi
  exit "$status"
}
trap cleanup EXIT

# 1) Create a branch restored to the target timestamp (PITR).
neonctl branches create \
  --project-id "${NEON_PROJECT_ID}" \
  --name "${BRANCH_NAME}" \
  --timestamp "${RESTORE_TS}"
BRANCH_CREATED=true

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

IFS='|' read -r COMPANIES USERS ASSIGNMENTS <<COUNTS
${RESULT}
COUNTS
if [ "${COMPANIES:-0}" -lt 1 ] || [ "${USERS:-0}" -lt 1 ] || [ "${ASSIGNMENTS:-0}" -lt 1 ]; then
  echo "✗ DRILL FAILED — restored branch is missing required core records"
  exit 1
fi

if [ -n "${DR_PILOT_COMPANY_CODE:-}" ]; then
  echo "▶ Verifying restored Safety pilot database contract"
  psql "${DR_CONN}" -v ON_ERROR_STOP=1 \
    -v pilot_company_code="${DR_PILOT_COMPANY_CODE}" \
    -f tools/verify-safety-pilot-restored-database.sql
  SAFETY_SCOPE="Safety pilot database contract verified"
else
  SAFETY_SCOPE="core row-count verification only; NOT sufficient for the Safety pilot release gate"
  SAFETY_CONTRACT_VERIFIED=false
fi

if [ -n "${DR_PILOT_COMPANY_CODE:-}" ]; then SAFETY_CONTRACT_VERIFIED=true; fi

END_TS=$(date +%s)
RTO_SECONDS=$((END_TS - START_TS))

# Provider acceptance of cleanup is part of the database-phase contract. Do it
# before recording a result rather than suppressing a stranded restore branch.
echo "▶ Deleting drill branch '${BRANCH_NAME}'"
neonctl branches delete "${BRANCH_NAME}" --project-id "${NEON_PROJECT_ID}" >/dev/null
BRANCH_CREATED=false
DELETION_ACCEPTED=true
END_UTC=$(date -u +%Y-%m-%dT%H:%M:%SZ)

if [ -n "${DR_DATABASE_EVIDENCE_OUTPUT:-}" ]; then
  case "$DR_DATABASE_EVIDENCE_OUTPUT" in ""|/|.|..) echo "✗ Unsafe DR_DATABASE_EVIDENCE_OUTPUT" >&2; exit 2;; esac
  if [ -L "$DR_DATABASE_EVIDENCE_OUTPUT" ]; then
    echo "✗ DR_DATABASE_EVIDENCE_OUTPUT must not be a symbolic link" >&2
    exit 2
  fi
  mkdir -p "$(dirname "$DR_DATABASE_EVIDENCE_OUTPUT")"
  output_tmp="${DR_DATABASE_EVIDENCE_OUTPUT}.tmp.${$}"
  jq -n \
    --arg candidate_sha "$CANDIDATE_SHA" \
    --arg environment "${DR_ENVIRONMENT:-unspecified}" \
    --arg started_utc "$START_UTC" --arg ended_utc "$END_UTC" \
    --arg restore_target_utc "$RESTORE_TS" \
    --arg safety_scope "$SAFETY_SCOPE" \
    --argjson requested_restore_age_minutes "$RESTORE_MINUTES" \
    --argjson measured_database_phase_rto_seconds "$RTO_SECONDS" \
    --argjson companies "$COMPANIES" --argjson users "$USERS" \
    --argjson assignments "$ASSIGNMENTS" \
    --argjson safety_contract_verified "$SAFETY_CONTRACT_VERIFIED" \
    --argjson branch_deletion_accepted "$DELETION_ACCEPTED" \
    '{schema_version:1,scope:"DATABASE_PITR_PHASE_ONLY",release_gate_status:"PARTIAL",
      candidate_sha:$candidate_sha,environment:$environment,started_utc:$started_utc,
      ended_utc:$ended_utc,restore_target_utc:$restore_target_utc,
      requested_restore_age_minutes:$requested_restore_age_minutes,
      measured_database_phase_rto_seconds:$measured_database_phase_rto_seconds,
      restored_row_counts:{companies:$companies,users:$users,dispatch_assignments:$assignments},
      safety_contract_verified:$safety_contract_verified,
      branch_deletion_accepted:$branch_deletion_accepted,safety_scope:$safety_scope,
      excluded_from_proof:["restricted application boot","tenant/branch application isolation",
        "object evidence retrieval/hash","external alert delivery","cutover time"]}' > "$output_tmp"
  chmod 600 "$output_tmp"
  mv "$output_tmp" "$DR_DATABASE_EVIDENCE_OUTPUT"
fi

echo "✓ DATABASE PITR PHASE PASSED"
echo "  RPO target : ${RESTORE_MINUTES} min (restore point reachable)"
echo "  RTO (drill): ${RTO_SECONDS}s to a verified, queryable restore"
echo "  scope       : ${SAFETY_SCOPE}"
echo "  cleanup     : provider accepted throwaway branch deletion"
echo "  remaining   : boot the exact candidate with restricted app/system roles, verify"
echo "                object evidence, tenant/branch isolation, alerting, and cutover time"
echo "  Record this result in Platform Admin → Backup Verifications (restore_tested=true)."

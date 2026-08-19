#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Apply the DDL that keeps losing the lock race, skipping anything already done.
#
# WHY RETRYING IS THE RIGHT SHAPE
#   The API's background workers hold transactions open for 80+ seconds (observed: a
#   maintenance_items COUNT and a driver_safety_scores INSERT, xact_age 00:01:24). Every
#   lock such a transaction holds is kept until it commits, so an ALTER TABLE asking for
#   ACCESS EXCLUSIVE cannot win inside a short timeout.
#
#   Raising the timeout is the WRONG fix: a queued exclusive request blocks every reader
#   arriving behind it, which on location_events (the hottest table here) is a
#   self-inflicted outage. So we keep the timeout short and retry instead — each attempt
#   either wins instantly or gives up without ever blocking traffic.
#
# WHY EACH STATEMENT IS PRE-CHECKED
#   ADD COLUMN IF NOT EXISTS still acquires ACCESS EXCLUSIVE even when the column is
#   already there — the IF NOT EXISTS only suppresses the error, it does not avoid the
#   lock. Without a pre-check this script spends its whole retry budget fighting for
#   locks to perform no-ops, and appears to hang on work that finished on a previous run.
#   Each entry therefore carries a SQL predicate; if it already returns true, the
#   statement is skipped entirely and no lock is taken.
#
# USAGE
#   export NEON_PG_URI='postgresql://...'
#   ./tools/pt40/08-retry-locked-ddl.sh            # default 60 attempts, 3s apart
#   ./tools/pt40/08-retry-locked-ddl.sh 120 2      # 120 attempts, 2s apart
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

if [ -z "${NEON_PG_URI:-}" ]; then
  echo "ERROR: export NEON_PG_URI first." >&2
  exit 1
fi
command -v psql >/dev/null || {
  echo "ERROR: psql not found. Try: export PATH=\"/opt/homebrew/opt/libpq/bin:\$PATH\"" >&2
  exit 1
}

MAX_ATTEMPTS="${1:-60}"
SLEEP_SECONDS="${2:-3}"

col_exists() { echo "SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='$1' AND column_name='$2')"; }
col_nullable() { echo "SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='$1' AND column_name='$2' AND is_nullable='YES')"; }

# Each entry: <predicate that is true when the work is already done> ||| <statement>
ENTRIES=(
  "$(col_exists location_events installation_id)|||ALTER TABLE location_events ADD COLUMN IF NOT EXISTS installation_id BIGINT NULL"
  "$(col_exists location_events assignment_id)|||ALTER TABLE location_events ADD COLUMN IF NOT EXISTS assignment_id BIGINT NULL"
  "$(col_exists location_events battery_voltage)|||ALTER TABLE location_events ADD COLUMN IF NOT EXISTS battery_voltage NUMERIC(10,3) NULL"
  "$(col_nullable customers sla_health_score)|||ALTER TABLE customers ALTER COLUMN sla_health_score DROP NOT NULL"
  "$(col_nullable customers delivery_experience_score)|||ALTER TABLE customers ALTER COLUMN delivery_experience_score DROP NOT NULL"
  "$(col_nullable customers risk_score)|||ALTER TABLE customers ALTER COLUMN risk_score DROP NOT NULL"
  # /api/trips: 42703 column x.branch_id does not exist -- 'x' is the dispatch_assignments
  # LATERAL join. No migration owns this column; DispatchSchemaService.cs:41 declares it
  # ("dispatch_assignments","branch_id","BIGINT NULL") at runtime, and production runs as
  # the restricted role which skips runtime schema init -- so it was never created here.
  "$(col_exists dispatch_assignments branch_id)|||ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL"
  # Backfills mirror DispatchSchemaService.cs:157-160. Guarded on branch_id IS NULL, and
  # skipped once no NULL rows with a resolvable branch remain.
  "SELECT NOT EXISTS(SELECT 1 FROM dispatch_assignments da JOIN jobs j ON j.id=da.job_id AND j.company_id=da.company_id WHERE da.branch_id IS NULL AND j.branch_id IS NOT NULL)|||UPDATE dispatch_assignments da SET branch_id=j.branch_id FROM jobs j WHERE da.branch_id IS NULL AND da.job_id=j.id AND da.company_id=j.company_id AND j.branch_id IS NOT NULL"
  "SELECT NOT EXISTS(SELECT 1 FROM dispatch_assignments da JOIN vehicles v ON v.id=da.vehicle_id AND v.company_id=da.company_id WHERE da.branch_id IS NULL AND v.branch_id IS NOT NULL)|||UPDATE dispatch_assignments da SET branch_id=v.branch_id FROM vehicles v WHERE da.branch_id IS NULL AND da.vehicle_id=v.id AND da.company_id=v.company_id AND v.branch_id IS NOT NULL"
)

failed=0
for entry in "${ENTRIES[@]}"; do
  check="${entry%%|||*}"
  stmt="${entry##*|||}"
  printf '%-64s ' "$(echo "$stmt" | cut -c1-62)"

  # Cheap read, no DDL lock. If the predicate errors (e.g. the table itself is absent)
  # treat it as "not done" and let the statement report the real problem.
  if [ "$(psql "$NEON_PG_URI" -t -A -c "$check" 2>/dev/null)" = "t" ]; then
    echo "already applied — skipped"
    continue
  fi

  attempt=1
  while [ "$attempt" -le "$MAX_ATTEMPTS" ]; do
    if out=$(psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q -t -A \
               -c "SET lock_timeout='2s'; $stmt" 2>&1); then
      echo "ok (attempt $attempt)"
      break
    fi
    if ! grep -q "lock timeout" <<<"$out"; then
      echo "FAILED — not a lock problem:"
      echo "    $out"
      failed=$((failed + 1))
      break
    fi
    attempt=$((attempt + 1))
    if [ "$attempt" -gt "$MAX_ATTEMPTS" ]; then
      echo "gave up after $MAX_ATTEMPTS attempts (still locked)"
      failed=$((failed + 1))
      break
    fi
    sleep "$SLEEP_SECONDS"
  done
done

echo
echo "== verification =="
psql "$NEON_PG_URI" -P pager=off -c "
SELECT
  (SELECT count(*) FROM information_schema.columns
    WHERE table_schema='public' AND table_name='location_events'
      AND column_name IN ('installation_id','assignment_id','battery_voltage')) AS location_events_cols_of_3,
  (SELECT count(*) FROM information_schema.columns
    WHERE table_schema='public' AND table_name='customers'
      AND column_name IN ('sla_health_score','delivery_experience_score','risk_score')
      AND is_nullable='YES') AS customers_nullable_of_3,
  (SELECT count(*) FROM information_schema.columns
    WHERE table_schema='public' AND table_name='dispatch_assignments'
      AND column_name='branch_id') AS dispatch_branch_id_of_1;"

if [ "$failed" -gt 0 ]; then
  echo
  echo "$failed statement(s) did not complete. Re-run this script; it is idempotent and"
  echo "will skip everything that already applied."
  echo "If it keeps losing, the API's background workers are holding long transactions —"
  echo "restarting the Render service drops those connections and frees the locks:"
  echo "    render restart srv-d93dha0k1i2s73dm6ub0 --confirm"
  exit 1
fi

echo
echo "All DDL applied."

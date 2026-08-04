#!/usr/bin/env bash
set -euo pipefail

repo_root=$(git rev-parse --show-toplevel 2>/dev/null || true)
[ -n "$repo_root" ] || { echo "ERROR: run from a Git worktree" >&2; exit 2; }
cd "$repo_root"

test_root=$(mktemp -d /tmp/opstrax-dr-contract-test.XXXXXX)
cleanup() { case "$test_root" in /tmp/opstrax-dr-contract-test.*) rm -rf "$test_root";; esac; }
trap cleanup EXIT
mkdir -p "$test_root/bin"

cat > "$test_root/bin/neonctl" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
case "$*" in
  "branches create "*) exit 0;;
  "connection-string "*) printf 'postgresql://opaque-invalid-for-mock\n';;
  "branches delete "*) [ "${MOCK_DELETE_FAIL:-false}" != true ];;
  *) echo "unexpected neonctl arguments: $*" >&2; exit 2;;
esac
MOCK
cat > "$test_root/bin/psql" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
printf '2|3|4\n'
MOCK
chmod +x "$test_root/bin/neonctl" "$test_root/bin/psql"

evidence="$test_root/database-phase.json"
PATH="$test_root/bin:$PATH" NEON_PROJECT_ID=project-mock DR_RESTORE_MINUTES=60 \
  DR_ENVIRONMENT=safety-rehearsal-us-east DR_DATABASE_EVIDENCE_OUTPUT="$evidence" \
  tools/dr-restore-drill.sh > "$test_root/pass.log" 2>&1

jq -e '
  .schema_version==1 and .scope=="DATABASE_PITR_PHASE_ONLY" and
  .release_gate_status=="PARTIAL" and .environment=="safety-rehearsal-us-east" and
  .requested_restore_age_minutes==60 and .restored_row_counts.companies==2 and
  .restored_row_counts.users==3 and .restored_row_counts.dispatch_assignments==4 and
  .safety_contract_verified==false and .branch_deletion_accepted==true and
  (.excluded_from_proof | index("restricted application boot") != null)
' "$evidence" >/dev/null
grep -q 'NOT sufficient for the Safety pilot release gate' "$test_root/pass.log"

if PATH="$test_root/bin:$PATH" NEON_PROJECT_ID=project-mock MOCK_DELETE_FAIL=true \
  DR_DATABASE_EVIDENCE_OUTPUT="$test_root/should-not-exist.json" \
  tools/dr-restore-drill.sh > "$test_root/delete-fail.log" 2>&1; then
  echo "ERROR: DR drill suppressed provider cleanup failure" >&2
  exit 1
fi
[ ! -e "$test_root/should-not-exist.json" ]

if PATH="$test_root/bin:$PATH" NEON_PROJECT_ID=project-mock DR_RESTORE_MINUTES=0 \
  tools/dr-restore-drill.sh > "$test_root/rpo.log" 2>&1; then
  echo "ERROR: DR drill accepted an invalid restore age" >&2
  exit 1
fi
grep -q 'must be an integer' "$test_root/rpo.log"

echo "DR restore drill contract regression passed."

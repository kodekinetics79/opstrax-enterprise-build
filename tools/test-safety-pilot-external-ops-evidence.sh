#!/usr/bin/env bash
set -euo pipefail

repo_root=$(git rev-parse --show-toplevel 2>/dev/null || true)
[ -n "$repo_root" ] || { echo "ERROR: run from a Git worktree" >&2; exit 2; }
cd "$repo_root"

test_root=$(mktemp -d /tmp/opstrax-external-ops-test.XXXXXX)
cleanup() {
  case "$test_root" in /tmp/opstrax-external-ops-test.*) rm -rf "$test_root";; esac
}
trap cleanup EXIT

bundle="$test_root/bundle"
mkdir -p "$bundle/raw"
candidate=$(git rev-parse HEAD)

sha256_value() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | awk '{print $1}'; else shasum -a 256 "$1" | awk '{print $1}'; fi
}

make_report() {
  gate=$1
  filename=$2
  evidence=$3
  raw="raw/${gate}.txt"
  printf 'sanitized provider export for %s\n' "$gate" > "$bundle/$raw"
  raw_hash=$(sha256_value "$bundle/$raw")
  jq -n \
    --arg gate "$gate" --arg candidate "$candidate" --arg raw "$raw" --arg raw_hash "$raw_hash" \
    --argjson evidence "$evidence" \
    '{schema_version:1,gate_id:$gate,candidate_sha:$candidate,outcome:"PASS",
      environment:"safety-rehearsal-us-east",run_id:("run-"+$gate),
      operator:"SRE Operator",approver:"Release Approver",
      started_utc:"2026-08-02T10:00:00Z",ended_utc:"2026-08-02T10:30:00Z",
      approved_utc:"2026-08-02T11:00:00Z",
      external_refs:["https://evidence.example.com/run/123"],
      source_artifacts:[{path:$raw,sha256:$raw_hash}],evidence:$evidence}' \
    > "$bundle/$filename"
}

make_report OPS-01 ops-01-monitor-alert.json \
  '{"synthetic_check_id":"check-123","correlation_id":"corr-123","primary_on_call":"Primary SRE","backup_on_call":"Backup SRE","dashboard_url":"https://monitor.example.com/d/1","delivery_threshold_seconds":120,"delivery_seconds":45,"failure_injected_utc":"2026-08-02T10:01:00Z","alert_delivered_utc":"2026-08-02T10:01:45Z","acknowledged_utc":"2026-08-02T10:03:00Z","recovered_utc":"2026-08-02T10:10:00Z"}'
make_report DR-01 dr-01-pitr-restore.json \
  '{"restore_target_utc":"2026-08-02T09:00:00Z","requested_restore_age_minutes":60,"accepted_rpo_minutes":60,"measured_end_to_end_rto_seconds":1800,"accepted_rto_seconds":1800,"database_contract_passed":true,"restricted_application_validation_passed":true,"tenant_branch_isolation_passed":true,"object_evidence_validation_passed":true,"branch_cleanup_verified":true}'
make_report REL-01 rel-01-rollback.json \
  '{"candidate_images":{"api":"registry.example.com/api@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","frontend":"registry.example.com/frontend@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","gateway":"registry.example.com/gateway@sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"},"last_known_good_images":{"api":"registry.example.com/api@sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd","frontend":"registry.example.com/frontend@sha256:eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee","gateway":"registry.example.com/gateway@sha256:ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"},"measured_rollback_seconds":1800,"accepted_rollback_seconds":1800,"schema_compatibility_passed":true,"config_recovery_passed":true,"write_freeze_and_resume_tested":true,"production_health_passed":true,"tenant_branch_isolation_passed":true,"synthetic_safety_mutation_passed":true,"alert_recovery_verified":true}'
make_report DATA-02 data-02-retention-privacy.json \
  '{"policy":{"audit_log_days":90,"telemetry_days":90,"notification_days":30,"report_execution_days":180,"security_event_days":365},"privacy_agreement_ref":"agreement-123","retention_scope_exception_ref":"exception-123","data_owner":"Privacy Owner","retention_worker_healthy":true,"expired_row_purge_exercised":true,"legal_hold_prevention_exercised":true,"subject_export_exercised":true,"implementation_scope_reviewed":true,"subject_delete_exercised":false,"subject_delete_exception_ref":"exception-123","object_store_retention_and_recovery_verified":true}'

tools/validate-safety-pilot-external-ops-evidence.sh \
  --bundle "$bundle" --candidate "$candidate" --output "$test_root/validation.tsv" \
  > "$test_root/validator.log" 2>&1
[ "$(grep -c $'CONTRACT_VALID_REVIEW_REQUIRED\t' "$test_root/validation.tsv")" = 4 ]

collector_output="$test_root/collector-output"
tools/collect-safety-pilot-evidence.sh --output "$collector_output" --external-ops "$bundle" \
  > "$test_root/collector.log" 2>&1
for gate in OPS-01 DR-01 REL-01 DATA-02; do
  grep -q "^${gate}$(printf '\t')REVIEW_REQUIRED$(printf '\t')" "$collector_output/gates.tsv"
done
[ "$(grep -c $'CONTRACT_VALID_REVIEW_REQUIRED\t' "$collector_output/external-ops-validation.tsv")" = 4 ]
test -s "$collector_output/external-ops-evidence/raw/DR-01.txt"
grep -q './external-ops-evidence/raw/DR-01.txt' "$collector_output/evidence-index.sha256"

cp "$bundle/ops-01-monitor-alert.json" "$test_root/ops-good.json"
jq '.operator=.approver' "$test_root/ops-good.json" > "$bundle/ops-01-monitor-alert.json"
if tools/validate-safety-pilot-external-ops-evidence.sh --bundle "$bundle" --candidate "$candidate" \
  > "$test_root/separation.log" 2>&1; then
  echo "ERROR: validator accepted self-approved external evidence" >&2
  exit 1
fi
grep -q 'common external-evidence contract' "$test_root/separation.log"

cp "$test_root/ops-good.json" "$bundle/ops-01-monitor-alert.json"
jq '.evidence.failure_injected_utc="2026-08-02T09:59:59Z"' "$test_root/ops-good.json" \
  > "$bundle/ops-01-monitor-alert.json"
if tools/validate-safety-pilot-external-ops-evidence.sh --bundle "$bundle" --candidate "$candidate" \
  > "$test_root/event-window.log" 2>&1; then
  echo "ERROR: validator accepted a monitoring event outside its run window" >&2
  exit 1
fi
grep -q 'monitoring/alert acceptance' "$test_root/event-window.log"

cp "$test_root/ops-good.json" "$bundle/ops-01-monitor-alert.json"
cp "$bundle/dr-01-pitr-restore.json" "$test_root/dr-good.json"
jq '.evidence.measured_end_to_end_rto_seconds=900' \
  "$test_root/dr-good.json" > "$bundle/dr-01-pitr-restore.json"
if tools/validate-safety-pilot-external-ops-evidence.sh --bundle "$bundle" --candidate "$candidate" \
  > "$test_root/measured-rto.log" 2>&1; then
  echo "ERROR: validator accepted a claimed RTO inconsistent with the run window" >&2
  exit 1
fi
grep -q 'PITR/application recovery acceptance' "$test_root/measured-rto.log"

jq '.evidence.requested_restore_age_minutes=61 | .evidence.accepted_rpo_minutes=60' \
  "$test_root/dr-good.json" > "$bundle/dr-01-pitr-restore.json"
if tools/validate-safety-pilot-external-ops-evidence.sh --bundle "$bundle" --candidate "$candidate" \
  > "$test_root/rpo.log" 2>&1; then
  echo "ERROR: validator accepted a restore outside the approved RPO" >&2
  exit 1
fi
grep -q 'PITR/application recovery acceptance' "$test_root/rpo.log"

cp "$test_root/dr-good.json" "$bundle/dr-01-pitr-restore.json"

printf 'tampered\n' >> "$bundle/raw/DR-01.txt"
if tools/validate-safety-pilot-external-ops-evidence.sh --bundle "$bundle" --candidate "$candidate" \
  > "$test_root/tamper.log" 2>&1; then
  echo "ERROR: validator accepted a tampered source artifact" >&2
  exit 1
fi
grep -q 'artifact hash mismatch' "$test_root/tamper.log"

printf 'sanitized provider export for DR-01\n' > "$bundle/raw/DR-01.txt"
if tools/validate-safety-pilot-external-ops-evidence.sh --bundle "$bundle" \
  --candidate bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb > "$test_root/candidate.log" 2>&1; then
  echo "ERROR: validator accepted evidence from another candidate" >&2
  exit 1
fi
grep -q 'common external-evidence contract' "$test_root/candidate.log"

printf '%s\n' '-----BEGIN PRIVATE KEY-----' > "$bundle/unreferenced-secret.txt"
if tools/validate-safety-pilot-external-ops-evidence.sh --bundle "$bundle" --candidate "$candidate" \
  > "$test_root/secret.log" 2>&1; then
  echo "ERROR: validator accepted a likely secret" >&2
  exit 1
fi
grep -q 'appears to contain credentials' "$test_root/secret.log"

echo "Safety external operations evidence validator regression passed."

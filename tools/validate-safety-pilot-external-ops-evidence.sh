#!/usr/bin/env bash
set -euo pipefail
umask 077

usage() {
  cat <<'USAGE'
Usage: tools/validate-safety-pilot-external-ops-evidence.sh --bundle DIR --candidate SHA [--output FILE]

Validates the structure, candidate binding, timestamps and referenced-file hashes
for the four external Safety release exercises. Validation proves bundle integrity;
it does not authenticate an operator/approver or turn the release gate into PASS.

Required files in DIR:
  ops-01-monitor-alert.json
  dr-01-pitr-restore.json
  rel-01-rollback.json
  data-02-retention-privacy.json
USAGE
}

bundle=""
candidate=""
output=""
while [ "$#" -gt 0 ]; do
  case "$1" in
    --bundle) [ "$#" -ge 2 ] || { usage >&2; exit 2; }; bundle=$2; shift 2 ;;
    --candidate) [ "$#" -ge 2 ] || { usage >&2; exit 2; }; candidate=$2; shift 2 ;;
    --output) [ "$#" -ge 2 ] || { usage >&2; exit 2; }; output=$2; shift 2 ;;
    --help|-h) usage; exit 0 ;;
    *) echo "ERROR: unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done

[ -n "$bundle" ] && [ -d "$bundle" ] || { echo "ERROR: --bundle must name a directory" >&2; exit 2; }
[[ "$candidate" =~ ^[0-9a-f]{40}$|^[0-9a-f]{64}$ ]] || {
  echo "ERROR: --candidate must be a lowercase 40- or 64-character hex digest" >&2
  exit 2
}
for command_name in jq find; do
  command -v "$command_name" >/dev/null || { echo "ERROR: required command unavailable: $command_name" >&2; exit 2; }
done

case "$bundle" in /|.|..) echo "ERROR: unsafe bundle directory" >&2; exit 2;; esac
if find "$bundle" -type l -print -quit | grep -q .; then
  echo "ERROR: external evidence bundle must not contain symbolic links" >&2
  exit 1
fi
if find "$bundle" ! -type d ! -type f -print -quit | grep -q .; then
  echo "ERROR: external evidence bundle contains a non-regular filesystem object" >&2
  exit 1
fi
file_count=$(find "$bundle" -type f | wc -l | tr -d ' ')
[ "$file_count" -le 200 ] || { echo "ERROR: external evidence bundle exceeds 200 files" >&2; exit 1; }
total_bytes=$(find "$bundle" -type f -exec wc -c {} \; | awk '{sum += $1} END {print sum+0}')
[ "$total_bytes" -le 262144000 ] || { echo "ERROR: external evidence bundle exceeds 250 MiB" >&2; exit 1; }

sha256_value() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | awk '{print $1}'; else shasum -a 256 "$1" | awk '{print $1}'; fi
}

fail() { echo "ERROR: $*" >&2; exit 1; }
valid_utc() { [[ "$1" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$ ]]; }

reports=(
  "OPS-01:ops-01-monitor-alert.json"
  "DR-01:dr-01-pitr-restore.json"
  "REL-01:rel-01-rollback.json"
  "DATA-02:data-02-retention-privacy.json"
)

tmp_report=$(mktemp /tmp/opstrax-external-evidence-validation.XXXXXX)
cleanup() { rm -f "$tmp_report"; }
trap cleanup EXIT
printf 'gate_id\tstatus\treport\treport_sha256\treferenced_artifacts\n' > "$tmp_report"

for spec in "${reports[@]}"; do
  gate=${spec%%:*}
  name=${spec#*:}
  report="$bundle/$name"
  [ -f "$report" ] || fail "missing required report: $name"
  jq -e . "$report" >/dev/null || fail "$name is not valid JSON"

  jq -e --arg gate "$gate" --arg candidate "$candidate" '
    .schema_version == 1 and .gate_id == $gate and .candidate_sha == $candidate and
    .outcome == "PASS" and
    (.environment | type == "string" and length >= 3) and
    (.run_id | type == "string" and length >= 6) and
    (.operator | type == "string" and length >= 3) and
    (.approver | type == "string" and length >= 3) and
    .operator != .approver and
    (.started_utc | type == "string") and (.ended_utc | type == "string") and
    (.approved_utc | type == "string") and
    (.external_refs | type == "array" and length >= 1 and all(.[]; test("^https://"))) and
    (.source_artifacts | type == "array" and length >= 1 and
      all(.[]; (.path | type == "string" and length >= 1) and
               (.sha256 | test("^[0-9a-f]{64}$"))))
  ' "$report" >/dev/null || fail "$name fails the common external-evidence contract"

  environment=$(jq -r '.environment' "$report")
  case "$(printf '%s' "$environment" | tr '[:upper:]' '[:lower:]')" in
    local|localhost|development|dev|test|example|*invalid*) fail "$name names a non-target environment: $environment" ;;
  esac
  started=$(jq -r '.started_utc' "$report")
  ended=$(jq -r '.ended_utc' "$report")
  approved=$(jq -r '.approved_utc' "$report")
  valid_utc "$started" && valid_utc "$ended" && valid_utc "$approved" || fail "$name timestamps must be UTC RFC3339 seconds ending in Z"
  [[ "$started" < "$ended" || "$started" == "$ended" ]] || fail "$name ended before it started"
  [[ "$ended" < "$approved" || "$ended" == "$approved" ]] || fail "$name was approved before it ended"

  case "$gate" in
    OPS-01)
      jq -e '
        (.evidence.synthetic_check_id | type=="string" and length>=3) and
        (.evidence.correlation_id | type=="string" and length>=6) and
        (.evidence.primary_on_call | type=="string" and length>=3) and
        (.evidence.backup_on_call | type=="string" and length>=3) and
        (.evidence.dashboard_url | test("^https://")) and
        (.evidence.delivery_threshold_seconds | type=="number" and .>0) and
        (.evidence.delivery_seconds | type=="number" and .>=0) and
        .evidence.delivery_seconds <= .evidence.delivery_threshold_seconds and
        (((.evidence.alert_delivered_utc | fromdateiso8601) -
          (.evidence.failure_injected_utc | fromdateiso8601)) as $actual_delivery |
          $actual_delivery >= (.evidence.delivery_seconds - 1) and
          $actual_delivery <= (.evidence.delivery_seconds + 1)) and
        .evidence.primary_on_call != .evidence.backup_on_call and
        .started_utc <= .evidence.failure_injected_utc and
        .evidence.failure_injected_utc <= .evidence.alert_delivered_utc and
        .evidence.alert_delivered_utc <= .evidence.acknowledged_utc and
        .evidence.acknowledged_utc <= .evidence.recovered_utc and
        .evidence.recovered_utc <= .ended_utc and
        ([.evidence.failure_injected_utc,.evidence.alert_delivered_utc,
          .evidence.acknowledged_utc,.evidence.recovered_utc] | all(.[]; test("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$")))
      ' "$report" >/dev/null || fail "$name fails monitoring/alert acceptance"
      ;;
    DR-01)
      jq -e '
        (.evidence.restore_target_utc | test("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$")) and
        (.evidence.requested_restore_age_minutes | type=="number" and .>0) and
        (.evidence.accepted_rpo_minutes | type=="number" and .>0) and
        (.evidence.measured_end_to_end_rto_seconds | type=="number" and .>0) and
        (.evidence.accepted_rto_seconds | type=="number" and .>0) and
        .evidence.restore_target_utc <= .started_utc and
        .evidence.requested_restore_age_minutes <= .evidence.accepted_rpo_minutes and
        (((.started_utc | fromdateiso8601) -
          (.evidence.restore_target_utc | fromdateiso8601)) as $actual_age |
          $actual_age >= (.evidence.requested_restore_age_minutes * 60 - 60) and
          $actual_age <= (.evidence.requested_restore_age_minutes * 60 + 60)) and
        (((.ended_utc | fromdateiso8601) - (.started_utc | fromdateiso8601)) as $actual_rto |
          $actual_rto >= (.evidence.measured_end_to_end_rto_seconds - 1) and
          $actual_rto <= (.evidence.measured_end_to_end_rto_seconds + 1)) and
        .evidence.measured_end_to_end_rto_seconds <= .evidence.accepted_rto_seconds and
        .evidence.database_contract_passed == true and
        .evidence.restricted_application_validation_passed == true and
        .evidence.tenant_branch_isolation_passed == true and
        .evidence.object_evidence_validation_passed == true and
        .evidence.branch_cleanup_verified == true
      ' "$report" >/dev/null || fail "$name fails PITR/application recovery acceptance"
      ;;
    REL-01)
      jq -e '
        (.evidence.candidate_images | type=="object" and
          has("api") and has("frontend") and has("gateway") and
          all(.[]; test("@sha256:[0-9a-f]{64}$"))) and
        (.evidence.last_known_good_images | type=="object" and
          has("api") and has("frontend") and has("gateway") and
          all(.[]; test("@sha256:[0-9a-f]{64}$"))) and
        (.evidence.measured_rollback_seconds | type=="number" and .>0) and
        (.evidence.accepted_rollback_seconds | type=="number" and .>0) and
        (((.ended_utc | fromdateiso8601) - (.started_utc | fromdateiso8601)) as $actual_rollback |
          $actual_rollback >= (.evidence.measured_rollback_seconds - 1) and
          $actual_rollback <= (.evidence.measured_rollback_seconds + 1)) and
        .evidence.measured_rollback_seconds <= .evidence.accepted_rollback_seconds and
        .evidence.schema_compatibility_passed == true and
        .evidence.config_recovery_passed == true and
        .evidence.write_freeze_and_resume_tested == true and
        .evidence.production_health_passed == true and
        .evidence.tenant_branch_isolation_passed == true and
        .evidence.synthetic_safety_mutation_passed == true and
        .evidence.alert_recovery_verified == true
      ' "$report" >/dev/null || fail "$name fails rollback acceptance"
      ;;
    DATA-02)
      jq -e '
        (.evidence.policy.audit_log_days | type=="number" and .>=30) and
        (.evidence.policy.telemetry_days | type=="number" and .>=7) and
        (.evidence.policy.notification_days | type=="number" and .>=7) and
        (.evidence.policy.report_execution_days | type=="number" and .>=30) and
        (.evidence.policy.security_event_days | type=="number" and .>=90) and
        (.evidence.privacy_agreement_ref | type=="string" and length>=6) and
        (.evidence.retention_scope_exception_ref | type=="string" and length>=6) and
        (.evidence.data_owner | type=="string" and length>=3) and
        .evidence.retention_worker_healthy == true and
        .evidence.expired_row_purge_exercised == true and
        .evidence.legal_hold_prevention_exercised == true and
        .evidence.subject_export_exercised == true and
        .evidence.implementation_scope_reviewed == true and
        (.evidence.subject_delete_exercised == true or
          (.evidence.subject_delete_exercised == false and
           (.evidence.subject_delete_exception_ref | type=="string" and length>=6))) and
        .evidence.object_store_retention_and_recovery_verified == true
      ' "$report" >/dev/null || fail "$name fails retention/privacy acceptance"
      ;;
  esac

  artifact_count=0
  while IFS=$'\t' read -r relative expected; do
    case "$relative" in ""|/*|../*|*/../*|*/..|.) fail "$name contains an unsafe artifact path: $relative";; esac
    artifact="$bundle/$relative"
    [ -f "$artifact" ] && [ ! -L "$artifact" ] || fail "$name references a missing/non-regular artifact: $relative"
    actual=$(sha256_value "$artifact")
    [ "$actual" = "$expected" ] || fail "$name artifact hash mismatch: $relative"
    artifact_count=$((artifact_count + 1))
  done < <(jq -r '.source_artifacts[] | [.path,.sha256] | @tsv' "$report")

  report_hash=$(sha256_value "$report")
  printf '%s\tCONTRACT_VALID_REVIEW_REQUIRED\t%s\t%s\t%s\n' "$gate" "$name" "$report_hash" "$artifact_count" >> "$tmp_report"
done

# Reject a few high-confidence secret forms anywhere in the submitted bundle.
if LC_ALL=C grep -R -a -E -i --exclude='*.sha256' \
  '(-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----|authorization:[[:space:]]*bearer[[:space:]]+|set-cookie:|postgres(ql)?://[^/@[:space:]]+:[^/@[:space:]]+@)' \
  "$bundle" >/dev/null 2>&1; then
  fail "external evidence bundle appears to contain credentials, cookies or a private key"
fi

if [ -n "$output" ]; then
  case "$output" in /|.|..) fail "unsafe validation output path";; esac
  mkdir -p "$(dirname "$output")"
  cp "$tmp_report" "$output"
else
  cat "$tmp_report"
fi

echo "External operations evidence is structurally valid and hash-bound to candidate $candidate." >&2
echo "Human custody/signature review is still required before any release gate becomes PASS." >&2

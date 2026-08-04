#!/usr/bin/env bash
set -euo pipefail
umask 077

usage() {
  cat <<'USAGE'
Usage: tools/collect-safety-pilot-evidence.sh [options]

Collects a redacted, timestamped Safety pilot evidence skeleton from the current
candidate. It never reads or writes browser session storage and never prints env
values or database connection strings.

Options:
  --output DIR          Evidence directory (default: artifacts/release-evidence/safety-<UTC>)
  --runtime-url URL     Rehearsal API base URL; captures public health endpoints
  --image COMPONENT=REF Inspect api/frontend/gateway image and generate CycloneDX SBOM
  --external-ops DIR    Import a validated, sanitized OPS/DR/rollback/privacy bundle
  --run-tests           Run bounded non-DB Safety/control tests and frontend build
  --help                Show this help
USAGE
}

repo_root=$(git rev-parse --show-toplevel 2>/dev/null || true)
if [ -z "$repo_root" ]; then
  echo "ERROR: run from an OpsTrax Git worktree" >&2
  exit 2
fi
cd "$repo_root"

utc_stamp=$(date -u +%Y%m%dT%H%M%SZ)
output_dir="artifacts/release-evidence/safety-${utc_stamp}"
runtime_url=""
run_tests=false
images=()
external_ops_dir=""

while [ "$#" -gt 0 ]; do
  case "$1" in
    --output)
      [ "$#" -ge 2 ] || { echo "ERROR: --output requires a directory" >&2; exit 2; }
      output_dir=$2
      shift 2
      ;;
    --runtime-url)
      [ "$#" -ge 2 ] || { echo "ERROR: --runtime-url requires a URL" >&2; exit 2; }
      runtime_url=${2%/}
      case "$runtime_url" in http://*|https://*) ;; *) echo "ERROR: runtime URL must use http or https" >&2; exit 2;; esac
      case "$runtime_url" in *'@'*|*'?'*|*'#'*) echo "ERROR: runtime URL must not contain credentials, query parameters, or fragments" >&2; exit 2;; esac
      shift 2
      ;;
    --run-tests)
      run_tests=true
      shift
      ;;
    --image)
      [ "$#" -ge 2 ] || { echo "ERROR: --image requires COMPONENT=REF" >&2; exit 2; }
      images+=("$2")
      shift 2
      ;;
    --external-ops)
      [ "$#" -ge 2 ] || { echo "ERROR: --external-ops requires a directory" >&2; exit 2; }
      external_ops_dir=$2
      shift 2
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      echo "ERROR: unknown option: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

case "$output_dir" in
  ""|/|.|..) echo "ERROR: unsafe output directory" >&2; exit 2;;
esac
if [ -L "$output_dir" ]; then
  echo "ERROR: output directory must not be a symbolic link" >&2
  exit 2
fi
if [ -d "$output_dir" ] && [ -n "$(find "$output_dir" -mindepth 1 -maxdepth 1 -print -quit)" ]; then
  echo "ERROR: output directory already contains evidence; choose a new directory" >&2
  exit 2
fi
mkdir -p "$output_dir"

sha256_file() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$@"; else shasum -a 256 "$@"; fi
}

record_gate() {
  printf '%s\t%s\t%s\n' "$1" "$2" "$3" >> "$output_dir/gates.tsv"
}

commit_sha=$(git rev-parse HEAD)
branch=$(git branch --show-current)
git status --porcelain=v1 > "$output_dir/git-status.txt"
dirty_count=$(wc -l < "$output_dir/git-status.txt" | tr -d ' ')

printf 'gate_id\tstatus\tevidence\n' > "$output_dir/gates.tsv"
record_gate RC-01-SHA PASS "manifest.md"
if [ "$dirty_count" = "0" ]; then
  record_gate RC-01-CLEAN PASS "git-status.txt"
else
  record_gate RC-01-CLEAN FAIL "git-status.txt contains ${dirty_count} changed paths"
fi

required_files=(
  render.yaml
  docker-compose.yml
  backend-dotnet/Dockerfile
  frontend/Dockerfile
  database/migrations/2026_07_31_stage58_nonforgeable_tenant_ticket.sql
  database/migrations/2026_07_22_stage47_detention_recovery.sql
  database/migrations/2026_07_31_stage59_data_protection_key_ring.sql
  database/migrations/2026_08_01_stage65_safety_pilot.sql
  database/migrations/2026_08_02_stage66_telematics_pilot.sql
  database/migrations/2026_08_02_stage67_telematics_diagnostics_integrity.sql
  database/migrations/2026_08_02_stage68_entitlement_policy_mode.sql
  database/migrations/2026_08_02_stage69_market_pack_control_hardening.sql
  database/migrations/2026_08_02_stage70_hos_pilot_schema_reconciliation.sql
  database/migrations/2026_08_02_stage71_coaching_evidence_reconciliation.sql
  database/migrations/2026_08_02_stage72_hos_offboarding_immutability_reconciliation.sql
  database/migrations/2026_08_02_stage73_hos_offboarding_null_fail_closed.sql
  database/migrations/2026_08_02_stage74_retention_policy_production_contract.sql
  database/migrations/2026_08_02_stage75_bounded_support_access.sql
  tools/apply-neon-predeploy-migrations.sh
  tools/test-predeploy-clean-chain.sh
  tools/test-production-shaped-local-rehearsal.sh
  tools/dr-restore-drill.sh
  tools/test-dr-restore-drill-contract.sh
  tools/verify-safety-pilot-restored-database.sql
  tools/collect-safety-pilot-evidence.sh
  tools/test-safety-pilot-evidence-collector.sh
  tools/validate-safety-pilot-external-ops-evidence.sh
  tools/test-safety-pilot-external-ops-evidence.sh
  tools/collect-release-candidate-provenance.sh
  tools/test-release-candidate-provenance.sh
  backend-dotnet.Tests/ReleaseProvenanceContractTests.cs
  docs/platform/PLATFORM_ADMIN_SAFETY_CONTROL_MATRIX.md
  docs/pilot/SAFETY_PILOT_RELEASE_GATE.md
  docs/pilot/SAFETY_PILOT_AS_BUILT.md
  docs/pilot/SAFETY_PILOT_EVIDENCE_INDEX.md
  docs/pilot/SAFETY_PILOT_REHEARSAL_CHECKLIST.md
  docs/pilot/SAFETY_PILOT_GO_NO_GO_DECISION.md
  docs/pilot/SAFETY_PILOT_ROLLBACK_RECOVERY_PLAN.md
  docs/pilot/SAFETY_PILOT_EXTERNAL_OPS_EVIDENCE_CONTRACT.md
  docs/pilot/SAFETY_PILOT_OWNERSHIP_AND_DEMO_RUNBOOK.md
  docs/pilot/SAFETY_PILOT_INDEPENDENT_READINESS_REVIEW_2026-08-02.md
  docs/pilot/RELEASE_CANDIDATE_PROVENANCE.md
)

missing=false
present_files=()
for path in "${required_files[@]}"; do
  if [ ! -f "$path" ]; then
    printf 'MISSING  %s\n' "$path" >> "$output_dir/static-checks.txt"
    missing=true
  else
    printf 'PRESENT  %s\n' "$path" >> "$output_dir/static-checks.txt"
    present_files+=("$path")
  fi
done
if [ "$missing" = false ]; then
  record_gate RC-01-FILES PASS static-checks.txt
else
  record_gate RC-01-FILES FAIL static-checks.txt
fi

sha256_file "${present_files[@]}" > "$output_dir/source-hashes.sha256"

provenance_args=(--allow-dirty --output "$output_dir/candidate-provenance")
if [ "${#images[@]}" -gt 0 ]; then
  for image_spec in "${images[@]}"; do provenance_args+=(--image "$image_spec"); done
fi
if tools/collect-release-candidate-provenance.sh "${provenance_args[@]}" > "$output_dir/provenance-collector.txt" 2>&1; then
  record_gate RC-01-PROVENANCE PASS candidate-provenance/candidate.tsv
  if [ "${#images[@]}" -gt 0 ]; then
    record_gate RC-01-LOCAL-IMAGES PASS candidate-provenance/images.tsv
  else
    record_gate RC-01-LOCAL-IMAGES NOT_EVIDENCED candidate-provenance/images.tsv
  fi
  complete_images=true
  for component in api frontend gateway; do
    grep -q "^${component}$(printf '\t')" "$output_dir/candidate-provenance/images.tsv" || complete_images=false
  done
  if [ "$complete_images" = true ]; then
    record_gate RC-01-SBOMS PASS candidate-provenance/images.tsv
    if awk -F '\t' 'NR>1 && $5=="NOT_EVIDENCED" {missing=1} END {exit !missing}' "$output_dir/candidate-provenance/images.tsv"; then
      record_gate RC-01-REGISTRY-DIGESTS NOT_EVIDENCED candidate-provenance/images.tsv
    else
      record_gate RC-01-REGISTRY-DIGESTS PASS candidate-provenance/images.tsv
    fi
  else
    record_gate RC-01-SBOMS NOT_EVIDENCED candidate-provenance/images.tsv
    record_gate RC-01-REGISTRY-DIGESTS NOT_EVIDENCED candidate-provenance/images.tsv
  fi
else
  record_gate RC-01-PROVENANCE FAIL provenance-collector.txt
  record_gate RC-01-LOCAL-IMAGES FAIL provenance-collector.txt
  record_gate RC-01-SBOMS FAIL provenance-collector.txt
  record_gate RC-01-REGISTRY-DIGESTS FAIL provenance-collector.txt
fi

if bash -n tools/apply-neon-predeploy-migrations.sh \
  && bash -n tools/test-predeploy-clean-chain.sh \
  && bash -n tools/test-production-shaped-local-rehearsal.sh \
  && bash -n tools/dr-restore-drill.sh \
    && bash -n tools/test-dr-restore-drill-contract.sh \
    && bash -n tools/collect-safety-pilot-evidence.sh \
    && bash -n tools/test-safety-pilot-evidence-collector.sh \
    && bash -n tools/validate-safety-pilot-external-ops-evidence.sh \
    && bash -n tools/test-safety-pilot-external-ops-evidence.sh \
    && bash -n tools/collect-release-candidate-provenance.sh \
    && bash -n tools/test-release-candidate-provenance.sh; then
  record_gate RC-02-SCRIPT-SYNTAX PASS static-checks.txt
else
  record_gate RC-02-SCRIPT-SYNTAX FAIL static-checks.txt
fi

compose_status=NOT_RUN
if command -v docker >/dev/null 2>&1; then
  if JWT_KEY=collector-placeholder-jwt-key-at-least-64-characters-long-000000000 \
    PLATFORM_SUPERADMIN_PASSWORD=collector-placeholder-platform-password \
    PG_CONNECTION_APP=postgresql://opstrax_app:placeholder-app@postgres/placeholder \
    PG_CONNECTION_SYSTEM=postgresql://opstrax_system:placeholder-system@postgres/placeholder \
    DATA_PROTECTION_CERTIFICATE_BASE64=Y29sbGVjdG9yLXBsYWNlaG9sZGVy \
    DATA_PROTECTION_CERTIFICATE_PASSWORD=collector-placeholder-password \
    docker compose config --quiet > "$output_dir/compose-validation.txt" 2>&1; then
    compose_status=PASS
    record_gate RC-02-COMPOSE PASS compose-validation.txt
  else
    compose_status=FAIL
    record_gate RC-02-COMPOSE FAIL compose-validation.txt
  fi
else
  printf 'Docker CLI unavailable; compose model not validated.\n' > "$output_dir/compose-validation.txt"
  record_gate RC-02-COMPOSE NOT_EVIDENCED compose-validation.txt
fi

runtime_status=NOT_EVIDENCED
if [ -n "$runtime_url" ]; then
  runtime_status=PASS
  for endpoint in health/live health/ready health/deep; do
    safe_name=$(printf '%s' "$endpoint" | tr '/' '-')
    http_code=$(curl --silent --show-error --location --max-time 20 \
      --output "$output_dir/${safe_name}.json" --write-out '%{http_code}' \
      "$runtime_url/$endpoint" || true)
    printf '%s\t%s\n' "$endpoint" "$http_code" >> "$output_dir/runtime-http.tsv"
    if [ "$http_code" != "200" ]; then runtime_status=FAIL; fi
  done
  if ! grep -Eq '"environment"[[:space:]]*:[[:space:]]*"Production"' "$output_dir/health-ready.json"; then
    runtime_status=FAIL
  fi
  if ! grep -Eq '"status"[[:space:]]*:[[:space:]]*"ready"' "$output_dir/health-ready.json" \
    || ! grep -Eq '"status"[[:space:]]*:[[:space:]]*"healthy"' "$output_dir/health-deep.json"; then
    runtime_status=FAIL
  fi
  if grep -Eq '"status"[[:space:]]*:[[:space:]]*"fail"|"ready"[[:space:]]*:[[:space:]]*false' "$output_dir/health-ready.json" "$output_dir/health-deep.json"; then
    runtime_status=FAIL
  fi
  if ! grep -Fq "$commit_sha" "$output_dir/health-ready.json"; then
    runtime_status=FAIL
  fi
  record_gate SEC-01-RUNTIME "$runtime_status" runtime-http.tsv
else
  printf 'No --runtime-url supplied; deployed Production readiness is not evidenced.\n' > "$output_dir/runtime-http.tsv"
  record_gate SEC-01-RUNTIME NOT_EVIDENCED runtime-http.tsv
fi

test_status=NOT_EVIDENCED
if [ "$run_tests" = true ]; then
  test_status=PASS
  test_filter='FullyQualifiedName~FleetPilotSafetyUiTests|FullyQualifiedName~PlatformSafetyControlPlaneContractTests|FullyQualifiedName~EntitlementAwareNavigationTests|FullyQualifiedName~CsrfMiddlewareTests|FullyQualifiedName~ConfigValidationRlsTests|FullyQualifiedName~FleetRuntimeRouteContractRegressionTests|FullyQualifiedName~DeepHealthSystemLaneRegressionTests|FullyQualifiedName~ReleaseProvenanceContractTests'
  if ! env -u PG_CONNECTION -u PG_CONNECTION_APP -u PG_CONNECTION_SYSTEM \
    -u ConnectionStrings__DefaultConnection -u ConnectionStrings__SystemConnection \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    dotnet test backend-dotnet.Tests/Opstrax.Tests.csproj --filter "$test_filter" \
    > "$output_dir/dotnet-focused-tests.txt" 2>&1; then
    test_status=FAIL
  fi
  if ! npm run build --prefix frontend > "$output_dir/frontend-build.txt" 2>&1; then
    test_status=FAIL
  fi
  record_gate RC-02-LOCAL-FOCUSED "$test_status" "dotnet-focused-tests.txt; frontend-build.txt"
else
  printf 'Tests not requested. Exact-SHA CI evidence remains mandatory.\n' > "$output_dir/local-tests.txt"
  record_gate RC-02-LOCAL-FOCUSED NOT_EVIDENCED local-tests.txt
fi

if [ -n "$external_ops_dir" ]; then
  [ -d "$external_ops_dir" ] || { echo "ERROR: --external-ops must name a directory" >&2; exit 2; }
  external_ops_abs=$(cd "$external_ops_dir" && pwd -P)
  output_abs=$(cd "$output_dir" && pwd -P)
  case "$external_ops_abs" in
    "$output_abs"|"$output_abs"/*)
      echo "ERROR: external operations evidence must be outside the collector output tree" >&2
      exit 2
      ;;
  esac
  tools/validate-safety-pilot-external-ops-evidence.sh \
    --bundle "$external_ops_abs" --candidate "$commit_sha" \
    > "$output_dir/external-ops-source-validator.txt" 2>&1
  mkdir -p "$output_dir/external-ops-evidence"
  cp -R "$external_ops_abs"/. "$output_dir/external-ops-evidence"/
  find "$output_dir/external-ops-evidence" -type d -exec chmod 700 {} +
  find "$output_dir/external-ops-evidence" -type f -exec chmod 600 {} +
  # Validate the imported copy too. This closes the copy race and proves the
  # hashes in the custody index cover exactly the files retained by the bundle.
  tools/validate-safety-pilot-external-ops-evidence.sh \
    --bundle "$output_dir/external-ops-evidence" --candidate "$commit_sha" \
    --output "$output_dir/external-ops-validation.tsv" \
    > "$output_dir/external-ops-validator.txt" 2>&1
  for gate in OPS-01 DR-01 REL-01 DATA-02; do
    record_gate "$gate" REVIEW_REQUIRED "external-ops-validation.tsv; external-ops-evidence/"
  done
else
  printf 'External operations evidence was not supplied.\n' > "$output_dir/external-ops-validation.tsv"
  for gate in OPS-01 DR-01 REL-01 DATA-02; do
    record_gate "$gate" NOT_EVIDENCED external-ops-validation.tsv
  done
fi

cat > "$output_dir/manifest.md" <<MANIFEST
# Safety pilot evidence collection manifest

- Collected UTC: ${utc_stamp}
- Branch: ${branch}
- Commit: ${commit_sha}
- Dirty paths: ${dirty_count}
- Runtime URL: ${runtime_url:-not supplied}
- Compose validation: ${compose_status}
- Runtime public-health collection: ${runtime_status}
- Optional focused local checks: ${test_status}
- Candidate provenance: candidate-provenance/candidate.tsv
- External OPS/DR/rollback/privacy bundle: ${external_ops_dir:-not supplied}

This collection is a skeleton, not a GO decision. Human/browser UAT, Platform control
snapshots, successful exact-SHA CI, registry image digests, external alert delivery, DR, privacy and
executive approvals remain mandatory under docs/pilot/SAFETY_PILOT_RELEASE_GATE.md.
Imported external operations reports remain REVIEW_REQUIRED until their source exports,
operator/approver identities and custody are independently authenticated.
MANIFEST

(
  cd "$output_dir"
  find . -type f ! -name evidence-index.sha256 -print | LC_ALL=C sort | while IFS= read -r artifact; do
    sha256_file "$artifact"
  done
) > "$output_dir/evidence-index.sha256"

echo "Evidence skeleton written to: $output_dir"
echo "Current release decision remains NO-GO until every mandatory gate is evidenced and signed."

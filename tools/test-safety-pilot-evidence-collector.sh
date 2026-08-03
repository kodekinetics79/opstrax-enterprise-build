#!/usr/bin/env bash
set -euo pipefail

repo_root=$(git rev-parse --show-toplevel 2>/dev/null || true)
[ -n "$repo_root" ] || { echo "ERROR: run from a Git worktree" >&2; exit 2; }
cd "$repo_root"

test_root=$(mktemp -d /tmp/opstrax-safety-evidence-test.XXXXXX)
cleanup() {
  case "$test_root" in
    /tmp/opstrax-safety-evidence-test.*) rm -rf "$test_root" ;;
    *) echo "Refusing unsafe test cleanup: $test_root" >&2 ;;
  esac
}
trap cleanup EXIT

sentinel='DO-NOT-LEAK-SAFETY-EVIDENCE-SECRET-97241'
export JWT_KEY="$sentinel"
export PG_CONNECTION_APP="postgresql://opstrax_app:${sentinel}@invalid/invalid"
export PG_CONNECTION_SYSTEM="postgresql://opstrax_system:${sentinel}@invalid/invalid"
export PLATFORM_SUPERADMIN_PASSWORD="$sentinel"

evidence_dir="$test_root/evidence"
tools/collect-safety-pilot-evidence.sh --output "$evidence_dir" > "$test_root/collector.log"

test -s "$evidence_dir/manifest.md"
test -s "$evidence_dir/gates.tsv"
test -s "$evidence_dir/source-hashes.sha256"
test -s "$evidence_dir/evidence-index.sha256"
test -s "$evidence_dir/candidate-provenance/candidate.tsv"
test -s "$evidence_dir/candidate-provenance/bundle.sha256"
grep -q $'RC-01-PROVENANCE\tPASS\t' "$evidence_dir/gates.tsv"
grep -q $'RC-01-SBOMS\tNOT_EVIDENCED\t' "$evidence_dir/gates.tsv"
grep -q $'RC-01-REGISTRY-DIGESTS\tNOT_EVIDENCED\t' "$evidence_dir/gates.tsv"
grep -q $'SEC-01-RUNTIME\tNOT_EVIDENCED\t' "$evidence_dir/gates.tsv"
grep -q $'RC-02-LOCAL-FOCUSED\tNOT_EVIDENCED\t' "$evidence_dir/gates.tsv"
grep -q $'OPS-01\tNOT_EVIDENCED\t' "$evidence_dir/gates.tsv"
grep -q $'DR-01\tNOT_EVIDENCED\t' "$evidence_dir/gates.tsv"
grep -q $'REL-01\tNOT_EVIDENCED\t' "$evidence_dir/gates.tsv"
grep -q $'DATA-02\tNOT_EVIDENCED\t' "$evidence_dir/gates.tsv"
grep -q './external-ops-validation.tsv' "$evidence_dir/evidence-index.sha256"
grep -q './runtime-http.tsv' "$evidence_dir/evidence-index.sha256"
grep -q './compose-validation.txt' "$evidence_dir/evidence-index.sha256"
expected_hashes=$(find "$evidence_dir" -type f ! -name evidence-index.sha256 | wc -l | tr -d ' ')
actual_hashes=$(wc -l < "$evidence_dir/evidence-index.sha256" | tr -d ' ')
test "$expected_hashes" = "$actual_hashes"

dirty_count=$(git status --porcelain=v1 | wc -l | tr -d ' ')
if [ "$dirty_count" = "0" ]; then
  grep -q $'RC-01-CLEAN\tPASS\t' "$evidence_dir/gates.tsv"
else
  grep -q $'RC-01-CLEAN\tFAIL\t' "$evidence_dir/gates.tsv"
fi

if grep -R --fixed-strings "$sentinel" "$evidence_dir" >/dev/null 2>&1; then
  echo "ERROR: collector leaked an environment secret into evidence" >&2
  exit 1
fi

if tools/collect-safety-pilot-evidence.sh --output "$evidence_dir" > "$test_root/reuse.log" 2>&1; then
  echo "ERROR: collector overwrote an existing evidence directory" >&2
  exit 1
fi
grep -q 'already contains evidence' "$test_root/reuse.log"

if tools/collect-safety-pilot-evidence.sh --output "$test_root/url-evidence" \
  --runtime-url "https://user:${sentinel}@example.invalid" > "$test_root/url.log" 2>&1; then
  echo "ERROR: collector accepted a credential-bearing runtime URL" >&2
  exit 1
fi
grep -q 'must not contain credentials' "$test_root/url.log"

echo "Safety pilot evidence collector regression passed."

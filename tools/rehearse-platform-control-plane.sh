#!/usr/bin/env bash
set -euo pipefail
umask 077

usage() {
  cat <<'USAGE'
Usage: tools/rehearse-platform-control-plane.sh [--output DIR]

Runs the deterministic Platform control-plane rehearsal against OPSTRAX_TEST_DB
and writes a redacted evidence bundle. The target must be a disposable, non-
production PostgreSQL database. Connection strings are never copied to evidence.

Options:
  --output DIR  New or empty evidence directory
                (default: /tmp/opstrax-control-plane-<UTC>)
  --help        Show this help
USAGE
}

repo_root=$(git rev-parse --show-toplevel 2>/dev/null || true)
[ -n "$repo_root" ] || { echo "ERROR: run inside an OpsTrax Git worktree" >&2; exit 2; }
cd "$repo_root"

stamp=$(date -u +%Y%m%dT%H%M%SZ)
output_dir="/tmp/opstrax-control-plane-${stamp}"
while [ "$#" -gt 0 ]; do
  case "$1" in
    --output)
      [ "$#" -ge 2 ] || { echo "ERROR: --output requires a directory" >&2; exit 2; }
      output_dir=$2
      shift 2
      ;;
    --help|-h) usage; exit 0 ;;
    *) echo "ERROR: unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done

case "$output_dir" in ""|/|.|..) echo "ERROR: unsafe evidence directory" >&2; exit 2;; esac
[ ! -L "$output_dir" ] || { echo "ERROR: evidence directory must not be a symlink" >&2; exit 2; }
if [ -d "$output_dir" ] && [ -n "$(find "$output_dir" -mindepth 1 -maxdepth 1 -print -quit)" ]; then
  echo "ERROR: evidence directory is not empty" >&2
  exit 2
fi
mkdir -p "$output_dir"

if [ "${OPSTRAX_CONTROL_REHEARSAL_ACK:-}" != "DISPOSABLE_NON_PRODUCTION" ]; then
  cat >&2 <<'ERROR'
ERROR: destructive integration tests require explicit target acknowledgement.
Set OPSTRAX_CONTROL_REHEARSAL_ACK=DISPOSABLE_NON_PRODUCTION only after confirming
OPSTRAX_TEST_DB is a disposable non-production database.
ERROR
  exit 2
fi

db_source=local_default
[ -n "${OPSTRAX_TEST_DB:-}" ] && db_source=explicit_test_database
commit_sha=$(git rev-parse HEAD)
branch=$(git branch --show-current)
dirty_count=$(git status --porcelain=v1 | wc -l | tr -d ' ')

cat > "$output_dir/manifest.md" <<EOF
# Platform control-plane rehearsal evidence

- UTC start: ${stamp}
- Commit: ${commit_sha}
- Branch: ${branch:-detached}
- Worktree changed paths: ${dirty_count}
- Database classification: operator-acknowledged disposable non-production
- Database source: ${db_source} (connection value intentionally omitted)
- Rehearsal scope: package transition, persistent override, tenant denial,
  authorization snapshot, Platform audit sequence, restoration and cleanup
EOF

git status --porcelain=v1 > "$output_dir/git-status.txt"

filter='FullyQualifiedName~PlatformControlPlaneRehearsalTests|FullyQualifiedName~PlatformEnterpriseControlMapTests|FullyQualifiedName~EntitlementAwareNavigationTests|FullyQualifiedName~EntitlementPolicyModePostgresTests|FullyQualifiedName~MarketPackPlatformControlPostgresTests'
set +e
dotnet test backend-dotnet.Tests/Opstrax.Tests.csproj \
  --filter "$filter" \
  --logger "trx;LogFileName=platform-control-plane.trx" \
  --logger "console;verbosity=minimal" \
  --results-directory "$output_dir" 2>&1 | tee "$output_dir/test-output.log"
test_status=${PIPESTATUS[0]}
set -e

if [ "$test_status" -eq 0 ]; then
  result=PASS
else
  result=FAIL
fi
cat >> "$output_dir/manifest.md" <<EOF
- Automated result: ${result}
- UTC finish: $(date -u +%Y%m%dT%H%M%SZ)
- Test log: test-output.log
- Machine-readable result: platform-control-plane.trx
EOF

printf '%s\n' "$result: evidence written to $output_dir"
exit "$test_status"

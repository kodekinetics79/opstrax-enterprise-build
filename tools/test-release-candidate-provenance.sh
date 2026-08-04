#!/usr/bin/env bash
set -euo pipefail

repo_root=$(git rev-parse --show-toplevel 2>/dev/null || true)
[ -n "$repo_root" ] || { echo "ERROR: run from a Git worktree" >&2; exit 2; }
cd "$repo_root"

test_root=$(mktemp -d /tmp/opstrax-provenance-test.XXXXXX)
cleanup() { case "$test_root" in /tmp/opstrax-provenance-test.*) rm -rf "$test_root";; esac; }
trap cleanup EXIT

tools/collect-release-candidate-provenance.sh --allow-dirty --output "$test_root/one" >/dev/null
tools/collect-release-candidate-provenance.sh --allow-dirty --output "$test_root/two" >/dev/null
cmp "$test_root/one/candidate.tsv" "$test_root/two/candidate.tsv"
cmp "$test_root/one/migrations.sha256" "$test_root/two/migrations.sha256"
cmp "$test_root/one/dependencies.sha256" "$test_root/two/dependencies.sha256"
cmp "$test_root/one/images.tsv" "$test_root/two/images.tsv"
grep -q $'^commit_sha\t' "$test_root/one/candidate.tsv"
grep -q $'^source_archive_sha256\t' "$test_root/one/candidate.tsv"
grep -q $'^component\timage_reference\tlocal_image_id\tlocal_repo_digest\tpublished_registry_digest' "$test_root/one/images.tsv"
test -s "$test_root/one/bundle.sha256"
expected=$(find "$test_root/one" -type f ! -name bundle.sha256 | wc -l | tr -d ' ')
actual=$(wc -l < "$test_root/one/bundle.sha256" | tr -d ' ')
test "$expected" = "$actual"

if [ -n "$(git status --porcelain=v1)" ]; then
  if tools/collect-release-candidate-provenance.sh --output "$test_root/dirty" >"$test_root/dirty.log" 2>&1; then
    echo "ERROR: provenance collector accepted a dirty release candidate" >&2
    exit 1
  fi
  grep -q 'release candidate worktree is dirty' "$test_root/dirty.log"
fi

if tools/collect-release-candidate-provenance.sh --allow-dirty --output "$test_root/one" >"$test_root/reuse.log" 2>&1; then
  echo "ERROR: provenance collector overwrote an existing bundle" >&2
  exit 1
fi
grep -q 'already contains provenance' "$test_root/reuse.log"

echo "Release-candidate provenance regression passed."

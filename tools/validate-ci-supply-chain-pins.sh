#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
failed=0

while IFS= read -r workflow; do
  while IFS= read -r use_ref; do
    [[ "$use_ref" == ./* ]] && continue
    if [[ ! "$use_ref" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[0-9a-f]{40}$ ]]; then
      echo "GitHub Action is not pinned to a full commit SHA: $workflow: $use_ref" >&2
      failed=1
    fi
  done < <(sed -nE 's/^[[:space:]]*-[[:space:]]+uses:[[:space:]]+([^ #]+).*/\1/p' "$workflow")

  while IFS= read -r image_ref; do
    if [[ ! "$image_ref" =~ @sha256:[0-9a-f]{64}$ ]]; then
      echo "Workflow service image is not digest pinned: $workflow: $image_ref" >&2
      failed=1
    fi
  done < <(sed -nE 's/^[[:space:]]+image:[[:space:]]+([^ #]+).*/\1/p' "$workflow")
done < <(find "$repo_root/.github/workflows" -type f \( -name '*.yml' -o -name '*.yaml' \) -print | LC_ALL=C sort)

while IFS= read -r dockerfile; do
  while IFS= read -r image_ref; do
    if [[ ! "$image_ref" =~ @sha256:[0-9a-f]{64}$ ]]; then
      echo "Docker base image is not digest pinned: $dockerfile: $image_ref" >&2
      failed=1
    fi
  done < <(sed -nE 's/^FROM[[:space:]]+([^[:space:]]+).*/\1/p' "$dockerfile")
done < <(find "$repo_root" -type f -name Dockerfile \
  -not -path "$repo_root/.git/*" -not -path "$repo_root/.claude/*" -not -path "$repo_root/.sso-wt/*" \
  -not -path '*/node_modules/*' -not -path '*/bin/*' -not -path '*/obj/*' \
  -print | LC_ALL=C sort)

exit "$failed"

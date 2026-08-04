#!/usr/bin/env bash
set -euo pipefail
umask 077

usage() {
  cat <<'USAGE'
Usage: tools/collect-release-candidate-provenance.sh --output DIR [options]

Creates a deterministic, secret-free release-candidate provenance bundle.
It records the exact Git commit/tree, source archive digest, migration and lockfile
hash manifests, and (when supplied) local image IDs, registry digests and CycloneDX
SBOMs. A local image ID proves what was built; only a registry RepoDigest proves the
immutable deployable reference.

Options:
  --output DIR              New or empty output directory (required)
  --image COMPONENT=REF     Inspect image and generate SBOM; repeatable
  --published-digest COMPONENT=REF@sha256:HEX
                             Record an externally published immutable reference
  --allow-dirty             Record a dirty worktree instead of failing
  --require-registry-digest Fail if any supplied image lacks a RepoDigest
  --help                    Show this help
USAGE
}

repo_root=$(git rev-parse --show-toplevel 2>/dev/null || true)
[ -n "$repo_root" ] || { echo "ERROR: run from a Git worktree" >&2; exit 2; }
cd "$repo_root"

output_dir=""
allow_dirty=false
require_registry_digest=false
images=()
published_digests=()
while [ "$#" -gt 0 ]; do
  case "$1" in
    --output) [ "$#" -ge 2 ] || { echo "ERROR: --output requires a directory" >&2; exit 2; }; output_dir=$2; shift 2 ;;
    --image) [ "$#" -ge 2 ] || { echo "ERROR: --image requires COMPONENT=REF" >&2; exit 2; }; images+=("$2"); shift 2 ;;
    --published-digest) [ "$#" -ge 2 ] || { echo "ERROR: --published-digest requires COMPONENT=REF@sha256:HEX" >&2; exit 2; }; published_digests+=("$2"); shift 2 ;;
    --allow-dirty) allow_dirty=true; shift ;;
    --require-registry-digest) require_registry_digest=true; shift ;;
    --help|-h) usage; exit 0 ;;
    *) echo "ERROR: unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done

[ -n "$output_dir" ] || { echo "ERROR: --output is required" >&2; exit 2; }
case "$output_dir" in ""|/|.|..) echo "ERROR: unsafe output directory" >&2; exit 2;; esac
[ ! -L "$output_dir" ] || { echo "ERROR: output directory must not be a symbolic link" >&2; exit 2; }
if [ -d "$output_dir" ] && [ -n "$(find "$output_dir" -mindepth 1 -maxdepth 1 -print -quit)" ]; then
  echo "ERROR: output directory already contains provenance" >&2
  exit 2
fi
sha256_stream() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum | awk '{print $1}'; else shasum -a 256 | awk '{print $1}'; fi
}
sha256_file() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | awk '{print $1}'; else shasum -a 256 "$1" | awk '{print $1}'; fi
}

commit_sha=$(git rev-parse HEAD)
tree_sha=$(git rev-parse 'HEAD^{tree}')
commit_time=$(git show -s --format=%cI HEAD)
worktree_status=$(git status --porcelain=v1)
if [ -n "$worktree_status" ]; then dirty_count=$(printf '%s\n' "$worktree_status" | wc -l | tr -d ' '); else dirty_count=0; fi
if [ "$dirty_count" != "0" ] && [ "$allow_dirty" = false ]; then
  echo "ERROR: release candidate worktree is dirty (${dirty_count} paths)" >&2
  exit 1
fi
mkdir -p "$output_dir/sboms"
if [ -n "$worktree_status" ]; then printf '%s\n' "$worktree_status"; fi > "$output_dir/worktree-status.txt"

git archive --format=tar HEAD | sha256_stream > "$output_dir/source-archive.sha256"

find database/migrations -type f -name '*.sql' -print | LC_ALL=C sort | while IFS= read -r path; do
  printf '%s  %s\n' "$(sha256_file "$path")" "$path"
done > "$output_dir/migrations.sha256"

{
  git ls-files '*package-lock.json' '*.csproj' '*.sln' | LC_ALL=C sort
} | while IFS= read -r path; do
  [ -f "$path" ] && printf '%s  %s\n' "$(sha256_file "$path")" "$path"
done > "$output_dir/dependencies.sha256"

printf 'component\timage_reference\tlocal_image_id\tlocal_repo_digest\tpublished_registry_digest\tsbom_file\tsbom_sha256\tcomponent_count\n' > "$output_dir/images.tsv"
scanner='aquasec/trivy:0.70.0@sha256:be1190afcb28352bfddc4ddeb71470835d16462af68d310f9f4bca710961a41e'
if [ "${#images[@]}" -gt 0 ]; then
for spec in "${images[@]}"; do
  component=${spec%%=*}
  image_ref=${spec#*=}
  if [ -z "$component" ] || [ -z "$image_ref" ] || [ "$component" = "$spec" ]; then
    echo "ERROR: invalid --image value '$spec'; expected COMPONENT=REF" >&2
    exit 2
  fi
  case "$component" in *[!A-Za-z0-9._-]*|'') echo "ERROR: unsafe image component '$component'" >&2; exit 2;; esac
  case "$image_ref" in -*|*$'\n'*|*$'\r'*|*$'\t'*) echo "ERROR: unsafe image reference" >&2; exit 2;; esac
  command -v docker >/dev/null 2>&1 || { echo "ERROR: docker is required for --image" >&2; exit 1; }
  command -v jq >/dev/null 2>&1 || { echo "ERROR: jq is required to verify CycloneDX SBOMs" >&2; exit 1; }
  local_id=$(docker image inspect --format '{{.Id}}' "$image_ref")
  local_repo_digest=$(docker image inspect --format '{{join .RepoDigests ","}}' "$image_ref")
  [ -n "$local_repo_digest" ] || local_repo_digest='NOT_AVAILABLE_LOCAL_BUILD'
  published_registry_digest='NOT_EVIDENCED'
  if [ "${#published_digests[@]}" -gt 0 ]; then
    for published_spec in "${published_digests[@]}"; do
      if [ "${published_spec%%=*}" = "$component" ]; then published_registry_digest=${published_spec#*=}; fi
    done
  fi
  if [ "$published_registry_digest" != 'NOT_EVIDENCED' ] && ! printf '%s' "$published_registry_digest" | grep -Eq '^.+@sha256:[0-9a-f]{64}$'; then
    echo "ERROR: invalid published digest for '$component'" >&2
    exit 2
  fi
  if [ "$require_registry_digest" = true ] && [ "$published_registry_digest" = 'NOT_EVIDENCED' ]; then
    echo "ERROR: image '$image_ref' has no evidenced published registry digest" >&2
    exit 1
  fi
  sbom_file="sboms/${component}.cyclonedx.json"
  docker run --rm -v /var/run/docker.sock:/var/run/docker.sock "$scanner" \
    image --quiet --format cyclonedx "$image_ref" > "$output_dir/$sbom_file"
  grep -Eq '"bomFormat"[[:space:]]*:[[:space:]]*"CycloneDX"' "$output_dir/$sbom_file" || {
    echo "ERROR: invalid CycloneDX SBOM for '$image_ref'" >&2; exit 1;
  }
  component_count=$(jq -e '.bomFormat == "CycloneDX" and ((.components | type) == "array") and ((.components | length) > 0)' "$output_dir/$sbom_file" >/dev/null && jq '.components | length' "$output_dir/$sbom_file")
  post_scan_id=$(docker image inspect --format '{{.Id}}' "$image_ref")
  [ "$post_scan_id" = "$local_id" ] || { echo "ERROR: image '$image_ref' changed while its SBOM was generated" >&2; exit 1; }
  printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
    "$component" "$image_ref" "$local_id" "$local_repo_digest" "$published_registry_digest" "$sbom_file" \
    "$(sha256_file "$output_dir/$sbom_file")" "$component_count" >> "$output_dir/images.tsv"
done
fi

{
  printf 'field\tvalue\n'
  printf 'format_version\t1\n'
  printf 'commit_sha\t%s\n' "$commit_sha"
  printf 'tree_sha\t%s\n' "$tree_sha"
  printf 'commit_time\t%s\n' "$commit_time"
  printf 'dirty_path_count\t%s\n' "$dirty_count"
  printf 'worktree_status_sha256\t%s\n' "$(sha256_file "$output_dir/worktree-status.txt")"
  printf 'source_archive_sha256\t%s\n' "$(cat "$output_dir/source-archive.sha256")"
  printf 'migration_manifest_sha256\t%s\n' "$(sha256_file "$output_dir/migrations.sha256")"
  printf 'dependency_manifest_sha256\t%s\n' "$(sha256_file "$output_dir/dependencies.sha256")"
  printf 'image_manifest_sha256\t%s\n' "$(sha256_file "$output_dir/images.tsv")"
} > "$output_dir/candidate.tsv"

(
  cd "$output_dir"
  find . -type f ! -name bundle.sha256 -print | LC_ALL=C sort | while IFS= read -r artifact; do
    if command -v sha256sum >/dev/null 2>&1; then sha256sum "$artifact"; else shasum -a 256 "$artifact"; fi
  done
) > "$output_dir/bundle.sha256"

echo "Release-candidate provenance written to: $output_dir"

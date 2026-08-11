#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
validator="$repo_root/tools/validate-mandatory-ci-gates.sh"
fixture="$(mktemp)"
trap 'rm -f "$fixture"' EXIT

write_ledger() {
  cat > "$fixture" <<'EOF'
gate	result
frontend-build	success
node-backend-build	success
demo-node-events-check	success
mobile-build-test	success
launch-tooling-tests	success
playwright-public-tests	success
dotnet-build-test	success
dotnet-integration-tests	success
production-shaped-release-rehearsal	success
release-container-builds	success
EOF
}

write_ledger
"$validator" --require-success "$fixture"

sed -i.bak 's/release-container-builds\tsuccess/release-container-builds\tfailure/' "$fixture"
if "$validator" --require-success "$fixture" 2>/dev/null; then
  echo "Validator accepted a failed mandatory gate" >&2
  exit 1
fi
"$validator" "$fixture"

write_ledger
sed -i.bak '/dotnet-integration-tests/d' "$fixture"
if "$validator" "$fixture" 2>/dev/null; then
  echo "Validator accepted an omitted mandatory gate" >&2
  exit 1
fi

write_ledger
printf 'frontend-build\tsuccess\n' >> "$fixture"
if "$validator" "$fixture" 2>/dev/null; then
  echo "Validator accepted a duplicate mandatory gate" >&2
  exit 1
fi

echo "Mandatory CI gate ledger contract tests passed"

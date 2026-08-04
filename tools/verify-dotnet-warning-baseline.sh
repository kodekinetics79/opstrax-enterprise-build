#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
baseline="${DOTNET_WARNING_BASELINE:-$repo_root/tools/dotnet-warning-baseline.tsv}"
project="${DOTNET_WARNING_PROJECT:-$repo_root/backend-dotnet.Tests/Opstrax.Tests.csproj}"
configuration="${DOTNET_WARNING_CONFIGURATION:-Release}"
build_log="$(mktemp)"
warning_lines="$(mktemp)"
counts="$(mktemp)"
trap 'rm -f "$build_log" "$warning_lines" "$counts"' EXIT

test -f "$baseline" || { echo "Missing warning baseline: $baseline" >&2; exit 2; }

set +e
dotnet build "$project" --no-restore --configuration "$configuration" \
  --target:Rebuild --consoleLoggerParameters:NoSummary 2>&1 | tee "$build_log"
build_status=${PIPESTATUS[0]}
set -e
test "$build_status" -eq 0 || exit "$build_status"

# MSBuild can print the same project warning more than once during a graph rebuild.
# Normalize the checkout path and count each diagnostic location only once.
grep -E 'warning [A-Za-z]+[0-9]+:' "$build_log" \
  | sed "s#${repo_root}/##g" \
  | LC_ALL=C sort -u > "$warning_lines" || true
grep -Eo 'warning [A-Za-z]+[0-9]+:' "$warning_lines" \
  | sed -E 's/warning ([A-Za-z]+[0-9]+):/\1/' \
  | LC_ALL=C sort | uniq -c \
  | awk '{ print $2 "\t" $1 }' > "$counts" || true

observed_total="$(wc -l < "$warning_lines" | tr -d ' ')"
baseline_total="$(awk -F '\t' '$1 == "TOTAL" { print $2 }' "$baseline")"
test -n "$baseline_total" || { echo "Baseline has no TOTAL row" >&2; exit 2; }

failed=0
while IFS=$'\t' read -r code observed; do
  maximum="$(awk -F '\t' -v code="$code" '$1 == code { print $2 }' "$baseline")"
  if [[ -z "$maximum" ]]; then
    echo "New warning code is not baselined: $code ($observed distinct warnings)" >&2
    failed=1
  elif (( observed > maximum )); then
    echo "Warning debt increased for $code: $observed > baseline $maximum" >&2
    failed=1
  fi
done < "$counts"

if (( observed_total > baseline_total )); then
  echo "Total warning debt increased: $observed_total > baseline $baseline_total" >&2
  failed=1
fi

echo "Distinct .NET warnings: $observed_total (baseline ceiling: $baseline_total)"
column -t -s $'\t' "$counts" 2>/dev/null || cat "$counts"
if (( observed_total < baseline_total )); then
  echo "Warning debt decreased; ratchet tools/dotnet-warning-baseline.tsv downward." >&2
fi
exit "$failed"

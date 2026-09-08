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
DOTNET_CLI_UI_LANGUAGE=en dotnet build "$project" --no-restore --configuration "$configuration" \
  --target:Rebuild --verbosity:minimal --tl:off \
  '--consoleLoggerParameters:Summary;DisableConsoleColor' 2>&1 | tee "$build_log"
pipeline_status=("${PIPESTATUS[@]}")
set -e
test "${pipeline_status[0]}" -eq 0 || exit "${pipeline_status[0]}"
if [[ "${pipeline_status[1]}" -ne 0 ]]; then
  echo "Build log capture failed; warning debt was not measured." >&2
  exit "${pipeline_status[1]}"
fi

# An interrupted dotnet child can return zero before MSBuild has completed. A
# missing/partial log is not a zero-warning build. Require the full classic
# English success trailer, including zero errors, before measuring any debt.
# The summary may repeat diagnostics between its success marker and counts.
if ! awk '
  { sub(/\r$/, "") }
  /^Build (FAILED|canceled|cancelled)\.$/ || /(^|:[[:space:]])error [A-Za-z]+[0-9]+:/ { invalid = 1 }
  {
    diagnostic = ($0 ~ /warning [A-Za-z]+[0-9]+:/)
    if (diagnostic) diagnostics++
  }
  /^Build succeeded\.$/ {
    if (phase != 0) invalid = 1
    successes++
    phase = 1
    next
  }
  /^[[:space:]]*$/ { next }
  phase == 1 && diagnostic { next }
  phase == 1 && /^[[:space:]]*[0-9]+ Warning\(s\)[[:space:]]*$/ {
    summary_warnings = $1 + 0
    phase = 2
    next
  }
  phase == 2 && /^[[:space:]]*0 Error\(s\)[[:space:]]*$/ { phase = 3; next }
  phase == 3 && /^Time Elapsed [0-9]+:[0-9][0-9]:[0-9][0-9](\.[0-9]+)?[[:space:]]*$/ {
    phase = 4
    next
  }
  phase != 0 { invalid = 1 }
  END {
    if (invalid || successes != 1 || phase != 4 || summary_warnings > diagnostics ||
        (summary_warnings == 0 && diagnostics != 0)) exit 1
  }
' "$build_log"; then
  echo "Build completion evidence is missing, incomplete, or inconsistent; warning debt was not measured." >&2
  exit 2
fi

# MSBuild can print the same project warning more than once during a graph rebuild.
# Normalize the checkout path and count each diagnostic location only once.
# Unlike grep, awk returns success for a legitimate no-match result, so capture
# and processing failures can propagate through pipefail instead of `|| true`.
awk '/warning [A-Za-z]+[0-9]+:/ { sub(/\r$/, ""); print }' "$build_log" \
  | sed "s#${repo_root}/##g" \
  | LC_ALL=C sort -u > "$warning_lines"
awk '{
  while (match($0, /warning [A-Za-z]+[0-9]+:/)) {
    print substr($0, RSTART + 8, RLENGTH - 9)
    $0 = substr($0, RSTART + RLENGTH)
  }
}' "$warning_lines" \
  | LC_ALL=C sort | uniq -c \
  | awk '{ print $2 "\t" $1 }' > "$counts"

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

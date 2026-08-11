#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "usage: $0 [--require-success] <mandatory-ci-gates.tsv>" >&2
}

require_success=false
if [[ "${1:-}" == "--require-success" ]]; then
  require_success=true
  shift
fi
[[ $# -eq 1 ]] || { usage; exit 2; }
ledger="$1"
test -f "$ledger" || { echo "Mandatory gate ledger not found: $ledger" >&2; exit 2; }

awk -F '\t' -v require_success="$require_success" '
  BEGIN {
    expected["frontend-build"] = 1
    expected["node-backend-build"] = 1
    expected["demo-node-events-check"] = 1
    expected["mobile-build-test"] = 1
    expected["launch-tooling-tests"] = 1
    expected["playwright-public-tests"] = 1
    expected["dotnet-build-test"] = 1
    expected["dotnet-integration-tests"] = 1
    expected["production-shaped-release-rehearsal"] = 1
    expected["release-container-builds"] = 1
  }
  NR == 1 {
    if ($1 != "gate" || $2 != "result" || NF != 2) {
      print "Mandatory gate ledger has an invalid header" > "/dev/stderr"
      failed = 1
    }
    next
  }
  {
    gate = $1
    result = $2
    if (NF != 2 || gate == "" || result == "") {
      print "Malformed mandatory gate row: " gate > "/dev/stderr"
      failed = 1
      next
    }
    if (!(gate in expected)) {
      print "Unexpected mandatory gate: " gate > "/dev/stderr"
      failed = 1
    }
    if (seen[gate]++) {
      print "Duplicate mandatory gate: " gate > "/dev/stderr"
      failed = 1
    }
    if (result != "success" && result != "failure" && result != "cancelled" && result != "skipped") {
      print "Invalid result " result " for mandatory gate " gate > "/dev/stderr"
      failed = 1
    }
    if (require_success == "true" && result != "success") {
      print "Mandatory gate did not succeed: " gate " (" result ")" > "/dev/stderr"
      failed = 1
    }
  }
  END {
    for (gate in expected) {
      if (!(gate in seen)) {
        print "Missing mandatory gate: " gate > "/dev/stderr"
        failed = 1
      }
    }
    exit failed
  }
' "$ledger"

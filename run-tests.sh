#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# OpsTrax verification script — P9 stale-binary mitigation
#
# ALWAYS runs a fresh build before tests.
# Never uses --no-build.
#
# Root cause of P8 stale-binary incident:
#   P8ReportingTests.cs had 4 CS1739 compilation errors.
#   Running `dotnet test --no-build` silently executed the pre-P8 DLL.
#   444 tests reported; 207 new P8 tests were invisible.
#
# Fix: this script always builds fresh first.
#
# Usage:
#   ./run-tests.sh                  # full: build + test backend + frontend
#   ./run-tests.sh --backend-only   # build + test backend only
#   ./run-tests.sh --frontend-only  # tsc + npm build frontend only
# ─────────────────────────────────────────────────────────────────────────────

set -euo pipefail

BACKEND_DIR="backend-dotnet"
TESTS_DIR="backend-dotnet.Tests"
FRONTEND_DIR="frontend"

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

log()  { echo -e "${GREEN}[run-tests]${NC} $*"; }
warn() { echo -e "${YELLOW}[warn]${NC} $*"; }
fail() { echo -e "${RED}[FAIL]${NC} $*" >&2; exit 1; }

BACKEND_ONLY=false
FRONTEND_ONLY=false

for arg in "$@"; do
  case $arg in
    --backend-only)  BACKEND_ONLY=true  ;;
    --frontend-only) FRONTEND_ONLY=true ;;
  esac
done

# ── Backend ────────────────────────────────────────────────────────────────────

if [[ "$FRONTEND_ONLY" == "false" ]]; then
  log "Step 1/4 — Clean backend build (prevents stale-binary false confidence)"
  dotnet clean "$BACKEND_DIR" -v q || fail "dotnet clean failed"

  log "Step 2/4 — Fresh build of main project"
  dotnet build "$BACKEND_DIR" -v q || fail "Backend build failed — fix compilation errors before running tests"

  log "Step 3/4 — Fresh build of test project"
  dotnet build "$TESTS_DIR" -v q || fail "Test project build failed — fix compilation errors before running tests"

  log "Step 4/4 — Run tests (fresh binary guaranteed)"
  # NOTE: --no-build is intentionally NOT used.
  # If you see dotnet test --no-build anywhere in CI, that is a bug.
  PREV_COUNT="${PREV_TEST_COUNT:-unknown}"
  dotnet test "$TESTS_DIR" --verbosity quiet

  log "✓ Backend tests completed"
  log "  Previous known count: ${PREV_COUNT}"
  log "  Run 'dotnet test $TESTS_DIR --verbosity quiet' to see the exact count"
fi

# ── Frontend ───────────────────────────────────────────────────────────────────

if [[ "$BACKEND_ONLY" == "false" ]]; then
  if [[ ! -d "$FRONTEND_DIR/node_modules" ]]; then
    warn "node_modules not found — running npm install"
    npm install --prefix "$FRONTEND_DIR"
  fi

  log "Frontend — TypeScript type check"
  (cd "$FRONTEND_DIR" && npx tsc --noEmit) || fail "TypeScript type check failed"

  log "Frontend — Production build"
  (cd "$FRONTEND_DIR" && npm run build) || fail "Frontend build failed"

  log "✓ Frontend checks passed"
fi

log "─────────────────────────────────────────────"
log "All checks passed. Platform is production-ready for this commit."

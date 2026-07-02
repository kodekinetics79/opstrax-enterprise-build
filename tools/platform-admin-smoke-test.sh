#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Platform Admin control-plane smoke test.
#
# Proves, against a RUNNING API, that the platform control plane is functional
# and safe: login, tenant lifecycle on a TEMPORARY tenant only, entitlement
# toggle, session revocation, tenant-token isolation, audit trail, and the
# protected offboarding cleanup. Exits non-zero on the first failure.
#
# SAFETY RULES
#   * Never prints passwords or tokens.
#   * Only mutates the temporary tenant it creates (unique SMOKE-* code).
#   * Existing tenants (e.g. Acme) are touched READ-ONLY.
#
# CONFIG (env)
#   API_BASE                 default http://localhost:8088
#   PLATFORM_SMOKE_EMAIL     default platform@opstrax.io  (local dev identity)
#   PLATFORM_SMOKE_PASSWORD  default the local dev default; MUST be set for any
#                            non-local environment.
#   PSQL_CMD                 how to reach the API's database with psql, used ONLY
#                            to mint a throwaway tenant session for the isolation
#                            + revocation checks. Default targets the local dev
#                            stack: "docker exec -i zayra_pg psql -U zayra -d opstrax_local"
#                            Set SKIP_TENANT_TOKEN_CHECK=1 to skip those steps.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

API_BASE="${API_BASE:-http://localhost:8088}"
PLATFORM_SMOKE_EMAIL="${PLATFORM_SMOKE_EMAIL:-platform@opstrax.io}"
PLATFORM_SMOKE_PASSWORD="${PLATFORM_SMOKE_PASSWORD:-Platform@12345}"
PSQL_CMD="${PSQL_CMD:-docker exec -i zayra_pg psql -U zayra -d opstrax_local -tA}"
SKIP_TENANT_TOKEN_CHECK="${SKIP_TENANT_TOKEN_CHECK:-0}"

COOKIES="$(mktemp)"
BODY="$(mktemp)"
trap 'rm -f "$COOKIES" "$BODY"' EXIT

PASS=0; FAIL=0
step() { printf '── %s\n' "$1"; }
ok()   { PASS=$((PASS+1)); printf '   ✅ %s\n' "$1"; }
die()  { printf '   ❌ %s\n' "$1"; exit 1; }

# request METHOD PATH [JSON_BODY] [EXTRA_TOKEN] -> sets HTTP_STATUS, body in $BODY
CSRF=""
PLATFORM_TOKEN=""
request() {
  local method="$1" path="$2" data="${3:-}" token="${4:-$PLATFORM_TOKEN}"
  local args=(-sS -o "$BODY" -w '%{http_code}' -X "$method" "$API_BASE$path"
              -H 'Accept: application/json' -b "$COOKIES" -c "$COOKIES" -D "$BODY.hdr")
  [ -n "$token" ] && args+=(-H "Authorization: Bearer $token")
  [ -n "$CSRF" ]  && args+=(-H "X-CSRF-Token: $CSRF")
  [ -n "$data" ]  && args+=(-H 'Content-Type: application/json' --data "$data")
  HTTP_STATUS="$(curl "${args[@]}")"
  # capture the CSRF token echoed on every response (double-submit pattern)
  local next_csrf
  next_csrf="$(sed -n 's/^[Xx]-[Cc][Ss][Rr][Ff]-[Tt]oken: *//p' "$BODY.hdr" | tr -d '\r' | tail -1)"
  [ -n "$next_csrf" ] && CSRF="$next_csrf"
}

json() { python3 -c "import json,sys;d=json.load(open('$BODY'));print(eval(sys.argv[1]))" "$1" 2>/dev/null; }

echo "Platform Admin smoke test → $API_BASE"

# ── 0. Prime CSRF cookie ─────────────────────────────────────────────────────
request GET /health
[ "$HTTP_STATUS" = "200" ] || die "API health check failed ($HTTP_STATUS)"

# ── 1. Platform login (credentials/token never printed) ─────────────────────
step "1. Platform admin login"
request POST /api/platform/auth/login \
  "$(python3 -c "import json,os;print(json.dumps({'email':os.environ['PLATFORM_SMOKE_EMAIL'],'password':os.environ['PLATFORM_SMOKE_PASSWORD']}))" 2>/dev/null || printf '{"email":"%s","password":"%s"}' "$PLATFORM_SMOKE_EMAIL" "$PLATFORM_SMOKE_PASSWORD")" ""
[ "$HTTP_STATUS" = "200" ] || die "platform login failed ($HTTP_STATUS)"
PLATFORM_TOKEN="$(json "d['data']['token']")"
[ -n "$PLATFORM_TOKEN" ] || die "no token in login response"
ok "logged in (token withheld from output)"

# ── 2. Unauthenticated platform access is rejected ───────────────────────────
step "2. Unauthenticated access rejected"
request GET /api/platform/tenants "" "-"
# "-" is not a valid session token; expect 401
[ "$HTTP_STATUS" = "401" ] || die "expected 401 for bad token, got $HTTP_STATUS"
ok "invalid token → 401"

# ── 3. Tenant list ────────────────────────────────────────────────────────────
step "3. Tenant list"
request GET /api/platform/tenants
[ "$HTTP_STATUS" = "200" ] || die "tenant list failed ($HTTP_STATUS)"
TENANT_COUNT="$(json "len(d['data'])")"
ok "listed $TENANT_COUNT tenants"

# ── 4. Acme tenant detail (READ-ONLY) ────────────────────────────────────────
step "4. Acme tenant detail (read-only)"
ACME_ID="$(json "[t['id'] for t in d['data'] if 'acme' in str(t.get('name','')).lower()][0]" || true)"
if [ -n "$ACME_ID" ]; then
  request GET "/api/platform/tenants/$ACME_ID"
  [ "$HTTP_STATUS" = "200" ] || die "Acme detail failed ($HTTP_STATUS)"
  ok "Acme tenant (id=$ACME_ID) detail loads"
else
  ok "no Acme tenant in this environment — skipped (non-fatal)"
fi

# ── 5. Create TEMPORARY tenant ────────────────────────────────────────────────
step "5. Create temporary tenant"
SMOKE_CODE="SMOKE-$(date +%s)-$RANDOM"
request POST /api/platform/tenants "{\"name\":\"Smoke Test Tenant $SMOKE_CODE\",\"companyCode\":\"$SMOKE_CODE\",\"seatLimit\":3,\"status\":\"trial\"}"
[ "$HTTP_STATUS" = "200" ] || die "tenant create failed ($HTTP_STATUS): $(cat "$BODY")"
TID="$(json "d['data']['id']")"
[ -n "$TID" ] || die "no tenant id returned"
ok "created temp tenant id=$TID code=$SMOKE_CODE"

# duplicate code must 409
request POST /api/platform/tenants "{\"name\":\"Dup\",\"companyCode\":\"$SMOKE_CODE\"}"
[ "$HTTP_STATUS" = "409" ] || die "duplicate code expected 409, got $HTTP_STATUS"
ok "duplicate tenant code → 409"

# ── 6. Update temp tenant ─────────────────────────────────────────────────────
step "6. Update temp tenant (seat limit)"
request PUT "/api/platform/tenants/$TID" '{"seatLimit":7}'
[ "$HTTP_STATUS" = "200" ] || die "tenant update failed ($HTTP_STATUS)"
ok "seat limit updated"

# ── 7. Toggle safe feature flag ───────────────────────────────────────────────
step "7. Feature flag toggle (reports off/on)"
request PUT "/api/platform/tenants/$TID/entitlements" '{"moduleKey":"reports","enabled":false}'
[ "$HTTP_STATUS" = "200" ] || die "entitlement disable failed ($HTTP_STATUS)"
request PUT "/api/platform/tenants/$TID/entitlements" '{"moduleKey":"reports","enabled":true}'
[ "$HTTP_STATUS" = "200" ] || die "entitlement re-enable failed ($HTTP_STATUS)"
# invalid module key must 400
request PUT "/api/platform/tenants/$TID/entitlements" '{"moduleKey":"NOT A KEY;--","enabled":false}'
[ "$HTTP_STATUS" = "400" ] || die "invalid module key expected 400, got $HTTP_STATUS"
ok "flag off/on OK; invalid key → 400"

# ── 8. Safe status change ─────────────────────────────────────────────────────
step "8. Status change (extend trial)"
request POST "/api/platform/tenants/$TID/status" '{"action":"extend-trial","days":7}'
[ "$HTTP_STATUS" = "200" ] || die "extend-trial failed ($HTTP_STATUS)"
ok "trial extended"

# ── mint a throwaway tenant user session (for steps 9/10/13) ─────────────────
TENANT_TOKEN=""
if [ "$SKIP_TENANT_TOKEN_CHECK" != "1" ]; then
  step "9-pre. Mint throwaway tenant session via DB (token withheld)"
  TENANT_TOKEN="smoke-$(openssl rand -hex 16)"
  if $PSQL_CMD >/dev/null 2>&1 <<SQL
INSERT INTO users (company_id, full_name, email, role_name, status)
VALUES ($TID, 'Smoke User', 'smoke-$SMOKE_CODE@opstrax.test', 'Company Admin', 'Active');
INSERT INTO user_sessions (user_id, company_id, session_token, expires_at)
SELECT id, $TID, '$TENANT_TOKEN', NOW() + INTERVAL '1 hour'
FROM users WHERE email = 'smoke-$SMOKE_CODE@opstrax.test';
SQL
  then
    ok "tenant session minted"
  else
    echo "   ⚠️  PSQL_CMD unavailable — skipping tenant-token checks (set SKIP_TENANT_TOKEN_CHECK=1 to silence)"
    TENANT_TOKEN=""
  fi
fi

# ── 9. Suspend → sessions revoked ────────────────────────────────────────────
step "9. Suspend temp tenant"
request POST "/api/platform/tenants/$TID/status" '{"action":"suspend"}'
[ "$HTTP_STATUS" = "200" ] || die "suspend failed ($HTTP_STATUS)"
ok "suspended"

# ── 10. Verify session revocation ────────────────────────────────────────────
if [ -n "$TENANT_TOKEN" ]; then
  step "10. Suspended tenant session is dead"
  request GET /api/auth/me "" "$TENANT_TOKEN"
  [ "$HTTP_STATUS" = "401" ] || die "revoked tenant session still worked ($HTTP_STATUS)"
  ok "revoked session → 401"
fi

# ── 11. Reactivate ────────────────────────────────────────────────────────────
step "11. Reactivate temp tenant"
request POST "/api/platform/tenants/$TID/status" '{"action":"reactivate"}'
[ "$HTTP_STATUS" = "200" ] || die "reactivate failed ($HTTP_STATUS)"
ok "reactivated"

# ── 12/13. Tenant token blocked from platform routes ─────────────────────────
if [ -n "$TENANT_TOKEN" ]; then
  step "12. Tenant token blocked from platform plane"
  # mint a fresh session (previous was revoked by suspend)
  TENANT_TOKEN2="smoke-$(openssl rand -hex 16)"
  $PSQL_CMD >/dev/null 2>&1 <<SQL || true
INSERT INTO user_sessions (user_id, company_id, session_token, expires_at)
SELECT id, $TID, '$TENANT_TOKEN2', NOW() + INTERVAL '1 hour'
FROM users WHERE email = 'smoke-$SMOKE_CODE@opstrax.test';
SQL
  request GET /api/auth/me "" "$TENANT_TOKEN2"
  [ "$HTTP_STATUS" = "200" ] || die "fresh tenant session should work on tenant API ($HTTP_STATUS)"
  request GET /api/platform/tenants "" "$TENANT_TOKEN2"
  [ "$HTTP_STATUS" = "401" ] || die "tenant token reached platform route ($HTTP_STATUS)"
  ok "tenant token valid on tenant API, 401 on platform route"

  step "13. Explicit revoke-sessions endpoint"
  request POST "/api/platform/tenants/$TID/revoke-sessions"
  [ "$HTTP_STATUS" = "200" ] || die "revoke-sessions failed ($HTTP_STATUS)"
  request GET /api/auth/me "" "$TENANT_TOKEN2"
  [ "$HTTP_STATUS" = "401" ] || die "session survived explicit revoke ($HTTP_STATUS)"
  ok "explicit revoke kills live session"
fi

# ── 14. Audit trail exists for everything above ───────────────────────────────
step "14. Audit trail"
request GET "/api/platform/tenants/$TID/audit"
[ "$HTTP_STATUS" = "200" ] || die "tenant audit fetch failed ($HTTP_STATUS)"
AUDIT_ACTIONS="$(json "sorted({r['action'] for r in d['data']})")"
echo "   actions: $AUDIT_ACTIONS"
for expected in tenant.created tenant.updated tenant.suspend tenant.reactivate; do
  json "0 if '$expected' in {r['action'] for r in d['data']} else (_ for _ in ()).throw(SystemExit(1))" >/dev/null \
    || die "missing audit action: $expected"
done
ok "audit rows present for create/update/suspend/reactivate"

# ── 15. Cleanup: offboard the TEMP tenant via the protected workflow ─────────
step "15. Offboard temp tenant (typed-confirm workflow)"
# unconfirmed delete must be rejected
request DELETE "/api/platform/tenants/$TID" '{}'
[ "$HTTP_STATUS" = "400" ] || die "unconfirmed delete expected 400, got $HTTP_STATUS"
request DELETE "/api/platform/tenants/$TID" "{\"confirm\":\"$SMOKE_CODE\"}"
[ "$HTTP_STATUS" = "200" ] || die "offboard failed ($HTTP_STATUS): $(cat "$BODY")"
request GET "/api/platform/tenants/$TID"
[ "$HTTP_STATUS" = "404" ] || die "tenant still exists after offboard ($HTTP_STATUS)"
ok "temp tenant fully offboarded (confirm token enforced)"

echo
echo "✅ PLATFORM ADMIN SMOKE TEST PASSED ($PASS checks)"

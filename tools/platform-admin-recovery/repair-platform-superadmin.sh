#!/usr/bin/env bash
#
# One-shot recovery for the bootstrap Platform Super Admin, run directly against the
# control-plane database. Use it when /platform/login rejects a credential you know is
# correct and you cannot wait for a deploy.
#
# It is the same repair PlatformSuperAdminReconciler performs on boot:
#   • ensures the platform_super_admin role exists and carries the platform:* grant
#   • creates the operator if absent, or repairs them (Active, super role, invite cleared)
#   • installs the password you supply, using the API's own PBKDF2 format
#   • revokes existing platform sessions, and writes the audit row (including the
#     credential fingerprint) so a later deploy sees the account as already in sync
#
# IMPORTANT: set PLATFORM_PASSWORD to the SAME value you put in Render's
# PLATFORM_SUPERADMIN_PASSWORD. If they differ, the next deploy applies the Render value.
#
# Usage:
#   PG_URL='postgresql://USER:PASS@HOST/DB?sslmode=require' \
#   PLATFORM_EMAIL='zack@kodekinetics.com' \
#   PLATFORM_PASSWORD='<the password you will also set in Render>' \
#   ./repair-platform-superadmin.sh            # preflight only, changes nothing
#
#   ...same env... APPLY=1 ./repair-platform-superadmin.sh   # perform the repair
#
set -euo pipefail

: "${PG_URL:?set PG_URL to the control-plane Postgres connection string}"
: "${PLATFORM_EMAIL:?set PLATFORM_EMAIL to the operator email that should be able to sign in}"
: "${PLATFORM_PASSWORD:?set PLATFORM_PASSWORD to the password to install}"
APPLY="${APPLY:-0}"

# Same floor the API enforces — refuse to install a credential the app would reject.
if [ "${#PLATFORM_PASSWORD}" -lt 12 ] \
   || ! printf '%s' "$PLATFORM_PASSWORD" | grep -q '[A-Za-z]' \
   || ! printf '%s' "$PLATFORM_PASSWORD" | grep -q '[0-9]'; then
  echo "ERROR: password must be at least 12 characters and contain a letter and a digit." >&2
  exit 1
fi

# PBKDF2$100000$<b64 salt>$<b64 subkey>, PBKDF2-HMAC-SHA256, 16-byte salt, 32-byte subkey —
# byte-for-byte the format PlatformSchemaService.HashPassword produces and the login path
# verifies. The fingerprint matches PlatformSuperAdminReconciler.CredentialFingerprint.
read -r PW_HASH FINGERPRINT <<<"$(python3 - <<'PY'
import base64, hashlib, os
password = os.environ["PLATFORM_PASSWORD"]
email = os.environ["PLATFORM_EMAIL"].strip().lower()

salt = os.urandom(16)
subkey = hashlib.pbkdf2_hmac("sha256", password.encode(), salt, 100_000, 32)
pw_hash = "PBKDF2$100000$%s$%s" % (
    base64.b64encode(salt).decode(), base64.b64encode(subkey).decode())

fp_salt = hashlib.sha256(("opstrax.platform.superadmin\n" + email).encode()).digest()
fingerprint = base64.b64encode(
    hashlib.pbkdf2_hmac("sha256", password.encode(), fp_salt, 100_000, 32)).decode()

print(pw_hash, fingerprint)
PY
)"

echo "── Preflight ─────────────────────────────────────────────────────────"
psql "$PG_URL" -v ON_ERROR_STOP=1 -v email="$PLATFORM_EMAIL" <<'SQL'
SELECT COALESCE((SELECT COUNT(*) FROM platform_admins), 0) AS total_platform_admins;
SELECT a.id, a.email, a.status, COALESCE(r.role_key, '(none)') AS role_key,
       (a.invite_token_hash IS NOT NULL) AS invite_pending,
       (a.password_hash IS NOT NULL)     AS has_password,
       a.last_login_at
FROM platform_admins a
LEFT JOIN platform_roles r ON r.id = a.role_id
WHERE LOWER(a.email) = LOWER(:'email');
SQL

if [ "$APPLY" != "1" ]; then
  echo
  echo "Preflight only — nothing was changed. An empty result row above means this email is"
  echo "not in platform_admins at all, which is exactly why login returns Invalid credentials."
  echo "Re-run with APPLY=1 to perform the repair."
  exit 0
fi

echo
echo "── Applying ──────────────────────────────────────────────────────────"
psql "$PG_URL" -v ON_ERROR_STOP=1 --single-transaction \
     -v email="$PLATFORM_EMAIL" -v pwhash="$PW_HASH" -v fingerprint="$FINGERPRINT" <<'SQL'
-- The RBAC row the account needs, in case the platform seed never ran on this database.
INSERT INTO platform_roles (role_key, name, description)
VALUES ('platform_super_admin', 'Platform Super Admin',
        'Full control of the SaaS business across all tenants.')
ON CONFLICT (role_key) DO NOTHING;

INSERT INTO platform_role_permissions (role_id, permission_key)
SELECT id, 'platform:*' FROM platform_roles WHERE role_key = 'platform_super_admin'
ON CONFLICT (role_id, permission_key) DO NOTHING;

-- Create the operator, or repair the existing row in place.
INSERT INTO platform_admins (email, full_name, password_hash, role_id, status)
SELECT :'email', 'Platform Owner', :'pwhash', id, 'Active'
FROM platform_roles WHERE role_key = 'platform_super_admin'
ON CONFLICT (email) DO NOTHING;

UPDATE platform_admins
SET password_hash     = :'pwhash',
    status            = 'Active',
    invite_token_hash = NULL,
    invite_expires_at = NULL,
    role_id           = (SELECT id FROM platform_roles WHERE role_key = 'platform_super_admin'),
    updated_at        = NOW()
WHERE LOWER(email) = LOWER(:'email');

-- The new password is the only way in from here.
DELETE FROM platform_sessions
WHERE admin_id IN (SELECT id FROM platform_admins WHERE LOWER(email) = LOWER(:'email'));

-- Clear the failed-login lockout ledger so a repaired account can sign in immediately.
DELETE FROM platform_audit_log
WHERE LOWER(actor_email) = LOWER(:'email')
  AND action IN ('platform.login_failed', 'platform.login_locked');

-- Same audit row the boot reconciler writes, so a later deploy sees this credential as
-- already in force and does not rewrite it.
INSERT INTO platform_audit_log
    (actor_admin_id, actor_email, actor_role, action, entity_type, entity_id, details_json, ip_address)
SELECT NULL, :'email', 'system', 'platform.superadmin.env_sync', 'PlatformAdmin', id,
       jsonb_build_object(
         'source', 'tools/platform-admin-recovery/repair-platform-superadmin.sh',
         'outcome', 'manual_recovery',
         'sessionsRevoked', 0,
         'credentialFingerprint', :'fingerprint'),
       'system'
FROM platform_admins WHERE LOWER(email) = LOWER(:'email');
SQL

echo
echo "── Result ────────────────────────────────────────────────────────────"
psql "$PG_URL" -v ON_ERROR_STOP=1 -v email="$PLATFORM_EMAIL" <<'SQL'
SELECT a.id, a.email, a.status, r.role_key,
       (a.invite_token_hash IS NULL) AS invite_cleared,
       (SELECT COUNT(*) FROM platform_role_permissions rp WHERE rp.role_id = a.role_id) AS grants
FROM platform_admins a
LEFT JOIN platform_roles r ON r.id = a.role_id
WHERE LOWER(a.email) = LOWER(:'email');
SQL

echo
echo "Done. Sign in at /platform/login with PLATFORM_EMAIL and the password you supplied."
echo "Set the SAME values as PLATFORM_SUPERADMIN_EMAIL / PLATFORM_SUPERADMIN_PASSWORD in"
echo "Render so the next deploy keeps them in force."

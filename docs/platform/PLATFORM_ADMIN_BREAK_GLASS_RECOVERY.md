# Platform Admin break-glass password reconciliation

Use this only when the existing active Platform Super Admin cannot authenticate because its stored PBKDF2 hash does not match `PLATFORM_SUPERADMIN_PASSWORD`, and no second authenticated Super Admin can issue a normal reset invite.

The startup bootstrap is deliberately first-install-only. Restarting or changing the Render secret does not update an existing administrator.

## Required approval

Obtain one written production change approval naming:

- the production service and Neon database;
- the existing administrator email and numeric ID;
- the change-ticket ID;
- authorization to replace only that administrator's password hash from the existing Render secret, clear pending invite state, revoke every platform session, write break-glass audit rows, and perform one login/`me`/logout verification;
- authorization for automatic hash rollback and another full session revocation if verification fails.

Do not paste the password, connection string, generated hash, session token, or invite token into the ticket, terminal command, logs, screenshots, or chat.

## Execution boundary

Run `tools/PlatformAdminRecovery` as an ephemeral Render one-off job built with `tools/PlatformAdminRecovery/Dockerfile` from the approved commit. Attach the same secret environment group as `opstrax-api`, including `PLATFORM_SUPERADMIN_EMAIL`, `PLATFORM_SUPERADMIN_PASSWORD`, and `PG_CONNECTION_SYSTEM`. Add only:

- `OPSTRAX_PLATFORM_RECOVERY_EXPECT_ADMIN_ID=<approved numeric ID>`
- `OPSTRAX_PLATFORM_BASE_URL=https://<production API origin>`

Do not add this tool to the web-service entrypoint and do not enable it at application startup.

## Procedure

1. Record a read-only snapshot: administrator ID, email, status, role key, MFA-enabled flag, active-session count, recent `platform.login_failed`/`platform.login_locked` audit counts, deployment version, and current UTC time. Never select `password_hash`, session tokens, or secret values into operator output.
2. Build the standalone tool and run `dotnet PlatformAdminRecovery.dll --check`. Exit `2` means mismatch confirmed and no mutation; exit `0` means stop because reconciliation is unnecessary.
3. Confirm MFA is disabled and the target is exactly the approved active `platform_super_admin`. If not, stop and use the normal MFA/admin recovery path.
4. With the approved change window open, run `dotnet PlatformAdminRecovery.dll --apply <change-ticket-id>` once.
5. The tool locks the exact row, updates only the password/invite fields, revokes all platform sessions, and commits a `platform.admin.break_glass_password_reconciled` audit row. It then proves production login, `/auth/me`, logout, and rejection of the logged-out token. A successful run records `platform.admin.break_glass_recovery_verified` and leaves zero sessions.
6. If any verification fails, the in-memory prior hash is restored only when the row still contains the hash written by this run. All sessions are revoked again and `platform.admin.break_glass_password_rollback` is recorded. A concurrent credential change causes rollback to refuse overwriting it and requires immediate escalation.
7. After success, sign in interactively from a trusted browser, enroll and verify MFA, sign out, sign in with MFA, and review the three login/recovery audit entries. Create a second named Super Admin through the normal invite lifecycle so future recovery does not require break glass.
8. Delete the ephemeral job and its recovery-only environment values. Retain the change ticket and non-secret audit evidence.

## Stop conditions

Stop without mutation for an email/ID mismatch, inactive or non-super target, enabled MFA, already-matching password, missing system DB credential, non-HTTPS API origin, absent approval ID, or more than one matching identity. Never bypass lockout by deleting login audit rows.

## Known product gaps

- There is no authenticated recovery path when the only Super Admin is locked out; reset-invite and session-revocation endpoints require another authenticated Super Admin.
- `PlatformLogout` deletes the token but does not itself write a logout audit row. This recovery tool records the verified logout outcome, but the endpoint should gain normal `platform.logout` auditing in a separately reviewed production change.
- Self-service password change intentionally keeps the caller's current session. Break-glass recovery instead revokes every session.

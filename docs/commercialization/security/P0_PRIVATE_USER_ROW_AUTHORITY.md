# P0 Private User-Row Authority

**Control status:** Engineering implementation and disposable-database verification

**Certification status:** Not certified; exact candidate SHA and real POC deployment evidence are still required

**Governing issue:** #226

**Terminal migration:** `2026_09_08_stage132_private_user_row_authority.sql`

## Decision

Tenant identity does not authorize access to another user’s private records. Authenticated application requests receive a short-lived database-signed principal ticket that binds tenant ID, user ID, PostgreSQL backend PID, transaction ID, expiry, and a random nonce. The application role cannot issue this ticket. The independently authenticated system role can issue it only for an active user who belongs to the stated tenant.

A tenant-only ticket remains valid for shared operational data. It has no user identity and therefore returns no rows through private-user policies.

## Private table inventory

| Table | Application authority | Administrative or operational exception | System authority |
|---|---|---|---|
| `mobile_device_tokens` | Own rows; select, insert, update | None | Full lifecycle |
| `user_notification_prefs` | Own row; select, insert, update | None | Full lifecycle |
| `user_locale_preferences` | Own row in authenticated tenant; select, insert, update | None | Full lifecycle |
| `user_mfa_status` | Own row | Persisted `users:view`, `users:update`, `security:view`, or `security:manage` permission by verb | Full pre-login lifecycle |
| `user_sessions` | Own rows; select, update, delete | Persisted `users:view` may inspect; `users:update` may revoke | Session creation and full lifecycle |
| `notification_recipients` | Own or matching role-target rows; select/update | `notifications:manage`; `dispatch:view` is select-only | Recipient creation and full lifecycle |
| `saved_reports` | Own rows; select, insert, update | Explicit tenant/role sharing for reads; `reports:export` management | Full lifecycle |
| `scheduled_reports` | Own rows; select, insert, update | `reports:export` management | Full lifecycle and delivery |
| `report_execution_log` | Own rows; select/append | `reports:export` read access | Full lifecycle |
| `messaging_conversations` | Creator or bound driver | Non-driver with persisted `messages:send` | Full lifecycle |
| `messaging_messages` | Participants admitted through the parent conversation; sender ID is bound on insert | Same conversation authority | Full lifecycle |
| `coaching_notes` | Append with bound author | `safety:view` reads; `safety:manage` appends | Full lifecycle |
| `telemetry_stream_ticket_nonces` | Append only for authenticated self | None | Full lifecycle |
| `password_reset_tokens` | No application-role access | None | System only |
| `mfa_login_challenge_consumptions` | No application-role access | None | System only |
| `alert_notification_deliveries` | No application-role access | None | System only |

All listed tables have RLS enabled and forced. Their application policies are command-specific. Table and sequence grants are an explicit allow-list. Public access, ownership, role membership, bypass-RLS, schema creation, database creation, and unsafe table verbs remain denied to runtime identities.

## Reviewed fields that are not private-row owners

The remaining schema inventory was reviewed for user-like columns. Fields such as `created_by`, `assigned_by`, `recorded_by`, `driver_id`, and support owner IDs on fleet, maintenance, safety, device, and audit records describe operational provenance or workflow responsibility. They do not make those records personal notebooks. Access continues through tenant RLS plus the route’s role, branch, customer, module, and permission checks.

Parent `notifications` rows describe a shared event. Per-user read and acknowledgement state is authoritative on `notification_recipients`; one recipient no longer changes shared parent state for everyone.

Provider inboxes, replay ledgers, authentication challenges, delivery ledgers, and background projections remain system-controlled. DeviceOps evidence and projection tables retain their migration-owned read-only or append-only contracts and are excluded from generic boot-time policy replacement.

## Required evidence before candidate certification

- Terminal migration applies twice without drift and all policy/ACL contract functions return true.
- Restricted PostgreSQL tests prove two users in one tenant and a third user in another tenant cannot read or mutate each other’s private rows.
- Tampered, wrong-connection, replayed, expired, tenant-only, and disabled-principal tickets fail closed.
- A real HTTP bearer-session journey proves the authenticated user ID reaches the signed database scope and `/api/security/my-sessions` returns only that user’s rows.
- Authentication, notification, messaging, MFA, coaching, reporting, and installation regressions pass.
- The full release gate passes at one frozen SHA.
- That exact SHA is deployed to the real customer POC URL and the authenticated Chrome journeys are captured there.

The first four items may be proven with disposable synthetic security fixtures. They do not prove real customers, devices, providers, video, routes, or regulatory acceptance. Those claims require their separate evidence gates.

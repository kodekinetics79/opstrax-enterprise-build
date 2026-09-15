# P0 Private User-Row Authority

**Control status:** Implemented, independently exercised by the mandatory SDET gates, and production-verified

**Certification status:** Software security gate accepted at release SHA `c6b09ccb93c084d9b025b76ef615dc8dc3e966f3` on 2026-09-09

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

## Candidate acceptance evidence

- Terminal migration applies twice without drift and all policy/ACL contract functions return true.
- Restricted PostgreSQL tests prove two users in one tenant and a third user in another tenant cannot read or mutate each other’s private rows.
- Tampered, wrong-connection, replayed, expired, tenant-only, and disabled-principal tickets fail closed.
- A real HTTP bearer-session journey proves the authenticated user ID reaches the signed database scope and `/api/security/my-sessions` returns only that user’s rows.
- Authentication, notification, messaging, MFA, coaching, reporting, and installation regressions pass.
- The full release gate passes at one frozen SHA.
- That exact SHA is deployed to the real customer POC URL and the authenticated customer-approved browser journey is captured there.

The first four items may be proven with disposable synthetic security fixtures. They do not prove real customers, devices, providers, video, routes, or regulatory acceptance. Those claims require their separate evidence gates.

## Automated evidence map

`RlsTenantIsolationPostgresTests.PrincipalScopes_IsolateTwoUsersAndTwoTenants_AndRejectSpoofTamperReplayExpiry` runs through the restricted `opstrax_app` and `opstrax_system` logins. It proves own-row reads, same-tenant and cross-tenant hidden rows, rejected same-tenant and cross-tenant ownership inserts, no-op foreign-row updates, tenant-only denial, ticket tamper denial, wrong-connection denial, transaction-replay denial, expiry denial, and disabled-principal denial.

`PrivateUserAuthorityHttpPostgresTests.MySessions_TwoUsersAndTwoTenants_ReturnOnlyAuthenticatedUsersRows` runs real Kestrel handlers in the protected Staging configuration with bearer sessions and the two restricted PostgreSQL identities. It proves that forged session-list query parameters do not change the authenticated principal, guessed same-tenant and cross-tenant session IDs return 404 and remain intact, an owned session can be revoked, and same-tenant plus cross-tenant `userId` / `companyId` JSON fields cannot redirect a notification-preference write away from the authenticated user.

`EveryTenantTable_HasExactSharedBoundedAndPrivatePolicies_AndNoPublicPolicy` and the Stage 132 migration verification lock the complete policy and grant inventory. CI reapplies the terminal migration chain, asserts every contract function, and runs these tests against disposable PostgreSQL for each exact candidate SHA.

## Accepted release record — 2026-09-09

- **Pull request:** [#230](https://github.com/kodekinetics79/opstrax-enterprise-build/pull/230)
- **Frozen test candidate:** `c15ea7f1fdaa02620b51a0da7b118d70cd2176b1`
- **Merged release SHA:** `c6b09ccb93c084d9b025b76ef615dc8dc3e966f3`
- **Exact-candidate CI:** [run 34408577991](https://github.com/kodekinetics79/opstrax-enterprise-build/actions/runs/34408577991), all 11 mandatory jobs passed, including `dotnet-integration-tests`, `production-shaped-release-rehearsal`, and `exact-sha-release-evidence`.
- **Production release:** [run 34409534997](https://github.com/kodekinetics79/opstrax-enterprise-build/actions/runs/34409534997), successful. The workflow applied the owner migration chain, deployed the API and frontend from the merge SHA, and verified both customer POC surfaces reported that SHA.
- **Sanitized principals:** Tenant A / User A, Tenant A / User B, and Tenant B / User C. Each CI execution creates fresh GUID-suffixed fixture identities and database IDs and removes them after the gate.
- **Staging replay:** the exact-candidate integration job used a disposable PostgreSQL 17 service with distinct restricted `opstrax_app` and `opstrax_system` connections. The actual handlers ran behind real Kestrel in `Staging` with RLS enforcement enabled. This is the isolated staging security gate; it is not a claim that the dormant shared staging resource shell is operational.
- **HTTP verdicts:** forged query ownership was ignored; same-tenant and cross-tenant guessed session deletes returned `404`; the target rows remained; the authenticated user could revoke its own secondary session with `200`; same-tenant and cross-tenant JSON ownership fields could not redirect notification-preference writes; an unknown bearer token returned `401`.
- **Database verdicts:** all 16 inventoried private tables retained forced RLS and the exact policy/grant contract. Same-tenant and cross-tenant reads, inserts, updates, and deletes failed closed. Tampered, wrong-connection, replayed, expired, tenant-only, and disabled-principal tickets failed closed.
- **Customer POC browser evidence:** after a hard refresh in the user-authorized authenticated Firefox session, the application header reported frontend and API SHA `c6b09ccb93c084d9b025b76ef615dc8dc3e966f3`. The Security & Auth view loaded only the signed-in account's active sessions, marked the current device as non-revocable, and the Notifications view loaded that account's preferences. No production preference or session was changed during this read-only check.

**Reviewer verdict:** GO for the private user-row software security control defined by issue #226. This verdict does not certify any customer dataset, hardware, device model, provider account, camera media, route, or regulatory claim; those remain governed by their own evidence gates.

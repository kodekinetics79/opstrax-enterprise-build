# Demo release gate — Users & Roles, tenant isolation, and PT40

This runbook is the release authority for the 2026-07-11 security/demo slice. It intentionally contains no credentials or customer/device identifiers.

## Current decision

**NO-GO until every blocking gate below is evidenced in the target environment.** A successful application build is necessary but does not prove that owner-only migrations, RLS, tenant separation, or physical tracker forwarding are active.

The production API connects as the restricted `opstrax_app` role with `Rls__EnforceTenantContext=true`. In that mode startup deliberately skips DDL. Apply migrations out of band as the database owner **before** deploying the API.

## Required order

1. Capture a database backup/restore point and record the current application commit.
2. Run the read-only preflight queries below as the database owner.
3. Resolve any duplicate non-null IMEI values. Do not choose a winning tenant/device automatically.
4. Apply these owner-only migrations in order:
   - `database/migrations/2026_07_11_users_roles_governance.sql`
   - `database/migrations/2026_07_11_stage32_device_imei.sql`
   - `database/migrations/2026_07_11_stage33_tenant_api_keys_webhooks.sql`
5. Verify schema, grants, RLS policies, and indexes before starting the new API.
6. Configure production secrets and controls in the hosting control plane.
7. Deploy the API; wait for `/health/ready` to return HTTP 200.
8. Deploy the frontend only after the API is ready.
9. Execute the tenant-isolation and demo smoke tests below.
10. Commission the physical PT40 only through the signed trusted-gateway path.

Use `psql -v ON_ERROR_STOP=1` for every migration. Never paste a connection string or secret into a shell history, ticket, screenshot, or this document.

## Database preflight

Run read-only checks against the target database:

```sql
SELECT imei, COUNT(*) AS device_count
FROM eld_devices
WHERE imei IS NOT NULL
GROUP BY imei
HAVING COUNT(*) > 1;

SELECT LOWER(name) AS role_name, COUNT(*) AS role_count
FROM roles
GROUP BY LOWER(name)
HAVING COUNT(*) > 1;

SELECT u.id, u.company_id AS user_company, r.id AS role_id, r.company_id AS role_company
FROM users u
JOIN roles r ON r.id = u.role_id
WHERE r.company_id IS NOT NULL AND r.company_id <> u.company_id;
```

All three result sets must be empty. A non-empty result is a blocking data-remediation decision, not a migration-script problem to bypass.

## Post-migration database evidence

```sql
SELECT indexname
FROM pg_indexes
WHERE schemaname = 'public'
  AND indexname IN ('ux_roles_system_name', 'ux_roles_tenant_name', 'ux_eld_devices_imei');

SELECT tablename, policyname
FROM pg_policies
WHERE tablename IN ('tenant_api_keys', 'tenant_webhook_settings', 'company_profile', 'user_notification_prefs')
ORDER BY tablename, policyname;

SELECT table_name, privilege_type
FROM information_schema.role_table_grants
WHERE grantee = 'opstrax_app'
  AND table_name IN ('tenant_api_keys', 'tenant_webhook_settings', 'company_profile', 'user_notification_prefs')
ORDER BY table_name, privilege_type;
```

Expected evidence:

- all three unique indexes exist;
- each Stage 33 tenant table has `tenant_isolation` and `platform_admin_bypass` policies;
- `opstrax_app` has only the required table privileges;
- the runtime role has sequence usage through the existing default-privilege policy (verify in the target database rather than assuming it was inherited).

## Production configuration gate

Required, secret-valued settings:

- `PG_CONNECTION` points to the restricted runtime role, not the database owner;
- `Jwt__Key` is unique to the environment and at least 64 characters;
- `Telemetry__DeviceSecret` is at least 32 characters;
- `Telemetry__GatewaySecret` is at least 32 characters and is known only to the trusted gateway and API;
- `Sse__TicketKey` is at least 32 characters;
- `PLATFORM_SUPERADMIN_PASSWORD` is strong, unique, and not a repository default;
- encryption, storage, and SMTP credentials are configured if those production features are in demo scope.

Required non-secret controls:

- `ASPNETCORE_ENVIRONMENT=Production`;
- `Rls__EnforceTenantContext=true`;
- `Telemetry__Simulator__Enabled=false`;
- `DemoSeed__Enabled=false`;
- `ENABLE_FLEET_DEMO_SEED=false` (or unset) and `Fleet__EnableDemoSeed=false` (or unset);
- `Cors__AllowedOrigins` contains only the exact deployed frontend origin(s), with no wildcard;
- the frontend API URL resolves to the deployed API, not localhost.

Do not log secret values while checking configuration. `/health/ready` and startup logs provide redacted presence/strength status.

## Tenant-isolation acceptance gate

Use two disposable test tenants, A and B. Never use live customer records for an authorization test.

- A tenant-A administrator cannot list, read, update, deactivate, or assign a tenant-B user.
- Tenant A cannot list or mutate tenant-B custom roles.
- A role identifier from tenant B is rejected when submitted by tenant A.
- Tenant A cannot assign a tenant-B vehicle or driver to a device.
- Tenant A cannot read tenant-B audit, security, API-key, webhook, or profile records.
- A revoked/deactivated user session is rejected on its next request.
- A user cannot deactivate themself or remove the final tenant administrator.
- Platform-control-plane credentials are rejected by tenant administration routes unless the route explicitly belongs to that separate control plane.
- Direct SQL under `opstrax_app`, with tenant A context set, cannot select tenant-B rows from every RLS-protected table touched by this release.

Record request IDs, status codes, and redacted object IDs as evidence. Any cross-tenant success response is an immediate **NO-GO** and incident-level defect.

## PT40 demo gate

Registry presence, matching IMEI, or simulator movement does not prove physical connectivity.

- APN and vendor protocol settings are confirmed from the device/vendor documentation.
- A trusted PT40/GT06 protocol gateway is deployed and uses HTTPS to the API.
- Gateway requests use `X-Gateway-Timestamp` and `X-Gateway-Signature` over the exact raw JSON body.
- An invalid signature, stale timestamp, replayed packet, unknown IMEI, and cross-tenant assignment each fail closed.
- One genuine packet from the physical tracker is captured end to end without logging the secret or full customer-sensitive payload.
- The genuine packet updates the expected tenant-owned device heartbeat and vehicle position.
- The UI identifies the source as real hardware, not simulator/seed data.

Until every item passes, present the fleet workflow as an application demo and do not claim the physical PT40 is live.

## Operational smoke gate

- Backend build and complete test suite pass from a clean checkout.
- Frontend lint and production build pass.
- `git diff --check` passes and the release commit contains every referenced migration.
- `/health/live` returns 200 and `/health/ready` returns 200 after deployment.
- Login succeeds for the designated tenant demo account without exposing credentials in screen recordings.
- Users & Roles tabs load persisted data, survive refresh, and show no fabricated fallback records.
- Create role, assign role, deactivate/reactivate user, audit review, and session-revocation flows work with authorized accounts.
- Unauthorized navigation and direct API calls both fail; hiding a UI control alone is insufficient.
- Responsive login and core demo routes are visually checked on the actual presentation viewport and browser.

## Go / no-go authority

**GO** requires a named owner and timestamped evidence for database, security, application, device, and presentation gates. **NO-GO** applies if any migration is unapplied, readiness is non-200, tenant isolation is unproven, simulator data can be mistaken for hardware, or the physical-device claim lacks a genuine signed packet.

If rollback is required, roll back the application first. Do not drop new columns/tables or recreate the old non-unique IMEI index during an incident; preserve data, restore the prior app version, and perform database reversal only through a separately reviewed owner-run change.

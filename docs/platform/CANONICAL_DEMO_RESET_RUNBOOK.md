# Canonical demo fixture reset

Use this only when the disposable Development demo tenant must be restored to the
authoritative `DemoTenantSeeder` fixture before a client rehearsal. It is not a
general tenant reset and it must never be enabled in a shared customer environment.

## Safety contract

- Route: `POST /api/platform/dev/reset-canonical-demo`
- Target: exactly `MERIDIAN-DEMO`; the caller cannot submit another tenant ID/code.
- Environment: `Development` exactly. The route is not mapped in Staging,
  Production, or custom environment names, and the handler repeats the check.
- Opt-in: `DemoSeed__ResetEnabled=true`; default/unset is disabled and returns 404.
- Identity: a valid Platform Admin bearer session with
  `platform:tenants:offboard` (currently the super-admin wildcard).
- Intent: exact confirmation `RESET MERIDIAN-DEMO` and an 8–500 character reason.
- Evidence: `demo.fixture.reset.started`, `.completed`, and `.failed` events are
  written to `platform_audit_log` with actor, IP, reason, fixture version and counts.

## Preflight

1. Verify this is a disposable Development database, not a browser/customer/shared
   environment. Record the database name without recording credentials.
2. Take a database snapshot if any demo interactions need to be retained.
3. Verify no client session is using the canonical demo tenant.
4. Set `DemoSeed__ResetEnabled=true` only for the reset window and restart the API.
5. Sign in through Platform Admin and obtain the short-lived bearer token through
   the normal login flow. Never paste it into a committed file or terminal history.

## Request

Send a JSON body to the route using the Platform Admin bearer token:

```json
{
  "confirm": "RESET MERIDIAN-DEMO",
  "reason": "Restore canonical fixture for client rehearsal CASE-1234"
}
```

The response identifies the previous and new company IDs, fixture version, deleted
row count, and canonical seeded entity counts. HTTP success alone is not release
evidence; validate the audit and UI checks below.

The current authoritative source fixture is **v7**. Its reset manifest includes
five vehicles, five drivers and twelve jobs and explicitly enables the nine
governed pilot entitlements under `package_allowlist`; those are acceptance
checks, not permission to hard-code database IDs, which change on reseed.

The seeded incident has a row named `Synthetic harsh-braking telemetry metadata`.
It has no object URL and explicitly records `not_verified`, `not_managed` custody
and `not_available` retrieval. Its deterministic hash is a synthetic pointer,
not proof of an uploaded object, content verification or chain of custody. A
rehearsal may create an external HTTPS evidence metadata reference through the real
workflow and verify only metadata persistence plus the disclaimer. It must not make
an object durability, upload, retrieval, independent hash-verification or custody
claim. Any generic object-store exercise is separate and does not validate Incident
evidence custody.

## Verification

1. In Platform Audit, confirm one `started` and one `completed` event from the same
   actor. There must be no subsequent `failed` event.
2. Confirm the response fixture version matches
   `DemoTenantSeeder.SafetyPilotFixtureVersion`.
3. Sign in as the canonical demo personas and perform the Safety pilot smoke checks.
4. Capture browser evidence only after verifying package and entitlement state.
5. Immediately remove `DemoSeed__ResetEnabled` (or set it to `false`) and restart.
   Confirm the route now returns 404.

## Failure recovery

Deletion and reseeding are separately committed operations. If seeding fails after
deletion, the canonical tenant can be temporarily absent. The durable `.failed`
audit event records this state. Fix the underlying seed/schema error and retry the
same endpoint; the seeder recreates a missing canonical tenant deterministically.
Do not create a substitute tenant manually.

Stage 72 keeps certified HOS logs, certification snapshots and detention evidence
protected from ordinary updates/deletes. Tenant erasure is allowed only when both
the transaction-local `opstrax.offboarding=on` flag and `opstrax_system` membership
are present. The offboarding deletion itself is one transaction and rolls back if
tenant rows remain; deletion and subsequent reseeding are separate commits. The
database offboarding service covers relational tenant rows. Object-store cleanup
is not part of this reset path; the current canonical fixture does not create stored
objects. If that fixture begins creating files, object-prefix cleanup must be added
before this path is used.

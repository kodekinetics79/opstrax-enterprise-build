# OpsTrax product manual: Telematics and Device Operations

This guide describes the customer-visible Telematics workflow. A device registry entry is not proof of live telemetry: connection, position, diagnostic, and assignment evidence must come from authenticated observations.

## Onboard devices

1. Sign in with an administrator role that can manage fleet devices.
2. Open **Device Health** and download the current customer import template.
3. Populate non-duplicated serial numbers, device type, provider, branch, and any supported optional identifiers.
4. Upload the CSV through the import workflow. Correct rejected rows in the product workflow and upload the corrected file.

### Bulk device installation

After devices and vehicles exist, open **Device Health** and use the separate **device installations** template and import control. Each row identifies an existing device and vehicle, their branch, the hardware role, effective timestamp, reason, and a unique idempotency key. Imports accept up to 500 rows, so a 1,000-installation fleet uses two files.

Review the server preview before committing. The installation import is atomic: every row must be valid, and a concurrent device, vehicle, branch, or primary-role conflict changes no rows. The effective timestamp must also be at or after any prior closed device or primary-role assignment; the preview identifies historical overlap so the timestamp can be corrected before commit. Re-uploading an already completed file reports those rows as already recorded and does not duplicate history. Bulk installation creates normal effective-dated **Installed** records and audit/history entries; it never marks a device commissioned or verified. Commissioning still requires authenticated telemetry evidence through the normal device workflow.
5. Store the one-time device credentials securely when the product presents them. OpsTrax does not display those secrets again.
6. Refresh the page and use search, filters, sorting, pagination, and export to verify that the accepted records persisted in the intended branches.

Large imports are branch-scoped and paginated. An import result is not complete until the accepted and rejected row counts reconcile with the source file and a subsequent export.

## Install, commission, transfer, and remove

Open a device's **Manage** action to inspect its current state and permitted actions.

- Install assigns a device to the selected vehicle with an effective date.
- Commissioning requires an authenticated observation from the assigned device or protocol gateway. Registry presence alone does not make a unit live.
- Transfer ends the prior effective assignment and creates the replacement assignment. It must not rewrite historical ownership.
- Remove ends the active assignment without deleting history.
- **Revoke & Archive** is permanent: it invalidates all device credentials, blocks future ingest, and retains history in the Archived view. Use **Suspend** when a reversible stop is required; a suspended device can later be activated.

After every assignment change, refresh the page, sign out and back in, and confirm the effective-dated history from both the device and asset views.

## Understand connection status

- **Online** means a recent authenticated heartbeat or telemetry observation exists.
- **Offline** means a previously connected device has exceeded the operational freshness threshold.
- **Never connected** means no authenticated observation has been accepted.
- **Needs attention** identifies a device requiring commissioning, connection, or diagnostic action.
- **Archived** means credentials were permanently revoked. It is a lifecycle/security state, not a temporary connection condition.

Status must remain honest when telemetry is absent. OpsTrax shows an empty or unavailable value instead of manufacturing a position, signal, diagnostic, or history reading.

The Device Health table and detail drawer use the same freshness, active-alert, and active-fault evidence. A snapshot reload must not change an Offline row into an Online detail unless a newer authenticated observation was actually accepted. Roles without live-position permission can still inspect permitted device identity, readiness, assignment, installation, and history; the detail shows that no live snapshot is available and does not fail or disclose GPS data.

## GPS and geofences

Open **GPS Tracking** to inspect the latest accepted vehicle position. Position freshness is displayed as current, delayed, stale, or no position. A newer ordered observation may advance the latest position; an older event remains historical and cannot move the vehicle backwards.

GPS export is a separate least-privilege action. A Company Admin can grant `telematics:gps:export` in **Users & Roles** without granting map management. **Export CSV** downloads the complete authorized result set, not only the visible page; verify the exported row count against **Fleet managed units** after the download completes.

Users with map-management permission can open **Manage Geofences**. Create geofences through the customer form, then validate entry and exit behaviour with authenticated location observations. Branch-restricted users can view only authorized assets and events.

## OBD/J1939 evidence

Open **OBD/J1939** to review authenticated diagnostic observations, active faults, and operational holds. The page is evidence-oriented: a compatible device type by itself is not a diagnostic reading. Critical-fault holds must persist until an authorized resolution workflow is completed; changing or replaying a duplicate event must not clear them.

Diagnostics export is governed separately by `telematics:diagnostics:export`. Granting it does not enable snapshot reload, fault mutation, or maintenance creation. **Export CSV** downloads every authorized diagnostics record; verify the exported row count against **Fleet managed units** and retain the file only in an approved customer evidence location.

Administrator and Maintenance Manager roles can inspect the full permitted diagnostic evidence. Dispatcher, Driver, Executive, and Customer access depends on their permissions and branch or asset scope. A denied role receives a safe authorization result without tenant data leakage.

## Duplicate and replay safety

Every native device request is signed and subject to timestamp and nonce validation. Reusing the same transport nonce is rejected. An exact retry with a fresh nonce and the same stable event identity is acknowledged without creating another event. Reusing that identity with altered content or from another device is rejected.

For certification, migration, and controlled replay exercises, every execution must also use a unique run identifier when generating customer event identities, correlation identities, and diagnostic source identities. Repeating a complete dataset with a new run identifier represents new observations; deliberate retry cases retain the same event identity so idempotency can be proved. Never alter an existing event identity merely to bypass a conflict—the conflict must be reconciled as duplicate, replay, or data corruption before continuing.

## Pilot acceptance

Customer acceptance is browser-first. For the certification tenant, validate Device Health, GPS Tracking, geofences, OBD/J1939 evidence, exports, assignment history, lifecycle actions, direct URLs, role restrictions, persistence, console errors, failed requests, and responsive layouts in visible Chrome. API responses, automated tests, and database queries support that evidence but do not replace it.

The deterministic certification harness qualifies OpsTrax's native authenticated ingest boundary at fleet volume. It does not certify a third-party provider. Samsara polling, protocol gateways, or another provider integration requires a separate provider sandbox and a small real-device pilot before production activation.

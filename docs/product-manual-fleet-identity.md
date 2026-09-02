# Fleet Identity & Asset Master

This module maintains the customer’s branch ownership structure, role accounts, vehicles, drivers, devices, and returnable assets. Tenant-wide administrators can see all branches. A branch-scoped account sees operational records owned by its assigned branch.

## Branches and role accounts

1. Open **Fleet → Branches** and select **Add Branch**.
2. Enter a unique branch code, name, type, location, country, and timezone.
3. Open **Governance → Users & Roles**, select **Add User**, choose a role, and select either a branch scope or tenant-wide access.
4. Leave the password blank to create a pending invitation, then securely share the one-time activation link. Setting an initial password creates an active account immediately.

Changing a user’s role, customer binding, branch scope, or active status revokes existing sessions. A branch-bound administrator cannot grant tenant-wide access or assign another branch.

### Driver portal access

Creating or importing a driver does not automatically create a login. Open **Fleet → Drivers → Records**, select the driver, and use **Grant portal access** only after the driver has a valid, unique work email. If an active Driver-role account with the same email already exists in the tenant, OpsTrax links it without resetting its password. The record then shows the sign-in email and **Active** status.

Use **Revoke access** to disable the Driver login, end its active sessions, and unlink it from the driver record. Regranting access is an explicit administrator action. A Driver login can be linked to only one active driver, and a staff-role identity cannot be converted into a Driver portal identity.

After granting access, sign out and authenticate as the Driver. Verify that the Driver home resolves to the intended driver name, then refresh and repeat logout/login. Direct fleet, device, and administration URLs must return the Driver to the self-service application without displaying fleet records.

## CSV onboarding

Vehicles, drivers, devices, and returnable assets support customer-facing CSV onboarding. Always download the current template from the relevant page. Files accept up to 500 rows; larger fleets should use multiple files.

1. Select **Template** and prepare the CSV without changing identity columns. Set `branchCode` on every row when signed in as a tenant-wide administrator. A branch-scoped importer may omit it to use their own branch, but cannot name another branch.
2. Select **Import**, choose the CSV, and review the server validation preview.
3. Correct invalid or duplicate rows in the CSV and upload again. Rows with errors are never committed.
4. Commit the valid rows, refresh the register, and use search/filter/sort/pagination to verify persistence. **Export CSV** downloads the complete tenant-and-branch-scoped dataset, not only the current page.

Vehicle and driver registers show 100 sorted rows per page after filters are applied. Returnable Assets and Devices also use 100-row server pages, so large fleets do not load every record into the browser at once. Their search, sort, totals, page controls, and exports are calculated by the server across the caller's complete authorized branch scope. Export controls appear only for accounts with the corresponding export or fleet-management permission.

Vehicle exports include branch ownership, class, VIN-exception type, alternate identifier, plate jurisdiction, status, odometer, and device status. Driver exports include branch ownership, licence expiry, safety, readiness, risk, and compliance fields. Driver licence values remain masked in operational exports. Rows are deterministically ordered by customer code, and text that could be interpreted as a spreadsheet formula is neutralized.

Vehicle codes, VINs, driver codes, licence numbers, device serials, IMEIs, and asset tags are validated before commit. Device imports never overwrite an existing hardware identity. A successful device import automatically downloads one-time API and HMAC credentials; store that file securely because the secrets cannot be displayed again.

Before importing assets, create the referenced asset type (for example `TRAILER`) through the Returnable Assets form. The `assetTypeCode` in each CSV row must match a type visible in the importing account’s branch scope.

## Assignment lifecycle

Provision devices without a vehicle or driver binding, then use the governed installation workflow to install, commission, transfer, or remove them. Use Assignments for vehicle/driver work and Returnable Assets for asset custody. Reassignment closes the prior effective period and preserves history; do not edit identity fields to simulate a transfer.

For large fleets, Device Health provides a separate **device installations** CSV template and atomic preview/commit workflow for existing devices and vehicles. Files are limited to 500 rows. Every row requires an effective timestamp, assignment reason, branch, and unique idempotency key. Correct all preview errors before commit. A replay is reported as already recorded without duplicating history; the bulk workflow creates only uncommissioned `Installed` records and cannot substitute for authenticated commissioning evidence.

To transfer an installed device, open its registry record and choose **Transfer**. Select the destination vehicle, enter an effective time that is not in the future, and record the odometer, location, method, and reason. Review the source and destination before confirming. Submit once and wait for the result; if the outcome is unclear, refresh the device history before trying again. A completed transfer closes the prior installation period and creates a new effective-dated row. The transfer is rejected without changing either assignment when telemetry is being ingested concurrently, the device or destination is outside the caller's branch scope, or telemetry evidence cannot be checked safely.

Provider audit information is restricted to authorized provider-support roles. Fleet users may see a neutral **Restricted** state; that is an authorization boundary, not a failed device transfer.

To assign or reassign a primary driver, open the vehicle or driver record, choose **Assign** or **Reassign**, select the intended available record, review the pairing, and choose **Confirm assignment**. The product rejects cross-branch pairs and ineligible resources. Refresh the detail drawer and open **Assignments** to verify the active pairing. The vehicle and driver drawers show the same ledger as effective-from/effective-to history; the released row remains visible after reassignment.

To retire a vehicle or driver, open the active record and choose **Archive**, then confirm in the in-product dialog. The product blocks archival while active dispatch assignments, jobs, routes, trips, or vehicle device installations still depend on the record. Resolve those dependencies and retry. A fleet-master vehicle/driver pairing is released automatically, with its effective-to history retained.

Archived records remain available from the **Archived** filter and are read-only. Open an archived record and choose **Reactivate** to return it to the active register. Its prior operational status is preserved; legacy records archived by older releases return as **Available**. Reactivation is rejected when another active record now uses the same governed vehicle or driver identity.

For vehicles and drivers, **Active** is a lifecycle view: it contains every non-archived record, including operational statuses such as Available, On Route, Maintenance, or Suspended. If archival is blocked by an active dependency, the confirmation dialog remains open and identifies the dependency to resolve.

Driver licence numbers are shown only as masked last-four values on operational screens. Internal identity-search indexes are never customer-facing; use the audited data-subject export workflow when legally authorized plaintext is required.

## Documents, readiness, and expiry

Open **Fleet → Documents** and choose **Upload Document**. Select the actual PDF, image, Word, Excel, text, or CSV file (maximum 25 MB), then identify its vehicle, driver, or returnable asset and enter issue/expiry metadata. Upload remains disabled until a file, title, entity type, and entity ID are supplied. Invalid or reversed dates are rejected in the dialog. The file is validated and stored in the tenant-private object store; a metadata-only placeholder is not accepted by this workflow. Expired and next-30-day documents are classified as **Expired** or **Expiring** with **Renewal Required**, so the readiness and expiry views reflect the uploaded evidence.

Document lists, summaries, detail, timelines, renewal actions, and downloads derive branch ownership from the linked vehicle, driver, or returnable-asset master. A branch-scoped user cannot bind a document to, list, or download another branch's record. Choose **Renew** to queue renewal and preserve the lifecycle event; upload the replacement as real evidence when it is available.

## Staging customer-pilot preparation

The **Platform Admin → Product Pilot** page is available only when the API is explicitly configured for the isolated staging certification environment. It is not a tenant-data seeder and is not exposed in production. A platform operator with the dedicated Product Pilot permission must type `CERT-LARGE-20260825`, acknowledge the staging boundary, and enable CRM. The action is fixed to that tenant and module, replay-safe, and recorded atomically in the platform audit log.

After CRM is enabled, sign out of Platform Admin and sign in separately as the certification Tenant Administrator. Create the customer in **Customer Master**, select that customer in **Jobs**, and create its route in **Route Planning**. Refresh and sign in again to verify persistence. If Chrome or an assistive tool cannot operate the native route date/time picker, select **Use plain date/time entry** and enter `YYYY-MM-DDTHH:MM`; the normal validation and UTC conversion still apply. The harness must never be used to create customer, job, route, fleet, or telemetry records outside those customer-facing workflows.

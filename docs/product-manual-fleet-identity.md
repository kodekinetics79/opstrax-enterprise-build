# Fleet Identity & Asset Master

This module maintains the customer’s branch ownership structure, role accounts, vehicles, drivers, devices, and returnable assets. Tenant-wide administrators can see all branches. A branch-scoped account sees operational records owned by its assigned branch.

## Branches and role accounts

1. Open **Fleet → Branches** and select **Add Branch**.
2. Enter a unique branch code, name, type, location, country, and timezone.
3. Open **Governance → Users & Roles**, select **Add User**, choose a role, and select either a branch scope or tenant-wide access.
4. Leave the password blank to create a pending invitation, then securely share the one-time activation link. Setting an initial password creates an active account immediately.

Changing a user’s role, customer binding, branch scope, or active status revokes existing sessions. A branch-bound administrator cannot grant tenant-wide access or assign another branch.

## CSV onboarding

Vehicles, drivers, devices, and returnable assets support customer-facing CSV onboarding. Always download the current template from the relevant page. Files accept up to 500 rows; larger fleets should use multiple files.

1. Select **Template** and prepare the CSV without changing identity columns. Set `branchCode` on every row when signed in as a tenant-wide administrator. A branch-scoped importer may omit it to use their own branch, but cannot name another branch.
2. Select **Import**, choose the CSV, and review the server validation preview.
3. Correct invalid or duplicate rows in the CSV and upload again. Rows with errors are never committed.
4. Commit the valid rows, refresh the register, and use search/filter/sort/pagination to verify persistence. **Export CSV** downloads the complete tenant-and-branch-scoped dataset, not only the current page.

Vehicle codes, VINs, driver codes, licence numbers, device serials, IMEIs, and asset tags are validated before commit. Device imports never overwrite an existing hardware identity. A successful device import automatically downloads one-time API and HMAC credentials; store that file securely because the secrets cannot be displayed again.

Before importing assets, create the referenced asset type (for example `TRAILER`) through the Returnable Assets form. The `assetTypeCode` in each CSV row must match a type visible in the importing account’s branch scope.

## Assignment lifecycle

Provision devices without a vehicle or driver binding, then use the governed installation workflow to install, commission, transfer, or remove them. Use Assignments for vehicle/driver work and Returnable Assets for asset custody. Reassignment closes the prior effective period and preserves history; do not edit identity fields to simulate a transfer.

To assign or reassign a primary driver, open the vehicle or driver record, choose **Assign** or **Reassign**, select the intended available record, review the pairing, and choose **Confirm assignment**. The product rejects cross-branch pairs and ineligible resources. Refresh the detail drawer and open **Assignments** to verify the active pairing. The vehicle and driver drawers show the same ledger as effective-from/effective-to history; the released row remains visible after reassignment.

To retire a vehicle or driver, open the active record and choose **Archive**, then confirm in the in-product dialog. The product blocks archival while active dispatch assignments, jobs, routes, trips, or vehicle device installations still depend on the record. Resolve those dependencies and retry. A fleet-master vehicle/driver pairing is released automatically, with its effective-to history retained.

Archived records remain available from the **Archived** filter and are read-only. Open an archived record and choose **Reactivate** to return it to the active register. Its prior operational status is preserved; legacy records archived by older releases return as **Available**. Reactivation is rejected when another active record now uses the same governed vehicle or driver identity.

Driver licence numbers are shown only as masked last-four values on operational screens. Internal identity-search indexes are never customer-facing; use the audited data-subject export workflow when legally authorized plaintext is required.

## Documents, readiness, and expiry

Open **Fleet → Documents** and choose **Upload Document**. Select the actual PDF, image, Word, Excel, text, or CSV file (maximum 25 MB), then identify its vehicle, driver, or asset and enter issue/expiry metadata. The file is validated and stored in the tenant-private object store; a metadata-only placeholder is not accepted by this workflow. Expired and next-30-day documents are classified as **Expired** or **Expiring** with **Renewal Required**, so the readiness and expiry views reflect the uploaded evidence. Branch-scoped users can attach documents only to records in their branch.

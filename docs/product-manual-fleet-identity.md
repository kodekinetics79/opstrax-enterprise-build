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

1. Select **Template** and prepare the CSV without changing identity columns.
2. Select **Import**, choose the CSV, and review the server validation preview.
3. Correct invalid or duplicate rows in the CSV and upload again. Rows with errors are never committed.
4. Commit the valid rows, refresh the register, and use search/filter/export to verify persistence.

Vehicle codes, VINs, driver codes, licence numbers, device serials, IMEIs, and asset tags are validated before commit. Device imports never overwrite an existing hardware identity. A successful device import automatically downloads one-time API and HMAC credentials; store that file securely because the secrets cannot be displayed again.

Before importing assets, create the referenced asset type (for example `TRAILER`) through the Returnable Assets form. The `assetTypeCode` in each CSV row must match a type visible in the importing account’s branch scope.

## Assignment lifecycle

Provision devices without a vehicle or driver binding, then use the governed installation workflow to install, commission, transfer, or remove them. Use Assignments for vehicle/driver work and Returnable Assets for asset custody. Reassignment closes the prior effective period and preserves history; do not edit identity fields to simulate a transfer.

To assign or reassign a primary driver, open the vehicle or driver record, choose **Assign** or **Reassign**, select the intended available record, review the pairing, and choose **Confirm assignment**. The product rejects cross-branch pairs and ineligible resources. Refresh the detail drawer and open **Assignments** to verify the active pairing. The vehicle and driver drawers show the same ledger as effective-from/effective-to history; the released row remains visible after reassignment.

Archive or deactivate records through their product action instead of reusing identities. Reactivate only after confirming the identity is not already active elsewhere.

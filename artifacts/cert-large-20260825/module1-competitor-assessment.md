# Module 1 competitor assessment

Assessment date: 2026-08-26
Scope: Fleet Identity & Asset Master only
Latest deployed OpsTrax candidate exercised: `5d7c16517e2d8a414f698d7382754c4280e241ca`

## Executive conclusion

OpsTrax now demonstrates credible large-fleet browser workflows for branch setup, template-based vehicle/driver/device/asset import, server-paged registers, governed assignments, reversible lifecycle, and branch-scoped roles. Exact totals of 1,000 vehicles, 1,250 drivers, 1,100 devices, and 300 assets persisted through the customer UI. Vehicle/driver, device, and trailer histories survived reassignment plus refresh/logout-login. The present pack is materially closer to leading fleet products, but it is still not certifiable because mandatory Customer, responsive-viewport, document/expiry, recording/HAR, and remaining controlled vehicle/driver correction evidence is unavailable.

The most material competitive gaps are evidence gaps that must be resolved in Chrome before certification:

1. Controlled device and asset invalid/duplicate behavior is preserved, including atomic rollback of a custody-conflicting asset update; the equivalent vehicle/driver correction cycle remains open.
2. Five non-admin role logins and representative branch/read-only/direct-URL restrictions passed, but the Customer account remains externally blocked by the unavailable CRM/customer entitlement.
3. Full-volume asset/device search, sort, pagination, and export passed; exact-SHA vehicle and driver exports also reconciled complete branch/governed fields, ordering, uniqueness, and masked licences.
4. The installed visible-Chrome controller cannot set or verify the four required viewport sizes, and no recording/HAR was produced.
5. Readiness views were inspected, but the certification data has no linked expiry documents and the separate Maintenance Center package is unavailable.
6. User onboarding remains form-based; there is no customer-facing user CSV import surface.

## Market benchmark

| Capability | Leading-product benchmark | OpsTrax browser evidence | Current assessment |
|---|---|---|---|
| Bulk onboarding | Samsara supports driver CSV preview/error review/finalization for fewer than 5,000 entries. Motive supports vehicle and asset imports, including downloadable templates and import history. Fleetio provides downloadable templates, mapping/review, import history, error files, and recommends batches of 500 rows or fewer. | Template/import dialogs are evidenced for vehicles, drivers, devices, and returnable assets. The observed OpsTrax wizard states a 500-row maximum. | Surface parity is plausible and 500-row batching is not itself uncompetitive, but end-to-end volume, correction, and reconciliation remain unproved. |
| Identity integrity | Samsara uses an immutable internal driver ID during bulk edits and warns that malformed spreadsheet handling can overwrite identifiers. Fleetio distinguishes new versus update imports through `fleetio_id`; its import guides require unique record identifiers for relational imports. Motive centralizes profile/device assignment and audits import/admin actions. | Device serial/IMEI and asset-tag duplicates were rejected without changing reconciled totals; a custody-conflicting asset update rolled back atomically with a useful row-level message. | Strong device/asset evidence; vehicle/driver controlled duplicate/correction proof remains required. |
| Assignment and history | Samsara exposes time-ranged driver-vehicle assignment records and supports bulk static assignments. Fleetio presents current, past, and future assignments, start/end times, reassignment, and assignment history; it rejects overlapping open-ended assignments. | Vehicle/driver, device, and trailer reassignment/custody histories are preserved through refresh and logout/login. | Effective-dated relationship coverage now approaches the benchmark for the exercised journeys. |
| Branch/group scoping | Samsara combines roles with tags to restrict access to tagged drivers, vehicles, and devices. Motive supports custom roles and group-bound visibility. Geotab groups organize assets/users and control data access, with site-wide group filtering. Fleetio uses hierarchical groups/subgroups and record-set permissions. | Five branches persist; representative Fleet Manager, Dispatcher, and Maintenance Manager CL-HQ views denied cross-branch searches; Driver and administrative direct URLs were safely restricted. | Representative enforcement is credible; exhaustive per-role/per-entity export-negative coverage remains open. |
| Roles and least privilege | Samsara and Motive provide default and custom granular roles; Geotab provides standard and custom security clearances; Fleetio separates owner/admin/user powers and group record sets. | Fleet Manager, Dispatcher, Maintenance Manager, Driver, and Executive authenticated journeys passed representative positive and negative checks. | Directionally competitive; Customer remains blocked and Documents/Maintenance entitlement coverage is incomplete. |
| Lifecycle/archive | Fleetio retains historical vehicle data in read-only archived records, supports restore, and prevents restoration when an active VIN/name conflicts. Geotab archives users while retaining data and can reactivate them. Motive deactivates users/vehicles and preserves historical data/audit context. | Driver `CLHQ-D-0003` archive, refresh/logout-login persistence, read-only history, and reactivation audit passed. | Core recoverable lifecycle is demonstrated for drivers; broader entity coverage is not exhaustive. |
| Large-list usability | Fleetio import history is searchable/filterable and exposes status/count columns; its asset/contact lists support searching, filtering, sorting, and group filtering. Geotab supports group filters and paginated/sorted device retrieval. Samsara APIs use cursor pagination. | Exact 1,000/1,250/1,100/300 totals persisted. Asset/device full-volume search, sort, server paging, and export passed. Exact-SHA vehicle and driver full exports completed in 15.289s/15.304s with complete ordered unique rows. | Export completeness is now credible; all four exact viewports and formal percentile performance traces remain required. |
| Auditability | Motive documents audit logging for imports, user changes, vehicle/device assignment, and deactivation. Fleetio retains import history with created/updated/error counts and supports rollback of new imports. | The pack contains screenshots but no product-level import history/rollback proof, recording, console log, or failed-network capture. | Evidence preservation exists, but product auditability and clean-console/network requirements remain open. |

## Product-specific observations

### Samsara

Samsara sets a strong bulk-onboarding and identity baseline: its driver CSV workflow provides a blank or current-data template, preview, error review, and explicit finalization for up to 5,000 rows. It treats its internal ID as non-editable, supports external IDs, and exposes driver-vehicle assignment intervals. Roles can be custom and restricted by tags. OpsTrax should match the safe preview/correct/retry experience and make identity-key behavior obvious to the customer.

Sources: [Add or Edit Drivers in Bulk](https://kb.samsara.com/hc/en-us/articles/26089120101901-Add-or-Edit-Drivers-in-Bulk), [Statically Assign Drivers to Vehicles in Bulk](https://kb.samsara.com/hc/en-us/articles/34793509414541-Statically-Assign-Drivers-to-Vehicles-in-Bulk), [Driver-Vehicle Assignments](https://developers.samsara.com/docs/migrating-from-driver-vehicle-assignment-or-vehicle-driver-assignment-endpoints), [Custom Roles](https://kb.samsara.com/hc/en-us/articles/360043852111-Custom-Roles), [Tags](https://kb.samsara.com/hc/en-us/articles/360043275091-Tags).

### Motive

Motive combines individual and bulk vehicle onboarding, device assignment from the vehicle profile, bulk deactivation, group-scoped custom roles, temporary accounts, invitation state, and activity logging. Its documented vehicle/asset document workflows add upload history and expiry handling. OpsTrax needs browser proof that role scope governs both visibility and mutation, and that lifecycle actions leave an auditable trail.

Sources: [Create & Edit Vehicles](https://helpcenter.gomotive.com/hc/en-us/articles/30205612188061-Create-Edit-Vehicles), [Bulk Upload Asset Profiles](https://helpcenter.gomotive.com/hc/en-us/articles/6190207245981-Bulk-Upload-Asset-Profiles), [Roles and Permissions](https://helpcenter.gomotive.com/hc/en-us/articles/30898429926685-Roles-and-Permissions), [Fleet Users](https://helpcenter.gomotive.com/hc/en-us/articles/31185986332701-Fleet-Users), [Audit Log](https://helpcenter.gomotive.com/hc/en-us/articles/31025862528669-Audit-Log), [Documents Use Cases and Structure](https://helpcenter.gomotive.com/hc/en-us/articles/34544557798301-Documents-Use-Cases-and-Structure).

### Geotab

Geotab's organizational model is a mature comparison for multi-branch fleets: hierarchical groups control assets, users, reporting, and data access, while security clearances separately govern feature permissions. Archived user accounts retain data and can be reactivated. Its documented pagination/sorting model demonstrates the expectation that large datasets remain deterministic and navigable. OpsTrax must show the same separation of functional permission and branch data scope in direct and navigated views.

Sources: [Groups overview](https://support.geotab.com/help/mygeotab/groups-and-rules/groups/groups-overview), [Group assignments](https://support.geotab.com/help/mygeotab/groups-and-rules/groups/group-assignments), [Security clearances](https://support.geotab.com/help/mygeotab/managing-users-and-vehicles/users/security-clearances), [Archived user accounts](https://support.geotab.com/help/mygeotab/managing-users-and-vehicles/users/archived-user-accounts), [API concepts: pagination](https://developers.geotab.com/myGeotab/guides/concepts/index.html).

### Fleetio

Fleetio is the strongest direct comparison for asset-master workflows. It supplies entity-specific templates, field mapping, review, import history, downloadable error files, and rollback. Its recommended 500-row import size validates a batched 500-row design when the workflow is reliable. It also preserves effective-dated assignment history and uses recoverable archive/restore behavior with duplicate checks on restore. These are appropriate acceptance patterns for OpsTrax Module 1.

Sources: [New Data Import Overview](https://help.fleetio.com/importarexportar-datos/new-data-import-overview-5), [Import Errors](https://help.fleetio.com/importexport-data/import-errors-8), [Vehicle Import Guide](https://help.fleetio.com/en_US/vehicle-import-guide), [View & Edit Vehicle Assignments](https://help.fleetio.com/en_US/vehicles/view-edit-vehicle-assignments), [Vehicle Assignments Scheduler](https://help.fleetio.com/vehicles/vehicle-assignments-scheduler), [Archive, Restore or Delete Vehicles](https://help.fleetio.com/en_US/archive-restore-or-delete-vehicles), [Groups & Subgroups](https://help.fleetio.com/getting-started-for-account-owners-admins/groups-subgroups).

## Module 1 competitive acceptance bar

OpsTrax should not be described as competitor-ready until visible Chrome evidence demonstrates all of the following:

- Template download, valid import, partial/failed import behavior, correction, retry, and reconciliation for every entity type.
- Stable identity keys and explicit duplicate conflicts; no silent overwrite or cross-identity reassignment.
- Current, past, and future/effective assignment dates, with history surviving reassignment and lifecycle changes.
- Feature permission and branch scope enforced independently in navigation, lists, detail pages, exports, and direct URLs.
- Recoverable archive/reactivate behavior that retains history and blocks active duplicate identities.
- Search, multi-filter, deterministic sort, pagination, and export over the complete certification dataset.
- Import/audit history with actor, timestamp, outcome, counts, and downloadable row-level errors.
- Usable desktop, tablet, and phone layouts without blocked actions or horizontal-loss defects.

## Current verdict contribution

**BLOCKED / not certifiable from the present evidence.** This is a Module 1 certification verdict, not a product-wide failure. No open P0/P1 finding remains in the final device-transfer or fleet-master export paths, and the populated staging tenant is suitable for a controlled pilot. Certification remains blocked by the explicit Customer entitlement/account gap, unavailable exact viewport control, unavailable Maintenance Center entitlement, missing recording/HAR, absent readiness-expiry records, and the incomplete controlled vehicle/driver correction cycle.

# Module 1 competitor assessment

Assessment date: 2026-08-25
Scope: Fleet Identity & Asset Master only
OpsTrax candidate observed in evidence: `6d06893881f3fd58482681549735892a201f2b39`

## Executive conclusion

OpsTrax now exposes credible browser workflows for branch setup and template-based vehicle, driver, device, and trailer/asset import. The current evidence does **not** yet establish competitive readiness for a 1,000-vehicle customer. It proves the existence of the surfaces and some setup persistence, but not the complete large-volume journey, identity-integrity behavior, assignment-history preservation, lifecycle behavior, or role/branch enforcement.

The most material competitive gaps are evidence gaps that must be resolved in Chrome before certification:

1. No completed 1,000-vehicle / 1,250-driver / 1,100-device / 300-asset import and reconciliation.
2. No browser proof that invalid and duplicate identities are rejected without partial corruption.
3. No effective-dated assign/reassign history proof across driver, vehicle, device, and trailer relationships.
4. No archive/reactivate proof for the Module 1 records.
5. No complete branch-bound and read-only role journey; the current pack proves account creation and scope configuration, not authorization outcomes.
6. No measured search, filter, sort, pagination, export, or responsive usability at full volume.
7. User onboarding is form-based in the observed OpsTrax UI; no customer-facing user CSV import surface is evidenced.

## Market benchmark

| Capability | Leading-product benchmark | OpsTrax browser evidence | Current assessment |
|---|---|---|---|
| Bulk onboarding | Samsara supports driver CSV preview/error review/finalization for fewer than 5,000 entries. Motive supports vehicle and asset imports, including downloadable templates and import history. Fleetio provides downloadable templates, mapping/review, import history, error files, and recommends batches of 500 rows or fewer. | Template/import dialogs are evidenced for vehicles, drivers, devices, and returnable assets. The observed OpsTrax wizard states a 500-row maximum. | Surface parity is plausible and 500-row batching is not itself uncompetitive, but end-to-end volume, correction, and reconciliation remain unproved. |
| Identity integrity | Samsara uses an immutable internal driver ID during bulk edits and warns that malformed spreadsheet handling can overwrite identifiers. Fleetio distinguishes new versus update imports through `fleetio_id`; its import guides require unique record identifiers for relational imports. Motive centralizes profile/device assignment and audits import/admin actions. | Device import modal and code changes are supporting evidence only. No Chrome evidence currently shows duplicate VIN, driver identity, serial, IMEI, asset tag, or update-key behavior. | Major certification gap. Duplicate and update semantics must be visibly proven before assignments are created at scale. |
| Assignment and history | Samsara exposes time-ranged driver-vehicle assignment records and supports bulk static assignments. Fleetio presents current, past, and future assignments, start/end times, reassignment, and assignment history; it rejects overlapping open-ended assignments. | Assignment entitlement recovery is evidenced, but no record-level assignment, reassignment, effective dates, or history is preserved in the pack. | Behind the demonstrated market bar until the complete assignment lifecycle is shown in Chrome. |
| Branch/group scoping | Samsara combines roles with tags to restrict access to tagged drivers, vehicles, and devices. Motive supports custom roles and group-bound visibility. Geotab groups organize assets/users and control data access, with site-wide group filtering. Fleetio uses hierarchical groups/subgroups and record-set permissions. | Five branches persist; branch selection exists on user creation; 24 active role accounts and branch scopes are evidenced. | Configuration surface exists. Enforcement is not certified until cross-branch positive and negative journeys pass for each relevant role and direct URL. |
| Roles and least privilege | Samsara and Motive provide default and custom granular roles; Geotab provides standard and custom security clearances; Fleetio separates owner/admin/user powers and group record sets. | Fleet Manager and custom Maintenance Manager/Executive role creation are evidenced. A prior evidence item shows missing telematics keys; candidate `6d068938…` is intended to expose narrow grants. | Role design is directionally competitive. Actual login results and mutation denials remain pending. The product permission catalog also lacked a Documents grant in the captured UI. |
| Lifecycle/archive | Fleetio retains historical vehicle data in read-only archived records, supports restore, and prevents restoration when an active VIN/name conflicts. Geotab archives users while retaining data and supports reactivation. Motive deactivates users/vehicles and preserves historical data/audit context. | No Module 1 record archive/reactivate journey is present. | Material gap. OpsTrax must prove recoverable lifecycle handling and duplicate-safe reactivation. |
| Large-list usability | Fleetio import history is searchable/filterable and exposes status/count columns; its asset/contact lists support searching, filtering, sorting, and group filtering. Geotab supports group filters and paginated/sorted device retrieval. Samsara APIs use cursor pagination. | Existing images show setup lists, not a populated 1,000-vehicle table. | Unassessed at target volume. Chrome timings, pagination correctness, export reconciliation, and all four viewports are required. |
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

**PILOT / not yet certifiable from the present evidence.** This is not a product-wide verdict. It is the Module 1 competitor-readiness contribution based on evidence currently preserved. A final `CERTIFIED` verdict requires the unresolved browser journeys and full-volume reconciliation to pass.

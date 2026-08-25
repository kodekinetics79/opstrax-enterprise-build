# Module 1 evidence inventory

Inventory date: 2026-08-25
Artifact root: `artifacts/cert-large-20260825/`
Visible Chrome screenshots inventoried: **80 PNG files**

## Coverage summary

| Evidence family | Representative files | What it establishes | Important limitation |
|---|---|---|---|
| Exact-SHA staging/readiness | `chrome/render-staging-pr53-live-ready-200-exact-sha.png`, `chrome/render-service-vars-exact-live-ready-200.png`, `chrome/stable-tenant-session-current-exact-sha.png` | Render readiness and exact-SHA surfaces were visibly exercised. | Latest role evidence points to `6d068938…`; verify backend/frontend exact SHA together for final pack. |
| Isolated tenant and authentication | `chrome/platform-tenant-cert-large-created.png`, `chrome/tenant-admin-active-after-activation.png`, `chrome/stable-tenant-admin-login-exact-sha.png`, `chrome/tenant-admin-second-login-success.png` | `CERT-LARGE-20260825` creation and administrator authentication/persistence journey. | Non-admin role logins are not yet evidenced. |
| Entitlements | `chrome/platform-dispatch-telematics-enabled-only.png`, `chrome/module1-assignments-dispatch-entitlement-resolved.png`, `chrome/module1-customers-not-in-plan-71fd868.png` | Only Dispatch and Telematics were intentionally enabled; assignments became reachable; Customer remains outside plan. | Customer role journey remains blocked. |
| Branches | `chrome/module1-branches-five-persisted-71fd868.png`, `chrome/module1-ea2aca6-stable-tenant-sidebar-branches.png`, `chrome/module1-ea2aca6-branches-search-navigation-persisted.png` | Five branches persisted; Branches became navigable/searchable after fix and hard-refresh regression was corrected. | Branch ownership enforcement and cross-branch denials remain unproved. |
| Import surfaces | Vehicle, driver, device, and returnable-asset template/import screenshots | Customer-visible templates and import dialogs exist; observed batch limit is 500 rows. | No completed valid, invalid, duplicate, retry, or reconciliation run at target volume. |
| Roles/accounts | `chrome/module1-6d068938-24-active-role-accounts.png`, `chrome/module1-6d068938-five-fleet-managers-active.png`, custom role and branch-scope screenshots | 24 active role accounts, five Fleet Managers, custom Maintenance Manager/Executive role setup. | Login and permissions outcomes are not proved; target is 25 accounts. |
| Defects/regressions | Pre-fix missing controls, 403, wrong driver guidance, and hard-refresh crash screenshots | Objective before/after evidence for several staging and Module 1 defects. | Some fixes have only a surface retest, not full workflow retest. |

## Data artifacts present

No large-fleet input CSV files, import result files, error files, export files, recordings, console logs, network logs/HAR, or timing reports were found under the artifact root at this evidence cut. Real templates were reportedly downloaded to the user's Downloads directory, but they are not preserved inside the certification artifact pack.

Expected downloaded templates reported outside this artifact root:

- `/Users/zackkhan/Downloads/vehicles-import-template (1).csv`
- `/Users/zackkhan/Downloads/drivers-import-template (1).csv`
- `/Users/zackkhan/Downloads/devices-import-template.csv`
- `/Users/zackkhan/Downloads/assets-import-template.csv`

These should be copied into a non-secret `inputs/templates/` evidence directory before final packaging, preserving original bytes and timestamps. Generated certification datasets should be added separately from product-generated exports and row-level error reports.

## Evidence integrity

All 80 PNG files were enumerated and SHA-256 hashed during this inventory. The detailed manifest should be regenerated after the browser cycle is complete so it covers final screenshots, recordings, network/console artifacts, datasets, exports, and performance files. The current screenshot set contains one duplicate-byte pair (two files with the same SHA-256); that is not inherently invalid, but final evidence should avoid presenting one image as proof of two distinct events.

## Required additions before closure

1. Exact candidate and deployed SHA captured in the authenticated product after final redeploy.
2. Original product templates plus valid, invalid, duplicate, and corrected CSV inputs.
3. Import receipts/results and exported reconciliation files for 1,000 vehicles, 1,250 drivers, 1,100 devices, and 300 assets.
4. Fresh login and direct-URL evidence for Fleet Manager, Dispatcher, Maintenance Manager, Driver, and Executive; Customer evidence if entitlement is explicitly authorized.
5. Assignment/reassignment and effective-date history captures.
6. Archive/reactivate and readiness/document/expiry captures.
7. Search/filter/sort/pagination/export captures and timings at full volume.
8. Screenshots at 1440×900, 1280×800, 768×1024, and 390×844.
9. Screen recording, console log, failed-request log/HAR, and an evidence manifest with hashes.
10. Independent adversarial review report and final defect retest disposition.

## Current evidence verdict

The inventory supports a **PILOT** evidence state, not `CERTIFIED`. It demonstrates a working staging foundation and several repaired Module 1 setup surfaces, but the core large-fleet and multi-role acceptance journeys remain open.

# Module 1 evidence inventory

Inventory date: 2026-08-25
Artifact root: `artifacts/cert-large-20260825/`
Visible Chrome screenshots currently present locally: **166 PNG files**

## Coverage summary

| Evidence family | Representative files | What it establishes | Important limitation |
|---|---|---|---|
| Exact-SHA staging/readiness | `chrome/render-staging-pr53-live-ready-200-exact-sha.png`, `chrome/render-service-vars-exact-live-ready-200.png`, `chrome/stable-tenant-session-current-exact-sha.png` | Render readiness and exact-SHA surfaces were visibly exercised. | Latest role evidence points to `6d068938…`; verify backend/frontend exact SHA together for final pack. |
| Isolated tenant and authentication | `chrome/platform-tenant-cert-large-created.png`, `chrome/tenant-admin-active-after-activation.png`, `chrome/stable-tenant-admin-login-exact-sha.png`, `chrome/tenant-admin-second-login-success.png` | `CERT-LARGE-20260825` creation and administrator authentication/persistence journey. | Non-admin role logins are not yet evidenced. |
| Entitlements | `chrome/platform-dispatch-telematics-enabled-only.png`, `chrome/module1-assignments-dispatch-entitlement-resolved.png`, `chrome/module1-customers-not-in-plan-71fd868.png` | Only Dispatch and Telematics were intentionally enabled; assignments became reachable; Customer remains outside plan. | Customer role journey remains blocked. |
| Branches | `chrome/module1-branches-five-persisted-71fd868.png`, `chrome/module1-ea2aca6-stable-tenant-sidebar-branches.png`, `chrome/module1-ea2aca6-branches-search-navigation-persisted.png` | Five branches persisted; Branches became navigable/searchable after fix and hard-refresh regression was corrected. | Branch ownership enforcement and cross-branch denials remain unproved. |
| Import surfaces | Vehicle, driver, device, and returnable-asset template/import screenshots plus 28 governed certification CSVs | Customer-visible templates and import dialogs exist; 1,000 vehicles and 500 drivers have persisted through observed valid imports. | Remaining 750 drivers, 1,100 devices, 300 assets, and controlled invalid/duplicate/correction journeys remain open because the native picker became unavailable. |
| Roles/accounts | `chrome/module1-6d068938-24-active-role-accounts.png`, `chrome/module1-6d068938-five-fleet-managers-active.png`, custom role and branch-scope screenshots | 24 active role accounts, five Fleet Managers, custom Maintenance Manager/Executive role setup. A CL-HQ Fleet Manager Chrome session denied a NE-HUB driver search on b131, but its screenshot bytes were not preserved. | Complete login/permission matrix is not proved; Customer entitlement and the 25th account remain externally blocked. |
| Defects/regressions | Pre-fix missing controls, 403, wrong driver guidance, and hard-refresh crash screenshots | Objective before/after evidence for several staging and Module 1 defects. | Some fixes have only a surface retest, not full workflow retest. |
| Exact `ef031fa` corrective cycle | `chrome/ef031fa-exact-frontend-api-live-gate.png`, lifecycle/driver records, zero-volume, export-failure, document, and Render captures | Matching full frontend/API SHA was visibly Live; D0003 Active and masked-license records passed; asset/device export SQL failures and remaining volume/tool blockers are objectively preserved. | Export fixes require a new exact-SHA Chrome retest; dataset and file-selection blockers remain. |

## Data artifacts present

The input directory now contains 30 CSV files:

- 20 valid branch batches totaling 1,000 vehicles, 1,250 drivers, 1,100 devices, and 300 returnable assets;
- eight controlled invalid/duplicate files (one invalid and one duplicate file per entity);
- the real b131 driver template download, whose missing `branchCode` proves M1-027;
- the real b131 driver server export with 501 lines (header plus all 500 records present at that evidence cut).

`module1-input-manifest.md` records the governed input counts and hashes. Product-generated preview error receipts, corrected re-uploads, remaining entity exports, recordings, console logs, failed-request logs/HAR, and final performance results are still required.

## Evidence integrity

The screenshot manifest must be regenerated after the corrective browser cycle so it covers final screenshots, recordings, network/console artifacts, datasets, exports, and performance files. The Browser Evidence Lead corrected an evidence-preservation error on b131: several reported screenshot path names never existed because returned bytes were not written. Those claims are withdrawn in the defect ledger. Only `chrome/b1313ed5-current-live-dashboard.png` is verified for b131, at 22,010 bytes and SHA-256 `be6c056501649307af6ba5ff86edfdc38eef6ee4d0d6abd7bdda0ac6b6f262ab`.

The `ef031fa` cycle added 15 byte-preserved PNGs whose local SHA-256 values were rechecked after capture. It includes both the initially failed frontend-provenance gate and the corrected full-SHA Live gate, plus the two export 500s and their visible Render error details. These are valid pre-fix evidence; they do not close M1-030 without a new deployed-SHA browser retest.

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

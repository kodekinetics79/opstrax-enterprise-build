# Module 1 evidence inventory

Inventory date: 2026-08-26
Artifact root: `artifacts/cert-large-20260825/`
Visible Chrome screenshots currently present locally: **338 PNG files**

## Coverage summary

| Evidence family | Representative files | What it establishes | Important limitation |
|---|---|---|---|
| Exact-SHA staging/readiness | `chrome/5d7c165-export-retest.txt`, `readiness/5d7c165-health-ready-exact.json` | The rendered product showed matching full frontend/API SHA `5d7c165…`, Live, tenant `CERT-LARGE`, and All systems operational; supporting readiness returned HTTP 200 at the same SHA. | Native screenshot capture remained attached to an unrelated Chrome window, so the final gate is preserved as a text evidence ledger plus sanitized readiness JSON rather than a claimed image. Raw headers containing rotating CSRF values are excluded. |
| Isolated tenant and authentication | `chrome/platform-tenant-cert-large-created.png`, `chrome/tenant-admin-active-after-activation.png`, `chrome/stable-tenant-admin-login-exact-sha.png`, `chrome/tenant-admin-second-login-success.png` | `CERT-LARGE-20260825` creation and administrator authentication/persistence journey. | Non-admin role logins are not yet evidenced. |
| Entitlements | `chrome/platform-dispatch-telematics-enabled-only.png`, `chrome/module1-assignments-dispatch-entitlement-resolved.png`, `chrome/module1-customers-not-in-plan-71fd868.png` | Only Dispatch and Telematics were intentionally enabled; assignments became reachable; Customer remains outside plan. | Customer role journey remains blocked. |
| Branches | `chrome/module1-branches-five-persisted-71fd868.png` plus final-SHA Fleet Manager, Dispatcher, and Maintenance Manager branch-negative captures | Five branches persisted; representative branch-bound roles could see CL-HQ data and could not search another branch's records. | Record-level negative coverage is representative rather than exhaustive for every role/entity combination. |
| Import/export surfaces | Vehicle, driver, device, and returnable-asset template/import evidence plus governed certification CSVs and exact `5d7c165` vehicle/driver exports | Customer UI imports persisted exactly 1,000 vehicles, 1,250 drivers, 1,100 devices, and 300 assets. Complete vehicle/driver exports reconcile counts, branches, governed fields, ordering, uniqueness, and licence masking. Device/asset controlled invalid and duplicate behavior is preserved. | Controlled vehicle/driver correction and duplicate-preview cycles remain incomplete because of external Chrome download/file-picker control failures. |
| Roles/accounts | Role setup plus `75eda29` fresh-login/branch/direct-URL evidence | Fleet Manager, Dispatcher, Maintenance Manager, Driver, and Executive representative journeys passed; 24 active accounts are present. | Customer entitlement and the twenty-fifth account remain externally blocked. |
| Defects/regressions | Pre-fix missing controls, 403, wrong driver guidance, and hard-refresh crash screenshots | Objective before/after evidence for several staging and Module 1 defects. | Some fixes have only a surface retest, not full workflow retest. |
| Exact `ef031fa` corrective cycle | `chrome/ef031fa-exact-frontend-api-live-gate.png`, lifecycle/driver records, zero-volume, export-failure, document, and Render captures | Matching full frontend/API SHA was visibly Live; D0003 Active and masked-license records passed; asset/device export SQL failures and remaining volume/tool blockers are objectively preserved. | Export fixes require a new exact-SHA Chrome retest; dataset and file-selection blockers remain. |

## Data artifacts present

The artifact pack contains 37 input/workflow files, including governed CSV inputs, the separately stored custody-conflict fixture, product-generated one-time device credential downloads, and a safe certification document fixture:

- 20 valid branch batches totaling 1,000 vehicles, 1,250 drivers, 1,100 devices, and 300 returnable assets;
- eight controlled invalid/duplicate files (one invalid and one duplicate file per entity);
- the real b131 driver template download, whose missing `branchCode` proves M1-027;
- the real b131 driver server export with 501 lines (header plus all 500 records present at that evidence cut).

`module1-input-manifest.md` records the governed input counts and hashes. Controlled device/asset preview errors and complete exports for all four entity families are preserved. Corrected vehicle/driver re-uploads, recordings, and structured failed-request logs/HAR are still required.

## Evidence integrity

The screenshot manifest must be regenerated after the corrective browser cycle so it covers final screenshots, recordings, network/console artifacts, datasets, exports, and performance files. The Browser Evidence Lead corrected an evidence-preservation error on b131: several reported screenshot path names never existed because returned bytes were not written. Those claims are withdrawn in the defect ledger. Only `chrome/b1313ed5-current-live-dashboard.png` is verified for b131, at 22,010 bytes and SHA-256 `be6c056501649307af6ba5ff86edfdc38eef6ee4d0d6abd7bdda0ac6b6f262ab`.

The `ef031fa` cycle added 15 byte-preserved PNGs whose local SHA-256 values were rechecked after capture. It includes both the initially failed frontend-provenance gate and the corrected full-SHA Live gate, plus the two export 500s and their visible Render error details. These are valid pre-fix evidence; they do not close M1-030 without a new deployed-SHA browser retest.

Exact `8483733` deployment evidence is preserved as `chrome/8483733-render-deploy-succeeded.png` and `chrome/8483733-exact-frontend-api-live.png`, both with rechecked SHA-256 hashes. The subsequent Asset Export click entered an ambiguous Chrome extension hang/reset: no download event or file was preserved, so supporting HTTP 200 probes do not certify the rendered export workflow.

## Required additions before certification

1. Authorize/enable the Customer/CRM entitlement and create/authenticate the twenty-fifth role account.
2. Complete controlled invalid/duplicate correction cycles for vehicles and drivers.
3. Create and exercise safe nonpersonal document/expiry/readiness records; enable Maintenance Center if its journey remains required.
4. Capture exact 1440×900, 1280×800, 768×1024, and 390×844 layouts with a controller that reports viewport dimensions.
5. Preserve a screen recording and structured network/HAR evidence.

## Current evidence verdict

The Module 1 certification verdict is **BLOCKED**, not `CERTIFIED`. The final exact-SHA device-transfer and fleet-master export P1s are closed and current Chrome diagnostics are clean. Mandatory external entitlement/viewport capabilities and the explicitly listed evidence gaps still prevent certification; Module 2 has not begun.

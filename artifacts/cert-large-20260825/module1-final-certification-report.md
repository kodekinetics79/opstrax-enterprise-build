# Module 1 final certification report

Date: 2026-08-26

Module: Fleet Identity & Asset Master — Large-Fleet Client Certification

Tenant: `CERT-LARGE-20260825`

Verdict: **BLOCKED**

## Release identity and staging readiness

- Candidate and deployed SHA: `7e98a39d66d67dbd5bb5419602532d7ec1aa23d1`
- Vercel staging deployment: `dpl_E8wscufzWvRjT6c1ETet3AFqbEwq`
- Render staging deployment: `dep-da77n195efls73ccqr2g`
- Render deployment reached Live in 1m44s.
- The authenticated rendered product showed the matching full frontend and API SHA, tenant `CERT-LARGE`, Live, and **All systems operational**.
- Supporting `/health/ready` request returned HTTP 200 in 0.869442s with the same deployment SHA, connected database (70ms), valid configuration, and zero RLS, grant, or tenant-coverage violations.
- Production was not touched.

## Certification population

All fleet records were generated as synthetic nonpersonal data and persisted through customer-facing forms/import workflows, not SQL seeding.

| Population | Required | Rendered-product result |
|---|---:|---:|
| Branches | 5 | 5 |
| Role users | 25 | 24; Customer account externally blocked |
| Vehicles | 1,000 | 1,000 |
| Drivers | 1,250 | 1,250 |
| Devices | 1,100 | 1,100 |
| Trailers/assets | 300 | 300 |

The exact valid, invalid, and duplicate source files and their SHA-256 hashes are listed in `module1-input-manifest.md`. Device one-time credential exports are preserved separately and must remain restricted.

## Browser journey result

Passed in visible Chrome:

- exact-SHA and Live release gate;
- valid full-volume imports and persistence totals;
- controlled invalid/duplicate device and asset rejection;
- actionable and atomic asset custody-conflict handling;
- large-volume asset/device search, filter, deterministic sort, pagination, and complete export;
- vehicle and driver boundary searches at the complete tenant totals;
- fresh Fleet Manager, Dispatcher, Maintenance Manager, Driver, and Executive logins with representative branch/read-only/direct-URL restrictions;
- vehicle/driver reassignment and effective-dated history;
- trailer custody reassignment and effective-dated history;
- driver archive, refresh/logout-login persistence, and reactivation;
- one controlled device transfer, submitted once without retry, with full history persistence and clean current diagnostics.

Final device retest details: `CLHQ-DEV-0001` transferred from `CLHQ-V-0001` to `CLHQ-V-0002` effective 2026-08-26 05:01Z. The new row retained Bay 2, odometer 57,838, method and reason; the closed V0001 row retained Bay 1, odometer 49,919, original method/reason, and effective-to time. Refresh and full logout/login preserved both rows. Provider Audit remained neutral `Restricted`. Current Chrome warning/error logs were empty and there was no unresolved current material failed request.

## Performance observations

| Journey | Observed result |
|---|---|
| 200-vehicle preview | 6.9s on the corrected import path |
| 200-vehicle commit | server POST 200 in 39.25s; visible completion about 62s |
| 250-driver preview | 6.4–7.5s |
| 250-driver commit | 50.2–50.4s |
| Asset custody-conflict commit-to-actionable-alert | 12.517s |
| Final device transfer | visible success about 10.4s; POST 200 in 1907.8ms |
| Device-history refresh | about 7.2s |
| Full logout/fresh login | about 14.5s |
| Re-open persisted device history | about 7.6s |

The 1,000-vehicle/1,250-driver totals and server-paged 1,100-device/300-asset registers remained usable in the observed journeys. A structured end-to-end performance trace was not available, so these are browser observations rather than a formal percentile benchmark.

## Independent adversarial review

The PR #62 transfer fix received an independent adversarial review focused on tenant isolation, runtime privilege separation, ingestion/transfer races, rollback, and deadlock risk. The reviewer returned CLEAR with no remaining P0/P1. Focused PostgreSQL runtime-role, cross-tenant, deterministic race/recheck, and privilege-rollback cases passed. This is supporting evidence; the rendered Chrome retest is the acceptance evidence.

## Why the verdict is BLOCKED

No open P0/P1 remains in the final device-transfer path, but Module 1 cannot honestly be marked CERTIFIED because mandatory acceptance evidence is unavailable or incomplete:

1. Customer/CRM entitlement is not enabled, preventing the twenty-fifth account and Customer journey.
2. The installed visible-Chrome controller cannot set/report the required 1440×900, 1280×800, 768×1024, and 390×844 viewports; native resize attempts remained at 1728×851/940.
3. Maintenance Center entitlement is unavailable.
4. Certification data has no linked readiness/expiry documents, so expiry behavior is not proved.
5. Full vehicle and driver export artifacts are missing.
6. Controlled invalid/duplicate correction cycles for vehicles and drivers are incomplete.
7. No screen recording or structured HAR was produced.

These are explicit certification blockers, not permission to start Module 2. Module 2 has not begun.

## Artifact index

- Input files and hashes: `module1-input-manifest.md` and `input/`
- Special controlled-workflow fixtures: `controlled-workflows/`
- Chrome evidence: `chrome/`
- Role/journey matrix: `module1-role-journey-matrix.md`
- Defect/fix/retest ledger: `module1-defect-fix-retest-ledger.md`
- Product manual: `../../docs/product-manual-fleet-identity.md`
- Competitor assessment: `module1-competitor-assessment.md`
- Supporting readiness result: `readiness/7e98a39-health-ready.txt`

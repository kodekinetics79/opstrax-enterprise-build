# Module 2 browser evidence inventory

Tenant: `CERT-LARGE-20260825`  
Primary acceptance surface: visible Google Chrome  
Module: Telematics and Live Operations

## Release identity

- Current candidate merge SHA: `4cdb6c4cf3a37b11529007f90dd0f685cc8a58a7`
- Staging frontend deployed SHA: `4cdb6c4cf3a37b11529007f90dd0f685cc8a58a7`
- Staging API deployed SHA: `4cdb6c4cf3a37b11529007f90dd0f685cc8a58a7`
- Staging frontend: `https://opstrax-staging-certification.vercel.app`
- Staging API readiness: `https://opstrax-staging-api.onrender.com/health/ready`
- Production was not changed by this certification cycle.

## Preserved rendered evidence

| Evidence | Candidate | Role / scope | Result |
|---|---|---|---|
| Render dashboard visibly reported exact commit `a0da774…` as `Deploy succeeded|Live`; readiness returned HTTP 200 with database connected, fleet contract ready, and zero critical-worker violations after startup grace | `a0da774…` | staging foundation | Pass |
| Runtime badge displayed exact frontend/API SHA `a0da774f932015f2444cc9e54fa610715416b785` and `Staging` after startup grace | `a0da774…` | Maintenance Manager stale session | Pass for release identity only |
| Runtime identity changed from `Mismatch` to `Starting` to `Staging` only after exact frontend/API SHA agreement and completed API startup | `d0e01c9…` | Dispatcher / CL-HQ | Pass |
| Control Tower rendered 220 branch-authorized devices, 100 rows per page, three pages | `d0e01c9…` | Dispatcher / CL-HQ | Pass |
| Diagnostics and Vehicle Intelligence actions were absent when the Dispatcher lacked permission | `d0e01c9…` | Dispatcher / CL-HQ | Pass |
| Direct `/obd-j1939` navigation rendered a safe permission denial without diagnostic data | `d0e01c9…` | Dispatcher / CL-HQ | Pass |
| GPS search for immutable serial `CLHQ-DEV-0138` rendered one matching record and separated serial from model in the detail drawer | `d0e01c9…` | Dispatcher / CL-HQ | Pass |
| Maintenance Manager navigation/page/API permission parity; 200 branch-scoped records, four pages, sort/search/filter and least-privilege disabled actions | `a894229…` | Maintenance Manager / CL-HQ | Pass; `chrome/a894229-maintenance-obd-exact-staging-pass.png` and companion text snapshot |
| Permission-scoped Control Tower and Device Health rendered 220 CL-HQ devices without manufacturing restricted GPS evidence | `a894229…` | Maintenance Manager / CL-HQ | Pass; `chrome/a894229-maintenance-control-tower-pass.png` and `chrome/a894229-maintenance-device-health-pass.png` |
| Sign out, fresh sign in, direct return to OBD/J1939 and persisted 200-record branch result | `a894229…` | Maintenance Manager / CL-HQ | Pass; `chrome/a894229-maintenance-obd-logout-relogin-pass.png` |
| Exact frontend/API full SHA and `Staging` runtime badge after environment correction | `a894229…` | Maintenance Manager / CL-HQ | Pass |
| Render exact-commit deployment reported `Deploy succeeded|Live`; public readiness returned HTTP 200 with matching deployment header/body and `Staging` | `4cdb6c4…` | staging foundation | Pass; `chrome/4cdb6c4-render-deploy-succeeded.jpg` and `chrome/4cdb6c4-staging-readiness.txt` |
| Device Health detail opens without live-position permission and truthfully reports no live snapshot | `4cdb6c4…` | Maintenance Manager / CL-HQ | Pass; `chrome/4cdb6c4-maintenance-device-health-parity-pass.jpg` and companion text snapshot |
| Device `CLHQ-DEV-0200` reports Offline / 32% consistently in table and detail after **Reload Snapshot** | `4cdb6c4…` | Maintenance Manager / CL-HQ | Pass; `chrome/4cdb6c4-maintenance-device-health-parity-pass.jpg` |
| Browser refresh plus full sign-out/sign-in retained the device result and exact runtime identity | `4cdb6c4…` | Maintenance Manager / CL-HQ | Pass; `chrome/4cdb6c4-maintenance-device-health-relogin-pass.jpg` |
| Captured Chrome console errors and warnings for the final Device Health journey | `4cdb6c4…` | Maintenance Manager / CL-HQ | Pass; zero entries in `chrome/4cdb6c4-maintenance-console-errors.json` |
| Tenant-wide 1,100-device and 1,000-position views | final candidate | Tenant/Fleet Administrator | Pending final Chrome retest |
| Read-only and portal negative authorization | final candidate | Executive, Driver, Customer | Pending final Chrome retest |
| Responsive layouts and Live Map soak | final candidate | authorized roles | Pending final Chrome retest |

## Supporting evidence

- Signed-ingest execution output is written outside the repository to the protected certification directory. Its final summary and SHA-256 digest must be copied into the final report without copying device credentials.
- CI and focused tests support, but do not replace, the rendered evidence.
- The defect ledger records each browser finding, fix, and final-candidate retest.
- The role/journey matrix records allowed and denied rendered customer paths.
- The performance report records timings measured through visible Chrome.
- The competitor assessment is based on official Samsara, Geotab, and Motive sources checked on 2026-08-28.

## Evidence hygiene

Screenshots, recordings, network failures, console failures, and timings must identify the exact candidate. No credential, raw authentication token, device secret, or readiness header is part of the customer evidence pack. A page rendering without completing its customer workflow is not a pass.

The files added for the `4cdb6c4…` cycle are integrity-identified in `4cdb6c4-module2-browser-evidence-sha256.md`.

## Evidence-pack identity

The deployed product candidate is `4cdb6c4cf3a37b11529007f90dd0f685cc8a58a7`, merged by PR #101. This evidence closes M2-007 and M2-008 but does not by itself certify the module.

# Module 1 current-SHA browser cycle

Evidence date: 2026-08-28 EDT

Tenant: `CERT-LARGE-20260825`

Candidate and staging deployed SHA: `4960df41edd4399d3b6c7b07fe8145936cf1905b`

Staging frontend deployment: `dpl_9e893ihqY5vvSMkU5fCai6YLJfi5`

Staging backend deployment: `dep-da8i1crbc2fs73aat4sg`

## Release gate

- Visible Chrome showed matching frontend/API SHA `4960df41edd4399d3b6c7b07fe8145936cf1905b` on the authenticated Fleet Manager view.
- `/health/ready` returned HTTP 200 after startup grace at uptime 140 seconds.
- Database, configuration, key ring and fleet contract were ready; raw, effective, missing, stale and failed critical-worker violations were all zero.
- The production Vercel project was found auto-deploying `main` while still compiled against the retired `osptrax-fleet-management.onrender.com` origin, which returned 502. The production UI was rebuilt from exact frontend SHA `8c17b5936360c5b204396ea8bf038b2d2262e04f`, repointed to healthy production API `opstrax-enterprise-build-8x41.onrender.com`, promoted, and verified at HTTP 200 with production-origin CORS. Git deployment was disconnected from the production Vercel project so future staging merges cannot silently update customers. Production API remained at `3f4d2adf777c6221f92836b438286646f9fa8166`.

## Browser journeys

| Role | Current-SHA result |
|---|---|
| Fleet Manager, CL-HQ | Driver `CLHQ-D-0001` shows portal login `driver-cl-hq@cert-large-20260825.invalid`, status **Active**, and the governed **Revoke access** control. Chrome console: 0 entries. |
| Driver, CL-HQ | Fresh login resolves to `Certification Driver CLHQ 0001`; refresh and logout/login persist the link. Direct `/vehicles`, `/iot-devices`, and `/admin` attempts settle at `/driver` with no fleet/admin leakage. Chrome console: 0 entries. |
| Executive | Tenant-wide read-only view shows 1,000 vehicles over 20 pages and 1,250 drivers over 13 pages. Create/edit/assign/archive controls are absent; direct `/admin` and `/assignments` deny. Chrome console: 0 entries. |
| Customer | The twenty-fifth active role account logs into Customer Portal. Direct `/vehicles`, `/drivers`, `/iot-devices`, and `/admin` attempts settle at `/customer-portal` with no fleet/admin leakage. Chrome console: 0 entries. |

Dispatcher and Maintenance Manager branch/pagination journeys were completed on predecessor SHA `8c17b593...`; PR #90 changed only the Driver detail portal-state projection. Their evidence remains valid supporting evidence, but current-SHA recapture is still desirable before a strict certification verdict.

## Performance observations

| Exact-SHA Chrome action | Observed wall time |
|---|---:|
| Executive fresh login response/landing | 2.178 s |
| 1,000-vehicle roster usable (`Page 1 of 20`) | 2.551 s |
| Search complete fleet for `WESTHUB-V-0200` | 1.011 s |
| Traverse pages 1 through 20 | 20.812 s plus 1.8 s final settle |

The final page contained `WESTHUB-V-0200`, Next was disabled, and the post-deploy Executive console was empty. These are customer-visible wall-time observations, not percentile load-test results.

## Preserved artifacts

- `chrome/4960df4-fleet-manager-driver-portal-active.png`
- `chrome/4960df4-driver-linked-refresh-relogin.png`
- `chrome/4960df4-driver-direct-url-safe.png`
- `chrome/4960df4-driver-direct-url-results.json`
- `chrome/4960df4-executive-vehicles-page20-1000-readonly.png`
- `chrome/4960df4-customer-portal-account-scoped.png`
- `chrome/4960df4-customer-direct-url-safe.png`
- `chrome/4960df4-customer-direct-url-results.json`
- role-specific `4960df4-*-console.json` files, each containing an empty array
- `readiness/4960df4-health-ready-post-grace.json`
- `chrome/8c17b59-production-login-route-repaired.png`
- `chrome/8c17b59-production-console.json`

## Remaining acceptance gaps

This cycle closes the Driver linkage/status blocker and the Customer account/entitlement blocker. Module 1 is not yet marked CERTIFIED because exact 1440x900, 1280x800, 768x1024 and 390x844 viewport capture is unavailable through the installed visible-Chrome controller; document/expiry certification data and the complete controlled vehicle/driver correction cycles remain incomplete; and no recording or structured HAR is preserved.

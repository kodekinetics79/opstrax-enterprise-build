# OpsTrax production cutover evidence — 2026-08-28

## Release identity

- Candidate SHA: `3f4d2adf777c6221f92836b438286646f9fa8166`
- Deployed SHA: `3f4d2adf777c6221f92836b438286646f9fa8166`
- Render service: `opstrax-enterprise-build` (`srv-d8u3sv37uimc73dnr14g`)
- Successful deployment: `dep-da8gbi0ae00c73cs1su0`
- Runtime: `https://opstrax-enterprise-build-8x41.onrender.com`
- Deployment mode after cutover: manual; Render auto-deploy disabled in visible Chrome.

## Readiness result

Four consecutive readiness probes passed HTTP 200 after startup grace. The final
probe reported:

- environment `Production`;
- release version equal to the candidate SHA;
- database connected;
- configuration failures `0`, warnings `0`;
- Data Protection key ring ready;
- restricted application role enabled;
- RLS, grant, tenant coverage, migration and fleet-integrity violations `0`;
- missing, stale and failed critical workers `0`.

Deep health passed HTTP 200. After the next Safety cycle completed, the critical
worker contract observed all `8/8` required workers and every critical worker was
healthy with zero consecutive failures.

## Corrective actions included in the cutover

- Rehearsed all migrations on an isolated Neon branch before production.
- Applied the production migration ledger through the exact candidate build.
- Rotated the application and system database roles to separate restricted
  credentials and verified both identities could authenticate.
- Replaced an incompatible legacy Data Protection key only after archiving it and
  preserving the pre-cutover Neon branch.
- Supplied the missing governed runtime secrets and production safety flags.
- Enabled the production outbox dispatcher explicitly; this closed the initial
  readiness failure for the missing critical worker.
- Disabled automatic production deployments so subsequent releases require an
  intentional exact-SHA action.

No credential value, private key, connection string or diagnostics key is stored
in this evidence directory.

## Rollback boundary

- Neon safety branch: `prod-pre-render-fix-20260828`
- Neon branch ID: `br-cold-morning-ad28cek8`
- Scheduled expiration: `2026-09-04T00:00:00Z`
- The prior successful Render deployment remains available through Render's
  deployment rollback control.
- The encrypted legacy Data Protection material and operator credentials remain
  outside the repository in mode-0600 temporary custody.

## Evidence files

- `chrome/render-auto-deploy-off-exact-sha-3f4d2ad.png`
- `readiness/prod-ready-soak-1-20260828T035411Z.json`
- `readiness/prod-ready-soak-2-20260828T035427Z.json`
- `readiness/prod-ready-soak-3-20260828T035443Z.json`
- `readiness/prod-ready-soak-4-20260828T035458Z.json`
- `readiness/prod-deep-soak-20260828T035705Z.json`
- `readiness/prod-deep-soak-20260828T035856Z.json`

## Verdict

Production infrastructure foundation: **READY** for controlled pilot
certification. This is not a product-wide certification verdict; authenticated
browser journeys and all open P0/P1 module findings remain gating work.

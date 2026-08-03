# Safety pilot local rendered rehearsal evidence — 2026-08-02

Status: **LOCAL FUNCTIONAL PREFLIGHT PASSED; CURRENT PRODUCTION-SHAPED MIGRATION PREFLIGHT PASSED; RELEASE AUTHORIZATION REMAINS NO-GO**

This record captures two rendered-Chrome rehearsals against the same frozen application source state. It is not an immutable release bundle, an exact-SHA CI artifact, or evidence from the target Production environment. The interactive API ran in `Development` because the canonical reset endpoint is deliberately absent outside Development.

The recorded 10/10 migration result was the evidence available during the
rendered runs and is superseded by the current fourteen-ledger owner lane (Stage
47, 58, 59 and 65–75). The exact-current local clean chain and Production-shaped
rehearsal now pass with every critical worker healthy. This strengthens migration
and restricted-runtime evidence; it does not retroactively turn the rendered
Development runs into immutable exact-SHA or target-environment evidence.

## Candidate and environment

| Field | Evidence |
|---|---|
| Base commit | `0814ecb48f9e3a053d55a7df2d157fa409ce52b4` |
| Frozen application worktree fingerprint captured before the rehearsals | `0ccfe162111c7e48e26ad5d9c35ee3040347cfc5439e8e9d73782619e940d752` |
| Fixture | `MERIDIAN-DEMO`, Safety fixture v7, `package_allowlist` |
| Browser/runtime | Rendered Chrome against local frontend and local Development API |
| Rehearsal A reset | HTTP 200; 186 rows replaced; 5 vehicles, 5 drivers, 12 jobs |
| Rehearsal B reset | HTTP 200; 263 rows replaced; 5 vehicles, 5 drivers, 12 jobs |
| Final recovery reset | HTTP 200; 289 rows replaced; fixture v7 restored with 5 vehicles, 5 drivers, 12 jobs |

No application source change was made between the two rehearsals. This document was added only after the final recovery reset.

## Platform control snapshots

| Run | Before SHA-256 | After SHA-256 |
|---|---|---|
| Rehearsal A | `492a21ae1df07b19b85f3e9413172d8e69232062e6aa8d0d8eb2e8a39fd0479f` | `ff7a23b7e524d34136d4cda351f2b461d882853421ee8029f33de6f6dce4cd94` |
| Rehearsal B | `3b0e3156f408cb285fe267d63e7261aeb41a194d240a05dc55c47bef060f2587` | `caac880208d4044fd865efc86cb144bee7070251a3f167479523644a9259db4f` |

These rehearsal artifacts used snapshot schema version 1. They are server-generated, SHA-256 hashed and recorded by `tenant.control_snapshot.captured` Platform audit events. Their hashes differ because generated timestamps and recent audit identifiers are part of the snapshot. Direct database verification found no commercial-control mutation during either workflow: exactly nine governed entitlements existed and all nine remained enabled. Snapshot schema version 2 now adds a drift-resistant semantic digest plus redacted effective role/user-branch evidence; it must be captured in the next frozen-candidate rehearsal rather than retroactively attributed to these runs.

## Rendered and persisted results

The following critical story was repeated after a clean reset in both runs:

- Platform Admin rendered fixture v7, the `package_allowlist` policy and all nine explicit governed entitlements, and captured audited before/after snapshots.
- Safety Manager rendered `MER-INC-1` as `Under Review`. Its only seeded child remained honestly labelled synthetic metadata, with URL absent, `not_verified`, `not_managed` custody and `not_available` retrieval.
- Driver acknowledged only their own `MER-COACH-1` task. Safety Manager recorded a manager note and completed it with an observational score (91 in A; 92 in B). Persisted state and `driver.coaching.acknowledged` plus `coaching.completed` audits agreed.
- Driver accepted the exact HOS attestation; submit was disabled beforehand; one certification and `hos.daily_log_certified` audit persisted. The UI stated that OpsTrax is not a certified ELD and does not submit certification to a regulator.
- Maintenance rendered `MER-DVIR-1` unsafe with pending mechanic/repair state, then reviewed, resolved with repair evidence and certified the repair. The owning Driver saw the continuing availability hold and acknowledged the certified repairs. The audit chain contained `dvir.mechanic.reviewed`, `defect.resolved`, `dvir.repair.certified` and `dvir.repairs.driver_acknowledged`; the vehicle returned to `available` only after acknowledgement.
- Safety Manager recorded an ELD `P1` malfunction and operational recovery evidence. With no provisioned credentials the device returned to `Diagnostic`, never false `Active`; two history rows and both malfunction audits persisted.
- South-branch Dispatcher saw zero incidents and a disabled create control; `MER-INC-1` did not leak. Fleet Manager saw the tenant-wide incident and an enabled create control. Safety Auditor saw the incident but the create control remained disabled.

Post-run database checks agreed with the rendered state. The final recovery reset restored `MER-COACH-1=Assigned`, zero HOS certifications, `MER-DVIR-1` mechanic/repair state `Pending`, its vehicle `out_of_service`, zero ELD malfunction-history rows, ELD `Diagnostic`, all nine entitlements enabled and the same honest incident metadata boundary.

## Automated closeout

- Frontend lint: PASS.
- Production frontend build and bundle budget: PASS; 202 chunks; largest JavaScript chunk 314.86 KiB raw / 95.22 KiB gzip.
- Focused fixture, Safety UI, Platform snapshot/map/control tests: 27/27 PASS.
- Current Production-shaped local rehearsal: PASS; restricted `opstrax_app` and distinct `opstrax_system`, contract-valid live/ready/deep health, all seven critical workers healthy, signed-ticket tenant/branch isolation, 14/14 migration ledgers, zero `PUBLIC` policies and zero unsafe runtime roles. Stage 75 is present, while Platform support access remains disabled and excluded from the Safety pilot.
- Final rendered candidate smoke: PASS for authenticated Safety Manager read paths. Incidents (`MER-INC-1`), coaching (`MER-COACH-1/2`), scorecards (`MER-DRV-1/2/3`), DVIR (`MER-DVIR-1/2`) and HOS/ELD rendered live fixture-v7 data with no page error. HOS/ELD retained the back-office certification boundary and non-certified-ELD disclosure. Chrome reported no warning/error console entries; 1440×900, 768×1024 and 390×844 checks retained headings/actions with no horizontal document overflow. This is a smoke extension to the two earlier story runs, not a replacement for the complete immutable UAT ledger.
- Final multi-role rendered boundary smoke: PASS. The authenticated Driver rendered only their coaching, HOS and DVIR surfaces and a direct `/incidents` navigation returned to the Driver workspace without rendering `MER-INC-1`. The South-branch Dispatcher rendered an empty incident register with `Create Incident` disabled and no North-branch `MER-INC-1`; the tenant-wide Safety Auditor rendered `MER-INC-1` with creation disabled; the Maintenance Manager rendered unsafe `MER-DVIR-1`; and the Safety Manager session was restored to the incident register for handoff. Chrome reported no warning/error console entries across the role changes. Server-side negative authorization remains evidenced by the automated/API suites and earlier network-verified runs; this smoke does not independently capture a new network archive.
- Local ended-session Chrome check (`AUTH-01`): PASS. From the authenticated Safety Manager `/incidents` page, Sign out redirected to `/login`; browser Back remained on `/login`; Forward remained on `/login`; and a direct `/incidents` navigation after logout redirected to `/login` without rendering `MER-INC-1`. This is a local observation without an immutable screenshot/network ledger and does not prove every persona, Platform Admin, another browser engine, cross-tab logout, or the target deployment.
- Local invalid-credential Chrome check (`AUTH-01`): PASS. Safety Manager invalid credentials remained on `/login` with a clear non-enumerating alert; the API log recorded `POST /api/auth/login` `401` in 32.8 ms. The login warm-up now targets `/health/live`, and the rejected password is cleared and refocused under `LoginHealthWarmupContractTests`. This is local, non-immutable evidence and does not replace frozen-candidate multi-persona/network capture or target-deployment proof.
- Local Fleet Overview authorization-alignment check (`UAT-02`/`UX-01`): PASS for the scoped defect. The Safety Manager saw Jobs and Alerts as “Not available for this role,” with no Jobs/Alerts navigation or action controls. A fresh reload issued no `/api/jobs/summary` or `/api/alerts` request and recorded no 403; the false service-outage wording was absent. This remains local, non-immutable evidence and does not close the complete negative/degradation ledger.
- Local Incident dialog keyboard check (`UX-01`): PASS for initial focus and close restoration. The named `Create Incident` dialog opened with focus on `Close record dialog`; Escape closed it; focus returned to the `Create Incident` trigger; and the contemporaneous API ledger contained reads only with no POST. This does not replace Tab/Shift-Tab, accessibility-scan or immutable target evidence.
- Local nested Incident dialog keyboard check (`UX-01`): PASS after topmost-dialog hardening. The detail drawer opened with focus on `Close detail`; its Status action opened `Change incident status`; the first Escape closed only the child and restored focus to `Status`; the second Escape closed the drawer and restored focus to the originating `View record details` row. This remains local and does not replace Tab/Shift-Tab, accessibility-scan or immutable target evidence.
- Evidence-collector regression: PASS.
- Release-provenance regression: PASS.
- Supply-chain pin validation: PASS; official GitHub tag references independently resolved to the pinned SHAs for checkout v4.2.2, setup-node v4.4.0, setup-dotnet v4.3.1, upload-artifact v4.6.2 and download-artifact v4.3.0. Preserve the command transcript in the immutable candidate bundle.
- Local runtime health was independently observed as HTTP 200 for live, ready and deep. One earlier generated collector run marked runtime HTTP collection failed; the retained artifact must be reviewed for its exact cause. That historical failure does not substitute for target-environment evidence.

## What this does not prove

These were essential local functional preflights, not complete acceptance runs of every scenario in `SAFETY_PILOT_REHEARSAL_CHECKLIST.md`. In particular, the immutable exact-SHA bundle, all create/idempotency/concurrency cases, complete entitlement disable/re-enable sequence in each run, full accessibility/console/network record, external alert delivery, target TLS/CORS/security headers, registry/deployed digests, PITR/rollback, privacy/retention agreement, named operational ownership and executive/Sales signatures remain mandatory. No client data is authorized.

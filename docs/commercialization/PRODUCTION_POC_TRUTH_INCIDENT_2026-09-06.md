# Production POC Truth and Visibility Incident — 2026-09-06

**Status:** OPEN — remediation prepared; production release and customer retest pending  
**Severity:** P0 — customer POC commercial-truth and critical-demo-path failure  
**Customer surface:** `https://opstrax.vercel.app`  
**API surface:** `https://osptrax-fleet-management.onrender.com`  
**Owner:** CTO / Release Engineering  

## Executive finding

The customer POC frontend is not a traceable representation of current `main`.
Its Vercel production project was deployed manually while disconnected from Git.
The running frontend reports an unknown source SHA, while the production API is
healthy at `44672a3ca2713dafc6d7580f392814be81da6e9f`. The POC therefore cannot be
used as exact-SHA acceptance or certification evidence.

The exposed `/dashcam` route compounds the problem. The old deployed bundle uses
video-product language around 33 seeded synthetic database records. Current
source has already replaced that presentation with an explicitly unverified
camera-metadata view, but those corrections were never released to the customer
POC. The route was also omitted from the sidebar, together with 28 other declared
modules.

No camera, video, provider or AI-video capability is certified. Any prior status
language that could reasonably be read as certifying that POC screen is withdrawn.
The governing status remains:

- road-facing / driver-facing video: **ROADMAP**;
- AI video safety / coaching: **ROADMAP**;
- stored camera metadata implementation: **DEVELOPMENT**;
- authentic provider media, physical camera, privacy and field evidence:
  **EXTERNAL HOLD / REQUIRED**.

## Evidence captured

| Check | Observed result | Consequence |
|---|---|---|
| `GET /deployment.json` on the POC at 2026-09-06 19:41 UTC | HTTP 200 `text/html`; the SPA shell was returned instead of a deployment manifest | Frontend source identity cannot be independently established |
| POC runtime diagnostics | Frontend `unknown`; API `44672a3…`; tenant marked `Demo Data` | Frontend/API exact-SHA parity is absent and the visible records are not certification data |
| Production API readiness | `ready`, Production, exact version `44672a3ca2713dafc6d7580f392814be81da6e9f` | API deployment is current and healthy; this does not validate the older frontend |
| Vercel project configuration | Production deployment created through `vercel deploy`; project UI offered `Connect Git` | Merges to `main` did not reliably reach the customer POC |
| Signed-in POC navigation | 29 declared module routes were absent from sidebar navigation | Implemented screens were inaccessible through the customer journey |
| POC `/dashcam` direct route | Old video-event wording and 33 seeded records; no authentic provider/media evidence | The page was misleading and is prohibited as certification evidence |

## Pull-request reconciliation

The red or stacked states previously visible in the GitHub screenshot are stale
for the following pull requests as of this incident review:

| PR | Current disposition | Current checks |
|---|---|---|
| #201 — Canada + KSA regulatory baseline | Merged as `0c2bc7c…` | All reported checks green; qualified regulatory acceptance remains separate evidence |
| #202 — Canada + KSA HOS calculator | Merged as `a828e24…` | All reported checks green; this does not certify an ELD product |
| #131 — G3A source-truth remediation lane | Closed without merge; branch reports conflicts | It is not waiting for a rebase or part of the release candidate |
| #216 — dead API host and login lockout | Merged as `44672a3…` | All reported checks green |
| #210 — Wave 3 current-main closeout | Merged as `058ec68…` | All reported checks green |
| #159 — governed automation | Merged as `51a4787…` | All reported checks green; customer-visible claims remain bound to evidence |
| #212 / #213 — mobile UI and notification work | Merged | All reported checks green; these are mobile deliverables, not web POC navigation |

Passing repository checks establish that a change integrated successfully. They
do not establish that the disconnected production frontend deployed it, that a
feature is visible to the signed-in customer, or that external certification
evidence exists.

## Remediation in this candidate

1. Put every declared customer-facing module into role- and entitlement-filtered
   sidebar navigation. The prior 29-route omission is now a failing contract test.
2. Present the camera route as **Camera Metadata** and disclose that media,
   provider connectivity and automated assessments are unavailable.
3. Generate `/deployment.json` in every frontend build. Production builds fail
   unless they receive a full lowercase 40-character source SHA and an explicit
   API origin.
4. Extend the guarded production release workflow to deploy both Render and
   Vercel from the same main-branch candidate.
5. Fail the release unless the public customer POC manifest and production API
   both report the exact candidate SHA.

## Required closure evidence

- remediation merged to `main` with required CI green;
- manually authorized production workflow completes from one exact main SHA;
- `https://opstrax.vercel.app/deployment.json` returns JSON with that exact SHA;
- production `/health/ready` returns the same SHA;
- signed-in customer-account browser retest confirms all entitled navigation;
- camera route displays only the current unverified metadata wording;
- synthetic tenant data is visibly labeled and excluded from certification;
- no camera/video certification claim is made until authentic provider/device,
  media, privacy, field and qualified-human evidence exists.

This incident remains open until those items are preserved against the released
candidate. A green source build alone does not close it.

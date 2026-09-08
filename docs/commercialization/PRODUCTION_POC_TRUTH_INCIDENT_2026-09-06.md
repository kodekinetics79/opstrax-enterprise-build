# Production POC Truth and Visibility Incident — 2026-09-06

**Status:** RESOLVED — production release truth and customer navigation restored at `a5e8963cde984c3e806f1a3adbc85d523211d80e`; camera certification remains external hold
**Severity:** P0 — customer POC commercial-truth and critical-demo-path failure  
**Customer surface:** `https://opstrax.vercel.app`  
**API surface:** `https://osptrax-fleet-management.onrender.com`  
**Owner:** CTO / Release Engineering  

## Executive finding

At incident discovery, the customer POC frontend was not a traceable
representation of current `main`. Its Vercel production project had been
deployed manually while disconnected from Git. The running frontend reported an
unknown source SHA, while the production API was healthy at
`44672a3ca2713dafc6d7580f392814be81da6e9f`. That POC could not be used as
exact-SHA acceptance or certification evidence.

The exposed `/dashcam` route compounded the problem. The old deployed bundle
used video-product language around 33 seeded synthetic database records. Source
had already replaced that presentation with an explicitly unverified
camera-metadata view, but those corrections had not reached the customer POC.
The route was also omitted from the sidebar, together with 28 other declared
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

All listed closure items were verified against released candidate
`a5e8963cde984c3e806f1a3adbc85d523211d80e`. This resolves the production
deployment-truth and navigation incident. It does not promote any camera, video,
provider, device, AI assessment, privacy or regulatory capability; those evidence
requirements retain their stated statuses and external holds.

## Release attempt log

| UTC time | Candidate | Result | Disposition |
| --- | --- | --- | --- |
| 2026-09-06 20:05 | `ec596dc9260cca86ecef24672a4ba42daeaad847` | API deployment and readiness passed; Vercel stopped during project-settings pull before build or alias promotion | Incident remains open. The frontend remains on its prior deployment. The follow-up binds every Vercel CLI command to the approved production project and scope before retrying the same candidate. |
| 2026-09-06 20:47 | `142c39c4f56736adf54119de666bd17e2320a413` | API deployment and readiness passed; Vercel project-settings pull stopped on the CLI user lookup with `User not found (404)` before build or alias promotion | Incident remains open. Vercel issue [#17506](https://github.com/vercel/vercel/issues/17506) documents that project-scoped tokens cannot run `vercel pull`. The next attempt uses a direct production deployment with explicit build inputs and retains the narrower project-scoped credential. |
| 2026-09-06 21:06 | `a5e8963cde984c3e806f1a3adbc85d523211d80e` | Guarded workflow run `34059921926` passed migrations, exact Render deployment, direct Vercel production deployment, and public frontend/API parity | Release accepted for deployment identity and customer navigation. Independent probes returned the same SHA from `/deployment.json` and `/health/ready`. |

## Signed-in customer acceptance

At 2026-09-06 22:02 UTC, the production tenant session was force-refreshed in
Firefox at `https://opstrax.vercel.app`. The following visible evidence was
observed:

- runtime diagnostics showed frontend and API at exact SHA
  `a5e8963cde984c3e806f1a3adbc85d523211d80e`;
- the tenant header showed **Demo Data** and **OpsTrax Demo Logistics**;
- module search showed **92 modules** and the expanded navigation exposed the
  previously omitted routes, including Owners and Camera Metadata;
- the Owners route loaded successfully through customer navigation;
- `/dashcam` rendered **Stored camera metadata** and stated that media, provider
  connectivity and automated assessments are not provided or verified;
- the record detail repeated **Manual metadata — unverified** and excluded
  media, provider and automated-assessment claims.

The 33 stored rows remain seeded synthetic database records. They are suitable
for demo workflow navigation only and are excluded from camera/video
certification evidence.

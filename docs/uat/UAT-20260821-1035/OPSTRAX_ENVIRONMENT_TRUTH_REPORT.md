# OpsTrax Environment Truth Report

Run ID: `UAT-20260821-1035`  
Gate: 0 - Environment truth and safety approval  
Status: **CORE STAGING APPROVED; TELEMETRY/OBJECT STORAGE BLOCKED**  
Recorded: 2026-08-21, America/New_York

## Repository truth

| Item | Actual result | Status | Evidence |
|---|---|---:|---|
| Repository | `opstrax-enterprise-build-fixed-nginx` | PASS | Working directory inspection |
| Branch | `main`, tracking `origin/main` | PASS | `git status --short --branch` |
| Local commit | `4653d2ec745004b16ea3eb644d4be66a72c10f07` | PASS | `git rev-parse HEAD` |
| Worktree | Dirty: existing telemetry gateway edits and untracked deployment/protocol/test files | PASS | Git status captured during preflight; changes were not modified or discarded |
| Root instructions | No root `AGENTS.md`; `mobile/AGENTS.md` applies only to `mobile/` | PASS | `rg --files -g AGENTS.md` |

## Runtime truth

| Component | Actual result | Status |
|---|---|---:|
| Public frontend | `https://opstrax.vercel.app` | PASS |
| Frontend route reached | `/login` | PASS |
| Isolated staging frontend | `https://opstrax-staging-certification-7qn54xuib-kode-kinetics-projects.vercel.app`; tenant login at `/`, staff login at `/platform/login` | PASS |
| Staging frontend deployed SHA | `979c142b3b0b228e7c84b88a37c2eacb66b76d38` | PASS |
| Compiled frontend asset | `assets/index-E1Qn9SUG.js` | PASS |
| Staging API | `https://opstrax-staging-api.onrender.com` | PASS |
| Staging API deployed SHA | `979c142b3b0b228e7c84b88a37c2eacb66b76d38` | PASS |
| Environment name | `Staging`, from live health response | PASS |
| Local/deployed comparison | Local `4653d2e...` does not match deployed `979c142...`; certification target is the deployed staging build | FAIL |
| Local frontend/API/events | No responding service on ports 10000/8088/8090 | NOT IMPLEMENTED |
| Database/migrations/RLS | Live readiness: connected; restricted identities; RLS ready; zero contract violations; required migrations applied | PASS |
| Worker/queue | Live deep health: 7/7 critical API-hosted workers healthy and fresh | PASS |
| Telemetry gateway | Separate .NET gateway and protocols found; deployed gateway/runtime status not verified | BLOCKED |
| Object storage | S3-compatible configuration exists; deployed durable-storage status not verified | BLOCKED |
| Authentication | Tenant organization code + work email, followed by organization-issued credentials/OIDC | PASS |
| Platform authentication | Rotated Render credential reconciled through one-time reset; authenticated Platform Super Admin dashboard reached | PASS |
| Test accounts/roles | Disposable tenant `T-C221B5BA`; `.invalid` tenant administrator activated for this run | PASS (admin only) |
| Browser | Connected Google Chrome available; live login screen inspected | PASS |
| Existing customer data | Authenticated dashboard showed 0 tenants before UAT provisioning | PASS |
| Company provisioning mode | Platform workflow created disposable tenant `T-C221B5BA` with US operating region and run-ID labels | PASS |

## Database target proof and repair

The user-supplied project `soft-rain-18335223` was inspected read-only and rejected as the Render staging target: its readiness fingerprint was `runtime_route_column_violations=0`, `market_catalog_ready=true`, `fleet_identity_ready=true`. Render reported `12/false/false`.

Project `wild-paper-65837531`, branch `br-flat-base-awwl7uwj`, database `opstrax_staging` matched every Render readiness field exactly. Repairs were first applied and validated on temporary branch `br-young-unit-awfw1dko`, then applied transactionally to the confirmed staging parent. The temporary branch was deleted after success. Live `/health/ready` now reports HTTP 200, zero route/RLS/grant violations, catalog ready, identity ready, and all migrations ready.

The staging CORS preflight now returns `Access-Control-Allow-Origin` for the exact Vercel preview origin.

## Architecture truth

The active implementation differs materially from the root README. The current deployment uses a React/Vite frontend on Vercel, an ASP.NET Core 8 API on Render, Neon PostgreSQL with restricted application/system identities and tenant RLS, API-hosted workers, and a separate .NET telemetry edge for raw TCP protocols. The README's MySQL 8.4 and separately active Node-events descriptions are stale and must not be used as release evidence.

## Safety decision

Core application test-data creation is approved for this run because the target is explicitly Staging, frontend/API versions match, database safety contracts pass, and the authenticated platform dashboard proved zero pre-existing tenants. The following boundaries remain:

1. The local source SHA differs from the matched staging frontend/API SHA; the deployed build remains the explicit certification target.
2. Public TCP telemetry edge/hardware and durable object storage remain unverified and are not approved for real telemetry/files.
3. Only the Platform Super Admin and disposable tenant administrator are provisioned; lower-privilege personas remain to be created and tested.

Created records are limited to disposable run `UAT-20260821-1035`: tenant `T-C221B5BA`, its pending/activated `.invalid` administrator, audit rows, and associated country/entitlement metadata.

## Gate 0 checkpoint

- Completed: repository, branch, SHA, dirty-worktree, deployment configuration, target classification, browser availability, login state, architecture/test/security static inspection
- Passed: staging API/build/database/RLS/worker checks and repository/browser preflight
- Failed: local/deployed SHA comparison
- Blocked: staging UI access, safe persona/customer-data inspection, object storage, and public telemetry-edge truth
- Defects opened: 7 static/configuration findings in `OPSTRAX_DEFECT_REGISTER.md`
- Defects fixed: 0; work stopped at the required authentication/production boundary
- Evidence created: screenshot 001 and Gate 0 documents
- Next gate: Gate 1 only after a dedicated non-production staging session is available and runtime truth passes

# Staging certification ledger — 2026-08-12

This ledger starts the post-merge evidence phase without claiming that a real staging environment was exercised. It is intentionally fail-closed: **prepared** and **locally executed** do not mean **staging passed**.

## Release lineage and verdict

- Launch-hardening PR: [#18](https://github.com/kodekinetics79/opstrax-enterprise-build/pull/18)
- Verified PR head: `b3d8b31ea56aabb3b086b378bb105da8e1983189`
- Merge commit on `main`: `f6a18b98ef106250781d98057e34131bb2f7b3e6`
- Exact-merge post-merge workflow: [run 31592079143](https://github.com/kodekinetics79/opstrax-enterprise-build/actions/runs/31592079143), **11/11 jobs passed**
- Staging verdict: **NO-GO** — no isolated staging deployment, credentials, persona states, database, or retained environment evidence was supplied.
- Production verdict: **NO-GO** — production deployment is out of scope and staging certification is incomplete.

The candidate SHA for a staging run is the eventual head of this branch. Frontend, API, database migrations, worker, telematics gateway, and mobile artifacts must all record that same immutable SHA before any environment test begins. A branch name, `latest` tag, or mixed set of image digests is not acceptable evidence.

## Evidence executed from the merged baseline

All commands below ran from merge commit `f6a18b98ef106250781d98057e34131bb2f7b3e6`. No staging or production traffic was sent.

| Gate | Result | Boundary |
| --- | --- | --- |
| Post-merge GitHub Actions | PASS, 11/11 jobs | Exact merge SHA |
| Frontend production build and bundle budget | PASS, 2,664 modules transformed; 203 chunks; 95.22 KiB largest gzip | Local build |
| Playwright safety guard | PASS, 12/12 | Zero network |
| Playwright collection | PASS, 49 tests in 7 files | Enumeration only |
| Anonymous Chromium journeys | PASS, 21/21 | Local built preview |
| Launch-plan tests | PASS, 33/33 | Local/mock only |
| Bounded launch-plan dry-run | PASS, 10,000/10,000 operations materialized; 0 network; SHA-256 `055f28754d821a149975e246b9977ab8252bbdc913a81ec1c7b49bc11afa8585` | Dry-run only |
| Load guard/static tests | PASS, 10/10 | No k6 traffic |
| Telematics tool tests | PASS, 18/18 | Offline/loopback |
| Telematics fingerprints | PASS, 9/9 vectors | Offline |
| DR contract validator | PASS | Contract only; no restore performed |
| Safety external-operations validator | PASS | No external provider call |
| Release-provenance regression | PASS | Local source validation |

The 28 non-public Playwright cases were not run: 27 require authenticated tenant, driver, customer, or Platform role state, and one mutation case additionally requires the isolated-staging acknowledgement and a disposable canary vehicle. A skipped or unconfigured persona project is not a pass.

## Staging execution matrix

| Required evidence | State | Acceptance evidence | Current blocker |
| --- | --- | --- | --- |
| Same-SHA deployment | BLOCKED | Artifact/image digest and reported commit for UI, API, database migrator, worker, gateway, iOS, and Android all resolve to the draft PR head | Isolated staging URLs, deployment authority, artifact registry, and mobile builds |
| Disposable personas | BLOCKED | Tenant admin, dispatcher, driver, customer, maintenance, safety, and Platform Admin identities with least-privilege role manifests | Persona accounts and credential/state provisioning |
| All 49 Playwright journeys | PARTIAL | 49/49 executed with retained report; no unexpected skip, runtime error, or 5xx | 21/49 local public cases passed; authenticated states and mutation approval absent |
| Tenant isolation and RBAC | BLOCKED | Positive and cross-tenant negative matrix for every persona; denied requests remain denied in UI, API, and audit trail | Two disposable tenants and persona credentials |
| 10,000-record execution | PREPARED | 10,000/10,000 against isolated staging plus reconciled database counts and cleanup proof | Staging API/database and disposable tenant |
| Load, stress, soak, concurrency | PREPARED | Retained k6 outputs within published caps and thresholds; abort proof on threshold failure | Approved staging host, mode-0600 credential file, k6 runner, monitoring |
| Retry, idempotency, recovery | BLOCKED | Duplicate/reordered/retried operations show one durable outcome and correct recovery after injected transient failure | Staging services, observability, and failure-injection authority |
| Backup and restore | BLOCKED | Timestamped backup, restore to a separate target, integrity checks, measured RPO/RTO, and teardown record | Staging database/backup infrastructure and restore authority |
| Samsara | BLOCKED | Disposable read-only credential, explicit vehicle mappings, cursor/retry evidence, source-time freshness, alerts, and secret redaction | Provider sandbox credentials and mapped vehicles |
| PT40/GT06 | PARTIAL | Authorized physical capture fingerprinted before ACK/replay; retained ingest-to-projection evidence | Synthetic/offline tooling passed; test hardware or authorized public capture absent |
| Physical mobile | BLOCKED | Supported physical iOS and Android devices complete MFA, identity switching, Proof, Workflow, telemetry, offline/retry, and accessibility checks | Devices, signed builds, staging accounts, push/provider setup |
| Authenticated WCAG | BLOCKED | Automated scan plus keyboard, focus, labels, contrast, errors, and screen-reader spot checks for each persona | Authenticated role states and deployed UI |
| Telemetry end to end | BLOCKED | Device/provider event reaches history/latest, heartbeat, alerts, map/dashboard, report/export, and immutable audit record with tenant isolation | Deployed gateway/API/worker/database and a disposable source |
| Business journeys | BLOCKED | Maintenance, DVIR, POD, incidents, orders, shipments, finance, exports, IAM, and Platform Admin workflows with API/database/audit reconciliation | Full staging stack and persona fixtures |

## Safe execution order

1. Record the draft PR head SHA and build immutable artifacts from it. Deploy every component to isolated staging and prove SHA/digest parity before loading data.
2. Create two disposable tenants and least-privilege personas. Keep all credential and Playwright state files ignored and mode `0600`; never commit or print them.
3. Run read-only health, migration/readiness, anonymous browser, and authenticated RBAC/isolation checks. Stop on any runtime error, 5xx, unexpected authorization, schema drift, or SHA mismatch.
4. Only after those pass, separately enable the single labeled Playwright mutation and the bounded 10,000-record plan. Reconcile counts and retain cleanup evidence.
5. Run bounded load, stress, and soak profiles only against the allowlisted isolated tenant while monitoring saturation and error thresholds. Production hosts are prohibited.
6. Perform retry/failure-injection and backup/restore drills on disposable infrastructure. Restore into a separate target and record measured RPO/RTO.
7. Add provider credentials, captured packets, and physical devices one lane at a time. Revoke disposable credentials and remove staging fixtures after evidence is retained.

## Guarded commands

```bash
# Browser guard and complete collection
cd tests/e2e
npm run test:guard
npm run test:list

# Local public lane (requires the built frontend preview on loopback)
npm run test:local

# Staging execution uses the environment variables documented by the harness.
# Do not set mutation acknowledgement until readonly persona checks pass.
npx playwright test

# Bounded data plan: dry-run first, then execute only with the ignored mode-0600
# staging environment file and exact allowlist/acknowledgement documented here:
node tools/launch/execute_launch_plan.mjs --dry-run
node tools/launch/execute_launch_plan.mjs --execute --plan tools/launch/generated/plan.json

# The load runner and telematics tools have additional fail-closed requirements.
node --test tests/load/test_load_guard.mjs
python3 tools/telematics/fingerprint.py --self-test
```

See `tests/e2e/README.md`, `tools/launch/README.md`, `tests/load/README.md`, and `tools/telematics/README.md` for the exact allowlists, acknowledgements, credential-file modes, caps, and cleanup requirements. Do not weaken these guards to make an environment run proceed.

## Stop conditions and required retained evidence

Abort the affected lane immediately for a component SHA mismatch, production hostname, missing tenant isolation, unexpected authorization, unbounded request rate, critical security finding, migration/readiness failure, data outside disposable tenants, unexplained 5xx/runtime error, unreconciled write, missing audit event, or inability to restore/clean up.

For each completed lane retain: exact Git SHA and artifact digests, UTC start/end, environment identifiers, sanitized command/configuration, test versions and counts, machine-readable results, relevant logs/metrics/traces, database reconciliation, cleanup status, and reviewer disposition. Secrets, auth-state files, raw provider tokens, customer data, and sensitive packet payloads must not enter git or PR comments.

This branch may fix confirmed staging defects with regression coverage. It must remain a draft and **must not be merged as a certification claim** until every required row above is executed or an authorized release owner explicitly documents an accepted exception.

## Staging execution update — 2026-08-12

The original candidate `c288c89418b794a65bada9a93977abf9023cee07` passed all 11 exact-SHA CI jobs. Hosting inventory then positively identified the existing OpsTrax Vercel project and two Render workspaces as Production-linked; they were not reused or modified. A separate Render workspace named `opstrax` contained no projects or services and was selected as the isolated staging boundary.

The first resource created there is `opstrax-staging-api` (`https://opstrax-staging-api.onrender.com`), with auto-deploy disabled and branch `agent/opstrax-staging-certification`. Render automatically began its initial build at exact commit `c288c89418b794a65bada9a93977abf9023cee07`. That build was cancelled before runtime configuration because the service selected the repository-root `Dockerfile`, which omitted Stage42 and the terminal Stage76 telemetry-security migration from its image payload.

This packaging gap is defect `STG-001`. Regression coverage now requires both production API Dockerfiles to package Stage42 and Stage76, and the root Dockerfile is corrected on this branch. The service must not be redeployed until the corrected exact head passes CI. No database connection, credential, migration, staging traffic, or production access occurred during discovery.

Neon provisioning is deferred by operator direction. Therefore PostgreSQL, migrator, API/embedded workers, gateway, seeding, authenticated browser journeys, data/load/recovery tests, and backup/restore remain **BLOCKED**. The isolated Render service is an unconfigured resource shell, not a deployed or healthy component.

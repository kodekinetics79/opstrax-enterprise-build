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

### Corrected-head evidence and resource inventory

- Corrected candidate: `4a35a97a5a725cd713330d1b112564303f4ffb79`.
- Exact-head CI: [run 31599015556](https://github.com/kodekinetics79/opstrax-enterprise-build/actions/runs/31599015556), **11/11 jobs passed**.
- Render boundary: isolated empty workspace `opstrax`; production-linked Render workspaces were excluded.
- Render API shell: `opstrax-staging-api`, service `srv-d9u6qnajobas73ef2590`, URL `https://opstrax-staging-api.onrender.com`, free plan, auto-deploy off. No successful deployment or runtime environment is configured.
- Vercel boundary: new project `opstrax-staging-certification`, project `prj_eSx1C33oCP4iZjg71mT3zP2E0GZS`; the existing OpsTrax production project was excluded.
- Vercel frontend: preview deployment `dpl_5df7QYzgoFCawY89zeRKm5gUHMmK`, URL `https://opstrax-staging-certification-c1le9wcs5-kode-kinetics-projects.vercel.app`, READY and access-protected. It was built from a clean archive of `4a35a97a5a725cd713330d1b112564303f4ffb79` with only the isolated API hostname configured. An initial deployment incorrectly labeled by Vercel as `production` inside this staging-only project was removed; no production OpsTrax project or resource was changed.
- Registry publication: BLOCKED. Local API and gateway images built, but GHCR rejected both uploads before publication because the authenticated token lacks package-write scope. Credential scope was not broadened.
- Gateway resource: BLOCKED on immutable image publication plus database identity. Render public web services are HTTP-facing; public PT40/GT06 TCP exposure also needs an explicitly supported isolated ingress topology.
- Migrator: BLOCKED on Neon. It is an out-of-band one-off execution of `tools/apply-neon-predeploy-migrations.sh`, not a long-running service.
- Background workers: BLOCKED on Neon. The critical workers are hosted in the API process at this candidate; there is no separate worker executable to deploy.
- Cache/queue: not required by the current runtime topology; PostgreSQL outbox/inbox provides durable dispatch semantics.
- Object storage: BLOCKED. Certification requires a new staging-only S3-compatible bucket; local Render storage is ephemeral and was not accepted.

### Defect ledger

| ID | Finding | Evidence | Regression/fix | State |
| --- | --- | --- | --- | --- |
| STG-001 | Repository-root API image omitted Stage42 and terminal Stage76 migrations | Cancelled Render build `dep-d9u6qnqjobas73ef25p0`; root Dockerfile inspection | `ReleaseProvenanceContractTests` now requires both migrations in both API Dockerfiles; corrected root image built and both payloads matched source byte-for-byte | FIXED at `4a35a97a5a725cd713330d1b112564303f4ffb79`; CI 11/11 |

### Cost boundary

Current incremental resource cost is estimated at **$0 before quota overages**: the Render API shell uses the Free instance type and the Vercel deployment is a protected static preview within the existing team. This is not a viable cost basis for the complete certification: Render Free services spin down, have ephemeral filesystems, cannot receive private-network traffic, and do not support one-off jobs; these constraints preclude reliable soak, failure-recovery, migrator, and durable-upload evidence. See the official [Render Free limitations](https://render.com/docs/free).

For planning, Vercel Pro is listed at $20/month with usage credit; Neon lists a $0 Free plan and a typical Launch workload near $15/month, with usage-based compute/storage. See official [Vercel pricing](https://vercel.com/pricing) and [Neon pricing](https://neon.com/pricing). The final staging estimate remains **BLOCKED** until the operator chooses Render instance types, Neon plan/autoscaling bounds, an S3-compatible bucket, retention, and observability. No paid plan or purchase was initiated.

## Telematics product-hardening update — 2026-08-12

The operator deferred Stripe Projects and Neon provisioning and redirected the branch to the stale/dead telematics product surface. No new cloud resource or paid service was created. The Stripe Projects authorization process was cancelled, and its generated local state was removed from the working tree without reading or committing credentials.

| ID | Finding | Regression/fix | State |
| --- | --- | --- | --- |
| STG-002 | The new Telematics Control Tower route existed but was omitted from the primary sidebar, leaving operators on the older device/GPS/OBD surfaces; Sensor Health and Cold Chain were hidden there too. | The Telematics navigation now exposes Control Tower first, followed by Device Health, GPS, OBD/J1939, Sensor Health, and Cold Chain. A launch regression requires the complete ordered surface. | FIXED on branch; exact-head CI pending |
| STG-003 | The `/cold-chain` telematics module queried the generic device-model projection, which has no standalone sensor-reading contract, despite a tenant-scoped cold-chain API already providing devices, readings, zones, battery and alerts. | Cold Chain now consumes the dedicated device, alert and summary/zone endpoints and renders only evidence-backed temperature, threshold, battery, shipment, freshness and breach state. Route permission now matches the backend `fleet:view` contract. | FIXED on branch; exact-head CI pending |
| STG-004 | Frontend telematics metadata silently substituted tenant `1` when the authenticated session lacked a tenant id. | The fallback is now unscoped `0`; the backend remains authoritative for tenancy. A regression rejects tenant-one fallbacks and mock-data imports in the telematics service. | FIXED on branch; exact-head CI pending |
| STG-005 | Pull-request CI described its provenance as exact-head evidence but Actions checked out the synthetic `refs/pull/19/merge` commit `24803170af7d0b10dd70f1aae7e85090a99ddeae`; artifacts therefore recorded that merge SHA instead of branch head `50d0c3ff623b4a07084094eb4a7b109a58ea2a52`. Both commits had tree `22a7cd531da3737e63f7ec4d615e0df5dd060fd2`, but the immutable commit identity did not match the staging candidate. | CI now derives `CANDIDATE_SHA` from `github.event.pull_request.head.sha` with `github.sha` only for push events, checks out that exact ref in every job, and uses it in provenance checks, context, artifact names and summaries. JavaScript and .NET release-contract regressions reject a return to synthetic-merge artifact identity. | FIXED in source; exact-head CI artifact pending |

Local evidence for this update: telematics surface regression **3/3 passed**; complete launch tooling **45/45 passed**, including the exact-head provenance contract; focused .NET release-provenance contracts **4/4 passed**; frontend lint passed; production frontend build passed with 2,664 modules, 203 chunks, and 95.22 KiB maximum gzip chunk. Live authenticated validation remains blocked until an isolated database/API and disposable personas exist; these local results are not relabeled as staging certification.

## Isolated pilot-closure environment update — 2026-08-12

This update supersedes the earlier Neon-deferred state. Production projects, databases, data, credentials, integrations and domains were not used or modified.

### Resource and cost inventory

| Component | Isolated resource | State | Incremental monthly cost |
| --- | --- | --- | ---: |
| PostgreSQL | Neon project `opstrax-staging-certification-2026-08-12` (`wild-paper-65837531`), branch `main` (`br-flat-base-awwl7uwj`), database `opstrax_staging` | CREATED; migrated with owner/migrator identity; no tenant/user/operational rows yet | $0 Free plan |
| API and embedded workers | Render `opstrax-staging-api` (`srv-d9u6qnajobas73ef2590`), `https://opstrax-staging-api.onrender.com` | CONFIGURED; exact corrected head deployment pending CI | $0 Free plan |
| Frontend | Vercel project `opstrax-staging-certification` (`prj_eSx1C33oCP4iZjg71mT3zP2E0GZS`) | Existing isolated preview is stale; exact corrected head deployment pending CI | $0 incremental |
| Durable object storage | None | BLOCKED: Render Objects is unavailable to this workspace and no staging bucket credential is authorized | $0 |
| Public raw-device TCP gateway | None | BLOCKED: the available Render web-service topology does not provide positively identified public TCP ingress | $0 |

Expected total is **$0/month before provider quota overages**. No annual commitment, paid upgrade or uncapped paid resource was created. Render Starter could not be selected because the workspace has no payment method; the service remains Free, so reliable soak/restart certification is blocked by spin-down and ephemeral-runtime limits.

The Render secret store contains 28 staging-only configuration keys, including separate application/system PostgreSQL connections, cryptographic keys and certificate material, environment/SHA inputs, RLS enforcement, CORS and safe worker switches. Values are redacted. Stripe, SMTP/SMS, provider, customer webhook and production object-storage credentials are absent. Demo and telemetry-simulator flags are disabled.

### Database migration evidence

- Positively identified target before migration: Neon project `wild-paper-65837531`, database `opstrax_staging`.
- Applied the schema baseline without `database/init/002_seed.sql`; Avery Stone, Batch-3 and all other demo operational seeds were excluded.
- Stage 42, Stage 58 and Stage 76 ledgers exist exactly once. The complete owner chain initially produced 54 unique ledgers.
- A clean protected database then exposed a release defect: `platform_roles=0`, global tenant `roles=0`, `platform_admins=0`, `companies=0`. Stage 26 claimed to seed platform roles but contained no inserts; protected Staging correctly skips runtime owner schema seeding, so the first administrator could not be created.
- Stage 77 now installs only 16 global tenant authorization roles, 9 platform authorization roles and 51 platform permission references. It creates no tenant, person, credential or operational data. Two consecutive applications produced one ledger row, proving idempotency.
- The full owner runner re-executed after Stage 77 and passed the terminal Stage 58/59/67/76 security reconciliation. The database still contained zero platform admins and zero tenants at the pre-deploy checkpoint.

### Pilot-closure defects

| ID | Finding | Regression/fix | State |
| --- | --- | --- | --- |
| STG-006 | Staging followed multiple Production-only security branches and could start without the protected dual-identity, RLS, Data Protection, retention, demo/simulator, readiness and outbound contracts. | One protected-environment predicate now covers both Staging and Production while diagnostics retain the exact `Staging` label. Config/startup regressions pass 29/29. | FIXED in source; exact-head CI/deploy pending |
| STG-007 | A clean protected database had no authorization reference roles, preventing first platform-admin creation and tenant admin invitations. | New idempotent Stage 77 migration adds authorization reference data only; owner runner, clean-chain and both API image provenance contracts require it. Applied twice successfully to `opstrax_staging`. | FIXED in source and isolated DB; exact-head CI pending |
| STG-008 | Trusted gateway harsh/crash/SOS alerts used lowercase `open`, while live-state and acknowledgement contracts require `Open`; alerts were hidden and unacknowledgeable. | Canonical `Open` status plus cross-contract regression. Telemetry launch tests pass 9/9. | FIXED in source; deploy/browser evidence pending |
| STG-009 | Mobile rendered a successful telemetry response containing `error` as zero-valued truth, called endpoints without their permissions and exposed silent/no-op workflow controls. | Error/retry truth, permission-scoped navigation and requests, proof lifecycle blockers, envelope types and accessibility metadata; mobile contracts pass 8/8. | FIXED in source; physical mobile remains BLOCKED |
| STG-010 | Authenticated Playwright projects could exit green after skipping every role journey, could use an unintended API origin and did not fail unexpected request failures. | Missing states now fail setup; UI/API origin is verified from runtime traffic; unexpected failures fail closed; CI builds the bundle against the declared local API. Guards pass 16/16 and anonymous Chromium passes 21/21. | FIXED in source; 28 authenticated journeys await deployed personas |

### Current gate

The branch remains **CUSTOMER PILOT NO-GO** and Staging/Production remain **NO-GO**. The isolated database is real and migrated, but the corrected exact SHA is not yet deployed; no synthetic tenants/personas exist; authenticated UI→API→PostgreSQL persistence/audit and cross-tenant denial have not run; object storage, public TCP gateway, restore, 10,000-record, load/soak and failure-recovery evidence remain outstanding. Samsara, PT40/GT06 hardware and physical iOS/Android remain explicitly **BLOCKED**, never passed by simulation.

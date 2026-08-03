# Independent Safety pilot readiness review — 2026-08-02

Reviewer role: independent enterprise release/readiness consultant.
Decision: **NO-GO** for client demonstration or client-data pilot on the current mutable candidate.

Historical-review notice: this report preserves findings from its review point. Later local preflight evidence is reconciled in `SAFETY_PILOT_LOCAL_RENDERED_REHEARSALS_2026-08-02.md` and the authoritative release gate. In particular, the later bundle budget passes and two essential fixture-v7 rendered preflights plus a final recovery reset have occurred; two **complete immutable acceptance rehearsals** remain outstanding.

## Post-review release-path hardening

- The exact-SHA workflow now has a blocking `production-shaped-release-rehearsal`
  dependency. It applies the owner migration chain from the real Stage19/20/22
  predecessor baseline, then boots the candidate in `Production` with separate
  restricted `opstrax_app` and `opstrax_system` identities and runs the focused
  tenant/branch/HOS/offboarding isolation suite. This closes a CI design gap in
  which startup schema materialization could have masked a missing owner migration.
- CI package checks and provenance contracts now explicitly include Stage 71 and
  Stage 72. The standalone rehearsal now restores and builds its test assembly,
  so it works in a fresh checkout instead of relying on prior local build output.
- A hermetic PostgreSQL 17 execution of the clean predecessor chain followed by
  the Production-shaped rehearsal passed: all ten terminal ledgers were present,
  readiness and deep health were contract-valid, no `PUBLIC` policies or unsafe
  runtime roles existed, and the focused signed-ticket/branch/HOS/offboarding
  checks passed. This is reproducible local design evidence; only an external
  run for a frozen SHA can satisfy RC-02.

That ten-ledger result is historical. The current owner lane requires fourteen
ledgers: Stage 47, 58, 59 and 65–75. Stage 47 is now atomic and supplies the
detention evidence/offboarding core before Stage 72/73 repair its immutable-delete
guard. Its financial charge and outbox indexes remain predecessor-dependent and
must be evidenced separately before any full Detention Recovery claim. After
Stage 60 reconciled the required dispatch-assignment completion timestamp, the
exact-current local clean chain and Production-shaped rehearsal passed: 14/14
ledgers, all seven critical workers healthy, contract-valid live/ready/deep
health, signed-ticket tenant and branch isolation, zero `PUBLIC` policies and
zero unsafe runtime roles. RC-02 remains failed for release because no successful
external exact-SHA CI artifact is indexed.

These improvements do not change the decision because the candidate remains
mutable and the target-environment, rendered-UAT, recovery and approval evidence
below is still absent.

## Subsequent local evidence reconciliation

- The authoritative Platform catalog contains **91** tenant-visible modules:
  **45** declare a Platform-commercial entitlement and **46** are included
  core/open modules controlled by tenant RBAC and scope rather than package
  omission. “Open” never means anonymous or authorization-free.
- A local rendered Platform Safety transition proved disable, navigation removal
  after refresh, direct-deep-link denial, an API 403, re-enable and restoration.
  This closes the functional observation locally, but it is not yet a timestamped,
  immutable artifact from the frozen release candidate.
- Fixture **v7** is the current source contract. It explicitly provisions the
  allowlist policy and all nine governed pilot entitlements, and repairs the
  seeded incident attachment into synthetic telemetry metadata with no URL,
  verification, managed custody or retrieval. The prior-version reset evidence
  was stale. Later local preflight completed two clean v7 resets/runs and a final
  recovery reset; two complete immutable acceptance rehearsals remain required.
- Stage 72 now reconciles certified HOS logs, certification snapshots and immutable
  detention evidence with tenant erasure. Ordinary mutation remains blocked;
  deletion is possible only when both the transaction-local offboarding flag and
  `opstrax_system` role membership are present. Offboarding remains atomic and
  fails closed on residual tenant rows.
- Selected rendered Safety paths passed locally, including HOS certification and
  duplicate replay, ELD malfunction/recovery, DVIR repair/acknowledgement,
  coaching acknowledgement/completion validation and incident required-field
  gating. This is meaningful UAT progress, not a complete multi-persona run.
- Subsequent independent Platform/RBAC, product-governance and release-integrity
  reviews closed generic/compatibility-route entitlement bypasses, removed a
  Platform bearer-token response echo, made malformed package catalogs fail
  closed and strengthened external evidence timing/approval validation. The
  discovered write-capable support-impersonation P0 is now contained by Stage 75:
  the seeded Support Admin permission is removed, deployment defaults off, one
  reviewed grant binds one session, only explicit Safety/audit reads are allowed,
  and Platform-attributed plus tenant-visible pseudonymous outcome audit is
  mandatory. Support access remains excluded from this pilot.

These observations preserve the **NO-GO** decision: the exact candidate is not
frozen, two complete rehearsals are absent, and the external deployment,
monitoring, recovery, privacy and approval gates remain open.

## Verified progress

- Platform API provisioning explicitly creates tenants in `package_allowlist`; migrated tenants remain `legacy_allow` for compatibility.
- Stage 68 is present in the owner migration runner and clean-chain assertions.
- Server entitlement middleware denies missing/disabled governed modules for allowlist tenants.
- Login, `/api/auth/me`, and refresh return policy mode plus explicit entitlements; navigation and deep-link boundaries consume that snapshot.
- Safety fixture v7 includes two branches, linked Safety stories, explicit `package_allowlist` plus all nine governed pilot entitlements, credential repair/version reconciliation, a branch-scoped Diagnostic ELD malfunction/recovery story without fake credentials, an executable seeded incident lifecycle, and least-privilege Safety Manager, Driver, Dispatcher, Maintenance Manager and Safety Auditor personas in addition to the existing tenant operator identities. Its incident “evidence” row is an honest synthetic metadata pointer, not an uploaded/retrievable object or custody proof. Stage 70 reconciles the HOS expansion with the legacy predecessor schema.
- A focused control/fixture run passed **32/32** tests, including real Postgres policy/package and fixture assertions. This is useful local evidence, not a substitute for exact-SHA CI.
- The current API process returned HTTP 200 from live/ready/deep and showed healthy worker heartbeats, including Safety. It also correctly exposed why it is not a release environment: `Development`, nine warnings, demo seed enabled, RLS disabled, no distinct system DB connection, and incomplete shared protection/device/SSE configuration.
- The production Compose model validates with placeholders. Production Dockerfiles are digest-pinned and the frontend CI job exercises its unprivileged user, SPA deep links and same-origin API proxy.
- Evidence collection now uses private file permissions, refuses mutable/nonempty or symlink evidence targets, rejects credential-bearing runtime URLs, hashes every generated artifact and does not silently turn missing source into a success.
- At this review point the collector's bounded suite passed **89/89** and the frontend build passed, but the main chunk exceeded the then-current 500 kB warning threshold. **Later disposition:** deterministic vendor splitting plus a build-failing 400 KiB raw / 125 KiB gzip budget now passes; the largest chunk is 314.86 KiB raw / 95.22 KiB gzip. Broader UX/performance acceptance remains open.

## Contradicted or corrected claims

- Earlier control documentation said auth omitted entitlements and navigation stayed visible. That is stale and has been corrected.
- Earlier release documentation said core package omission failed open for all tenants. Current policy is explicit: Platform-provisioned tenants fail closed; reviewed migrated tenants remain legacy until conversion.
- The generic DR script previously printed a full “DR drill passed” after only checking three database row counts. It now labels that as a database PITR phase and requires separate Safety, application, object, isolation, alert and cutover proof.
- Generic reliability documentation asserted an external alerting behavior and deployment rollback outcome that repository source cannot provision or prove. It now separates source controls from target-environment evidence and uses dual restricted DB identities.
- The review found that market-pack mutation wrote usage metering but no mandatory Platform audit and accepted arbitrary status/unknown pack codes. **Disposition: resolved after review** — the handler now validates tenant/catalog/status/price, changes assignment and mirrored entitlement atomically, and writes actor plus reason and before/after state to `platform_audit_log`; Stage 69 and the focused PostgreSQL control test enforce the contract.
- The review found that Stage 66 assumed an identity sequence absent from the runtime-created stream-ticket ledger. **Disposition: resolved after review** — Stage 66 now additively reconciles the legacy table and preserves outstanding nonces; the clean/predecessor chain applies the runner twice, reruns Stage 66 directly, inserts under `opstrax_app`, and verifies identity/sequence grants. A separate disposable copy of the actual current runtime database also completed the integrated predeploy runner successfully.

## Resolved HOS migration finding

The integrated Safety fixture exposed that the predecessor `hos_clocks` table lacked `break_needed_at`, `reset_at` and `updated_at`, while those fields previously existed only in Batch 6's create-if-missing shape. **Disposition: resolved after review** — Stage 70 additively reconciles the legacy HOS schema, the runtime repair list agrees, the migration runner and restored-database verifier require Stage 70, and the legacy clean chain plus integrated fixture/control run pass.

## Resolved critical observability finding

Production readiness previously counted stale or repeatedly failing rows in `service_heartbeats` but had no authoritative expected-worker roster, allowing a never-started worker to disappear from health. **Disposition: resolved after review** — seven always-on workers are now explicit; a process-start-bound two-minute grace exposes them as `starting`, after which missing rows, heartbeats older than ten minutes and repeated failures make readiness and deep health fail. Retention enforcement fails after one recorded failure; the other six use threshold three. Focused source and live-Postgres tests prove empty, one-missing, stale, failed, fresh and recovered states. External monitor delivery remains a separate environment gate.

## Residual stop-ship blockers

1. Produce a clean immutable candidate and exact-SHA CI/image/SBOM/vulnerability evidence.
2. Deploy a Production-mode rehearsal environment using distinct restricted app/system identities, forced RLS, shared Data Protection, durable object storage and disabled demo/simulator settings.
3. Export and approve the pilot tenant control snapshot, including the 91/45/46 ownership boundary, resolved market-pack audit event IDs, and every legacy tenant conversion decision.
4. Run fixture-v7 reset/reconciliation twice and prove all named persona logins in the rendered browser, including cross-branch/Driver negative cases; retain the completed local Platform disable/403/re-enable sequence as candidate-bound evidence and never represent the synthetic incident metadata as verified evidence custody.
5. Complete both full rehearsals on the unchanged candidate, including accessibility, console, latency, empty/stale/provider-down and fallback behavior.
6. Provision and exercise external monitoring/alert delivery with named on-call and support owners, including a critical-worker 503 and recovery.
7. Complete Safety-specific PITR plus restricted-application/object-evidence validation, deploy/config rollback, RPO/RTO and write reconciliation.
8. Approve retention/privacy/client-data terms, capability exclusions and the CTO/CIO/Sales/support decision record.

## Gate ownership: local versus external

Code-local/evidence gates that the delivery team can close in this worktree:

1. Freeze a clean commit, run the complete focused and regression suites, and
   eliminate any unexplained post-rehearsal diff.
2. Complete rendered Platform entitlement disable / hidden navigation / direct
   API 403 / re-enable restoration evidence and the remaining multi-persona
   Safety negative paths.
3. Run two clean full demo rehearsals on that unchanged candidate, with console,
   accessibility, latency, fixture-reset and defect evidence.
4. Reconcile or explicitly supersede stale generic readiness documentation and
   resolve the frontend main-chunk warning against an accepted UX budget.
5. GitHub Actions and the CI PostgreSQL service are now commit/digest pinned and
   `tools/validate-ci-supply-chain-pins.sh` passes. Retain official upstream tag-to-SHA
   verification evidence for the frozen candidate; local pin validation is not a
   substitute for the exact-SHA external CI artifact.

External authorization/operations gates that repository changes cannot satisfy:

1. Successful exact-SHA CI artifact identity, approved registry publication,
   image signatures/attestations, admission result and deployed digest proof.
2. Target TLS/CORS/security-header evidence, external synthetic monitoring,
   delivered alerts, named on-call and dashboard/correlation trace.
3. PITR and application/object-evidence restore, measured RPO/RTO, deployment
   rollback and write-reconciliation evidence in the selected provider.
4. Approved retention/privacy/client terms plus signed CTO, CIO, Sales, support,
   safety, security, privacy, SRE/DR and QA/accessibility acceptance.

## Review evidence

- Earlier control/fixture focus: 32 passed, 0 failed, 0 skipped. After the Stage 70 reconciliation, the integrated Stage 66/69/70, entitlement, Platform-control and fixture focus passed **45/45**; the later hermetic release rehearsal additionally proves the terminal Stage 71/72 chain and focused HOS/offboarding contract.
- Critical-worker readiness focus: **11/11 passed**, including live-Postgres empty, one-missing, stale, repeatedly failed, fresh, recovery and process-start grace states plus deep-health/source contracts.
- Collector regression: passed, including secret non-disclosure, complete generated-artifact hash coverage and evidence-directory immutability.
- Collector optional checks: 89 passed, 0 failed; frontend production build passed with the recorded chunk-size warning.
- Compose validation through the collector: passed.
- Current-runtime collector: correctly returned `RC-01-CLEAN=FAIL` and `SEC-01-RUNTIME=FAIL` despite HTTP 200 health responses.
- Migration evidence: the legacy clean/predecessor chain passes through Stage 75 with runner idempotency, and the Production-shaped rehearsal passes immediately afterward with restricted identities and 14/14 required ledgers. The Safety restored-database verifier requires the terminal pilot ledgers plus current fixture/HOS/support-access boundaries; full recovery proof remains absent because no real PITR/object/application rehearsal is indexed.
- Provenance evidence: deterministic source/migration/dependency manifests, all three local image IDs and CycloneDX SBOMs were generated successfully. The blocking exact-SHA CI aggregation workflow is implemented, but has not run for a frozen candidate; published registry digests remain external and unevidenced.

# OpsTrax Safety pilot release gate

Status: **NO-GO pending evidence**
Gate owner: CTO
Business co-owner: CIO
Scope: the client-facing Safety pilot release candidate, its demonstration tenant, and the production-shaped environment used for the rehearsal

This document is the authoritative entry point for the Safety pilot decision. Historical readiness reports are inputs, not proof of this release. A gate passes only when the evidence index points to an immutable artifact produced from the exact candidate commit and environment.

## Release decision rule

The decision is **GO** only when every mandatory gate below is `PASS`, every approval is signed, the candidate commit and container digests are immutable, and there are no unexplained changes after the final rehearsal. `NOT EVIDENCED`, `STALE`, and `PARTIAL` are all release-blocking states.

| ID | Mandatory gate | Minimum authoritative evidence | Current state (2026-08-02) |
|---|---|---|---|
| RC-01 | Immutable candidate | Clean worktree, commit SHA, frontend/API/gateway image digests, migration hashes | **FAIL** — deterministic source/migration/dependency manifests, three local content-addressed image IDs and CycloneDX SBOMs are reproducible, but the shared worktree is materially dirty and approved published registry digests are absent |
| RC-02 | Reproducible build and tests | CI run for the exact SHA; unit, Postgres integration, Safety contract, frontend build/lint, container, dependency and migration-chain jobs all green | **FAIL for release / local preflight PASS** — the clean predecessor chain and Production-shaped restricted-identity rehearsal pass locally through the fourteen required ledgers (Stage 47, 58, 59 and 65–75), all seven critical workers, contract-valid health, signed-ticket tenant/branch isolation, zero `PUBLIC` policies and zero unsafe runtime roles. Stage 47 proves the detention evidence/offboarding core; its conditional financial/outbox integrations remain separately scoped. Stage 75 proves the default-off bounded support-access schema/role boundary; support access remains excluded from the pilot. No completed external CI run for a frozen candidate is indexed |
| SEC-01 | Production startup fails closed | `/health/ready` and `/health/deep` from `Production`; config output shows no failures; demo/simulator disabled | **FAIL for the target** — a hermetic `Production` rehearsal returned contract-valid ready/deep health with separate restricted identities and demo/simulator disabled, while the interactive 2026-08-02 runtime remains `Development`; no deployed target-environment evidence exists |
| SEC-02 | Tenant and branch isolation | Restricted `opstrax_app` and distinct `opstrax_system`; forced RLS; adversarial tenant, branch, Driver-self and Platform-boundary tests | **PARTIAL** — source/migrations and test coverage exist; deployed-role evidence is absent |
| SEC-03 | Browser and delivery-edge security | HTTPS/TLS, exact CORS origins, CSP/security headers, secure session/CSRF behavior, abuse controls and frontend/API same-origin proxy proven on the deployed candidate | **NOT EVIDENCED** in the target environment |
| SEC-04 | Exposed credentials rotated | Provider-side rotation receipt, target secret update/redeploy, old-credential rejection and healthy distinct restricted app/system identities; evidence contains no secret | **FAIL** — `docs/platform/CREDENTIAL_ROTATION_REQUIRED.md` remains OPEN and no provider/operator receipt is indexed |
| CTRL-01 | Platform Admin control snapshot | Tenant lifecycle, package policy, entitlements, market packs, roles, branch bindings, integration state and environment controls exported before and after rehearsal | **PARTIAL** — the version-2 snapshot derives the 91/45/46 ownership boundary from a cross-stack-reconciled catalog, exports effective role grants and pseudonymous user-to-branch bindings, removes audit actor email, and supplies a stable semantic digest for before/after drift detection. Earlier local rehearsals used version 1; no complete signed version-2 before/after artifact tied to an immutable candidate and target environment is indexed |
| CTRL-02 | Commercial boundary is intentional | Existing tenants explicitly reviewed; pilot uses `package_allowlist`; every pilot surface explicitly enabled/disabled; navigation and deep links agree with API enforcement | **PARTIAL** — Stage 68, API enforcement, new-tenant defaults, auth snapshot and focused contract/Postgres tests exist. Local rendered UAT proved Safety disable, hidden navigation after refresh, denied deep link, API 403 and re-enable/restoration. The proof is not yet an immutable exact-candidate artifact, and existing-tenant review plus signed pilot control approval remain absent |
| DATA-01 | Deterministic multi-role fixtures | Versioned/resettable fixtures for Platform Admin, Fleet Manager, Safety Manager, Dispatcher, Maintenance, Driver and denied/branch-isolated personas | **PARTIAL** — fixture v7 defines two branches, named least-privilege personas, explicit allowlist state for all nine governed pilot modules, a Diagnostic ELD malfunction/recovery story without synthetic credentials and an executable `Under Review` incident. Two clean local resets plus a post-run recovery reset recreated the expected 5 vehicles, 5 drivers and 12 jobs and restored exact critical baseline state. Immutable exact-SHA bundle evidence remains absent |
| UAT-01 | Critical Safety workflows | Rendered-browser evidence for incident/evidence, coaching lifecycle, scorecard provenance, DVIR defect-to-repair/acknowledgement, HOS certification/invalidation, and ELD degraded state | **PARTIAL** — two same-source rendered local critical-story preflights passed for Platform Admin and all named tenant personas, with persisted/audit agreement for incident truth, coaching, HOS, DVIR, ELD and branch/read-only access. They did not execute every checklist create/idempotency/concurrency/invalidation scenario and are not target-environment evidence |
| UAT-02 | Negative and degradation behavior | 401/403/404 boundaries, disabled entitlement, cross-branch IDOR, stale/empty/provider-down, duplicate submit and optimistic-concurrency outcomes | **NOT EVIDENCED** as a complete rendered-browser run — supplemental PostgreSQL coverage now includes a true two-connection ELD malfunction race (one 200, one 409, one state/history/audit mutation) and explicit no-side-effect assertions for denied/stale ELD and precondition/stale/unresolved DVIR repair paths; these tests do not substitute for browser duplicate-click, degradation, accessibility or immutable target evidence |
| OPS-01 | Observability and support | External synthetic check, alert delivery, named on-call, dashboard, correlation ID trace, background-worker failure and recovery exercise | **PARTIAL** — expected-worker roster, startup grace, freshness and missing/stale/failed/recovery tests now fail closed in readiness/deep health. A hash-bound report contract rejects incomplete on-call/threshold/correlation/recovery evidence, but the target alert exercise and operator authenticity remain unproven |
| DR-01 | Backup/restore and rollback | Timestamped PITR drill from the pilot data store, measured RPO/RTO, restored integrity checks, application validation, rollback rehearsal | **NOT EVIDENCED** — the drill now rejects empty core restores and provider cleanup-command failure and can emit a candidate-bound `PARTIAL` database-phase receipt. No signed application/object validation, cutover, verified cleanup or accepted end-to-end RPO/RTO result is indexed |
| REL-01 | Deploy/config rollback | Last-known-good immutable digests, schema compatibility, config version, write-freeze authority and rollback/forward-fix rehearsal meet the accepted recovery time | **NOT EVIDENCED** — the validator requires immutable candidate/known-good digests, compatibility, write freeze/resume, health, isolation, mutation, alert recovery and timing, but no target exercise report exists |
| DATA-02 | Retention, privacy and evidence handling | Pilot retention values, worker state, object-store durability, export/delete procedure, evidence access policy and client agreement | **FAIL for client data / local enforcement contract PASS** — Production now fails closed unless the retention worker is explicitly enabled; the owner migration supplies bounded policy minimums, worker readiness is critical, and category failures fail the cycle rather than being swallowed. Enforcement remains deliberately limited to three database log categories; source includes subject export/anonymization for supported types, but target exercise and privacy approval are absent; file/object deletion remains outside the retention worker, and approved numeric pilot policy plus any separately scoped generic object durability/recovery evidence are absent |
| DEMO-01 | Two repeatable rehearsals | Two clean resets and full runs by different operators, with timings, screenshots/video references, defect log and control-snapshot diff | **PARTIAL** — two local rendered essential-story runs and final recovery reset passed with audited snapshot hashes and no application-source change. They used one automated operator, lack immutable screenshot/video ledgers and did not execute every checklist scenario; formal acceptance rehearsals remain required |
| UX-01 | Professional client experience | Supported Chrome viewport, keyboard/focus/modal behavior, critical accessibility scan, no console errors, responsive latency and truthful empty/loading/error/degraded states | **PARTIAL** — focused UI tests/build and selected rendered checks exist; shared record drawers and critical Safety/DVIR/HOS/ELD/Driver dialogs now have source contracts for naming, focus trapping, Escape and restoration, with additional Driver DVIR/HOS live retry states. Local Incident checks now prove named initial focus, child-only then parent Escape behavior, and exact trigger restoration with a read-only network ledger; Tab/Shift-Tab, an actual accessibility scan, degradation and latency evidence remain incomplete |
| GOV-01 | Independent review | Fleet Safety SME, security, privacy, SRE/DR, QA/accessibility and product/commercial reviews; P0/P1 dispositions recorded | **PARTIAL** — independent Platform/RBAC, product-governance, Safety QA, SRE/DR/privacy and release-integrity reviews were completed locally. They closed compatibility-route entitlement bypasses, credential-response echoing, malformed package catalogs, evidence-validator gaps and the support-access P0. Named human reviewer identity, privacy approval, accessibility review and immutable disposition artifacts remain absent |
| GOV-02 | Executive acceptance | Completed CTO/CIO decision record, Sales demo-owner acknowledgement and rollback/support owner acceptance | **NOT SIGNED** |
| GOV-03 | Control ownership and demo command | Named accountable owner, operator, backup, on-call, rollback authority, evidence custodian and client communicator; abort/fallback rules rehearsed | **NOT EVIDENCED** |
| DOC-01 | Operational documentation agrees with the candidate | Package-specific recovery plan approved; conflicting legacy readiness/runbook claims reconciled or explicitly superseded | **PARTIAL** — the Safety package now has a dated reconciliation record and consistently separates local source/runtime evidence from external proof. Historical/general reports are explicitly non-authoritative for this pilot; environment-specific recovery values, owners, evidence references and approval are still pending |

## Stop-ship conditions

Any one of these overrides a nominal score or successful happy-path demo:

- a cross-tenant, cross-branch, cross-driver or Platform/tenant authorization bypass;
- an untracked change, mutable image tag, missing migration hash or environment drift after rehearsal;
- an open exposed-credential rotation, an old credential that still authenticates, an owner identity in runtime, or uncontrolled automatic deployment after candidate approval;
- a migration chain that passes only on a clean synthetic baseline but fails to upgrade the actual rehearsal/production predecessor schema;
- production readiness reporting a failure, or production running with owner credentials, demo seed, simulator, legacy gateway/device secret, local file storage for required evidence, or a shared application/system DB credential;
- package/entitlement policy or session/UI drift that exposes a customer surface outside the approved pilot contract;
- fabricated, unexplained or non-deterministic pilot data presented as live customer truth;
- synthetic fixture metadata presented as a retrieved evidence object, verified content or chain-of-custody proof;
- loss, duplication or silent mutation of incident evidence, DVIR repair state, HOS certification, score provenance or audit history;
- no tested rollback/restore path, no reachable escalation owner, or no externally delivered service alert;
- an open P0 or P1 without a signed, time-bounded exception from the CTO, CIO and accountable business owner.

## Evidence precedence

1. Exact-SHA CI artifacts and signed deployment manifests.
2. Runtime output from the named rehearsal environment and database roles.
3. Timestamped rendered-browser evidence tied to persona and scenario IDs.
4. Source inspection and automated tests.
5. Historical reports and narrative claims.

A lower-ranked artifact cannot overrule contradictory higher-ranked evidence. Secrets, tokens, cookies, connection strings, personal data and raw production database extracts must never enter the evidence bundle.

## Package contents

- [As-built architecture and capability boundary](SAFETY_PILOT_AS_BUILT.md)
- [Evidence index and collection contract](SAFETY_PILOT_EVIDENCE_INDEX.md)
- [Two-pass rehearsal checklist](SAFETY_PILOT_REHEARSAL_CHECKLIST.md)
- [CTO/CIO decision record](SAFETY_PILOT_GO_NO_GO_DECISION.md)
- [Rollback and recovery plan](SAFETY_PILOT_ROLLBACK_RECOVERY_PLAN.md)
- [Control ownership and professional demo runbook](SAFETY_PILOT_OWNERSHIP_AND_DEMO_RUNBOOK.md)
- [Independent readiness review](SAFETY_PILOT_INDEPENDENT_READINESS_REVIEW_2026-08-02.md)
- [Release-candidate provenance and SBOM contract](RELEASE_CANDIDATE_PROVENANCE.md)
- [Documentation and evidence reconciliation](SAFETY_PILOT_DOCUMENTATION_RECONCILIATION_2026-08-02.md)
- [Local rendered rehearsal preflight evidence](SAFETY_PILOT_LOCAL_RENDERED_REHEARSALS_2026-08-02.md)
- [Scenario-to-evidence coverage matrix](SAFETY_PILOT_SCENARIO_COVERAGE_MATRIX_2026-08-02.md)
- [External operations evidence contract](SAFETY_PILOT_EXTERNAL_OPS_EVIDENCE_CONTRACT.md)
- [Platform Admin Safety control matrix](../platform/PLATFORM_ADMIN_SAFETY_CONTROL_MATRIX.md)
- Evidence collector: `tools/collect-safety-pilot-evidence.sh`

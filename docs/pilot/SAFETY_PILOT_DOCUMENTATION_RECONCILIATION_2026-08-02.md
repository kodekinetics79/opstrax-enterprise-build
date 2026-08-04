# Safety pilot documentation and evidence reconciliation — 2026-08-02

Status: **current documentation baseline; release remains NO-GO**.

This record reconciles the Safety pilot package with the current mutable
worktree. It is a documentation-governance artifact, not release authorization.
The release gate and signed decision record remain authoritative. Historical
repository readiness reports, broad product audits and generic runbooks are
context only when they conflict with this package.

## Reconciled current state

| Area | Current local evidence | Release disposition |
|---|---|---|
| Platform ownership | 91 tenant-visible modules: 45 Platform-commercially governed and 46 included core/open modules controlled by tenant RBAC/scope | Boundary documented; immutable candidate snapshot and approval pending |
| Platform transition | Local rendered Safety disable, hidden navigation after refresh, direct-link denial, API 403, re-enable and restoration passed | Functional local pass; repeat/capture against frozen candidate |
| Fixture | `DemoTenantSeeder.SafetyPilotFixtureVersion` is 7; source expects five vehicles, five drivers and twelve jobs and explicitly enables all nine governed pilot entitlements under `package_allowlist` | Two local clean resets/runs and a final recovery reset passed; two complete immutable acceptance rehearsals remain required for DATA-01/DEMO-01 |
| Seeded incident metadata | Synthetic telemetry metadata only; URL absent, `verificationStatus=not_verified`, `custodyStatus=not_managed`, `retrievalStatus=not_available` | Cannot satisfy incident upload, retrieval, integrity or custody UAT |
| Migration/security | The canonical lane includes atomic Stage 47 plus Stages 66–75; Stage 73 makes absence of the offboarding marker explicitly false rather than SQL NULL; Stage 74 owner-manages the Production retention-policy ledger and minimums; Stage 75 owner-manages the default-off bounded support-access contract | Clean predecessor and Production-shaped reruns pass locally with 14/14 ledgers, all seven critical workers healthy, contract-valid health, signed-ticket tenant/branch isolation, zero `PUBLIC` policies and zero unsafe roles; exact-SHA external CI and deployed-role proof remain pending |
| Stage 47 scope | Five detention/evidence tables are always created for immutable-evidence offboarding; charge columns/index and outbox indexes are conditional on separately owned predecessor tables | Stage47 ledger proves the core only. Full Detention Recovery billing/outbox claims require explicit integration-object and workflow evidence or must remain out of scope |
| Stages 72–73 | Ordinary HOS certification/detention evidence mutation remains immutable; only dual-gated `opstrax_system` offboarding deletion is allowed, with null-safe fail-closed evaluation | Local schema/test evidence only |
| Rendered Safety UAT | Two same-source essential multi-persona preflights passed locally, covering honest incident metadata, coaching, HOS, ELD, DVIR and branch/read-only boundaries | Complete immutable scenario ledgers, negative/concurrency coverage and different operators remain pending |
| Candidate provenance | Collector, local image identities and three SBOM paths exist | Dirty worktree, published/signed digests, exact-SHA CI and deployed digest proof pending |
| Operations/recovery | Source health, migration, evidence and recovery tooling exists | External monitoring/alert receipt, on-call, PITR/application/object restore, rollback and RPO/RTO pending |
| Governance | Independent technical reviews and this reconciliation exist | Named SME dispositions plus CTO/CIO/Sales/support/privacy/SRE/QA signatures pending |

“Local pass” means observed against mutable local state and must never be copied
into the evidence index as `PASS` without a candidate-bound artifact. The two
complete rehearsals must use the same frozen source, migration manifest, image
digests, configuration and fixture version.

## Documentation precedence and supersession

1. `SAFETY_PILOT_RELEASE_GATE.md` defines mandatory gates and current status.
2. `SAFETY_PILOT_GO_NO_GO_DECISION.md` records authorization; its checked NO-GO
   remains controlling until signed otherwise.
3. `SAFETY_PILOT_EVIDENCE_INDEX.md` defines acceptable immutable evidence.
4. The remaining files in this package define capability, rehearsal, ownership,
   provenance and recovery contracts.
5. Generic or older documents may describe mechanisms or historical findings,
   but cannot prove this candidate, environment or release decision.

## Open release gates

- freeze a clean commit and complete exact-SHA CI, vulnerability and provenance;
- publish/sign approved image digests and prove deployment/admission by digest;
- capture deployed TLS/CORS/CSP/session/CSRF and restricted-role/RLS evidence;
- repeat fixture-v7 reset and complete two unchanged-build multi-persona runs;
- exercise external monitoring/alert delivery, named on-call and correlation;
- complete PITR, application/object validation, rollback and accepted RPO/RTO;
- approve retention/privacy/client-data terms and capability exclusions; and
- obtain all executive, operational, commercial and SME signatures.

Until every mandatory artifact is indexed and every approval is signed, the
correct decision is **NO-GO for a client demonstration or client-data pilot**.

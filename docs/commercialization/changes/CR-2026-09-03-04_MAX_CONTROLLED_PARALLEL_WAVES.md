# CR-2026-09-03-04 — Maximum Controlled Parallel Wave Execution

**Class:** Class 2 sequence/execution-model change  
**Approved by:** CTO / Program Owner  
**Effective:** on merge  
**Entry baseline:** `main@1f3b5de029b33e9315fb96c80988e610665c41b0`  
**Parent:** #110  
**Supersedes for execution priority:** `CR-2026-09-03-03` / v2.4 certification-first pause rules only.  
**Does not supersede:** evidence hierarchy, Appendix B, truth matrix, physical/provider/regulatory requirements, exact-SHA acceptance, no-self-certification, P0 independent-perspective rules, or the defect loop.

## Directive

Activate every governed commercialization wave from Wave 1 through Wave 6 concurrently so independent work can progress and gates can close as soon as their evidence is complete.

The objective is maximum throughput, not maximum uncontrolled branching. A wave may complete its engineering, test, evidence-preparation, provider/hardware, resilience, support, commercial, and isolated acceptance work before preceding waves formally close. A capability may be promoted only when its own prerequisite evidence and dependency gates are actually satisfied.

## Active portfolio lanes

- G1A — Fleet Identity + Telematics full certification: #108 / PR #137.
- G1B — exact GT06-family physical certification: #109, including candidate acquisition #139.
- G2A — Samsara production connector certification: #115.
- G2B — certified ELD partner/device selection and integration: #116.
- G3A — HOS operational workflow: #128 / PR #131.
- G3B — dual-facing camera integration: #129 / PR #132.
- G4A — Video Safety: #125 / PR #134.
- G4B — Geotab/Motive/OEM provider breadth: #123 / PR #135.
- G5A — DeviceOps 2.0 and certified compatibility lifecycle: new Wave 5 gate.
- G5B — J1939/PT40/OEM hardware depth: new Wave 5 gate.
- G6A — scale, resilience, DR and observability certification: new Wave 6 gate.
- G6B — support, billing, packaging and commercial release operations: new Wave 6 gate.

## Concurrency model

There is no longer a two-wave or two-workstream limit. Concurrency is bounded by conflict domain and evidence integrity instead of wave number.

1. All portfolio lanes may run simultaneously.
2. Up to four shared-core merge-bound production-code lanes may be in active integration at once by default.
3. Additional merge-bound lanes are allowed only when their changed-file/domain sets are demonstrably disjoint and the integration owner records that determination.
4. There is no numeric cap on read-only research, real-provider acquisition, physical-hardware procurement/capture, regulatory verification, test-harness work, evidence preparation, performance/DR execution, documentation, support, pricing or commercial packaging.
5. Production migration-chain edits are serialized through one schema-authority slot. Competing migrations queue rather than being independently reordered.
6. Shared auth/RBAC/session changes are serialized through one security-authority slot.
7. Shared frontend primitives/tokens are coordinated as one design-system authority; module-local UI work may continue in parallel.
8. Certification candidates are immutable once frozen. Later-wave branches must not mutate or silently broaden the frozen candidate; any new candidate requires explicit evidence renewal.
9. Every merge-bound branch rebases/reconciles against current main before final exact-SHA acceptance.

## Dependency-aware closure

Parallel execution does not remove dependencies.

- G1A can certify software independently of provider/device certification but cannot claim hardware/provider support that belongs to later gates.
- G1B cannot close without physical units and real bench/road/recovery/soak evidence.
- G2A cannot certify Samsara without a real authorized provider account/API path.
- G2B cannot certify ELD/HOS without real partner/device, commercial rights and jurisdiction-specific regulatory evidence.
- G3A may finish product workflow engineering early, but regulated HOS certification remains blocked until G2B supplies an accepted source boundary.
- G3B may finish privacy/media workflow engineering early, but camera capability remains blocked until real provider/device evidence exists.
- G4A may finish safety workflow engineering early, but authentic video-event claims remain blocked by G3B evidence.
- G4B provider connectors certify independently per provider; no umbrella provider claim is allowed.
- G5 device lifecycle/J1939/PT40 work may complete independently, but each hardware/protocol claim remains exact-model evidence-bound.
- G6 resilience/support/billing work may close its internal subgates early, but `COMMERCIAL RELEASE — GO` cannot be issued until the product packages being sold have passed their applicable prerequisite capability gates.

## Integration board / anti-chaos rules

Before any merge-bound implementation starts, record: owning gate, branch, intended file/domain ownership, schema impact, shared-auth impact, shared-UI impact, and acceptance tests.

A branch that unexpectedly enters another active lane's owned conflict domain pauses at integration until the owners reconcile it. Do not solve conflict by deleting the other lane's work, relaxing tests, or bypassing migrations.

No broad repeated audits. Each lane continues defect-by-defect:

`Observe -> Evidence -> Root Cause -> Fix -> Test -> Exact-SHA Deploy -> Same Journey Retest -> Close`

## Commercial truth

Activation is not promotion. Current capability classifications remain unchanged until owning gates close. Sales/website/proposals remain constrained by the Capability Truth Matrix.

## Exit objective

Drive all Waves 1–6 toward closure concurrently, then issue one evidence-backed `COMMERCIAL RELEASE — GO` only when the package-level prerequisites, scale/recovery, support and commercial-readiness evidence are satisfied.
# Safety pilot CTO/CIO go/no-go decision record

Decision status: **DRAFT — NO AUTHORIZATION TO DEMO OR LOAD CLIENT DATA**

Complete this record only after the evidence index is populated. Empty, `PENDING`, `PARTIAL`, `STALE` or unsigned fields mean NO-GO.

## Candidate

| Field | Value |
|---|---|
| Release/tag | PENDING |
| Git SHA | PENDING |
| Frontend image digest | PENDING |
| API image digest | PENDING |
| Gateway image digest / not in scope | PENDING |
| Migration-manifest hash | PENDING |
| Rehearsal environment | PENDING |
| Demo tenant and fixture version | PENDING |
| Rehearsal run IDs | PENDING |
| Evidence-index hash | PENDING |

## Gate disposition

| Gate group | Owner | Result | Evidence / exception |
|---|---|---|---|
| Candidate and CI provenance | Release/QA | PENDING | PENDING |
| Security, RLS and identity separation | Security | PENDING | PENDING |
| Exposed-credential rotation and rejection | Security/SRE | FAIL | `docs/platform/CREDENTIAL_ROTATION_REQUIRED.md` remains OPEN; provider-side rotation evidence is absent |
| Platform controls/commercial boundary | Product/Platform | PENDING | PENDING |
| Multi-persona functional UAT | QA/Fleet Safety SME | PENDING | PENDING |
| Observability, support and recovery | SRE/Support | PENDING | PENDING |
| Privacy, retention and client data | CIO/Privacy | PENDING | PENDING |
| Demo repeatability and sales narrative | Product/Sales | PENDING | PENDING |

## Exceptions

For each exception record severity, customer impact, affected promise, compensating control, owner, expiry, monitoring, rollback trigger and approving signatures. P0/P1 exceptions require CTO, CIO and accountable business-owner approval; they remain stop-ship unless all three explicitly accept the risk.

| ID | Severity | Description/impact | Control and owner | Expiry/trigger | Approval |
|---|---|---|---|---|---|
| None | — | — | — | — | — |

## Operational commitments

| Commitment | Named owner / value |
|---|---|
| Demo conductor and backup | PENDING |
| On-call technical owner and contact route | PENDING |
| Client/Sales escalation owner | PENDING |
| Rollback authority and target | PENDING |
| Recovery RPO/RTO accepted | PENDING |
| Monitoring and alert destination | PENDING |
| Pilot data classification/retention/deletion agreement | PENDING |
| Supported capability statement delivered to Sales/client | PENDING |

## Decision

Select exactly one:

- [ ] **GO — client demonstration with synthetic pilot fixtures only.**
- [ ] **GO — controlled client-data pilot** within the signed capability/data/support boundaries.
- [ ] **CONDITIONAL GO** with the signed, unexpired exceptions above.
- [x] **NO-GO** — mandatory evidence or approval is incomplete.

The documented Neon credential exposure remains OPEN. Provider-side rotation,
target secret update/redeploy, old-credential rejection and healthy distinct
restricted app/system-role evidence are mandatory before GO; no secret may appear
in the evidence artifact.

Decision rationale: Current default remains NO-GO until all mandatory gates in `SAFETY_PILOT_RELEASE_GATE.md` pass and this record is signed. At the integrated 2026-08-02 review the candidate is mutable and the interactive local runtime is Development. The fourteen-ledger owner lane (Stage 47, 58, 59 and 65–75), market-pack control, fixture v7, critical-worker fail-closed checks, the clean predecessor chain and a hermetic Production-shaped restricted-identity rehearsal pass locally with all seven critical workers, contract-valid health, signed-ticket tenant/branch isolation, zero `PUBLIC` policies and zero unsafe runtime roles. Stage 75 removes the seeded Support Admin impersonation grant and supplies a uniquely bound, explicit-read-only, dual-audited support-access design; deployment policy remains default-off and the capability is excluded from this Safety pilot. Fixture v7 uses `package_allowlist`, makes all nine governed pilot entitlements explicit, and labels seeded incident telemetry honestly as URL-free, unverified, unmanaged and unavailable for retrieval. Two same-source local rendered critical-story preflights and a final fixture-v7 recovery reset now pass, including all named tenant personas, audited Platform snapshots, coaching, HOS, DVIR, ELD and branch/read-only boundaries. A later local Chrome check also proved tenant logout, Back, Forward and direct protected-route navigation remained at `/login` without rendering the incident fixture; see `SAFETY_PILOT_LOCAL_RENDERED_REHEARSALS_2026-08-02.md`. These checks are not complete executions of every acceptance scenario and are not immutable target-environment evidence. The local Platform Safety disable/hidden-navigation/deep-link/API-403/re-enable/restoration sequence also passed. No clean frozen-candidate CI artifact, published registry/deployed digests, target monitoring/edge evidence, complete acceptance ledgers, external recovery evidence, privacy agreement or approvals are indexed.

| Approver | Name | Decision | UTC date/time | Signature/reference |
|---|---|---|---|---|
| CTO | PENDING | PENDING | PENDING | PENDING |
| CIO / privacy owner | PENDING | PENDING | PENDING | PENDING |
| Fleet Safety SME | PENDING | PENDING | PENDING | PENDING |
| QA/release owner | PENDING | PENDING | PENDING | PENDING |
| SRE/support owner | PENDING | PENDING | PENDING | PENDING |
| Product owner | PENDING | PENDING | PENDING | PENDING |
| Sales/demo owner | PENDING | PENDING | PENDING | PENDING |

The approval becomes void if the candidate SHA/image/migration manifest changes, the environment control snapshot drifts without approval, a P0/P1 is discovered, or the agreed exception/decision window expires.

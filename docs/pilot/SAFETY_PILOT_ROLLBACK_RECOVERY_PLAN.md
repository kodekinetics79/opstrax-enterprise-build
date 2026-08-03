# Safety pilot rollback and recovery plan

Status: template requiring environment-specific completion and rehearsal. This plan supersedes generic rollback language for the Safety pilot.

## Recovery principle

Code rollback, configuration rollback, database recovery and client-visible data correction are different actions. Reverting an image does not undo a committed migration or user mutation. Restoring a database can discard valid writes after the restore point. The incident commander must classify the failure before authorizing an action.

## Pre-release recovery envelope

| Required fact | Value/evidence |
|---|---|
| Last known-good frontend/API/gateway image digests | PENDING |
| Candidate image digests | PENDING |
| Candidate and last-known-good schema compatibility result | PENDING |
| Migration manifest and forward-fix owner | PENDING |
| Database PITR retention and earliest restorable time | PENDING |
| Agreed RPO / RTO | PENDING |
| Last successful Safety-specific restore drill | PENDING |
| Object-store versioning/retention and restore method | PENDING |
| Client write-freeze authority and communication channel | PENDING |
| Incident commander / database recovery approver / backup | PENDING |

## Decision tree

1. **Presentation/UI defect with healthy API and data** — stop the affected story; use only a rehearsed truthful fallback. Roll back the frontend digest if the prior frontend is API/schema compatible.
2. **API regression without corrupt writes** — drain/stop new traffic, preserve logs and correlation IDs, deploy the last-known-good API digest, run readiness and critical smoke checks, then resume.
3. **Bad configuration or secret rotation** — do not rebuild code. Restore the approved configuration version through the deployment control plane, restart, verify exact DB identities and `/health/ready`/`deep`, then test login and one read-only Safety path.
4. **Forward migration incompatible with prior code** — do not deploy the old image blindly. Keep traffic stopped and apply the reviewed forward-fix, or recover into an isolated branch and validate a compatible candidate.
5. **Data corruption, cross-tenant write or evidence-integrity failure** — stop writes immediately, preserve the affected store/log/object versions, notify security/privacy owners, establish the last known-good point, restore to an isolated branch, validate tenant/Safety integrity and quantify post-point data loss before any cutover.
6. **Provider or evidence-object-store outage** — keep authoritative state intact, disable affected mutations if needed, show degraded/unknown state, and do not substitute fabricated evidence.

## Safe rollback sequence

- [ ] Incident commander records UTC time, candidate version, symptoms, affected tenants/personas and correlation IDs.
- [ ] Freeze deployment and, when integrity is at risk, customer writes. Preserve logs and database/object-store evidence.
- [ ] Select last-known-good immutable digests; verify they are compatible with the current schema and configuration contract.
- [ ] Execute the platform’s approved deploy rollback/redeploy operation. Do not rewrite shared branch history.
- [ ] Require `/health/live`, `/health/ready` and `/health/deep` to show the expected Production version and zero release-blocking checks.
- [ ] Run read-only tenant/branch boundary smoke tests before permitting writes.
- [ ] Run one idempotent Safety mutation in an approved synthetic tenant, then verify audit, database row and UI result.
- [ ] Confirm background workers, external synthetic monitor and alert delivery recover.
- [ ] Reopen traffic/demo only with incident-commander and business-owner approval.
- [ ] Reconcile writes/events that occurred during the incident; never silently discard or replay them.

## Safety-specific database restore acceptance

The repository’s `tools/dr-restore-drill.sh` establishes only database PITR reachability unless a pilot tenant is supplied. For release evidence run it with `DR_PILOT_COMPANY_CODE` and retain its output, then additionally boot the exact candidate against the isolated restored branch using the restricted runtime identities.

Before the external PITR exercise, run `tools/test-production-shaped-local-rehearsal.sh` against disposable local PostgreSQL. It applies the owner migration chain, boots the candidate in Production with separate `opstrax_app` and `opstrax_system` identities, requires healthy readiness/deep-health contracts, and executes signed-ticket tenant plus branch-isolation checks. This is deterministic preflight evidence; it does not replace the external restore or deployed-environment rehearsal.

Required restored-state checks:

- Stage 47, 58, 59 and 65–75 migration ledgers and required Safety/stream-ticket/commercial-control/retention/support-access tables, evidence columns, immutable-record offboarding guards, sequences, indexes, constraints and policies exist;
- the expected pilot tenant, Driver, Safety Manager, Maintenance and branch records exist;
- incident/evidence, coaching/scorecard, DVIR/defect and HOS/certification fixture records meet expected minimum counts and relational integrity;
- if generic managed document/POD storage is contractually in scope, a separately
  labelled test object can be retrieved and independently hashed. This infrastructure
  check does not prove Incident evidence upload, retrieval, verification or custody;
- login, tenant/branch isolation and one read-only workflow pass through the restored application;
- measured RPO/RTO meet the signed targets, including validation and cutover time—not just branch creation time.

Set `DR_DATABASE_EVIDENCE_OUTPUT` when running the repository drill to retain a hashable JSON result. It is intentionally labelled `DATABASE_PITR_PHASE_ONLY` and `PARTIAL`. Use [the external operations evidence contract](SAFETY_PILOT_EXTERNAL_OPS_EVIDENCE_CONTRACT.md) for the final DR, rollback, monitoring and privacy exercise reports; structural validation remains `REVIEW_REQUIRED` until source exports and approvals are authenticated.

## Recovery proof does not include

- success from basic row counts alone;
- an API booted with owner credentials or RLS disabled;
- a database restore without any separately scoped managed-object verification that
  the signed pilot contract actually requires;
- a previous-image deploy without schema compatibility;
- a successful health endpoint with the wrong version/environment;
- deletion of newer valid writes without an impact assessment and approval.

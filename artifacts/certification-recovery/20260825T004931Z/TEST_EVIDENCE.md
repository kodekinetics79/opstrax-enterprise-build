# Phase 1 Test Evidence

## Migration-pure empty-cluster baseline

Disposable database identity:

- PostgreSQL `16.14`, container `opstrax-phase1-pg-20260825`
- loopback port `55491`
- database `phase1_clean`
- never booted against the application runtime

The unmodified runner failed on a genuinely empty PostgreSQL cluster at:

```text
database/migrations/2026_08_13_stage80_driver_proof_upload_binding.sql:63:
ERROR: role "opstrax_system" does not exist
```

Falsifiable hypothesis: the Stage80 driver-proof migration unconditionally grants
to `opstrax_system`, while the runner creates that role only in its post-array
terminal Stage58 reconciliation. The existing CI lane was unable to expose this
because it created `opstrax_system` cluster-wide for another database first.

Regression added before remediation:

```text
MigrationRunnerEnrollmentParityTests.
PreTerminalDriverProofMigration_DoesNotRequireTheSystemRoleOnAnEmptyCluster
Baseline: failed 1, passed 0, skipped 0
```

After guarding the pre-terminal system grants (Stage58 remains the terminal grant
authority):

```text
Failed 0, passed 1, skipped 0
```

The repaired runner then completed from the truly empty database:

```text
public tables: 336
schema_migrations rows: 76
restricted roles present: opstrax_app + opstrax_system
terminal result: Stage76 prepared successfully
```

The production-shaped local rehearsal initially refused an intentionally invalid
test input where app and system connection credentials were identical. With distinct
local-only identity inputs it passed:

```text
owner migrations + Stage76 terminal reconciliation: passed
restricted identities: opstrax_app + opstrax_system
/health/live, /health/ready, /health/deep: 200 and contract-valid
signed-ticket tenant isolation + branch isolation: focused tests passed
migration ledgers: 16/16; PUBLIC policies: 0; unsafe runtime roles: 0;
Stage76 ACL violations: 0
```

After outbox was added to the critical-worker roster, the rehearsal oracle was
updated to start the worker and require eight (not seven) critical heartbeats. The
final combined candidate passed the same gate again with `/health/live`,
`/health/ready`, and `/health/deep` all HTTP 200.

## Existing focused authorization baseline

The pre-remediation suite remained green despite the audit findings:

```text
MutationPermissionTierTests: passed 35, failed 0
CustomerPortalAuthBoundaryTests + P8AnalyticsScopeTests: passed 15, failed 0
```

This is evidence of missing oracles, not closure of AUD-003, AUD-004, or AUD-029.

## Remediation regressions

Baseline witnesses:

- directed-permission witnesses reproduced narrow writes satisfying sibling writes;
- the first isolation oracle run was 1 passed / 12 failed: four customer-identity
  cases and all eight analytics branch cases failed on `b982ef8`;
- exact production logs proved no outbox dispatcher start event for the governing SHA.

Combined candidate results:

```text
Directed authorization + portal/branch focused suite: 61 passed, 0 failed, 0 skipped
Consolidated authorization suite:                 136 passed, 0 failed, 0 skipped
Full-topology PostgreSQL Phase 1 slice:             24 passed, 0 failed, 0 skipped
Outbox/inbox dispatcher integration:                10 passed, 0 failed, 0 skipped
Complete backend suite:                           2275 passed, 0 failed, 0 skipped
Frontend contracts:                                 passed
Frontend production build + bundle budget:          passed
Frontend lint:                                      passed
Production-shaped local rehearsal:                  passed
git diff --check:                                   passed
```

The first broad run found 11 consolidation failures. They were not waived: approved
read-only vocabulary edges were restored as directed edges, portal personas were
made portal-only on both frontend and backend, and two outbox tests were corrected
to retain the actual outbox ID rather than a coincident domain-event sequence ID.
Independent adversarial review then found legacy customer-route bypasses, Stage9
frontend/API drift, a stale production-enablement oracle, seed/default drift, and an
inbox backoff claim defect. Each was fixed with a regression. The final full suite
then passed with zero skips. The reviewer re-ran the final focused non-DB security
set (57 passed) and reported no remaining material source or oracle findings.

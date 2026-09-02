# G2A old-schema refusal evidence — 2026-09-02

Parent: `f50411ef3e787c25cf582e59411f6eb92d55a0b3`, published as PR120.
This is a subsequent test-only delta, not included in that PR head. No application
source, migration, provider setting, master-plan or capability status is changed.
Samsara remains PILOT / HOLD.

`SamsaraOldSchemaRefusalPostgresTests` exercises actual page ingestion, connector
and fenced finalizer code with minimal old-shape schemas and synthetic HTTP:

- Eleven masks cover zero through five nullable columns, including every single
  missing nullable column among speed/heading across the three stores.
- Two default-off controls refuse partial GPS with either zero or six nullable
  columns; the real connector configuration omits the opt-in flag.
- The six-nullable-column positive control reaches the intentional discovery-write
  barrier, proving the negative cases reach the intended preflight. It does not
  claim a complete successful sync against these minimal schemas.
- Self-tested nontransactional sequence sentinels detect attempted provider DML
  or runtime DDL even if the transaction rolls back. Refusal makes no such attempt.
- The real finalizer may record Error and release the lease, but retains the
  preexisting sync checkpoint and unrelated marker; zero pages are committed.

Each case requires explicit localhost:5433/opstrax_local and an already-privileged
disposable test role. It creates a random, test-owned database from template0,
not an old-shape schema inside the shared database. Cleanup verifies generated
name, observed OID and owner; no forced drops, global pool clearing, new grants,
production/staging calls or shared-table alteration occurs.

Implementer: provider/data test agent, **14/14 passed**, zero skips,14seconds.
Independent SDET: **14/14 passed**, zero skips,12seconds; source review found no
bounded blocker and issued LIMITED GO for local test readiness only. Both runs
verified zero test-owned databases remaining and an unchanged shared six-column
schema fingerprint. Their fingerprint encodings differ, so the digest strings
are not compared across runs. Root also reviewed the entire test source and CI's
explicit disposable database/port contract. `git diff --check` passed.

This is not a protected-role/RLS test, full owner migration rehearsal, process-kill
test, actual HTTP middleware/SSE/browser journey, live-provider evidence or human
Appendix B signature. Those boundaries remain separate. The ASP.NET guidance
keeps the tests layered around real persistence and explicit failure boundaries.

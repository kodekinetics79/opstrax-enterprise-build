# OpsTrax PostgreSQL Rollback Strategy

## Principles
- Prefer expand-and-contract over destructive in-place changes.
- Every migration should have an explicit rollback note.
- Production rollback must require human approval.

## Rollback Types
- Safe rollback: revert schema objects added in the same migration.
- Data rollback: restore from backup or replay a compensating change.
- Destructive rollback: only after explicit approval and a verified backup.

## Required Controls
- Capture before/after SQL for risky changes.
- Log migration version, actor, timestamp, and environment.
- Never assume rollback is lossless for data backfills.


# Stage 5B P0-B1A2 Rollback Notes

## Rollback Scope

This slice is additive only. Rollback is a reverse-order cleanup of local schema and code changes.

## Reverse Order

1. Remove any local references to the PostgreSQL-backed foundation services from `backend-dotnet/Program.cs`.
2. Revert the `RequirePermission` helper changes and restore the tenant helper only if a later branch explicitly requires it.
3. Drop the Stage 5B additive migration file.
4. If a local database was manually migrated, drop the Stage 5B-added columns and indexes before removing the Stage 5A tables.

## Tables / Columns to Remove If a Local DB Was Altered

- `approval_requests.correlation_id`
- `approval_decisions.tenant_id`
- `approval_decisions.approver_actor_type`
- `approval_decisions.correlation_id`
- `domain_events.retry_count`
- `outbox_messages.retry_count`
- `outbox_messages.next_attempt_at`
- `outbox_messages.processed_at`
- `inbox_messages.idempotency_key`
- `inbox_messages.payload_hash`
- `inbox_messages.retry_count`
- `inbox_messages.processed_at`
- `event_processing_logs.retry_count`
- `ai_recommendations.correlation_id`
- `ai_recommendations.causation_id`
- `ai_action_requests.correlation_id`
- `ai_action_requests.causation_id`
- `ai_action_outcomes.correlation_id`
- `ai_action_outcomes.causation_id`

## Safety Notes

- Do not run rollback SQL against production from this repo.
- Do not remove shared tables if another local branch or branch-specific test depends on them.
- If the database was never migrated locally, code-only rollback is sufficient.


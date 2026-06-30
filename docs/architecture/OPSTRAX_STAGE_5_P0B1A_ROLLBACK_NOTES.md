# Stage 5 P0-B1A Rollback Notes

## Scope

Local-only rollback guidance for the additive foundation migration.

## Rollback Order

1. Stop any local process that may be writing to the new foundation tables.
2. Revert code changes that depend on the new foundation tables.
3. Drop the new foundation tables in reverse dependency order:
   - `ai_action_outcomes`
   - `ai_action_requests`
   - `ai_recommendation_impacts`
   - `ai_recommendation_reasons`
   - `ai_recommendations`
   - `ai_reasoning_runs`
   - `idempotency_keys`
   - `event_processing_logs`
   - `inbox_messages`
   - `outbox_messages`
   - `domain_events`
   - `approval_decisions`
   - `approval_requests`
   - `approval_policies`
   - `authorization_decision_logs`
4. Remove the local migration file only after the schema is confirmed unused.

## Notes

- This migration is additive and non-destructive, so rollback is a drop-and-revert operation.
- Do not apply rollback steps to production from this workspace.
- If later stages add foreign keys or workers, update this list to match the final dependency graph.

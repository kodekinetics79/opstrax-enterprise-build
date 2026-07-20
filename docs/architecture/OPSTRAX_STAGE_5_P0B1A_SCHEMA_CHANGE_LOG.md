# Stage 5 P0-B1A Schema Change Log

## Added Foundation Tables

- `authorization_decision_logs`
- `approval_policies`
- `approval_requests`
- `approval_decisions`
- `domain_events`
- `outbox_messages`
- `inbox_messages`
- `event_processing_logs`
- `idempotency_keys`
- `ai_reasoning_runs`
- `ai_recommendations`
- `ai_recommendation_reasons`
- `ai_recommendation_impacts`
- `ai_action_requests`
- `ai_action_outcomes`

## Discipline Notes

- The migration is additive and local-only.
- All tables use `CREATE TABLE IF NOT EXISTS` and matching indexes to keep the slice idempotent.
- The schema mirrors the new backend foundation service so startup checks and local migration runs stay aligned.

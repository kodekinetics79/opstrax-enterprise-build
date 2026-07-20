# Stage 5B P0-B1A2 Schema Change Log

## Added Or Updated

- `authorization_decision_logs`
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

## Stage 5B Additions

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

## Index Additions

- `idx_approval_decisions_tenant`
- `idx_outbox_tenant_next_attempt`
- `idx_inbox_tenant_source_external`
- `idx_ai_action_outcomes_tenant_recorded`

## Notes

- The migration remains additive and PostgreSQL-specific.
- No destructive statements were introduced.
- No production migration was run from this shell.


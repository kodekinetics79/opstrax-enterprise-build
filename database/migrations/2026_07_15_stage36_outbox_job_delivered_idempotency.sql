-- Stage 36 — Delivery -> billing automation (ADR-008 §B Phase 2)
-- Durable idempotency for the job.delivered outbox event. Both delivery entry points (the
-- status-transition path and the proof-of-delivery path) enqueue a job.delivered event via
-- INSERT ... ON CONFLICT DO NOTHING against this partial unique index, so a delivery fired or
-- proof re-submitted twice enqueues the billing event exactly once per (tenant, job).
--
-- Owner migration for restricted-role prod (which skips FoundationSchemaService.EnsureAsync).
-- Mirrors the EnsureAsync definition; IF NOT EXISTS keeps it re-runnable and drift-safe.

CREATE UNIQUE INDEX IF NOT EXISTS ux_outbox_job_delivered
    ON outbox_messages (tenant_id, aggregate_id)
    WHERE event_type = 'job.delivered';

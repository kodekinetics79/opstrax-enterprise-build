-- Telematics 004 — Idempotent projection inbox + monotonic latest-position upsert contract
-- ============================================================================
-- PURPOSE
--   The gateway delivers canonical telemetry AT-LEAST-ONCE (see IEventBackbone:
--   "a consumer may see the same EventId more than once and must deduplicate on
--   it") and, after a broker outage, the store-and-forward REPLAY path re-emits
--   parked events. Both mean the live-map projector — the consumer that folds
--   canonical events down into latest_vehicle_positions — will be handed the SAME
--   event twice and, under replay/reordering, sometimes OUT OF FIX-TIME ORDER.
--
--   A naive "UPDATE latest_vehicle_positions SET ... " projector is wrong on both
--   counts: it double-counts event_count on a redelivery, and it can stamp an
--   OLDER fix over a NEWER one when a delayed replay lands after a live fix.
--
--   This migration adds the two DB-level invariants that make the projection
--   correct regardless of delivery order or duplication:
--     1. telemetry_projection_inbox — a dedupe ledger keyed UNIQUE on event_id.
--        The projector INSERTs ... ON CONFLICT (event_id) DO NOTHING inside the
--        same transaction as the upsert; a redelivery inserts 0 rows and the
--        projector skips the upsert entirely (idempotent no-op).
--     2. A MONOTONIC upsert contract on latest_vehicle_positions whose
--        ON CONFLICT DO UPDATE ... WHERE guard refuses to overwrite a stored fix
--        with one bearing an OLDER device_fix_time (last-write-wins by fix time,
--        not by arrival time). See section 3 — it is executed verbatim by
--        PostgresPositionProjectionStore.
--
-- GROUNDING
--   * telemetry_projection_inbox is tenant/company scoped (tenant_id UUID +
--     company_id BIGINT NOT NULL) and RLS-enabled + FORCE'd on company_id, exactly
--     like canonical_telemetry_events / raw_packets (telematics 003) and the
--     stage19/stage20 convention (predicate current_setting('app.current_tenant_id')).
--   * latest_vehicle_positions already carries the provenance/trust columns from
--     telematics 001 (source, provider, protocol, adapter_version, device_fix_time,
--     gateway_received_at, normalized_at, confidence, trust_score, quality_flags)
--     and its natural key is UNIQUE (company_id, vehicle_id) (TelemetrySchemaService).
--     This migration ADDS NO COLUMNS to it — it only documents + owns the upsert
--     CONTRACT the projector runs against those existing columns.
--
-- SAFETY / REVERSIBILITY
--   ADDITIVE + idempotent + re-runnable. Creates one new table (+ its policies,
--   indexes, grants); touches no existing row and drops nothing. MUST be applied by
--   the DB OWNER (the app runs as the restricted opstrax_app role under RLS —
--   see stage28/stage29). Explicit -- ROLLBACK section at the foot.
-- ============================================================================

BEGIN;

-- ── 1. Dedupe ledger: telemetry_projection_inbox ─────────────────────────────
-- One row per canonical event that has been *seen* by the projector. event_id is
-- the canonical observation's globally-unique idempotency key (CanonicalTelemetryEvent.
-- EventId / EventEnvelope.EventId). UNIQUE(event_id) is the whole mechanism: the
-- projector's INSERT ... ON CONFLICT (event_id) DO NOTHING returns 1 row the first
-- time and 0 rows on every redelivery, and the projector branches on that count.
CREATE TABLE IF NOT EXISTS telemetry_projection_inbox (
    event_id        UUID        NOT NULL,             -- canonical EventId; the idempotency key
    correlation_id  UUID        NULL,                 -- ties the projection back to the ingest trail
    tenant_id       UUID        NOT NULL,             -- registry-resolved owning tenant
    company_id      BIGINT      NOT NULL,             -- tenant scope (RLS predicate column)
    device_id       TEXT        NULL,                 -- fabric device id (string), for forensic lookup
    vehicle_id      BIGINT      NULL,                 -- vehicle the fix projected onto, when bound
    device_fix_time TIMESTAMPTZ NULL,                 -- device-asserted fix time of the projected event
    schema_version  INT         NOT NULL DEFAULT 1,   -- canonical schema version the event was produced against
    projected_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),-- when the projector first accepted this event
    CONSTRAINT pk_telemetry_projection_inbox PRIMARY KEY (event_id)  -- UNIQUE(event_id): dedupe key
);

-- Retention / housekeeping: the inbox only needs to remember an event for as long
-- as a duplicate could plausibly be redelivered (replay window). Prune by age,
-- tenant-leading so it composes with the RLS predicate.
CREATE INDEX IF NOT EXISTS idx_projection_inbox_company_projected
    ON telemetry_projection_inbox (company_id, projected_at DESC);
-- Provenance join: inbox row -> canonical event / raw packet by correlation.
CREATE INDEX IF NOT EXISTS idx_projection_inbox_correlation
    ON telemetry_projection_inbox (correlation_id)
    WHERE correlation_id IS NOT NULL;

-- Tenant isolation (mirrors telematics 003). The projector SET LOCAL
-- app.current_tenant_id = <company_id> inside its transaction; the platform admin
-- bypass lets an owner/back-office consumer read across tenants.
ALTER TABLE telemetry_projection_inbox ENABLE ROW LEVEL SECURITY;
ALTER TABLE telemetry_projection_inbox FORCE  ROW LEVEL SECURITY;
DO $rls$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'telemetry_projection_inbox' AND policyname = 'tenant_isolation') THEN
        CREATE POLICY tenant_isolation ON telemetry_projection_inbox
            USING      (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
            WITH CHECK (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'telemetry_projection_inbox' AND policyname = 'platform_admin_bypass') THEN
        CREATE POLICY platform_admin_bypass ON telemetry_projection_inbox
            USING      (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
            WITH CHECK (NULLIF(current_setting('app.platform_admin', true), '') = 'on');
    END IF;
END
$rls$;

-- ── 2. Grants to the restricted app role (skip where role absent — stage33 note) ─
-- The projector INSERTs into the inbox and never mutates it after the fact, so no
-- UPDATE/DELETE grant. DELETE for the pruning job is granted to the owner only.
DO $grant$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'opstrax_app') THEN
        GRANT SELECT, INSERT ON telemetry_projection_inbox TO opstrax_app;
    END IF;
END
$grant$;

-- ── 3. THE UPSERT CONTRACT (reference; executed verbatim by the projector) ────
-- PostgresPositionProjectionStore runs the two statements below, in ONE
-- transaction, after SET LOCAL app.current_tenant_id = <company_id>:
--
--   (a) INSERT INTO telemetry_projection_inbox
--           (event_id, correlation_id, tenant_id, company_id, device_id,
--            vehicle_id, device_fix_time, schema_version)
--       VALUES (@event_id, @correlation_id, @tenant_id, @company_id, @device_id,
--               @vehicle_id, @device_fix_time, @schema_version)
--       ON CONFLICT (event_id) DO NOTHING;
--       -- rows affected = 0  => duplicate  => COMMIT and skip (b)  (idempotent no-op)
--       -- rows affected = 1  => first sight => run (b)
--
--   (b) MONOTONIC upsert onto the existing latest_vehicle_positions natural key.
--       The DO UPDATE ... WHERE clause is the monotonicity guard: an incoming fix
--       is applied only when it is NOT OLDER than the fix already stored, so a
--       delayed replay can never stamp a stale position over a fresher one. On a
--       true stale event the UPDATE matches 0 rows and the projection is a no-op.
--
--       INSERT INTO latest_vehicle_positions
--           (company_id, vehicle_id, device_id, lat, lng, speed_mph, heading,
--            source, provider, protocol, adapter_version, confidence, trust_score,
--            quality_flags, device_fix_time, gateway_received_at, normalized_at,
--            event_time, received_at, event_count)
--       VALUES
--           (@company_id, @vehicle_id, NULL, @lat, @lng, @speed_mph, @heading,
--            @source, @provider, @protocol, @adapter_version, @confidence, @trust_score,
--            @quality_flags::jsonb, @device_fix_time, @gateway_received_at, @normalized_at,
--            @event_time, @received_at, 1)
--       ON CONFLICT (company_id, vehicle_id) DO UPDATE SET
--            lat = EXCLUDED.lat, lng = EXCLUDED.lng,
--            speed_mph = EXCLUDED.speed_mph, heading = EXCLUDED.heading,
--            source = EXCLUDED.source, provider = EXCLUDED.provider,
--            protocol = EXCLUDED.protocol, adapter_version = EXCLUDED.adapter_version,
--            confidence = EXCLUDED.confidence, trust_score = EXCLUDED.trust_score,
--            quality_flags = EXCLUDED.quality_flags,
--            device_fix_time = EXCLUDED.device_fix_time,
--            gateway_received_at = EXCLUDED.gateway_received_at,
--            normalized_at = EXCLUDED.normalized_at,
--            event_time = EXCLUDED.event_time,
--            received_at = EXCLUDED.received_at,
--            event_count = latest_vehicle_positions.event_count + 1
--       WHERE EXCLUDED.device_fix_time IS NOT NULL
--         AND (latest_vehicle_positions.device_fix_time IS NULL
--              OR EXCLUDED.device_fix_time >= latest_vehicle_positions.device_fix_time);
--
--   NOTE (correlation_id drift): latest_vehicle_positions.correlation_id was added
--   by stage12a as VARCHAR(120) and only conditionally reconciled to UUID by
--   telematics 001. To stay compatible with BOTH shapes the projector does NOT
--   write correlation_id onto latest_vehicle_positions; the authoritative,
--   cleanly-typed correlation lives on telemetry_projection_inbox.correlation_id
--   (UUID) above, which is the provenance-join anchor.

-- ── 4. Ledger ────────────────────────────────────────────────────────────────
INSERT INTO schema_migrations (version, description)
VALUES ('telematics_004_projection_inbox',
        'Idempotent projection inbox (UNIQUE event_id dedupe) + monotonic latest-position upsert contract')
ON CONFLICT (version) DO NOTHING;

COMMIT;

-- ============================================================================
-- ROLLBACK  (manual; run as DB OWNER — NOT auto-applied)
-- ----------------------------------------------------------------------------
-- BEGIN;
--   DROP TABLE IF EXISTS telemetry_projection_inbox;   -- drops its policies, indexes and grants
--   DELETE FROM schema_migrations WHERE version = 'telematics_004_projection_inbox';
--   -- latest_vehicle_positions is intentionally untouched here: this migration
--   -- added no columns to it (it only documented the upsert contract), so there
--   -- is nothing on that table to reverse.
-- COMMIT;
-- ============================================================================

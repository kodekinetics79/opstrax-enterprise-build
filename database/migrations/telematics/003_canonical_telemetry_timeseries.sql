-- Telematics 003 — Canonical telemetry time-series (warm) + raw packet archive (cold)
-- ============================================================================
-- PURPOSE
--   location_events (database/init/001_schema.sql:318) is a flat, unpartitioned
--   breadcrumb log. At real device volume (per-vehicle pings every few seconds ×
--   4 tenants × growing fleets) an unpartitioned log becomes an unbounded scan
--   for any historical query and impossible to age out without a costly DELETE.
--
--   This migration introduces the tiered telemetry store the retention design
--   (docs/telematics/db/partitioning-retention.md) depends on:
--     * canonical_telemetry_events — WARM. Normalized, provenance-bearing events,
--       RANGE-partitioned by month on event_time so old months prune out of every
--       query plan and can be DETACHed/DROPped in O(1) instead of row-by-row.
--     * raw_packets — COLD. Byte-exact inbound frames for forensic replay /
--       compliance, kept cheap and separate from the query-hot warm store (or
--       offloaded to object storage — see the doc + note below).
--
-- GROUNDING
--   Columns mirror the provenance contract from telematics 001 (source, provider,
--   protocol, adapter_version, confidence, trust_score, quality_flags,
--   correlation_id UUID) and the tenant-scope convention (company_id BIGINT NOT
--   NULL, RLS per stage19/stage20). Partition key event_time matches the
--   device-fix-time axis the live-map / breadcrumb / stale-device queries filter on.
--
-- PARTITIONING RULES (Postgres)
--   * A partitioned table's PRIMARY KEY / UNIQUE must include the partition key,
--     so PK = (id, event_time).
--   * Indexes created on the parent propagate to every existing AND future
--     partition automatically (PG11+), so the plan stays index-bounded per month.
--   * RLS enabled+FORCE'd on the parent applies to all partitions.
--   * A DEFAULT partition catches out-of-window inserts so ingest never 500s on a
--     missing month; the monthly job (pg_cron / pg_partman / app) pre-creates the
--     next month and should keep the default empty. See the retention doc.
--
-- SAFETY / REVERSIBILITY
--   ADDITIVE + idempotent + re-runnable. Creates new tables/partitions only;
--   touches no existing data. MUST be applied by the DB OWNER. Explicit
--   -- ROLLBACK section at the foot.
-- ============================================================================

BEGIN;

-- ── 1. WARM store: canonical_telemetry_events (RANGE partitioned by month) ────
CREATE TABLE IF NOT EXISTS canonical_telemetry_events (
    id              BIGINT       GENERATED ALWAYS AS IDENTITY,
    company_id      BIGINT       NOT NULL,          -- tenant scope (RLS predicate column)
    vehicle_id      BIGINT       NULL,
    device_id       BIGINT       NULL,
    driver_id       BIGINT       NULL,
    correlation_id  UUID         NULL,              -- ties event -> ingest trail / live snapshot / raw packet
    event_type      VARCHAR(80)  NOT NULL DEFAULT 'location.updated',
    -- Positional payload (normalized)
    lat             DECIMAL(10,7) NULL,
    lng             DECIMAL(10,7) NULL,
    speed_mph       DECIMAL(8,2)  NULL,
    heading         DECIMAL(8,2)  NULL,
    altitude_m      DECIMAL(8,2)  NULL,
    -- Provenance + trust (mirrors telematics 001)
    source          TEXT          NULL,
    provider        TEXT          NULL,
    protocol        TEXT          NULL,
    adapter_version TEXT          NULL,
    confidence      NUMERIC(4,3)  NULL,
    trust_score     NUMERIC(4,3)  NULL,
    quality_flags   JSONB         NULL,
    payload         JSONB         NULL,             -- full normalized canonical event body
    -- Time axis
    device_fix_time     TIMESTAMPTZ NULL,           -- device-asserted fix time
    gateway_received_at TIMESTAMPTZ NULL,           -- gateway receipt
    ingested_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    event_time          TIMESTAMPTZ NOT NULL,       -- canonical event time = PARTITION KEY
    PRIMARY KEY (id, event_time),                   -- partition key must be in the PK
    CONSTRAINT ck_cte_confidence  CHECK (confidence  IS NULL OR (confidence  >= 0 AND confidence  <= 1)),
    CONSTRAINT ck_cte_trust_score CHECK (trust_score IS NULL OR (trust_score >= 0 AND trust_score <= 1))
) PARTITION BY RANGE (event_time);

-- Live-map / breadcrumb: newest-first per (tenant, vehicle) window. Tenant-leading
-- so it composes with the RLS predicate; propagates to every partition.
CREATE INDEX IF NOT EXISTS idx_cte_company_vehicle_time
    ON canonical_telemetry_events (company_id, vehicle_id, event_time DESC);
-- Provenance / correlation join (event -> raw packet -> live snapshot).
CREATE INDEX IF NOT EXISTS idx_cte_correlation
    ON canonical_telemetry_events (correlation_id)
    WHERE correlation_id IS NOT NULL;

-- RLS on the partitioned parent (applies to all partitions).
ALTER TABLE canonical_telemetry_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE canonical_telemetry_events FORCE  ROW LEVEL SECURITY;
DO $rls$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'canonical_telemetry_events' AND policyname = 'tenant_isolation') THEN
        CREATE POLICY tenant_isolation ON canonical_telemetry_events
            USING      (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
            WITH CHECK (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'canonical_telemetry_events' AND policyname = 'platform_admin_bypass') THEN
        CREATE POLICY platform_admin_bypass ON canonical_telemetry_events
            USING      (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
            WITH CHECK (NULLIF(current_setting('app.platform_admin', true), '') = 'on');
    END IF;
END
$rls$;

-- Example monthly partitions. In steady state a monthly job pre-creates the NEXT
-- month before it is needed; these seed the current window so ingest has a home.
-- (Upper bound is EXCLUSIVE, so [month, next month) is exactly one calendar month.)
CREATE TABLE IF NOT EXISTS canonical_telemetry_events_2026_07
    PARTITION OF canonical_telemetry_events
    FOR VALUES FROM ('2026-07-01 00:00:00+00') TO ('2026-08-01 00:00:00+00');
CREATE TABLE IF NOT EXISTS canonical_telemetry_events_2026_08
    PARTITION OF canonical_telemetry_events
    FOR VALUES FROM ('2026-08-01 00:00:00+00') TO ('2026-09-01 00:00:00+00');

-- DEFAULT partition: safety net so an out-of-window fix never fails ingest. The
-- monthly job keeps it empty by pre-creating real month partitions. NOTE: attaching
-- a new month partition scans the default for overlapping rows, so do not let it grow.
CREATE TABLE IF NOT EXISTS canonical_telemetry_events_default
    PARTITION OF canonical_telemetry_events DEFAULT;

-- ── 2. COLD archive: raw_packets (byte-exact inbound frames) ─────────────────
-- Kept separate from the warm store so forensic bytes never bloat the query-hot
-- path. Small fleets: keep here. Large fleets / long compliance windows: offload
-- the `raw` bytes to object storage and keep only the pointer — see OFFLOAD NOTE.
CREATE TABLE IF NOT EXISTS raw_packets (
    id             BIGINT      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id     BIGINT      NOT NULL,            -- tenant scope (RLS predicate column)
    device_id      BIGINT      NULL,
    correlation_id UUID        NULL,                -- joins the frame to its canonical event
    protocol       VARCHAR(40) NULL,               -- 'GT06','JT808','rest_json',...
    raw            BYTEA       NULL,                -- byte-exact frame; NULL when offloaded (see storage_url)
    storage_url    TEXT        NULL,               -- OFFLOAD pointer: s3://bucket/tenant/<id> when raw is NULL
    byte_size      INTEGER     NULL,
    received_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_raw_packets_body CHECK (raw IS NOT NULL OR storage_url IS NOT NULL)  -- bytes here OR offloaded, never neither
);

CREATE INDEX IF NOT EXISTS idx_raw_packets_company_received
    ON raw_packets (company_id, received_at DESC);
CREATE INDEX IF NOT EXISTS idx_raw_packets_correlation
    ON raw_packets (correlation_id)
    WHERE correlation_id IS NOT NULL;

ALTER TABLE raw_packets ENABLE ROW LEVEL SECURITY;
ALTER TABLE raw_packets FORCE  ROW LEVEL SECURITY;
DO $rls$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'raw_packets' AND policyname = 'tenant_isolation') THEN
        CREATE POLICY tenant_isolation ON raw_packets
            USING      (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
            WITH CHECK (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'raw_packets' AND policyname = 'platform_admin_bypass') THEN
        CREATE POLICY platform_admin_bypass ON raw_packets
            USING      (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
            WITH CHECK (NULLIF(current_setting('app.platform_admin', true), '') = 'on');
    END IF;
END
$rls$;

-- ── 3. Grants to the restricted app role (skip where role absent — stage33 note) ─
DO $grant$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'opstrax_app') THEN
        -- Warm store: append + read; no destructive DML (aging-out is DROP PARTITION by the owner).
        GRANT SELECT, INSERT ON canonical_telemetry_events TO opstrax_app;
        GRANT SELECT, INSERT ON raw_packets                TO opstrax_app;
        GRANT USAGE, SELECT ON SEQUENCE canonical_telemetry_events_id_seq TO opstrax_app;
        GRANT USAGE, SELECT ON SEQUENCE raw_packets_id_seq                TO opstrax_app;
    END IF;
END
$grant$;

-- ── 4. Ledger ───────────────────────────────────────────────────────────────
INSERT INTO schema_migrations (version, description)
VALUES ('telematics_003_canonical_telemetry_timeseries',
        'Monthly RANGE-partitioned canonical_telemetry_events (warm) + raw_packets (cold), RLS')
ON CONFLICT (version) DO NOTHING;

COMMIT;

-- ============================================================================
-- RETENTION / OPERATIONS NOTES  (full plan: docs/telematics/db/partitioning-retention.md)
-- ----------------------------------------------------------------------------
--  * Monthly rollout (owner job — pg_cron / pg_partman / app worker):
--      -- pre-create NEXT month before month start
--      CREATE TABLE IF NOT EXISTS canonical_telemetry_events_2026_09
--        PARTITION OF canonical_telemetry_events
--        FOR VALUES FROM ('2026-09-01 00:00:00+00') TO ('2026-10-01 00:00:00+00');
--  * Age-out WARM (retain ~13 months) — O(1), no row DELETE, index untouched:
--      ALTER TABLE canonical_telemetry_events DETACH PARTITION canonical_telemetry_events_2025_06;
--      -- then DROP TABLE (purge) or move to slow storage / dump to object store.
--  * COLD raw_packets: retain per compliance (e.g. KSA/ELD multi-year). Prefer
--    the OFFLOAD path (raw=NULL + storage_url) to keep the DB small; or partition
--    raw_packets by month too and DETACH+DROP on the compliance window.
--
-- OFFLOAD NOTE (object storage): for large fleets do NOT store frames as BYTEA in
--   Postgres long-term. Write the frame to object storage (S3/R2/GCS) under a
--   tenant-prefixed key and persist only the pointer: INSERT raw_packets(company_id,
--   device_id, correlation_id, protocol, raw=NULL, storage_url='s3://.../<uuid>',
--   byte_size, received_at). The ck_raw_packets_body CHECK enforces bytes-here XOR
--   pointer. Object lifecycle rules then own cold retention/expiry.
-- ============================================================================

-- ============================================================================
-- ROLLBACK  (manual; run as DB OWNER — NOT auto-applied)
-- ----------------------------------------------------------------------------
-- BEGIN;
--   DROP TABLE IF EXISTS raw_packets;                            -- drops its policies + sequence
--   DROP TABLE IF EXISTS canonical_telemetry_events CASCADE;     -- CASCADE drops all partitions + policies + sequence
--   DELETE FROM schema_migrations WHERE version = 'telematics_003_canonical_telemetry_timeseries';
-- COMMIT;
-- ============================================================================

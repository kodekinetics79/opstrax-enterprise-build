-- 009_driver_portal_identity.sql
-- Driver Portal: identity, proof-of-delivery persistence, and status hygiene.
--
-- CONTEXT — the driver portal was 100% non-functional for every driver in every tenant.
-- Three independent defects, all repaired here or in the code that ships with this file:
--
--   1. IDENTITY. `drivers.user_id` exists (added at boot by DriverSchemaService) and is read
--      by 5 call sites — and written by NOTHING. Zero rows in any tenant had it set, so
--      GetDriverIdFromAuthAsync returned -1 for every driver and every /api/driver/* route
--      403'd. There was never a product path to give a driver a login. Sections A + B below
--      make the column trustworthy; the `POST /api/drivers/{id}/portal-invite` endpoint that
--      ships with this migration is the path that finally writes it.
--
--   2. PROOF OF DELIVERY. The driver app packs its POD artifact references (photo +
--      signature object keys, ~185 chars of JSON) into `dispatch_proofs.evidence_hash`,
--      which is VARCHAR(128). Every POD-with-media submit therefore died on Postgres 22001
--      (value too long) — a driver physically could not confirm a delivery. Section C gives
--      the artifacts a real home and leaves evidence_hash for an actual hash.
--
--   3. STATUS VOCABULARY. `assignment_status` carries a mix of the canonical lowercase P4
--      tokens and legacy title-case values ('Assigned', 'In Progress', 'Accepted'). The
--      driver endpoints compare the raw column against lowercase tokens, so a legacy row is
--      untransitionable ("invalid transition from ''") and 'Cancelled' rows leak into the
--      driver's active-assignment list (they do not equal 'cancelled'). Section D backfills.
--
-- Permission repair (the Driver role lacking `driver:self`) is NOT here on purpose: it is
-- DML, and it is applied on every boot in every environment by RolePermissionReconciler, so
-- it self-heals rather than depending on someone remembering to run a migration.
--
-- Apply as the DB OWNER. Production runs as the restricted `opstrax_app` role under RLS
-- enforcement, where the app deliberately SKIPS schema init — DDL never runs itself there.
-- Idempotent; safe to re-run.

BEGIN;

-- ── A. drivers.user_id becomes a real foreign key ────────────────────────────────────────
-- It was a bare BIGINT with no FK: nothing stopped it pointing at a deleted user, or at a
-- user in a different tenant.
ALTER TABLE drivers ADD COLUMN IF NOT EXISTS user_id BIGINT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'fk_drivers_user'
    ) THEN
        ALTER TABLE drivers
            ADD CONSTRAINT fk_drivers_user
            FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE SET NULL;
    END IF;
END $$;

-- ── B. One login ⇄ one driver ────────────────────────────────────────────────────────────
-- The old index (idx_drivers_user_id) was NON-unique, so two drivers could share a user_id
-- and the resolver's `LIMIT 1` would silently pick whichever row came back first — a driver
-- could have acted as, and been paid for, another driver's assignments. The unique partial
-- index makes that unrepresentable. Partial so the 1281 un-provisioned drivers (user_id
-- NULL) and soft-deleted rows do not collide with each other.
DROP INDEX IF EXISTS idx_drivers_user_id;
CREATE UNIQUE INDEX IF NOT EXISTS uq_drivers_user_id
    ON drivers (user_id)
    WHERE user_id IS NOT NULL AND deleted_at IS NULL;

-- ── C. Proof-of-delivery artifacts get a real table ──────────────────────────────────────
CREATE TABLE IF NOT EXISTS dispatch_proof_artifacts (
    id            BIGSERIAL PRIMARY KEY,
    company_id    BIGINT       NOT NULL,
    proof_id      BIGINT       NOT NULL REFERENCES dispatch_proofs(id) ON DELETE CASCADE,
    kind          VARCHAR(30)  NOT NULL,           -- 'photo' | 'signature'
    reference     TEXT         NOT NULL,           -- object-storage key (objkey:tenant/…)
    content_type  VARCHAR(120) NULL,
    size_bytes    BIGINT       NULL,
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_proof_artifact_kind CHECK (kind IN ('photo', 'signature'))
);

CREATE INDEX IF NOT EXISTS ix_proof_artifacts_proof   ON dispatch_proof_artifacts (proof_id);
CREATE INDEX IF NOT EXISTS ix_proof_artifacts_company ON dispatch_proof_artifacts (company_id);

-- Tenant isolation, matching the 199 other RLS-protected tenant tables.
ALTER TABLE dispatch_proof_artifacts ENABLE ROW LEVEL SECURITY;
ALTER TABLE dispatch_proof_artifacts FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation ON dispatch_proof_artifacts;
CREATE POLICY tenant_isolation ON dispatch_proof_artifacts
    USING (
        current_setting('app.platform_admin', TRUE) = 'on'
        OR company_id = NULLIF(current_setting('app.current_tenant_id', TRUE), '')::BIGINT
    )
    WITH CHECK (
        current_setting('app.platform_admin', TRUE) = 'on'
        OR company_id = NULLIF(current_setting('app.current_tenant_id', TRUE), '')::BIGINT
    );

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'opstrax_app') THEN
        GRANT SELECT, INSERT, UPDATE, DELETE ON dispatch_proof_artifacts TO opstrax_app;
        GRANT USAGE, SELECT ON SEQUENCE dispatch_proof_artifacts_id_seq TO opstrax_app;
    END IF;
END $$;

-- ── D. Canonicalise legacy assignment_status values ──────────────────────────────────────
-- Mirrors EndpointMappings.NormalizeAssignmentStatus exactly. Without this, rows written
-- before the P4 token vocabulary landed are invisible/untransitionable to the driver app.
UPDATE dispatch_assignments
SET assignment_status = CASE LOWER(REPLACE(REPLACE(TRIM(assignment_status), '-', '_'), ' ', '_'))
        WHEN 'pending'            THEN 'unassigned'
        WHEN 'en_route'           THEN 'en_route_pickup'
        WHEN 'enroute'            THEN 'en_route_pickup'
        WHEN 'at_pickup'          THEN 'arrived_pickup'
        WHEN 'in_progress'        THEN 'in_transit'
        WHEN 'intransit'          THEN 'in_transit'
        WHEN 'en_route_delivery'  THEN 'in_transit'
        WHEN 'at_delivery'        THEN 'arrived_delivery'
        WHEN 'completed'          THEN 'delivered'
        WHEN 'complete'           THEN 'delivered'
        WHEN 'canceled'           THEN 'cancelled'
        ELSE LOWER(REPLACE(REPLACE(TRIM(assignment_status), '-', '_'), ' ', '_'))
    END
WHERE assignment_status IS NOT NULL
  AND assignment_status <> CASE LOWER(REPLACE(REPLACE(TRIM(assignment_status), '-', '_'), ' ', '_'))
        WHEN 'pending'            THEN 'unassigned'
        WHEN 'en_route'           THEN 'en_route_pickup'
        WHEN 'enroute'            THEN 'en_route_pickup'
        WHEN 'at_pickup'          THEN 'arrived_pickup'
        WHEN 'in_progress'        THEN 'in_transit'
        WHEN 'intransit'          THEN 'in_transit'
        WHEN 'en_route_delivery'  THEN 'in_transit'
        WHEN 'at_delivery'        THEN 'arrived_delivery'
        WHEN 'completed'          THEN 'delivered'
        WHEN 'complete'           THEN 'delivered'
        WHEN 'canceled'           THEN 'cancelled'
        ELSE LOWER(REPLACE(REPLACE(TRIM(assignment_status), '-', '_'), ' ', '_'))
    END;

-- A NULL assignment_status fails every `NOT IN (…)` predicate under SQL three-valued logic,
-- so such a row is silently invisible to the driver. Derive it from the legacy `status`.
UPDATE dispatch_assignments
SET assignment_status = COALESCE(NULLIF(LOWER(REPLACE(REPLACE(TRIM(status), '-', '_'), ' ', '_')), ''), 'assigned')
WHERE assignment_status IS NULL OR TRIM(assignment_status) = '';

-- ── E. Stop users.permissions_json from lying ────────────────────────────────────────────
-- When a user has a role, the resolver reads the ROLE's grants and ignores this column
-- entirely — yet 22 users carry a non-empty value here, and the admin UI renders it. Someone
-- had already tried to hand the Acme QA driver `driver:self` this way; it was never read.
-- Blank it for role-bearing users so the DB stops asserting access it does not confer.
-- (The column stays: it is still live for legacy accounts with no role_id.)
UPDATE users
SET permissions_json = '[]'::jsonb
WHERE role_id IS NOT NULL
  AND role_id > 0
  AND permissions_json IS NOT NULL
  AND jsonb_array_length(permissions_json) > 0;

COMMIT;

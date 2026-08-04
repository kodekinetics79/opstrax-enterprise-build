-- Stage 72: preserve immutable evidence while permitting audited tenant erasure.
-- Delete is allowed only for the isolated system identity inside a transaction
-- explicitly marked by TenantOffboardingService. Updates remain impossible.
BEGIN;

CREATE OR REPLACE FUNCTION stage65_prevent_certified_hos_log_delete()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
  IF OLD.is_certified
     AND NOT (
       COALESCE(current_setting('opstrax.offboarding', true) = 'on', FALSE)
       AND pg_has_role(current_user, 'opstrax_system', 'MEMBER')
     ) THEN
    RAISE EXCEPTION 'Certified HOS segments cannot be deleted; create a correction and recertify';
  END IF;
  RETURN OLD;
END $$;

CREATE OR REPLACE FUNCTION stage65_guard_hos_certification_snapshot()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
  IF TG_OP = 'DELETE'
     AND COALESCE(current_setting('opstrax.offboarding', true) = 'on', FALSE)
     AND pg_has_role(current_user, 'opstrax_system', 'MEMBER') THEN
    RETURN OLD;
  END IF;
  RAISE EXCEPTION 'HOS certification snapshots are immutable';
END $$;

-- Detention evidence is also an immutable tenant-owned record. Keep ordinary
-- UPDATE/DELETE behavior unchanged, but permit DELETE only for the same
-- dual-gated system offboarding transaction used for HOS evidence.
CREATE OR REPLACE FUNCTION detention_evidence_immutable() RETURNS trigger AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF COALESCE(current_setting('opstrax.offboarding', true) = 'on', FALSE)
           AND pg_has_role(current_user, 'opstrax_system', 'MEMBER') THEN
            RETURN OLD;
        END IF;
        RAISE EXCEPTION 'detention_evidence is immutable';
    END IF;
    IF NEW.evidence_canonical IS DISTINCT FROM OLD.evidence_canonical
       OR NEW.evidence_json::text IS DISTINCT FROM OLD.evidence_json::text
       OR NEW.evidence_sha256 IS DISTINCT FROM OLD.evidence_sha256
       OR NEW.dwell_id IS DISTINCT FROM OLD.dwell_id
       OR NEW.company_id IS DISTINCT FROM OLD.company_id
       OR NEW.schema_version IS DISTINCT FROM OLD.schema_version
       OR NEW.breadcrumb_count IS DISTINCT FROM OLD.breadcrumb_count
       OR NEW.breadcrumbs_included IS DISTINCT FROM OLD.breadcrumbs_included
       OR NEW.full_trail_sha256 IS DISTINCT FROM OLD.full_trail_sha256
       OR NEW.created_at IS DISTINCT FROM OLD.created_at THEN
        RAISE EXCEPTION 'detention_evidence is immutable (only share fields may change)';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_08_02_stage72_hos_offboarding_immutability_reconciliation',
        'Dual-gated system offboarding deletion for immutable HOS and detention evidence')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;

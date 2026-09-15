-- Stage 138 — evidence-package branch ownership and commercial-truth boundary.
--
-- Preserve legacy rows for audit while preventing generated placeholders from
-- appearing as package evidence or supporting a custody lock.

BEGIN;

ALTER TABLE evidence_packages ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
ALTER TABLE evidence_package_items ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;

WITH ownership AS (
  SELECT ep.id,
         MIN(ref.branch_id) AS branch_id,
         COUNT(DISTINCT ref.branch_id) AS branch_count
    FROM evidence_packages ep
    LEFT JOIN LATERAL (
      SELECT i.branch_id FROM incidents i WHERE i.id=ep.incident_id AND i.company_id=ep.company_id
      UNION ALL SELECT se.branch_id FROM safety_events se WHERE se.id=ep.safety_event_id AND se.company_id=ep.company_id
      UNION ALL SELECT de.branch_id FROM dashcam_events de WHERE de.id=ep.dashcam_event_id AND de.company_id=ep.company_id
      UNION ALL SELECT d.branch_id FROM drivers d WHERE d.id=ep.driver_id AND d.company_id=ep.company_id
      UNION ALL SELECT v.branch_id FROM vehicles v WHERE v.id=ep.vehicle_id AND v.company_id=ep.company_id
      UNION ALL SELECT j.branch_id FROM jobs j WHERE j.id=ep.job_id AND j.company_id=ep.company_id
    ) ref ON TRUE
   GROUP BY ep.id
)
UPDATE evidence_packages ep
   SET branch_id=ownership.branch_id
  FROM ownership
 WHERE ep.id=ownership.id AND ep.branch_id IS NULL AND ownership.branch_count<=1;

UPDATE evidence_package_items item
   SET branch_id=ep.branch_id
  FROM evidence_packages ep
 WHERE ep.id=item.evidence_package_id AND ep.company_id=item.company_id
   AND item.branch_id IS NULL;

UPDATE evidence_package_items
   SET item_json=COALESCE(item_json,'{}'::jsonb) ||
       jsonb_build_object('excludedFromEvidence',TRUE,'exclusionReason','legacy_generated_placeholder')
 WHERE COALESCE(item_url,'') LIKE '/placeholder/%'
    OR COALESCE(item_url,'') LIKE 'https://example.invalid/%';

UPDATE evidence_packages ep
   SET locked=FALSE,
       status='Draft',
       export_url=NULL,
       summary='Legacy generated placeholder content was excluded. Add retrievable, hashed and verified evidence before locking.',
       updated_at=NOW()
 WHERE EXISTS (
   SELECT 1 FROM evidence_package_items item
    WHERE item.evidence_package_id=ep.id AND item.company_id=ep.company_id
      AND COALESCE(item.item_json->>'excludedFromEvidence','false')='true'
 );

CREATE INDEX IF NOT EXISTS idx_evidence_packages_company_branch_created
  ON evidence_packages(company_id,branch_id,created_at DESC)
  WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_evidence_package_items_company_branch_package
  ON evidence_package_items(company_id,branch_id,evidence_package_id,created_at);

INSERT INTO schema_migrations(version,description)
VALUES ('2026_09_11_stage138_evidence_package_truth_boundary',
        'Evidence packages gain branch ownership; generated placeholders are retained only as excluded audit history')
ON CONFLICT(version) DO NOTHING;

COMMIT;

-- Stage 104 — retire recognized legacy camera demo claims
--
-- Stage 100 removed provider/media authority from unverified rows, but the original
-- demo fixtures retained titles such as "AI dashcam event" and event types that
-- looked like observed video findings. Those rows are not evidence. Soft-delete
-- only the exact seeded pattern in an explicitly named demo tenant. Manually entered
-- metadata, non-demo tenants and every authoritative provider row remain untouched.

BEGIN;

UPDATE dashcam_events de
SET deleted_at = NOW(),
    updated_at = NOW(),
    review_status = 'Retired Demo Record',
    coaching_status = 'Not Created',
    evidence_status = 'Not Packaged',
    false_positive = FALSE,
    ai_summary = NULL,
    ai_confidence = NULL,
    recommended_action = NULL
WHERE de.deleted_at IS NULL
  AND de.source_authority IN ('LegacyUnverified','ProviderPending')
  AND de.title ~ '^AI dashcam (review|event) [0-9]+$'
  AND (
    de.event_number ~ '^VID-[0-9]{5}$'
    OR de.event_number ~ '^VID-B4-[0-9]{4}$'
  )
  AND EXISTS (
    SELECT 1
    FROM companies c
    WHERE c.id = de.company_id
      AND (LOWER(c.name) LIKE '%demo%' OR LOWER(c.company_code) LIKE '%demo%')
  );

COMMIT;

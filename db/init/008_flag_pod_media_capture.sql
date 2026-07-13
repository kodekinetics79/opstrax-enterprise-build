-- 008_flag_pod_media_capture.sql — ramp control + kill switch for POD media capture.
--
-- Owner-applied (schema init is skipped under the restricted-role + RLS deployment).
-- Idempotent — safe to re-run.
--
-- POD photo/signature capture is the riskiest thing we shipped recently: driver-facing,
-- writes to object storage (cost + failure surface), and if uploads misbehave drivers
-- can't confirm deliveries. This flag lets an operator dial it back or kill it WITHOUT a
-- deploy.
--
-- Seeded ENABLED at 100% so behaviour is unchanged. Turning it off (or lowering the
-- rollout) is safe: the driver app falls back to the text evidence reference, so proof of
-- delivery keeps working — it just loses the photo/signature.
--
-- Rollout is bucketed on the driver's user id, so a partial rollout is a STABLE slice of
-- drivers, not a different set on every request.

INSERT INTO feature_flags (company_id, flag_key, name, description, enabled, rollout_pct, environment)
SELECT c.id,
       'pod_media_capture',
       'POD photo & signature capture',
       'Driver photo/signature upload for proof of delivery. Turn off (or dial back the rollout) and drivers fall back to the text evidence reference — delivery confirmation keeps working.',
       true, 100, 'production'
FROM companies c
ON CONFLICT (company_id, flag_key) DO NOTHING;

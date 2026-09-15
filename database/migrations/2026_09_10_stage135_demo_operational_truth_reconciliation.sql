-- Stage 135 — retire legacy demo rows that could be mistaken for operating evidence.
--
-- Batch 2 used to create proof_of_delivery rows whose type and notes explicitly said
-- "Placeholder" while their status said "Captured". That contradiction made the Jobs
-- surface and the optional billing POD gate treat generated demo rows as evidence even
-- though the Proof Center correctly had no package or artifacts. Delete only that exact
-- historical seed signature and only inside visibly named demo tenants.

BEGIN;

DELETE FROM proof_of_delivery pod
USING companies c
WHERE c.id = pod.company_id
  AND (LOWER(c.name) LIKE '%demo%' OR LOWER(c.company_code) LIKE '%demo%')
  AND LOWER(COALESCE(pod.proof_type,'')) = 'placeholder'
  AND COALESCE(pod.notes,'') = 'Batch 2 proof placeholder.';

-- One Batch 7 demo audit row claimed that JOB-1005 had been deleted even while the
-- same current demo fixture still exposed it as an active job. Retire only that exact
-- seed-shaped contradiction; customer-created audit records and non-demo tenants are
-- untouched.
DELETE FROM audit_logs al
USING companies c, jobs j
WHERE c.id = al.company_id
  AND j.company_id = al.company_id
  AND j.id = al.entity_id
  AND j.deleted_at IS NULL
  AND (LOWER(c.name) LIKE '%demo%' OR LOWER(c.company_code) LIKE '%demo%')
  AND COALESCE(j.job_number,j.job_code) = 'JOB-1005'
  AND al.action_name = 'job.deleted'
  AND COALESCE(al.actor_name,'') = 'admin'
  AND COALESCE(al.details_json,'{}'::jsonb) @> '{"source":"api"}'::jsonb;

INSERT INTO schema_migrations(version, description)
VALUES ('2026_09_10_stage135_demo_operational_truth_reconciliation',
        'Retire exact legacy demo POD placeholders and contradictory JOB-1005 deletion audit seed')
ON CONFLICT(version) DO NOTHING;

COMMIT;

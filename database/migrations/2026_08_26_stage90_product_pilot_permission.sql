-- Stage 90 — least-privilege Product Pilot Harness permission.
-- The harness remains absent unless the runtime is the Staging host and all
-- dedicated ProductPilot configuration gates are enabled. This migration only
-- grants its narrow control-plane permission to the existing Product Admin role.

BEGIN;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM platform_roles WHERE role_key='product_admin') THEN
    RAISE EXCEPTION 'Stage90 requires the protected platform role bootstrap through Stage77';
  END IF;
END
$$;

INSERT INTO platform_role_permissions (role_id, permission_key)
SELECT id, 'platform:pilot:run'
FROM platform_roles
WHERE role_key = 'product_admin'
ON CONFLICT (role_id, permission_key) DO NOTHING;

-- The platform audit log is the durable command ledger for this single fixed
-- action. The unique partial index supplements the transaction advisory lock and
-- makes a request id impossible to commit twice across API instances.
CREATE UNIQUE INDEX IF NOT EXISTS ux_platform_audit_product_pilot_request
  ON platform_audit_log ((details_json->>'requestId'))
  WHERE action LIKE 'product_pilot.crm.%' AND details_json ? 'requestId';

INSERT INTO schema_migrations (version, description)
VALUES ('2026_08_26_stage90_product_pilot_permission', 'Least-privilege staging Product Pilot Harness permission')
ON CONFLICT (version) DO NOTHING;

COMMIT;

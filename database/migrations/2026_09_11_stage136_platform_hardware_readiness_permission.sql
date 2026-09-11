-- Stage 136 — Platform Hardware Readiness least-privilege permissions.
--
-- Exact model/HW/FW/SHA candidate intake is a platform product-governance action.
-- Protected environments skip runtime schema seeding, so the Product Admin grants
-- must be installed by an owner migration. Tenant roles receive no access.

BEGIN;

SELECT 1 / (COUNT(*) = 1)::integer AS stage136_product_admin_guard
FROM platform_roles
WHERE role_key = 'product_admin';

INSERT INTO platform_role_permissions (role_id, permission_key)
SELECT role.id, permission.permission_key
FROM platform_roles role
CROSS JOIN (VALUES
  ('platform:devices:view'),
  ('platform:devices:manage')
) AS permission(permission_key)
WHERE role.role_key = 'product_admin'
ON CONFLICT (role_id, permission_key) DO NOTHING;

INSERT INTO schema_migrations (version, description)
VALUES (
  '2026_09_11_stage136_platform_hardware_readiness_permission',
  'Least-privilege Product Admin access to exact device hardware readiness intake'
)
ON CONFLICT (version) DO NOTHING;

COMMIT;

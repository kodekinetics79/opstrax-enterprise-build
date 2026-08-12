-- Stage 77 — Protected-environment authorization bootstrap
--
-- Protected Staging and Production deliberately skip runtime schema initialization.
-- Stage 26 created the platform control-plane tables but did not perform the role
-- seed its header promised, and the core schema migration likewise creates the
-- tenant role table without its reference rows. A clean protected database could
-- therefore never create the first platform administrator or a tenant admin invite.
--
-- This migration inserts authorization reference data only. It creates no tenant,
-- user, customer, vehicle, operational record, or credential. Natural role keys are
-- used throughout; no environment-specific numeric identifiers are embedded.

BEGIN;

INSERT INTO roles (name, permissions_json, is_system)
SELECT seed.name, seed.permissions_json, TRUE
FROM (VALUES
  ('Super Admin',               jsonb_build_array('*')),
  ('Company Admin',             jsonb_build_array('*')),
  ('Fleet Manager',             jsonb_build_array('dashboard:view','fleet:view','fleet:manage','maintenance:view','maintenance:manage','telematics:view','dispatch:view','intelligence:view','map:view')),
  ('Dispatcher',                jsonb_build_array('dashboard:view','dispatch:view','dispatch:manage','fleet:view','jobs:view','jobs:manage','map:view','customers:view')),
  ('Driver',                    jsonb_build_array('driver:self','jobs:view')),
  ('Mechanic',                  jsonb_build_array('maintenance:view','maintenance:manage','dvir:review','fleet:view')),
  ('Safety Manager',            jsonb_build_array('dashboard:view','safety:view','safety:manage','compliance:view','fleet:view','telematics:view','intelligence:view')),
  ('Compliance Manager',        jsonb_build_array('dashboard:view','compliance:view','compliance:manage','audit:view','fleet:view','intelligence:view')),
  ('Customer Service',          jsonb_build_array('customers:view','customer-portal:view','dispatch:view','crm:view')),
  ('Customer Portal User',      jsonb_build_array('customer-portal:view')),
  ('Reseller / Partner Admin',  jsonb_build_array('*')),
  ('Read-only Auditor',         jsonb_build_array('audit:view','fleet:view','dashboard:view')),
  ('Operations Manager',        jsonb_build_array('dashboard:view','map:view','fleet:view','dispatch:view','dispatch:manage','orders:view','orders:manage','shipments:view','shipments:manage','pod:view','pod:upload','maintenance:view','safety:view','dashcam:view','compliance:view','reports:view','settings:view')),
  ('Finance & Billing Manager', jsonb_build_array('finance:view','finance:manage','fuel:view','fuel:manage','reports:view','settings:view')),
  ('CRM & Sales Manager',       jsonb_build_array('crm:view','crm:manage','campaigns:view','campaigns:manage','customer_portal:view','reports:view')),
  ('Vendor Service Provider',   jsonb_build_array('vendor_portal:view','maintenance:view','pod:view'))
) AS seed(name, permissions_json)
WHERE NOT EXISTS (
  SELECT 1 FROM roles existing
  WHERE existing.company_id IS NULL AND LOWER(existing.name)=LOWER(seed.name)
);

WITH platform_role_seed(role_key, name, description) AS (VALUES
  ('platform_super_admin',    'Platform Super Admin',   'Full control of the SaaS business across all tenants.'),
  ('sales_admin',             'Sales Admin',            'Manage CRM pipeline, proposals and tenant provisioning.'),
  ('marketing_admin',         'Marketing Admin',        'Manage campaigns and customer segments.'),
  ('finance_admin',           'Finance Admin',          'Manage billing, invoices and revenue. Read-only on entitlements.'),
  ('customer_success_admin',  'Customer Success Admin', 'Tenant health, renewals and upsell follow-up.'),
  ('support_admin',           'Support Admin',          'Inspect tenant status. Bounded support access requires an explicit, separately reviewed grant.'),
  ('product_admin',           'Product Admin',          'Manage feature entitlements, packages and platform health.'),
  ('compliance_admin',        'Compliance Admin',       'Audit, security and access review oversight.'),
  ('readonly_executive',      'Read-only Executive',    'Executive read-only visibility of the whole business.')
)
INSERT INTO platform_roles (role_key, name, description)
SELECT role_key, name, description FROM platform_role_seed
ON CONFLICT (role_key) DO UPDATE
SET name=EXCLUDED.name, description=EXCLUDED.description;

WITH permission_seed(role_key, permission_key) AS (VALUES
  ('platform_super_admin','platform:*'),
  ('sales_admin','platform:dashboard:view'),('sales_admin','platform:tenants:view'),('sales_admin','platform:tenants:manage'),('sales_admin','platform:packages:view'),('sales_admin','platform:countries:view'),('sales_admin','platform:crm:view'),('sales_admin','platform:crm:manage'),('sales_admin','platform:proposals:view'),('sales_admin','platform:proposals:manage'),
  ('marketing_admin','platform:dashboard:view'),('marketing_admin','platform:tenants:view'),('marketing_admin','platform:marketing:view'),('marketing_admin','platform:marketing:manage'),('marketing_admin','platform:crm:view'),
  ('finance_admin','platform:dashboard:view'),('finance_admin','platform:tenants:view'),('finance_admin','platform:packages:view'),('finance_admin','platform:packages:manage'),('finance_admin','platform:billing:view'),('finance_admin','platform:billing:manage'),('finance_admin','platform:audit:view'),
  ('customer_success_admin','platform:dashboard:view'),('customer_success_admin','platform:tenants:view'),('customer_success_admin','platform:health:view'),('customer_success_admin','platform:health:manage'),('customer_success_admin','platform:crm:view'),
  ('support_admin','platform:dashboard:view'),('support_admin','platform:tenants:view'),('support_admin','platform:support:view'),
  ('product_admin','platform:dashboard:view'),('product_admin','platform:tenants:view'),('product_admin','platform:entitlements:view'),('product_admin','platform:entitlements:manage'),('product_admin','platform:packages:view'),('product_admin','platform:packages:manage'),('product_admin','platform:countries:view'),('product_admin','platform:countries:manage'),('product_admin','platform:ops:view'),
  ('compliance_admin','platform:dashboard:view'),('compliance_admin','platform:tenants:view'),('compliance_admin','platform:audit:view'),('compliance_admin','platform:ops:view'),('compliance_admin','platform:admins:view'),
  ('readonly_executive','platform:dashboard:view'),('readonly_executive','platform:tenants:view'),('readonly_executive','platform:packages:view'),('readonly_executive','platform:billing:view'),('readonly_executive','platform:health:view'),('readonly_executive','platform:crm:view'),('readonly_executive','platform:audit:view')
)
INSERT INTO platform_role_permissions (role_id, permission_key)
SELECT role.id, seed.permission_key
FROM permission_seed seed
JOIN platform_roles role ON role.role_key=seed.role_key
ON CONFLICT (role_id, permission_key) DO NOTHING;

DELETE FROM platform_role_permissions permission
USING platform_roles role
WHERE permission.role_id=role.id
  AND role.role_key='support_admin'
  AND permission.permission_key='platform:impersonation:start';

INSERT INTO schema_migrations (version, description)
VALUES ('2026_08_12_stage77_protected_role_bootstrap', 'Protected-environment authorization reference bootstrap')
ON CONFLICT (version) DO NOTHING;

COMMIT;

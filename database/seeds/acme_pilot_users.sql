-- ACME TRANSPORT — pilot users across every testable role. Idempotent (ON CONFLICT
-- email). Run AFTER acme_pilot_harness.sql (needs the ACME company) and after
-- acme_pilot_enrich.sql if you want the branch manager bound to a real branch.
--
-- SECURITY: seeded identities are invitation-only and contain no password material.
-- Activate them through the normal admin invitation/reset workflow.

DO $users$
DECLARE cid bigint; b1 bigint;
BEGIN
    SELECT id INTO cid FROM companies WHERE company_code='ACME-TRANSPORT';
    IF cid IS NULL THEN RAISE NOTICE 'ACME-TRANSPORT not found — run harness first.'; RETURN; END IF;
    SELECT id INTO b1 FROM branches WHERE company_id=cid AND branch_type='branch' ORDER BY id LIMIT 1;

    -- (email, full_name, role_name, role_id, branch_id, permissions_json, temp_password)
    -- NOTE: this schema binds customer-portal scope by permission, not a users.customer_id
    -- column, so the portal user carries only customer_portal:view here.
    INSERT INTO users (company_id, email, full_name, role_name, role_id, branch_id, status, permissions_json, demo_password)
    VALUES
      (cid, 'admin@acme-transport.com',        'Acme Company Admin',     'Company Admin',        2, NULL, 'Invited', '["*"]'::jsonb, NULL),
      (cid, 'dispatcher@acme-transport.com',   'Acme Dispatcher',        'Dispatcher',           4, NULL, 'Invited', '["dashboard:view","dispatch:view","dispatch:assign","dispatch:update","shipments:view","vehicles:view","drivers:view"]'::jsonb, NULL),
      (cid, 'fleetmgr@acme-transport.com',     'Acme Fleet Manager',     'Fleet Manager',        3, NULL, 'Invited', '["dashboard:view","vehicles:view","vehicles:update","fleet:manage","drivers:view","maintenance:view","shipments:view"]'::jsonb, NULL),
      (cid, 'branchmgr@acme-transport.com',    'Acme Branch Manager',    'Fleet Manager',        3, b1,   'Invited', '["dashboard:view","vehicles:view","drivers:view","shipments:view","dispatch:view"]'::jsonb, NULL),
      (cid, 'safety@acme-transport.com',       'Acme Safety Manager',    'Safety Manager',       7, NULL, 'Invited', '["dashboard:view","safety:view","safety:review","safety:manage","drivers:view","alerts:view"]'::jsonb, NULL),
      (cid, 'maintmgr@acme-transport.com',     'Acme Maintenance Mgr',   'Mechanic',             6, NULL, 'Invited', '["dashboard:view","maintenance:view","maintenance:manage","maintenance:close","vehicles:view"]'::jsonb, NULL),
      (cid, 'tech@acme-transport.com',         'Acme Technician',        'Mechanic',             6, NULL, 'Invited', '["dashboard:view","maintenance:view","maintenance:update","vehicles:view"]'::jsonb, NULL),
      (cid, 'finance@acme-transport.com',      'Acme Finance User',      'Finance & Billing Manager', 14, NULL, 'Invited', '["dashboard:view","finance:view","finance.invoice.read","finance.ar.summary.read","reports:view"]'::jsonb, NULL),
      (cid, 'driver@acme-transport.com',       'Acme Driver',            'Driver',               5, NULL, 'Invited', '["driver:self","driver:portal","jobs:view","dvir:manage"]'::jsonb, NULL),
      (cid, 'portal@acme-cus.example',         'Acme Customer Portal',   'Customer Portal User', 10, NULL, 'Invited', '["customer_portal:view","shipments:view"]'::jsonb, NULL),
      (cid, 'viewer@acme-transport.com',       'Acme Viewer/Auditor',    'Read-only Auditor',    12, NULL, 'Invited', '["dashboard:view","vehicles:view","drivers:view","shipments:view","reports:view","audit:view"]'::jsonb, NULL)
    ON CONFLICT (email) DO UPDATE
       SET company_id = EXCLUDED.company_id, role_name = EXCLUDED.role_name, role_id = EXCLUDED.role_id,
           branch_id = EXCLUDED.branch_id, permissions_json = EXCLUDED.permissions_json;

    RAISE NOTICE 'ACME pilot users seeded/updated: 11 roles (admin, dispatcher, fleet, branch, safety, maint, tech, finance, driver, portal, viewer).';
END
$users$;

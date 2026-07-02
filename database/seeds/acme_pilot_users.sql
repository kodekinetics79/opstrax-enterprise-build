-- ACME TRANSPORT — pilot users across every testable role. Idempotent (ON CONFLICT
-- email). Run AFTER acme_pilot_harness.sql (needs the ACME company) and after
-- acme_pilot_enrich.sql if you want the branch manager bound to a real branch.
--
-- SECURITY: demo_password values are TEMPORARY pilot credentials for staging/pilot
-- testing only. They are NOT production user passwords. Rotate / replace with secure
-- reset links before any real handover. Never reuse these for real accounts.

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
      (cid, 'admin@acme-transport.com',        'Acme Company Admin',     'Company Admin',        2, NULL, 'Active', '["*"]'::jsonb, 'AcmePilot!23'),
      (cid, 'dispatcher@acme-transport.com',   'Acme Dispatcher',        'Dispatcher',           4, NULL, 'Active', '["dashboard:view","dispatch:view","dispatch:assign","dispatch:update","shipments:view","vehicles:view","drivers:view"]'::jsonb, 'AcmeDisp!23'),
      (cid, 'fleetmgr@acme-transport.com',     'Acme Fleet Manager',     'Fleet Manager',        3, NULL, 'Active', '["dashboard:view","vehicles:view","vehicles:update","fleet:manage","drivers:view","maintenance:view","shipments:view"]'::jsonb, 'AcmeFleet!23'),
      (cid, 'branchmgr@acme-transport.com',    'Acme Branch Manager',    'Fleet Manager',        3, b1,   'Active', '["dashboard:view","vehicles:view","drivers:view","shipments:view","dispatch:view"]'::jsonb, 'AcmeBr!23'),
      (cid, 'safety@acme-transport.com',       'Acme Safety Manager',    'Safety Manager',       7, NULL, 'Active', '["dashboard:view","safety:view","safety:review","safety:manage","drivers:view","alerts:view"]'::jsonb, 'AcmeSafe!23'),
      (cid, 'maintmgr@acme-transport.com',     'Acme Maintenance Mgr',   'Mechanic',             6, NULL, 'Active', '["dashboard:view","maintenance:view","maintenance:manage","maintenance:close","vehicles:view"]'::jsonb, 'AcmeMaint!23'),
      (cid, 'tech@acme-transport.com',         'Acme Technician',        'Mechanic',             6, NULL, 'Active', '["dashboard:view","maintenance:view","maintenance:update","vehicles:view"]'::jsonb, 'AcmeTech!23'),
      (cid, 'finance@acme-transport.com',      'Acme Finance User',      'Finance & Billing Manager', 14, NULL, 'Active', '["dashboard:view","finance:view","finance.invoice.read","finance.ar.summary.read","reports:view"]'::jsonb, 'AcmeFin!23'),
      (cid, 'driver@acme-transport.com',       'Acme Driver',            'Driver',               5, NULL, 'Active', '["driver:self","driver:portal","jobs:view","dvir:manage"]'::jsonb, 'AcmeDrv!23'),
      (cid, 'portal@acme-cus.example',         'Acme Customer Portal',   'Customer Portal User', 10, NULL, 'Active', '["customer_portal:view","shipments:view"]'::jsonb, 'AcmePortal!23'),
      (cid, 'viewer@acme-transport.com',       'Acme Viewer/Auditor',    'Read-only Auditor',    12, NULL, 'Active', '["dashboard:view","vehicles:view","drivers:view","shipments:view","reports:view","audit:view"]'::jsonb, 'AcmeView!23')
    ON CONFLICT (email) DO UPDATE
       SET company_id = EXCLUDED.company_id, role_name = EXCLUDED.role_name, role_id = EXCLUDED.role_id,
           branch_id = EXCLUDED.branch_id, permissions_json = EXCLUDED.permissions_json, status = 'Active';

    RAISE NOTICE 'ACME pilot users seeded/updated: 11 roles (admin, dispatcher, fleet, branch, safety, maint, tech, finance, driver, portal, viewer).';
END
$users$;

-- 2026_07_13 — Customer entity referential integrity + customer-health rollup indexes
--
-- PROBLEM
--   `customers` (database/init/001_schema.sql:89) has a mix of children WITH a real FK
--   (contracts, assets, customer_contacts, customer_addresses, jobs, invoice_drafts,
--   issued_invoices) and children with a bare `customer_id BIGINT` and NO FK at all:
--     customer_sites, customer_communications, customer_eta_links, customer_feedback,
--     sla_records, cost_margin_records, rate_cards, customer_visibility,
--     site_access_requirements, users.customer_id
--   Nothing stops a dangling customer_id today. Worse, several of these tables are created
--   at RUNTIME by the C# *SchemaService classes (customer_visibility ->
--   CustomerVisibilitySchemaService, site_access_requirements -> Stage9SchemaService), and
--   those CREATE TABLE blocks carry no FKs — so on Render/Neon, where the DB is provisioned
--   by the app rather than by 001_schema.sql, the FKs do not exist at all.
--
-- WHAT THIS DOES
--   1. Repairs pre-existing orphans (customer_id -> nonexistent customers.id) so
--      ADD CONSTRAINT can succeed; reports every count via RAISE NOTICE.
--   2. Adds the missing FKs, each with a deliberate ON DELETE rule (justified inline).
--   3. Adds the indexes the customer-health rollup and the bulk customer endpoint need.
--
-- ON DELETE POLICY (product norm is SOFT delete — customers.deleted_at; a hard DELETE of a
-- customer is an operator/GDPR-purge action, so these rules describe purge semantics):
--   RESTRICT  — financial / contractual / security-bearing children. A purge must NOT
--               silently destroy or detach money and identity records; an operator has to
--               deal with them explicitly:  rate_cards, sla_records, cost_margin_records,
--               customer_sites (contracted service locations), users (portal principals).
--   CASCADE   — pure per-customer satellites with no standalone meaning once the customer
--               is gone:  customer_communications, customer_eta_links, customer_visibility.
--               These are outbound notifications / share tokens; leaving them alive after a
--               purge would keep live public tracking tokens pointing at a deleted customer.
--   SET NULL  — job-anchored records whose primary owner is the JOB, not the customer, and
--               which stay analytically useful without a customer:  customer_feedback,
--               site_access_requirements.
--
-- SAFETY / REVERSIBILITY
--   Idempotent and re-runnable: every ADD CONSTRAINT is guarded, every index is
--   IF NOT EXISTS, and every table/column touch is guarded by to_regclass / information_schema
--   (the runtime-created tables may legitimately not exist yet in a given environment).
--   MUST be applied by the DB OWNER (the app runs as the restricted opstrax_app role).
--   Rollback: DROP CONSTRAINT / DROP INDEX for each object named below. Orphan repair is
--   NOT reversible — it only touches rows that already violate integrity.

BEGIN;

-- =====================================================================
-- 1. ORPHAN REPAIR  (must run BEFORE any ADD CONSTRAINT)
--    Strategy per table mirrors its chosen ON DELETE rule:
--      * CASCADE tables  -> DELETE the orphan (it has no meaning without its customer)
--      * SET NULL tables -> NULL the pointer (keep the row; the job still owns it)
--      * RESTRICT tables -> NULL the pointer where the column is nullable (never destroy
--                           financial history); customer_sites.customer_id is NOT NULL so
--                           an orphan site is unrecoverable and is deleted.
-- =====================================================================
DO $$
DECLARE
  n BIGINT;
BEGIN
  -- customer_sites.customer_id is NOT NULL -> cannot be detached, must be removed.
  IF to_regclass('public.customer_sites') IS NOT NULL THEN
    DELETE FROM customer_sites s
     WHERE NOT EXISTS (SELECT 1 FROM customers c WHERE c.id = s.customer_id);
    GET DIAGNOSTICS n = ROW_COUNT;
    RAISE NOTICE 'customer_entity_integrity: customer_sites orphans deleted = %', n;
  END IF;

  -- CASCADE satellites: delete orphans.
  IF to_regclass('public.customer_communications') IS NOT NULL THEN
    DELETE FROM customer_communications x
     WHERE x.customer_id IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM customers c WHERE c.id = x.customer_id);
    GET DIAGNOSTICS n = ROW_COUNT;
    RAISE NOTICE 'customer_entity_integrity: customer_communications orphans deleted = %', n;
  END IF;

  IF to_regclass('public.customer_eta_links') IS NOT NULL THEN
    DELETE FROM customer_eta_links x
     WHERE x.customer_id IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM customers c WHERE c.id = x.customer_id);
    GET DIAGNOSTICS n = ROW_COUNT;
    RAISE NOTICE 'customer_entity_integrity: customer_eta_links orphans deleted = %', n;
  END IF;

  IF to_regclass('public.customer_visibility') IS NOT NULL THEN
    DELETE FROM customer_visibility x
     WHERE x.customer_id IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM customers c WHERE c.id = x.customer_id);
    GET DIAGNOSTICS n = ROW_COUNT;
    RAISE NOTICE 'customer_entity_integrity: customer_visibility orphans deleted = %', n;
  END IF;

  -- SET NULL children: detach, keep the row (the job still owns it).
  IF to_regclass('public.customer_feedback') IS NOT NULL THEN
    UPDATE customer_feedback x SET customer_id = NULL
     WHERE x.customer_id IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM customers c WHERE c.id = x.customer_id);
    GET DIAGNOSTICS n = ROW_COUNT;
    RAISE NOTICE 'customer_entity_integrity: customer_feedback orphans detached = %', n;
  END IF;

  IF to_regclass('public.site_access_requirements') IS NOT NULL THEN
    UPDATE site_access_requirements x SET customer_id = NULL
     WHERE x.customer_id IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM customers c WHERE c.id = x.customer_id);
    GET DIAGNOSTICS n = ROW_COUNT;
    RAISE NOTICE 'customer_entity_integrity: site_access_requirements orphans detached = %', n;
  END IF;

  -- RESTRICT / financial children: NEVER delete money rows — detach the dangling pointer
  -- so the historical amounts survive and stay tenant-scoped by company_id.
  IF to_regclass('public.sla_records') IS NOT NULL THEN
    UPDATE sla_records x SET customer_id = NULL
     WHERE x.customer_id IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM customers c WHERE c.id = x.customer_id);
    GET DIAGNOSTICS n = ROW_COUNT;
    RAISE NOTICE 'customer_entity_integrity: sla_records orphans detached = %', n;
  END IF;

  IF to_regclass('public.cost_margin_records') IS NOT NULL THEN
    UPDATE cost_margin_records x SET customer_id = NULL
     WHERE x.customer_id IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM customers c WHERE c.id = x.customer_id);
    GET DIAGNOSTICS n = ROW_COUNT;
    RAISE NOTICE 'customer_entity_integrity: cost_margin_records orphans detached = %', n;
  END IF;

  IF to_regclass('public.rate_cards') IS NOT NULL THEN
    UPDATE rate_cards x SET customer_id = NULL
     WHERE x.customer_id IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM customers c WHERE c.id = x.customer_id);
    GET DIAGNOSTICS n = ROW_COUNT;
    RAISE NOTICE 'customer_entity_integrity: rate_cards orphans detached = %', n;
  END IF;

  -- users.customer_id is the CUSTOMER-PORTAL principal binding (stage 21): NULL means
  -- "internal staff", non-NULL means "portal user restricted to that customer". An orphan
  -- portal user is a PRIVILEGE-ESCALATION hazard — merely NULLing it would silently promote
  -- that login to internal-staff scope. So: detach AND disable the account for operator review.
  IF EXISTS (
      SELECT 1 FROM information_schema.columns
       WHERE table_schema = 'public' AND table_name = 'users' AND column_name = 'customer_id'
  ) THEN
    UPDATE users u
       SET customer_id = NULL,
           status = 'Disabled'
     WHERE u.customer_id IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM customers c WHERE c.id = u.customer_id);
    GET DIAGNOSTICS n = ROW_COUNT;
    RAISE NOTICE 'customer_entity_integrity: orphan portal users detached + disabled = %', n;
  END IF;
END $$;

-- Cross-tenant integrity is a separate class of bug (a child row whose company_id differs
-- from its customer's company_id). A single-column FK to customers(id) cannot catch it.
-- Report it loudly here rather than silently "repairing" tenant ownership, which only an
-- operator may decide.
DO $$
DECLARE
  n BIGINT;
BEGIN
  SELECT COUNT(*) INTO n
    FROM jobs j JOIN customers c ON c.id = j.customer_id
   WHERE j.customer_id IS NOT NULL AND j.company_id <> c.company_id;
  IF n > 0 THEN
    RAISE WARNING 'customer_entity_integrity: % jobs rows have company_id != customers.company_id (cross-tenant drift; needs operator review)', n;
  END IF;
END $$;

-- =====================================================================
-- 2. FOREIGN KEYS  (idempotent: guarded on pg_constraint by name)
-- =====================================================================
DO $$
BEGIN
  -- ---- RESTRICT: financial / contractual / security-bearing ----

  -- rate_cards: priced commercial terms. A purge must never vaporize pricing history.
  IF to_regclass('public.rate_cards') IS NOT NULL
     AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_rate_cards_customer') THEN
    ALTER TABLE rate_cards
      ADD CONSTRAINT fk_rate_cards_customer
      FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE RESTRICT;
  END IF;

  -- sla_records: contractual SLA attainment evidence — same argument as rate_cards.
  IF to_regclass('public.sla_records') IS NOT NULL
     AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_sla_records_customer') THEN
    ALTER TABLE sla_records
      ADD CONSTRAINT fk_sla_records_customer
      FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE RESTRICT;
  END IF;

  -- cost_margin_records: revenue/cost/margin ledger rows. Financial history is immutable.
  IF to_regclass('public.cost_margin_records') IS NOT NULL
     AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_cost_margin_records_customer') THEN
    ALTER TABLE cost_margin_records
      ADD CONSTRAINT fk_cost_margin_records_customer
      FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE RESTRICT;
  END IF;

  -- customer_sites: contracted service locations, referenced operationally by jobs/routes.
  -- customer_id is NOT NULL here, so SET NULL is impossible and CASCADE would silently
  -- destroy master data. RESTRICT forces the operator to retire sites first.
  IF to_regclass('public.customer_sites') IS NOT NULL
     AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_customer_sites_customer') THEN
    ALTER TABLE customer_sites
      ADD CONSTRAINT fk_customer_sites_customer
      FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE RESTRICT;
  END IF;

  -- users.customer_id: portal principal binding. CASCADE would delete real logins;
  -- SET NULL would PROMOTE a customer-portal login to internal-staff scope. Both are
  -- unacceptable, so the delete must be blocked.
  IF EXISTS (
        SELECT 1 FROM information_schema.columns
         WHERE table_schema = 'public' AND table_name = 'users' AND column_name = 'customer_id')
     AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_users_customer') THEN
    ALTER TABLE users
      ADD CONSTRAINT fk_users_customer
      FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE RESTRICT;
  END IF;

  -- ---- CASCADE: pure per-customer satellites ----

  -- customer_communications: outbound notifications addressed TO the customer. Once the
  -- customer is purged the message log has no subject and no legal basis to retain.
  IF to_regclass('public.customer_communications') IS NOT NULL
     AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_customer_communications_customer') THEN
    ALTER TABLE customer_communications
      ADD CONSTRAINT fk_customer_communications_customer
      FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE CASCADE;
  END IF;

  -- customer_eta_links: public tracking-code share links. Leaving these alive after a purge
  -- keeps a live public URL bound to a deleted customer — they must die with the customer.
  IF to_regclass('public.customer_eta_links') IS NOT NULL
     AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_customer_eta_links_customer') THEN
    ALTER TABLE customer_eta_links
      ADD CONSTRAINT fk_customer_eta_links_customer
      FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE CASCADE;
  END IF;

  -- customer_visibility: public_tracking_token share controls — same live-public-token
  -- argument as customer_eta_links.
  IF to_regclass('public.customer_visibility') IS NOT NULL
     AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_customer_visibility_customer') THEN
    ALTER TABLE customer_visibility
      ADD CONSTRAINT fk_customer_visibility_customer
      FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE CASCADE;
  END IF;

  -- ---- SET NULL: job-anchored, customer is a secondary attribute ----

  -- customer_feedback: keyed on job_id (NOT NULL); customer_id is nullable colour. Delivery
  -- quality analytics stay valid after the customer is gone.
  IF to_regclass('public.customer_feedback') IS NOT NULL
     AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_customer_feedback_customer') THEN
    ALTER TABLE customer_feedback
      ADD CONSTRAINT fk_customer_feedback_customer
      FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE SET NULL;
  END IF;

  -- site_access_requirements: operational gate on a job/trip (gate codes, escorts, PPE).
  -- The requirement still governs the job in flight even if the customer record is purged.
  IF to_regclass('public.site_access_requirements') IS NOT NULL
     AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_site_access_requirements_customer') THEN
    ALTER TABLE site_access_requirements
      ADD CONSTRAINT fk_site_access_requirements_customer
      FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE SET NULL;
  END IF;
END $$;

-- =====================================================================
-- 3. INDEXES
-- =====================================================================

-- 3a. Customer-health rollup.
-- Existing jobs indexes are ix_jobs_tenant_status (company_id, status, priority),
-- ix_jobs_company_status (company_id, status) and ix_jobs_sla_status (company_id, sla_status)
-- — NONE of them lead with customer_id, so a per-customer rollup degrades to a tenant-wide
-- scan + filter. These two cover the rollup's two aggregates:
--   * job counts by status per customer (open / in-flight / delivered / exception)
CREATE INDEX IF NOT EXISTS ix_jobs_company_customer_status
  ON jobs (company_id, customer_id, status)
  WHERE deleted_at IS NULL AND customer_id IS NOT NULL;

--   * on-time-delivery rate and SLA-exception rate per customer. sla_status is the
--     breach/at-risk marker and sla_due_at is the on-time comparison basis; created_at
--     lets the rollup window the aggregate (e.g. trailing 90 days) from the same index.
CREATE INDEX IF NOT EXISTS ix_jobs_company_customer_sla
  ON jobs (company_id, customer_id, sla_status, sla_due_at, created_at)
  WHERE deleted_at IS NULL AND customer_id IS NOT NULL;

-- 3b. FK-supporting indexes on the new children. Postgres does NOT auto-index the
-- referencing side, and a customer DELETE must scan every child to enforce RESTRICT /
-- apply SET NULL / apply CASCADE. These are also the natural per-customer read path.
-- Guarded on table presence: the runtime *SchemaService-created tables may not exist yet
-- in a given environment. (CustomerVisibilitySchemaService already ships
-- idx_cv_company_customer, so customer_visibility is covered.)
DO $$
BEGIN
  IF to_regclass('public.rate_cards') IS NOT NULL THEN
    CREATE INDEX IF NOT EXISTS ix_rate_cards_company_customer
      ON rate_cards (company_id, customer_id) WHERE customer_id IS NOT NULL;
  END IF;
  IF to_regclass('public.sla_records') IS NOT NULL THEN
    CREATE INDEX IF NOT EXISTS ix_sla_records_company_customer
      ON sla_records (company_id, customer_id) WHERE customer_id IS NOT NULL;
  END IF;
  IF to_regclass('public.cost_margin_records') IS NOT NULL THEN
    CREATE INDEX IF NOT EXISTS ix_cost_margin_records_company_customer
      ON cost_margin_records (company_id, customer_id) WHERE customer_id IS NOT NULL;
  END IF;
  IF to_regclass('public.customer_sites') IS NOT NULL THEN
    CREATE INDEX IF NOT EXISTS ix_customer_sites_company_customer
      ON customer_sites (company_id, customer_id);
  END IF;
  IF to_regclass('public.customer_communications') IS NOT NULL THEN
    CREATE INDEX IF NOT EXISTS ix_customer_communications_company_customer
      ON customer_communications (company_id, customer_id) WHERE customer_id IS NOT NULL;
  END IF;
  IF to_regclass('public.customer_eta_links') IS NOT NULL THEN
    CREATE INDEX IF NOT EXISTS ix_customer_eta_links_company_customer
      ON customer_eta_links (company_id, customer_id) WHERE customer_id IS NOT NULL;
  END IF;
  IF to_regclass('public.customer_feedback') IS NOT NULL THEN
    CREATE INDEX IF NOT EXISTS ix_customer_feedback_company_customer
      ON customer_feedback (company_id, customer_id) WHERE customer_id IS NOT NULL;
  END IF;
  IF to_regclass('public.site_access_requirements') IS NOT NULL THEN
    CREATE INDEX IF NOT EXISTS ix_site_access_requirements_company_customer
      ON site_access_requirements (company_id, customer_id) WHERE customer_id IS NOT NULL;
  END IF;
  IF to_regclass('public.customer_visibility') IS NOT NULL THEN
    CREATE INDEX IF NOT EXISTS idx_cv_company_customer
      ON customer_visibility (company_id, customer_id);
  END IF;
END $$;

-- 3c. Bulk customer endpoint: WHERE id = ANY($ids) AND company_id = $tenant.
-- 001_schema.sql only has ix_customers_tenant_status_risk (company_id, status, risk_score)
-- and the id PK — so the tenant predicate is a heap recheck on every fetched row. A
-- (company_id, id) index serves the whole predicate from the index (index-only scan) and
-- keeps the tenant filter inside the index, which is exactly the multi-tenant read path.
CREATE INDEX IF NOT EXISTS ix_customers_company_id
  ON customers (company_id, id) WHERE deleted_at IS NULL;

-- =====================================================================
-- 4. LEDGER
-- =====================================================================
INSERT INTO schema_migrations (version, description) VALUES
    ('2026_07_13_customer_entity_integrity', 'Customer FKs (orphan repair + ON DELETE policy) and customer-health rollup indexes')
ON CONFLICT (version) DO NOTHING;

COMMIT;

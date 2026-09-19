-- Stage 144 — remove implicit tenant identity fallbacks
-- A missing company_id/tenant_id must fail at the write boundary. Silently routing
-- an unscoped insert to tenant 1 can create cross-tenant data exposure.

BEGIN;

DO $remove_tenant_defaults$
DECLARE
  target RECORD;
BEGIN
  FOR target IN
    SELECT c.table_schema, c.table_name, c.column_name
      FROM information_schema.columns c
     WHERE c.table_schema = 'public'
       AND c.column_name IN ('company_id', 'tenant_id')
       AND c.column_default IS NOT NULL
       AND regexp_replace(
             c.column_default,
             '[[:space:]()]|::(bigint|integer|smallint|numeric)',
             '',
             'g') = '1'
     ORDER BY c.table_name, c.column_name
  LOOP
    EXECUTE format(
      'ALTER TABLE %I.%I ALTER COLUMN %I DROP DEFAULT',
      target.table_schema,
      target.table_name,
      target.column_name);
  END LOOP;
END
$remove_tenant_defaults$;

DO $assert_no_tenant_defaults$
DECLARE
  unsafe_columns TEXT;
BEGIN
  SELECT string_agg(format('%I.%I', c.table_name, c.column_name), ', ' ORDER BY c.table_name, c.column_name)
    INTO unsafe_columns
    FROM information_schema.columns c
   WHERE c.table_schema = 'public'
     AND c.column_name IN ('company_id', 'tenant_id')
     AND c.column_default IS NOT NULL
     AND regexp_replace(
           c.column_default,
           '[[:space:]()]|::(bigint|integer|smallint|numeric)',
           '',
           'g') = '1';

  IF unsafe_columns IS NOT NULL THEN
    RAISE EXCEPTION 'unsafe tenant identity defaults remain: %', unsafe_columns;
  END IF;
END
$assert_no_tenant_defaults$;

INSERT INTO schema_migrations(version, description)
VALUES (
  '2026_09_15_stage144_remove_tenant_identity_defaults',
  'Remove unsafe literal tenant/company identity defaults')
ON CONFLICT (version) DO NOTHING;

COMMIT;

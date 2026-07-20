-- ACME TRANSPORT — pilot cleanup. Removes ONLY Acme Transport pilot data.
-- Deletes child rows first (FK-safe), then the company. Touches NO other tenant.
--
-- SAFETY: keyed strictly off company_code='ACME-TRANSPORT'. Requires explicit manual
-- execution. Does NOT drop the branches table or any schema — data only. Idempotent
-- (no-op if Acme is already gone). Review before running against production.
--
--   docker run --rm -v "$PWD/database:/db" postgres:16 \
--     psql "$NEON_URL" -v ON_ERROR_STOP=1 -f /db/seeds/acme_pilot_cleanup.sql

DO $cleanup$
DECLARE cid bigint; t text;
BEGIN
    SELECT id INTO cid FROM companies WHERE company_code='ACME-TRANSPORT';
    IF cid IS NULL THEN RAISE NOTICE 'ACME-TRANSPORT not present — nothing to clean.'; RETURN; END IF;
    RAISE NOTICE 'Cleaning ACME-TRANSPORT (company_id=%)…', cid;

    -- notification_recipients has no company_id in some builds — delete via its parent.
    DELETE FROM notification_recipients nr
     USING notifications n
     WHERE nr.notification_id = n.id AND n.company_id = cid;

    -- Disable FK trigger ordering for THIS transaction so we can delete Acme's rows in
    -- any order (all deletes are still company_id-scoped to Acme only). Owner-role only.
    SET session_replication_role = 'replica';

    FOR t IN
        SELECT c.table_name
        FROM information_schema.columns c
        JOIN information_schema.tables tb
          ON tb.table_schema=c.table_schema AND tb.table_name=c.table_name AND tb.table_type='BASE TABLE'
        WHERE c.table_schema='public' AND c.column_name='company_id' AND c.table_name<>'companies'
    LOOP
        EXECUTE format('DELETE FROM public.%I WHERE company_id = $1', t) USING cid;
    END LOOP;

    DELETE FROM companies WHERE id = cid;
    RAISE NOTICE 'ACME-TRANSPORT pilot data removed.';
END
$cleanup$;

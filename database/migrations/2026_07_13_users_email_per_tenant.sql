-- ─────────────────────────────────────────────────────────────────────────────
-- 2026_07_13_users_email_per_tenant
--
-- P0 (breach-class) fix, part 2 of 2. See PlatformEndpoints.CreateAdminInviteAsync.
--
-- users.email was declared GLOBALLY UNIQUE in database/init/001_schema.sql
-- (`email VARCHAR(220) NOT NULL UNIQUE` → auto-named constraint users_email_key).
-- Tenant provisioning used `INSERT ... ON CONFLICT (email) DO UPDATE SET
-- company_id=@cid`, so provisioning tenant B with an email that already existed in
-- tenant A silently RELOCATED that user's row (password_hash / role / permissions
-- intact) into tenant B — a provisioning typo became cross-tenant account takeover.
--
-- This migration relaxes uniqueness to PER TENANT: UNIQUE (company_id, email). The
-- application-side conflict handling is rewritten to REFUSE a foreign-tenant email
-- rather than steal it.
--
-- Idempotent: safe to run repeatedly. Wrapped in one transaction so a detected
-- collision aborts the whole thing.
-- ─────────────────────────────────────────────────────────────────────────────
BEGIN;

-- 1. SAFETY GATE. The old global constraint made cross-tenant duplicate emails
--    impossible, so a clean database has none. But if the constraint was ever
--    dropped/disabled out-of-band and duplicates crept in, relaxing uniqueness would
--    leave the tenant login lookup (WHERE LOWER(email)=... LIMIT 1) AMBIGUOUS — a user
--    could be logged into the wrong tenant. Detect that loudly and REFUSE to proceed
--    rather than silently corrupting logins. (Matched case-insensitively, exactly like
--    the login lookup.)
DO $$
DECLARE
    dup RECORD;
    collisions INT := 0;
BEGIN
    FOR dup IN
        SELECT LOWER(email) AS email,
               COUNT(DISTINCT company_id) AS tenants,
               array_agg(DISTINCT company_id ORDER BY company_id) AS company_ids
        FROM users
        GROUP BY LOWER(email)
        HAVING COUNT(DISTINCT company_id) > 1
    LOOP
        collisions := collisions + 1;
        RAISE NOTICE 'CROSS-TENANT EMAIL COLLISION: "%" exists in companies % — this must be resolved before uniqueness is relaxed, or tenant login becomes ambiguous.',
            dup.email, dup.company_ids;
    END LOOP;

    IF collisions > 0 THEN
        RAISE EXCEPTION
            'Aborting users-email-per-tenant migration: % cross-tenant email collision(s) detected (listed above). Resolve the duplicates and re-run.',
            collisions;
    END IF;
END $$;

-- 2. Drop the legacy GLOBAL unique on users(email). Handles it whether it exists as a
--    table constraint (the 001_schema.sql inline UNIQUE → users_email_key) or as a
--    bare unique index, and regardless of the exact auto-generated name — we drop ANY
--    single-column UNIQUE constraint on (email).
DO $$
DECLARE
    c RECORD;
BEGIN
    FOR c IN
        SELECT con.conname
        FROM pg_constraint con
        WHERE con.conrelid = 'users'::regclass
          AND con.contype = 'u'
          AND (
                SELECT array_agg(att.attname::text ORDER BY att.attname::text)
                FROM unnest(con.conkey) AS k
                JOIN pg_attribute att ON att.attrelid = con.conrelid AND att.attnum = k
              ) = ARRAY['email']
    LOOP
        EXECUTE format('ALTER TABLE users DROP CONSTRAINT %I', c.conname);
        RAISE NOTICE 'Dropped legacy global unique constraint % on users(email).', c.conname;
    END LOOP;
END $$;

-- Also drop the well-known name and any bare unique index form, for older/hand-built DBs.
ALTER TABLE users DROP CONSTRAINT IF EXISTS users_email_key;
DROP INDEX IF EXISTS users_email_key;

-- 3. Add the PER-TENANT unique constraint. Guarded so re-runs are no-ops.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'users_company_email_key'
          AND conrelid = 'users'::regclass
    ) THEN
        ALTER TABLE users ADD CONSTRAINT users_company_email_key UNIQUE (company_id, email);
        RAISE NOTICE 'Added per-tenant unique constraint users_company_email_key (company_id, email).';
    END IF;
END $$;

COMMIT;

-- ─────────────────────────────────────────────────────────────────────────────
-- Stage 87 — users.role_id backfill from role_name (DEF-021 schema half)
--
-- users.role_id has always been nullable while the application resolves roles
-- through ResolveRoleRecord (EndpointMappings): match roles by name, visible to
-- the user's tenant (r.company_id IS NULL OR r.company_id = u.company_id), and
-- when BOTH a tenant-local and a global role share the name, PREFER THE
-- TENANT-LOCAL ROW (ORDER BY company_id NULLS LAST LIMIT 1). Rows created
-- before role_id was written therefore still carry only role_name.
--
-- A plain joined UPDATE (UPDATE users u SET role_id=r.id FROM roles r WHERE …)
-- cannot express that precedence — with two candidate rows the picked join row
-- is arbitrary. The correlated subquery below reproduces the ORDER BY … NULLS
-- LAST LIMIT 1 resolution per user exactly.
--
-- ── WHY THIS IS NOT AN UNCONDITIONAL BACKFILL (privilege-escalation guard) ────
-- EndpointMappings.ResolveEffectivePermissionsAsync selects the permission
-- SOURCE on role_id:
--
--     ParsePermissionKeys(roleId > 0 ? rolePermissionsJson : userPermissionsJson)
--     … then UNIONs every role_permissions.permission_key for that role_id
--
-- so writing role_id makes the user's OWN permissions_json stop being consulted
-- and the ROLE's grant set take over. Permissions are re-resolved per request,
-- so an unconditional backfill would widen live, in-flight sessions the instant
-- it committed — with no audit row and no session revocation — for every legacy
-- user whose per-user grants were deliberately NARROWER than their role name.
-- Worst case a trimmed-json user carrying role_name='Company Admin' resolves
-- straight to ["*"]. That is a silent privilege escalation, not a backfill, and
-- it is the opposite of what DEF-021 asked for.
--
-- DEF-021 is about HALF-PROVISIONED rows: a user who has a role_name but no
-- role_id AND no usable per-user grants, so the role card cannot count them and
-- the middleware falls through to RolePermissionDefaults. Those rows are the
-- only ones this migration writes:
--
--   1. HALF-PROVISIONED  — permissions_json is NULL / JSON null / '[]' / an
--      empty string. Effective permissions today come from the role-name
--      defaults; adopting the role's real grants is the intended repair.
--   2. PROVABLY NO-OP    — the user's own grant set is EXACTLY equal (trimmed,
--      case-insensitive) to the role's permissions_json UNION its
--      role_permissions rows. Resolving role_id cannot change one single
--      effective permission, so there is nothing to widen.
--
-- Every OTHER row — any user whose json differs from the role's grants in either
-- direction — is left with role_id NULL and recorded in
-- stage87_role_backfill_review for an operator to adjudicate. Silently widening
-- (or silently narrowing) a live account is not a decision a migration may make.
--
-- Session hygiene: any row this migration DOES update has its user_sessions
-- deleted, mirroring UpdateAdminUser, which revokes sessions on any role change.
-- Even a provably-equal resolution re-issues cleanly rather than leaving a token
-- minted under the pre-backfill resolution path.
--
-- Idempotent: only touches rows where role_id IS NULL, and only when a matching
-- role exists; a second run finds nothing left to update. The review table is
-- rebuilt from scratch on each run so it always describes the CURRENT backlog.
-- fk_users_role guarantees every written id references roles(id).
--
-- Guarded (stage83/84 pattern): users, roles, role_permissions and user_sessions
-- may be under FORCE ROW LEVEL SECURITY (stage50/53/58); under FORCE the table
-- OWNER is policy-constrained too and no policy names the owner (only
-- opstrax_app / opstrax_system), so an unguarded statement would silently match
-- zero rows on a cutover database. FORCE is lifted only inside this file's
-- single transaction and restored to the exact prior state.
--
-- The lift list is EVERY table these statements read or write, enumerated:
--   public.users              read + UPDATE   FORCE-able (stage50/53/58)
--   public.roles              read            FORCE-able
--   public.role_permissions   read            FORCE-able  <-- see below
--   public.user_sessions      DELETE          FORCE-able
--   public.stage87_role_backfill_review  TRUNCATE + INSERT  (not RLS, migration artifact)
--   public.schema_migrations  INSERT          (not RLS, ledger)
--
-- role_permissions was MISSING from this list and that was a privilege-ESCALATION
-- bug, not a fail-safe one. The token-comparison query below unions
-- role_permissions.permission_key into role_tokens. Under a non-superuser owner —
-- the Neon/production shape this whole guard exists for — that subquery returns
-- ZERO rows, so role_tokens silently omits every permission that lives only in
-- role_permissions. Under-computing role_tokens normally just holds more rows for
-- review, but when a role's grants live PARTLY in role_permissions it converts a
-- genuine widening into a "provably no-op" classification:
--     true role grants  = {billing.write, jobs.read}
--     role_tokens seen  = {jobs.read}                 <-- truncated by RLS
--     user json         = ["jobs.read"]               <-- now equal both ways
-- …so role_id is written and the user gains billing.write on the next request —
-- the exact silent escalation the header above promises to prevent. It also
-- corrupts would_gain/would_lose, so the operator's review table reports wrong
-- deltas. A superuser owner (our local test rig) cannot reproduce it: BYPASSRLS
-- makes the subquery return everything, which is why this survived the suite.
-- ─────────────────────────────────────────────────────────────────────────────

BEGIN;

-- Operator review surface for rows this migration deliberately refuses to touch.
-- Not tenant data: it is a migration artifact listing users whose per-user grants
-- disagree with their role, together with the exact delta, so the operator can
-- decide per row whether to adopt the role or keep the narrower grants.
--
-- The owning company is recorded as owner_company_id, NOT company_id, ON PURPOSE.
-- FleetProductionReadinessService derives the tenant_scope contract DYNAMICALLY from
-- every public table carrying a BIGINT column literally named company_id or tenant_id
-- (FleetProductionReadinessService.cs:223/234/506) and then REQUIRES RLS + FORCE + the
-- stage58 policy pair + peer grants on it. A migration-artifact table named company_id
-- would therefore be auto-enrolled, fail the contract, and turn /health/ready RED —
-- which makes Render withhold traffic. Do not rename this column back.
-- An earlier revision of THIS file created the table with `company_id`. On a database
-- built from that revision, CREATE TABLE IF NOT EXISTS below is a no-op and the INSERT
-- at the end would raise 42703 (owner_company_id does not exist). Not reachable through
-- the runner today (stage87 is not in the repair_migration list) but live the moment
-- anyone enrols it or replays this file by hand, so rename first and idempotently.
DO $stage87_rename$
BEGIN
  IF to_regclass('public.stage87_role_backfill_review') IS NOT NULL
     AND EXISTS (SELECT 1 FROM information_schema.columns
                  WHERE table_schema='public' AND table_name='stage87_role_backfill_review'
                    AND column_name='company_id')
     AND NOT EXISTS (SELECT 1 FROM information_schema.columns
                      WHERE table_schema='public' AND table_name='stage87_role_backfill_review'
                        AND column_name='owner_company_id')
  THEN
    EXECUTE 'ALTER TABLE public.stage87_role_backfill_review RENAME COLUMN company_id TO owner_company_id';
  END IF;
END
$stage87_rename$;

CREATE TABLE IF NOT EXISTS public.stage87_role_backfill_review (
  user_id           BIGINT PRIMARY KEY,
  owner_company_id  BIGINT      NOT NULL,
  email             TEXT,
  role_name         TEXT,
  resolved_role_id  BIGINT,
  user_permissions  JSONB,
  role_permissions  JSONB,
  would_gain        TEXT[]      NOT NULL DEFAULT '{}',
  would_lose        TEXT[]      NOT NULL DEFAULT '{}',
  reviewed_at       TIMESTAMPTZ,
  recorded_at       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE public.stage87_role_backfill_review IS
  'Stage87: users with role_id IS NULL whose own permissions_json disagrees with their role name. '
  'Backfilling role_id would change their effective permissions (see would_gain/would_lose), so the '
  'migration left role_id NULL. Adjudicate each row, then set role_id explicitly.';

DO $stage87_backfill$
DECLARE
  users_forced       boolean;
  roles_forced       boolean;
  role_perms_forced  boolean;
  sessions_forced    boolean;
  updated_ids        bigint[];
BEGIN
  SELECT c.relforcerowsecurity INTO users_forced
  FROM pg_class c WHERE c.oid = to_regclass('public.users');
  SELECT c.relforcerowsecurity INTO roles_forced
  FROM pg_class c WHERE c.oid = to_regclass('public.roles');
  SELECT c.relforcerowsecurity INTO role_perms_forced
  FROM pg_class c WHERE c.oid = to_regclass('public.role_permissions');
  SELECT c.relforcerowsecurity INTO sessions_forced
  FROM pg_class c WHERE c.oid = to_regclass('public.user_sessions');
  IF users_forced THEN
    EXECUTE 'ALTER TABLE public.users NO FORCE ROW LEVEL SECURITY';
  END IF;
  IF roles_forced THEN
    EXECUTE 'ALTER TABLE public.roles NO FORCE ROW LEVEL SECURITY';
  END IF;
  -- Reading role_permissions under FORCE returns zero rows to a non-superuser owner,
  -- which TRUNCATES role_tokens and turns a real widening into "provably no-op".
  IF role_perms_forced THEN
    EXECUTE 'ALTER TABLE public.role_permissions NO FORCE ROW LEVEL SECURITY';
  END IF;
  IF sessions_forced THEN
    EXECUTE 'ALTER TABLE public.user_sessions NO FORCE ROW LEVEL SECURITY';
  END IF;

  -- Every role_id-less user that resolves to a role, with both grant sets
  -- normalised to a trimmed, lower-cased, de-duplicated text array so the two
  -- can be compared the way ResolveEffectivePermissionsAsync compares them
  -- (HashSet<string> with StringComparer.OrdinalIgnoreCase).
  -- ON COMMIT DROP only fires at COMMIT; drop first so the file stays re-runnable
  -- inside a single session/transaction (a runner that batches migrations, or a retry).
  DROP TABLE IF EXISTS stage87_candidates;
  CREATE TEMP TABLE stage87_candidates ON COMMIT DROP AS
  WITH resolved AS (
    SELECT u.id            AS user_id,
           u.company_id,
           u.email,
           u.role_name,
           u.permissions_json AS user_json,
           (SELECT r.id
              FROM public.roles r
             WHERE r.name = u.role_name
               AND (r.company_id IS NULL OR r.company_id = u.company_id)
             ORDER BY r.company_id NULLS LAST
             LIMIT 1) AS role_id
      FROM public.users u
     WHERE u.role_id IS NULL
  )
  SELECT resolved.user_id,
         resolved.company_id,
         resolved.email,
         resolved.role_name,
         resolved.role_id,
         resolved.user_json,
         r.permissions_json AS role_json,
         -- Half-provisioned: no usable per-user grant set at all.
         (resolved.user_json IS NULL
          OR jsonb_typeof(resolved.user_json) = 'null'
          OR (jsonb_typeof(resolved.user_json) = 'array' AND jsonb_array_length(resolved.user_json) = 0)
          OR (jsonb_typeof(resolved.user_json) = 'string'
              AND btrim(resolved.user_json #>> '{}') IN ('', '[]'))
         ) AS half_provisioned,
         COALESCE((
           SELECT array_agg(DISTINCT lower(btrim(token)))
             FROM jsonb_array_elements_text(
                    CASE WHEN jsonb_typeof(resolved.user_json) = 'array'
                         THEN resolved.user_json ELSE '[]'::jsonb END) token
            WHERE btrim(token) <> ''
         ), ARRAY[]::text[]) AS user_tokens,
         COALESCE((
           SELECT array_agg(DISTINCT token) FROM (
             SELECT lower(btrim(t)) AS token
               FROM jsonb_array_elements_text(
                      CASE WHEN jsonb_typeof(r.permissions_json) = 'array'
                           THEN r.permissions_json ELSE '[]'::jsonb END) t
              WHERE btrim(t) <> ''
             UNION
             SELECT lower(btrim(rp.permission_key))
               FROM public.role_permissions rp
              WHERE rp.role_id = r.id
                AND btrim(COALESCE(rp.permission_key, '')) <> ''
           ) role_grants
         ), ARRAY[]::text[]) AS role_tokens
    FROM resolved
    JOIN public.roles r ON r.id = resolved.role_id;

  -- (1) half-provisioned, or (2) provably no-op: the two safe classes.
  --
  -- The no-op arm requires jsonb_typeof(user_json) = 'array'. Without it, ANY non-array
  -- shape (object / non-'[]' string / number / boolean) normalises to '[]'::jsonb in the
  -- user_tokens CASE above, yields user_tokens = {}, and fails half_provisioned — so
  -- `{} <@ role_tokens AND role_tokens <@ {}` was TRUE for any role whose own grant set is
  -- also empty, and the row was classified "provably no-op" and backfilled. A legacy user
  -- carrying role_name='Company Admin' and a JSON OBJECT would then be re-resolved through
  -- role_id, hit the runtime `Count == 0 -> RolePermissionDefaults` fallback and land on
  -- wildcard '*' — precisely the silent escalation this file's header promises to prevent.
  -- cardinality(role_tokens) > 0 is required for the same reason from the other side: an
  -- equality between two EMPTY sets proves nothing about what resolution will hand back,
  -- because both sides then fall through to the role-name defaults. Every excluded shape
  -- lands in stage87_role_backfill_review for an operator instead of being written.
  -- Not reachable on current data (empty_grants = 0 in both databases) — this is the
  -- latent trap in the legacy production database the migration exists to repair.
  WITH safe AS (
    SELECT user_id, role_id
      FROM stage87_candidates
     WHERE half_provisioned
        OR (jsonb_typeof(user_json) = 'array'
            AND cardinality(role_tokens) > 0
            AND user_tokens <@ role_tokens
            AND role_tokens <@ user_tokens)
  ), applied AS (
    UPDATE public.users u
       SET role_id = safe.role_id
      FROM safe
     WHERE u.id = safe.user_id
       AND u.role_id IS NULL
    RETURNING u.id
  )
  SELECT COALESCE(array_agg(id), ARRAY[]::bigint[]) INTO updated_ids FROM applied;

  -- Session hygiene for every row actually written (UpdateAdminUser parity).
  IF array_length(updated_ids, 1) IS NOT NULL THEN
    DELETE FROM public.user_sessions WHERE user_id = ANY(updated_ids);
  END IF;

  -- Everything else is reported, never silently changed. Rebuilt each run so the
  -- table always reflects the backlog as it stands after this migration.
  -- This predicate is the EXACT complement of the `safe` predicate above — a row that is
  -- neither backfilled nor reviewed would be silently dropped from the operator's backlog,
  -- which is worse than either outcome. Keep the two in lockstep.
  TRUNCATE public.stage87_role_backfill_review;
  INSERT INTO public.stage87_role_backfill_review
    (user_id, owner_company_id, email, role_name, resolved_role_id,
     user_permissions, role_permissions, would_gain, would_lose)
  SELECT c.user_id, c.company_id, c.email, c.role_name, c.role_id,
         c.user_json, c.role_json,
         COALESCE((SELECT array_agg(t) FROM unnest(c.role_tokens) t WHERE NOT t = ANY(c.user_tokens)), ARRAY[]::text[]),
         COALESCE((SELECT array_agg(t) FROM unnest(c.user_tokens) t WHERE NOT t = ANY(c.role_tokens)), ARRAY[]::text[])
    FROM stage87_candidates c
   WHERE NOT c.half_provisioned
     AND NOT (jsonb_typeof(c.user_json) = 'array'
              AND cardinality(c.role_tokens) > 0
              AND c.user_tokens <@ c.role_tokens
              AND c.role_tokens <@ c.user_tokens);

  RAISE NOTICE 'Stage87: backfilled % user(s); % user(s) held for operator review (see stage87_role_backfill_review).',
    COALESCE(array_length(updated_ids, 1), 0),
    (SELECT COUNT(*) FROM public.stage87_role_backfill_review);

  IF users_forced THEN
    EXECUTE 'ALTER TABLE public.users FORCE ROW LEVEL SECURITY';
  END IF;
  IF roles_forced THEN
    EXECUTE 'ALTER TABLE public.roles FORCE ROW LEVEL SECURITY';
  END IF;
  IF role_perms_forced THEN
    EXECUTE 'ALTER TABLE public.role_permissions FORCE ROW LEVEL SECURITY';
  END IF;
  IF sessions_forced THEN
    EXECUTE 'ALTER TABLE public.user_sessions FORCE ROW LEVEL SECURITY';
  END IF;
END
$stage87_backfill$;

INSERT INTO public.schema_migrations (version, description)
VALUES ('2026_08_22_stage87_user_role_id_backfill',
        'Backfill users.role_id from role_name for half-provisioned/no-op rows only (ResolveRoleRecord precedence); permission-changing rows held in stage87_role_backfill_review')
ON CONFLICT (version) DO NOTHING;

COMMIT;

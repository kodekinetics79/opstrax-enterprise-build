#!/usr/bin/env bash
# Database-enforced document mutation hold for the certification-staging release.
# No production target is supported. Reconcile an uncertain action with `status`;
# do not blindly repeat `enable` or `disable`.
set -euo pipefail

action="${1:-}"
if [[ "$action" != "enable" && "$action" != "status" && "$action" != "disable" ]]; then
  echo "Usage: $0 enable|status|disable" >&2
  exit 2
fi
if [[ -z "${NEON_PG_URI:-}" ]]; then
  echo "ERROR: set NEON_PG_URI in the environment" >&2
  exit 1
fi
command -v psql >/dev/null || { echo "ERROR: psql is required" >&2; exit 1; }
command -v python3 >/dev/null || { echo "ERROR: python3 is required" >&2; exit 1; }

psql_hold() { python3 tools/psql-neon-env.py -v ON_ERROR_STOP=1 "$@"; }

# Each supported label is bound to a complete non-secret URI identity. This tool
# intentionally has no production case.
IFS=$'\t' read -r uri_host uri_port uri_database uri_user < <(python3 tools/psql-neon-env.py --print-safe-target)
case "${OPSTRAX_RELEASE_HOLD_TARGET:-}" in
  staging-certification)
    expected_database=opstrax_staging
    expected_user=opstrax_staging_migrator
    if [[ "$uri_host" != "ep-empty-king-awv39an5.c-12.us-east-1.aws.neon.tech" \
       && "$uri_host" != "ep-empty-king-awv39an5-pooler.c-12.us-east-1.aws.neon.tech" ]]; then
      echo "ERROR: URI host is not the certification-staging main-branch endpoint" >&2
      exit 1
    fi
    ;;
  local-test)
    expected_database=opstrax_local
    expected_user=zayra
    if [[ "$uri_host" != "127.0.0.1" && "$uri_host" != "localhost" ]]; then
      echo "ERROR: local-test requires a loopback host" >&2
      exit 1
    fi
    ;;
  *)
    echo "ERROR: OPSTRAX_RELEASE_HOLD_TARGET must be staging-certification or local-test" >&2
    exit 1
    ;;
esac
if [[ "$uri_database" != "$expected_database" || "$uri_user" != "$expected_user" ]]; then
  echo "ERROR: URI database or user does not match the selected release-hold target" >&2
  exit 1
fi
if [[ "${OPSTRAX_RELEASE_HOLD_TARGET}" == "staging-certification" && "$uri_port" != "5432" ]]; then
  echo "ERROR: certification staging requires the canonical PostgreSQL port" >&2
  exit 1
fi

read -r database_name session_user_name table_owner < <(psql_hold -tA -F ' ' -c \
  "SELECT current_database(), session_user, pg_get_userbyid(c.relowner) FROM pg_class c WHERE c.oid='public.documents'::regclass")
if [[ "$database_name" != "$expected_database" || "$session_user_name" != "$expected_user" || "$table_owner" != "$expected_user" ]]; then
  echo "ERROR: connected database/session/table-owner identity does not match the selected target" >&2
  exit 1
fi

catalog_state() {
  psql_hold -tA -c "
WITH fn AS (
  SELECT p.oid,p.proowner,p.prosrc,p.prosecdef,p.proleakproof,p.provolatile,p.proparallel,p.proconfig,
         p.prorettype,p.pronargs,p.prolang,p.prokind,p.proretset
  FROM pg_proc p
  WHERE p.oid=to_regprocedure('public.opstrax_document_release_hold()')
), trg AS (
  SELECT t.tgfoid,t.tgtype,t.tgenabled,t.tgnargs,t.tgargs,t.tgqual,t.tgconstraint,
         t.tgoldtable,t.tgnewtable
  FROM pg_trigger t
  WHERE t.tgrelid='public.documents'::regclass
    AND t.tgname='trg_opstrax_document_release_hold'
    AND NOT t.tgisinternal
), counts AS (
  SELECT (SELECT COUNT(*) FROM fn) fn_count,(SELECT COUNT(*) FROM trg) trg_count
)
SELECT CASE
  WHEN fn_count=0 AND trg_count=0 THEN 'absent'
  WHEN fn_count=1 AND trg_count=1
    AND (SELECT proowner FROM fn)=(SELECT relowner FROM pg_class WHERE oid='public.documents'::regclass)
    AND (SELECT prosrc FROM fn)='BEGIN RAISE EXCEPTION USING ERRCODE = ''55000'', MESSAGE = ''Document writes are temporarily paused for a controlled staging release.''; END;'
    AND NOT (SELECT prosecdef FROM fn) AND NOT (SELECT proleakproof FROM fn)
    AND (SELECT provolatile FROM fn)='v' AND (SELECT proparallel FROM fn)='u'
    AND (SELECT proconfig FROM fn)=ARRAY['search_path=pg_catalog, public']::text[]
    AND (SELECT prorettype FROM fn)='trigger'::regtype AND (SELECT pronargs FROM fn)=0
    AND (SELECT prolang FROM fn)=(SELECT oid FROM pg_language WHERE lanname='plpgsql')
    AND (SELECT prokind FROM fn)='f'
    AND NOT (SELECT proretset FROM fn)
    AND (SELECT tgfoid FROM trg)=(SELECT oid FROM fn)
    AND (SELECT tgtype FROM trg)=30 AND (SELECT tgenabled FROM trg)='O'
    AND (SELECT tgnargs FROM trg)=0 AND octet_length((SELECT tgargs FROM trg))=0
    AND (SELECT tgqual FROM trg) IS NULL AND (SELECT tgconstraint FROM trg)=0
    AND (SELECT tgoldtable FROM trg) IS NULL AND (SELECT tgnewtable FROM trg) IS NULL
  THEN 'exact'
  ELSE 'invalid'
END FROM counts;"
}

emit_status() {
  local state="$1"
  printf '{"database":"%s","holdActive":%s,"catalogState":"%s","readsAllowed":true,"writesBlocked":"INSERT,UPDATE,DELETE"}\n' \
    "$database_name" "$([[ "$state" == "exact" ]] && printf true || printf false)" "$state"
}

state="$(catalog_state)"
if [[ "$state" == "invalid" ]]; then
  echo "ERROR: unexpected or incomplete document release-hold catalog objects" >&2
  exit 1
fi
if [[ "$action" == "status" ]]; then
  emit_status "$state"
  exit 0
fi

if [[ "$action" == "enable" ]]; then
  if [[ "$state" != "absent" ]]; then
    echo "ERROR: hold already exists; use status instead of repeating enable" >&2
    exit 1
  fi
  psql_hold -q <<'SQL'
BEGIN;
SET LOCAL lock_timeout = '15s';
LOCK TABLE public.documents IN SHARE ROW EXCLUSIVE MODE;
CREATE FUNCTION public.opstrax_document_release_hold()
RETURNS trigger
LANGUAGE plpgsql
SECURITY INVOKER
VOLATILE
PARALLEL UNSAFE
SET search_path = pg_catalog, public
AS $opstrax_hold$BEGIN RAISE EXCEPTION USING ERRCODE = '55000', MESSAGE = 'Document writes are temporarily paused for a controlled staging release.'; END;$opstrax_hold$;
CREATE TRIGGER trg_opstrax_document_release_hold
  BEFORE INSERT OR UPDATE OR DELETE ON public.documents
  FOR EACH STATEMENT
  EXECUTE FUNCTION public.opstrax_document_release_hold();
COMMENT ON FUNCTION public.opstrax_document_release_hold() IS
  'Temporary certification-staging release hold; remove after paired exact-SHA verification.';
COMMIT;
SQL
  expected_state=exact
else
  if [[ "$state" == "absent" ]]; then
    emit_status "$state"
    exit 0
  fi
  psql_hold -q <<'SQL'
BEGIN;
SET LOCAL lock_timeout = '15s';
LOCK TABLE public.documents IN SHARE ROW EXCLUSIVE MODE;
WITH fn AS (
  SELECT p.oid,p.proowner,p.prosrc,p.prosecdef,p.proleakproof,p.provolatile,p.proparallel,p.proconfig,
         p.prorettype,p.pronargs,p.prolang,p.prokind,p.proretset
  FROM pg_proc p
  WHERE p.oid=to_regprocedure('public.opstrax_document_release_hold()')
), trg AS (
  SELECT t.tgfoid,t.tgtype,t.tgenabled,t.tgnargs,t.tgargs,t.tgqual,t.tgconstraint,
         t.tgoldtable,t.tgnewtable
  FROM pg_trigger t
  WHERE t.tgrelid='public.documents'::regclass
    AND t.tgname='trg_opstrax_document_release_hold'
    AND NOT t.tgisinternal
), counts AS (
  SELECT (SELECT COUNT(*) FROM fn) fn_count,(SELECT COUNT(*) FROM trg) trg_count
)
SELECT fn_count=1 AND trg_count=1
    AND (SELECT proowner FROM fn)=(SELECT relowner FROM pg_class WHERE oid='public.documents'::regclass)
    AND (SELECT prosrc FROM fn)='BEGIN RAISE EXCEPTION USING ERRCODE = ''55000'', MESSAGE = ''Document writes are temporarily paused for a controlled staging release.''; END;'
    AND NOT (SELECT prosecdef FROM fn) AND NOT (SELECT proleakproof FROM fn)
    AND (SELECT provolatile FROM fn)='v' AND (SELECT proparallel FROM fn)='u'
    AND (SELECT proconfig FROM fn)=ARRAY['search_path=pg_catalog, public']::text[]
    AND (SELECT prorettype FROM fn)='trigger'::regtype AND (SELECT pronargs FROM fn)=0
    AND (SELECT prolang FROM fn)=(SELECT oid FROM pg_language WHERE lanname='plpgsql')
    AND (SELECT prokind FROM fn)='f'
    AND NOT (SELECT proretset FROM fn)
    AND (SELECT tgfoid FROM trg)=(SELECT oid FROM fn)
    AND (SELECT tgtype FROM trg)=30 AND (SELECT tgenabled FROM trg)='O'
    AND (SELECT tgnargs FROM trg)=0 AND octet_length((SELECT tgargs FROM trg))=0
    AND (SELECT tgqual FROM trg) IS NULL AND (SELECT tgconstraint FROM trg)=0
    AND (SELECT tgoldtable FROM trg) IS NULL AND (SELECT tgnewtable FROM trg) IS NULL
  AS locked_catalog_exact
FROM counts \gset
\if :locked_catalog_exact
DROP TRIGGER trg_opstrax_document_release_hold ON public.documents;
DROP FUNCTION public.opstrax_document_release_hold();
\else
\echo ERROR: release-hold catalog changed before disable; transaction will roll back
\quit 1
\endif
COMMIT;
SQL
  expected_state=absent
fi

state="$(catalog_state)"
if [[ "$state" != "$expected_state" ]]; then
  echo "ERROR: document release-hold postcondition failed" >&2
  exit 1
fi
emit_status "$state"

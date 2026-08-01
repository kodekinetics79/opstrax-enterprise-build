#!/usr/bin/env bash
set -euo pipefail

: "${OPSTRAX_TEST_DB_HOST:=127.0.0.1}"
: "${OPSTRAX_TEST_DB_PORT:=59955}"
: "${OPSTRAX_TEST_DB_USER:=zayra}"
: "${OPSTRAX_TEST_DB_PASSWORD:=zayra}"

audit_db="opstrax_stage62_cleanbase_${$}"
case "$audit_db" in
  opstrax_stage62_cleanbase_[0-9]*) ;;
  *) echo "Unsafe audit database name" >&2; exit 2 ;;
esac

export PGPASSWORD="$OPSTRAX_TEST_DB_PASSWORD"
cleanup() { dropdb --if-exists -h "$OPSTRAX_TEST_DB_HOST" -p "$OPSTRAX_TEST_DB_PORT" -U "$OPSTRAX_TEST_DB_USER" "$audit_db" >/dev/null 2>&1 || true; }
trap cleanup EXIT

createdb -h "$OPSTRAX_TEST_DB_HOST" -p "$OPSTRAX_TEST_DB_PORT" -U "$OPSTRAX_TEST_DB_USER" "$audit_db"
psql -h "$OPSTRAX_TEST_DB_HOST" -p "$OPSTRAX_TEST_DB_PORT" -U "$OPSTRAX_TEST_DB_USER" -d "$audit_db" -v ON_ERROR_STOP=1 -q -f database/init/001_schema.sql

# Fleet runtime bootstrap precedes pre-deploy migrations, while the billing module
# may still be absent. Keep this fixture intentionally free of job_charges.
psql -h "$OPSTRAX_TEST_DB_HOST" -p "$OPSTRAX_TEST_DB_PORT" -U "$OPSTRAX_TEST_DB_USER" -d "$audit_db" -v ON_ERROR_STOP=1 -q <<'SQL'
CREATE TABLE fleet_tms_dispatch_orders(id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,company_id BIGINT NOT NULL,branch_id BIGINT,order_number VARCHAR(60) NOT NULL DEFAULT '',status VARCHAR(30) NOT NULL DEFAULT 'Queued',item_count INT NOT NULL DEFAULT 1,order_value NUMERIC(14,2) NOT NULL DEFAULT 0);
CREATE TABLE fleet_tms_delivery_routes(id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,company_id BIGINT NOT NULL,branch_id BIGINT,route_code VARCHAR(60) NOT NULL DEFAULT '',status VARCHAR(30) NOT NULL DEFAULT 'Planned',planned_stops INT NOT NULL DEFAULT 0,completed_stops INT NOT NULL DEFAULT 0,distance_km NUMERIC(10,2) NOT NULL DEFAULT 0,completion_percent NUMERIC(6,2) NOT NULL DEFAULT 0);
CREATE TABLE fleet_tms_last_mile_stops(id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,company_id BIGINT NOT NULL,branch_id BIGINT,order_number VARCHAR(60) NOT NULL DEFAULT '',route_code VARCHAR(60) NOT NULL DEFAULT '',status VARCHAR(30) NOT NULL DEFAULT 'OutForDelivery',attempt_count INT NOT NULL DEFAULT 0);
SQL

psql -h "$OPSTRAX_TEST_DB_HOST" -p "$OPSTRAX_TEST_DB_PORT" -U "$OPSTRAX_TEST_DB_USER" -d "$audit_db" -v ON_ERROR_STOP=1 -q -f database/migrations/2026_08_01_stage62_last_mile_pilot.sql

result=$(psql -h "$OPSTRAX_TEST_DB_HOST" -p "$OPSTRAX_TEST_DB_PORT" -U "$OPSTRAX_TEST_DB_USER" -d "$audit_db" -Atc "SELECT (to_regclass('public.job_charges') IS NULL)::int || ':' || (to_regclass('public.uq_job_charges_last_mile') IS NULL)::int || ':' || COUNT(*) FROM pg_indexes WHERE indexname IN ('uq_ftms_dorders_company_number','uq_ftms_droutes_company_code','uq_ftms_lmstops_company_order','uq_ftms_route_progress_key','uq_ftms_stop_action_key')")
test "$result" = "1:1:5"
echo "Stage62 clean-base regression passed (job_charges absent; five Last Mile indexes present)."

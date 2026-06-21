#!/usr/bin/env bash
# import-tidb-schema.sh
# Imports the Zayra schema SQL files into TiDB Cloud using Docker mysql:8.0.
# No local mysql or brew installation required.

set -euo pipefail

# ── Colour helpers ────────────────────────────────────────────────────────────
RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; CYAN='\033[0;36m'; NC='\033[0m'
info()    { echo -e "${CYAN}[info]${NC}  $*"; }
success() { echo -e "${GREEN}[ok]${NC}    $*"; }
warn()    { echo -e "${YELLOW}[warn]${NC}  $*"; }
die()     { echo -e "${RED}[error]${NC} $*" >&2; exit 1; }

# ── Check Docker is available ─────────────────────────────────────────────────
command -v docker >/dev/null 2>&1 || die "Docker is not installed or not in PATH."
docker info >/dev/null 2>&1      || die "Docker daemon is not running. Start Docker Desktop and retry."

# ── Locate SQL files ──────────────────────────────────────────────────────────
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DESKTOP="$HOME/Desktop"

# Prefer Desktop (where the files were placed), fall back to repo root
if [[ -f "$DESKTOP/schema_part1.sql" ]]; then
    SQL_DIR="$DESKTOP"
elif [[ -f "$SCRIPT_DIR/../Desktop/schema_part1.sql" ]]; then
    SQL_DIR="$SCRIPT_DIR/../Desktop"
else
    SQL_DIR="$DESKTOP"
fi

PART1="$SQL_DIR/schema_part1.sql"
PART2="$SQL_DIR/schema_part2.sql"
PART3="$SQL_DIR/schema_part3.sql"

echo ""
echo -e "${CYAN}═══════════════════════════════════════════════════════${NC}"
echo -e "${CYAN}  Zayra → TiDB Cloud schema import${NC}"
echo -e "${CYAN}═══════════════════════════════════════════════════════${NC}"
echo ""

# ── Verify SQL files exist ────────────────────────────────────────────────────
info "Checking SQL files in: $SQL_DIR"
for f in "$PART1" "$PART2" "$PART3"; do
    [[ -f "$f" ]] || die "Missing file: $f — run the app locally first to regenerate."
    success "Found $(basename "$f") ($(wc -c < "$f" | tr -d ' ') bytes)"
done
echo ""

# ── Collect TiDB credentials ──────────────────────────────────────────────────
echo -e "${YELLOW}Enter TiDB Cloud connection details${NC}"
echo -e "  (Find these in TiDB Cloud → Connect → Standard Connection)"
echo ""

read -r -p "  TiDB host (e.g. gateway01.us-east-1.prod.aws.tidbcloud.com): " TIDB_HOST
[[ -n "$TIDB_HOST" ]] || die "Host cannot be empty."

read -r -p "  TiDB user (e.g. 2tQbtxZvgkqei27.root): " TIDB_USER
[[ -n "$TIDB_USER" ]] || die "User cannot be empty."

read -r -p "  Database name [zayra]: " TIDB_DB
TIDB_DB="${TIDB_DB:-zayra}"
[[ -n "$TIDB_DB" ]] || die "Database cannot be empty."

read -r -s -p "  Password (input hidden): " TIDB_PASS
echo ""
[[ -n "$TIDB_PASS" ]] || die "Password cannot be empty."
echo ""

# ── Summary before running ────────────────────────────────────────────────────
echo -e "${YELLOW}Connection plan:${NC}"
echo "  host     : $TIDB_HOST"
echo "  port     : 4000"
echo "  user     : $TIDB_USER"
echo "  database : $TIDB_DB"
echo "  ssl      : REQUIRED"
echo "  password : [hidden]"
echo "  client   : docker run mysql:8.0"
echo ""
read -r -p "Proceed? [y/N] " CONFIRM
[[ "$(printf "%s" "$CONFIRM" | tr "[:upper:]" "[:lower:]")" == "y" ]] || { warn "Aborted."; exit 0; }
echo ""

# ── Pull mysql:8.0 if not already cached ─────────────────────────────────────
if ! docker image inspect mysql:8.0 >/dev/null 2>&1; then
    info "Pulling mysql:8.0 (one-time, ~150 MB)..."
    docker pull mysql:8.0
fi

# ── Helper: run a SQL file via Docker ─────────────────────────────────────────
run_sql_stream() {
    local label="$1"
    # Reads SQL from stdin, passes to mysql inside Docker.
    # MYSQL_PWD is passed as an env var — never appears in docker ps or process args.
    MYSQL_PWD="$TIDB_PASS" docker run --rm -i \
        -e MYSQL_PWD \
        mysql:8.0 \
        mysql \
            --protocol=TCP \
            --ssl-mode=REQUIRED \
            --host="$TIDB_HOST" \
            --port=4000 \
            --user="$TIDB_USER" \
            "$TIDB_DB" \
        || die "$label failed. Fix the error above and re-run."

    success "$label imported."
}

run_sql_file() {
    local label="$1"
    local file="$2"
    local filesize
    filesize=$(wc -c < "$file" | tr -d ' ')

    info "Importing $label ($filesize bytes, patching collation)..."
    # TiDB Cloud new-collation mode rejects ascii_general_ci — replace with ascii_bin.
    sed 's/ascii_general_ci/ascii_bin/g' "$file" | run_sql_stream "$label"
}

# ── Missing tables (not in any migration — created at runtime by MissingTableCreator) ──
inject_missing_tables() {
    info "Injecting tables missing from migrations (gosi_contribution_rules, mfa_challenge_tokens, statutory_rules)..."
    run_sql_stream "missing tables" <<'MISSING_SQL'
USE `zayra`;

CREATE TABLE IF NOT EXISTS `gosi_contribution_rules` (
    `id` char(36) COLLATE ascii_bin NOT NULL,
    `tenant_id` char(36) COLLATE ascii_bin NOT NULL,
    `country_code` varchar(5) CHARACTER SET utf8mb4 NOT NULL,
    `classification` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
    `branch` varchar(30) CHARACTER SET utf8mb4 NOT NULL,
    `payer` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
    `rate` decimal(7,4) NOT NULL,
    `min_contributory_wage` decimal(12,2) NULL,
    `max_contributory_wage` decimal(12,2) NULL,
    `effective_from` date NOT NULL,
    `effective_to` date NULL,
    `is_active` tinyint(1) NOT NULL DEFAULT 1,
    `source_reference` varchar(200) CHARACTER SET utf8mb4 NULL,
    `notes` varchar(500) CHARACTER SET utf8mb4 NULL,
    `created_at_utc` datetime(6) NOT NULL,
    `created_by` char(36) COLLATE ascii_bin NULL,
    CONSTRAINT `PK_gosi_contribution_rules` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4;

CREATE INDEX IF NOT EXISTS `IX_gosi_contribution_rules_tenant_classification_branch_payer`
ON `gosi_contribution_rules` (`tenant_id`, `classification`, `branch`, `payer`, `is_active`);

CREATE TABLE IF NOT EXISTS `mfa_challenge_tokens` (
    `id` char(36) COLLATE ascii_bin NOT NULL,
    `user_id` char(36) COLLATE ascii_bin NULL,
    `platform_user_id` char(36) COLLATE ascii_bin NULL,
    `tenant_id` char(36) COLLATE ascii_bin NULL,
    `token_hash` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `expires_at_utc` datetime(6) NOT NULL,
    `created_by_ip` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
    `used_at_utc` datetime(6) NULL,
    `created_at_utc` datetime(6) NOT NULL,
    CONSTRAINT `PK_mfa_challenge_tokens` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4;

CREATE UNIQUE INDEX IF NOT EXISTS `IX_mfa_challenge_tokens_token_hash`
ON `mfa_challenge_tokens` (`token_hash`);

CREATE INDEX IF NOT EXISTS `IX_mfa_challenge_tokens_expires_at_utc`
ON `mfa_challenge_tokens` (`expires_at_utc`);

CREATE TABLE IF NOT EXISTS `statutory_rules` (
    `id` char(36) COLLATE ascii_bin NOT NULL,
    `tenant_id` char(36) COLLATE ascii_bin NULL,
    `country_code` varchar(5) CHARACTER SET utf8mb4 NOT NULL,
    `jurisdiction` varchar(30) CHARACTER SET utf8mb4 NOT NULL,
    `rule_key` varchar(120) CHARACTER SET utf8mb4 NOT NULL,
    `rule_value` longtext CHARACTER SET utf8mb4 NOT NULL,
    `data_type` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
    `description` longtext CHARACTER SET utf8mb4 NOT NULL,
    `effective_from` datetime(6) NOT NULL,
    `effective_to` datetime(6) NULL,
    `created_at_utc` datetime(6) NOT NULL,
    `created_by` char(36) COLLATE ascii_bin NULL,
    CONSTRAINT `PK_statutory_rules` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4;

CREATE UNIQUE INDEX IF NOT EXISTS `IX_statutory_rules_tenant_id_country_code_jurisdiction_rule_key~`
ON `statutory_rules` (`tenant_id`, `country_code`, `jurisdiction`, `rule_key`, `effective_from`);
MISSING_SQL
}

# ── Import parts in order ─────────────────────────────────────────────────────
run_sql_file "Part 1 (core tables — migration 1)" "$PART1"
run_sql_file "Part 2 (modules — migrations 2–8)"   "$PART2"
inject_missing_tables
run_sql_file "Part 3 (payroll/RBAC — migrations 9–20)" "$PART3"

echo ""
echo -e "${CYAN}─── Verification ────────────────────────────────────────${NC}"

# ── Table count ───────────────────────────────────────────────────────────────
TABLE_COUNT=$(MYSQL_PWD="$TIDB_PASS" docker run --rm -i \
    -e MYSQL_PWD \
    mysql:8.0 \
    mysql \
        --protocol=TCP \
        --ssl-mode=REQUIRED \
        --host="$TIDB_HOST" \
        --port=4000 \
        --user="$TIDB_USER" \
        --skip-column-names \
        --batch \
        "$TIDB_DB" \
        --execute="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE();")

# ── Migration history count ───────────────────────────────────────────────────
MIGRATION_COUNT=$(MYSQL_PWD="$TIDB_PASS" docker run --rm -i \
    -e MYSQL_PWD \
    mysql:8.0 \
    mysql \
        --protocol=TCP \
        --ssl-mode=REQUIRED \
        --host="$TIDB_HOST" \
        --port=4000 \
        --user="$TIDB_USER" \
        --skip-column-names \
        --batch \
        "$TIDB_DB" \
        --execute="SELECT COUNT(*) FROM \`__EFMigrationsHistory\`;")

echo ""
success "Tables in database   : $TABLE_COUNT"
success "Migrations applied   : $MIGRATION_COUNT / 20"
echo ""

if [[ "$TABLE_COUNT" -ge 200 ]] && [[ "$MIGRATION_COUNT" -eq 20 ]]; then
    echo -e "${GREEN}✓ Schema import complete.${NC}"
    echo ""
    echo -e "  Next steps:"
    echo -e "  1. Render web service will now seed the admin user automatically on the"
    echo -e "     next startup (seeder is non-fatal and runs on every boot)."
    echo -e "  2. Wait ~60s for Render's health check to flip to 200 with tables > 200."
    echo -e "  3. Test login:"
    echo -e "     curl -s -X POST https://kynexone.onrender.com/api/auth/login \\"
    echo -e "       -H 'Content-Type: application/json' \\"
    echo -e "       -d '{\"email\":\"admin@zayra.local\",\"password\":\"ChangeMe123!\",\"tenantSlug\":\"zayra\"}'"
else
    warn "Table count ($TABLE_COUNT) or migration count ($MIGRATION_COUNT) looks low."
    warn "Check the error output above. You may need to re-run a failed part."
fi
echo ""

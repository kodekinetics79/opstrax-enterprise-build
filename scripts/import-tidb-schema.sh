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
[[ "${CONFIRM,,}" == "y" ]] || { warn "Aborted."; exit 0; }
echo ""

# ── Pull mysql:8.0 if not already cached ─────────────────────────────────────
if ! docker image inspect mysql:8.0 >/dev/null 2>&1; then
    info "Pulling mysql:8.0 (one-time, ~150 MB)..."
    docker pull mysql:8.0
fi

# ── Helper: run a SQL file via Docker ─────────────────────────────────────────
run_sql_file() {
    local label="$1"
    local file="$2"
    local filesize
    filesize=$(wc -c < "$file" | tr -d ' ')

    info "Importing $label ($filesize bytes)..."

    # MYSQL_PWD is used by the mysql client; it is passed as an env var inside
    # the container and never printed. The password is NOT in the docker run args.
    docker run --rm \
        -e MYSQL_PWD="$TIDB_PASS" \
        -v "$file:/import.sql:ro" \
        mysql:8.0 \
        mysql \
            --protocol=TCP \
            --ssl-mode=REQUIRED \
            --host="$TIDB_HOST" \
            --port=4000 \
            --user="$TIDB_USER" \
            "$TIDB_DB" \
            < /import.sql || die "$label failed. Fix the error above and re-run."

    success "$label imported."
}

# ── Import parts in order ─────────────────────────────────────────────────────
run_sql_file "Part 1 (core tables — migration 1)" "$PART1"
run_sql_file "Part 2 (modules — migrations 2–8)"   "$PART2"
run_sql_file "Part 3 (payroll/RBAC — migrations 9–20)" "$PART3"

echo ""
echo -e "${CYAN}─── Verification ────────────────────────────────────────${NC}"

# ── Table count ───────────────────────────────────────────────────────────────
TABLE_COUNT=$(docker run --rm \
    -e MYSQL_PWD="$TIDB_PASS" \
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
MIGRATION_COUNT=$(docker run --rm \
    -e MYSQL_PWD="$TIDB_PASS" \
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

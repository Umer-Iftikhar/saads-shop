#!/usr/bin/env bash
#
# Apply the Saad's Shop database: schema, then types, then indexes, then
# procedures, then seed data. Every script is idempotent, so running this
# against an existing database is safe and is the normal way to deploy a
# change.
#
#   ./apply.sh                        # uses the environment / defaults below
#   ./apply.sh --demo                 # also seed demo orders and floor jobs
#   ./apply.sh --database SaadsShopTest
#
# Environment:
#   MSSQL_SERVER    default localhost,1433
#   MSSQL_USER      default sa
#   MSSQL_PASSWORD  required
#   MSSQL_DATABASE  default SaadsShop

set -euo pipefail

SERVER="${MSSQL_SERVER:-localhost,1433}"
USER="${MSSQL_USER:-sa}"
PASSWORD="${MSSQL_PASSWORD:-}"
DATABASE="${MSSQL_DATABASE:-SaadsShop}"
SEED_DEMO=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        --demo)     SEED_DEMO=1; shift ;;
        --database) DATABASE="$2"; shift 2 ;;
        --server)   SERVER="$2"; shift 2 ;;
        --user)     USER="$2"; shift 2 ;;
        -h|--help)  sed -n '2,20p' "$0"; exit 0 ;;
        *) echo "unknown option: $1" >&2; exit 2 ;;
    esac
done

if [[ -z "$PASSWORD" ]]; then
    echo "MSSQL_PASSWORD is not set." >&2
    exit 2
fi

# sqlcmd moved to 'go-sqlcmd' in recent images; accept either, and take the
# -C (trust server certificate) flag only where it is understood.
if command -v sqlcmd >/dev/null 2>&1; then
    SQLCMD=sqlcmd
elif [[ -x /opt/mssql-tools18/bin/sqlcmd ]]; then
    SQLCMD=/opt/mssql-tools18/bin/sqlcmd
elif [[ -x /opt/mssql-tools/bin/sqlcmd ]]; then
    SQLCMD=/opt/mssql-tools/bin/sqlcmd
else
    echo "sqlcmd not found. Install mssql-tools18, or run this inside the SQL Server container." >&2
    exit 2
fi

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

run_sql() {
    local file="$1"
    echo "  → $(basename "$file")"
    # -b: exit non-zero on SQL error, so set -e actually stops us.
    # -I: quoted identifiers on, required by filtered indexes.
    "$SQLCMD" -S "$SERVER" -U "$USER" -P "$PASSWORD" -d "$DATABASE" \
              -b -I -C -i "$file"
}

echo "Creating database $DATABASE if it does not exist..."
"$SQLCMD" -S "$SERVER" -U "$USER" -P "$PASSWORD" -d master -b -I -C \
    -Q "IF DB_ID(N'$DATABASE') IS NULL CREATE DATABASE [$DATABASE];"

echo "Schema..."
for f in "$HERE"/schema/*.sql; do run_sql "$f"; done

echo "Procedures..."
for f in "$HERE"/procedures/*.sql; do run_sql "$f"; done

echo "Reference data..."
run_sql "$HERE/seed/01_reference.sql"
run_sql "$HERE/seed/02_catalog.sql"

if [[ "$SEED_DEMO" -eq 1 ]]; then
    echo "Demo data..."
    run_sql "$HERE/seed/03_demo.sql"
fi

echo "Done. $DATABASE is ready."

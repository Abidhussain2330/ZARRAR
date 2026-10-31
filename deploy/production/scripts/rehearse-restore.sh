#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

if [[ $# -ne 1 || ! -d "$1" ]]; then
    printf 'Usage: %s /absolute/path/to/verified-zarrar-backup\n' "$0" >&2
    exit 64
fi

BACKUP_DIR="$(cd -- "$1" && pwd)"
"$SCRIPT_DIR/verify-backup.sh" "$BACKUP_DIR"

suffix="$(date -u +%Y%m%d%H%M%S)-$$"
container_name="zarrar-restore-rehearsal-$suffix"
cleanup() {
    docker rm --force --volumes "$container_name" >/dev/null 2>&1 || true
}
trap cleanup EXIT INT TERM

docker run --detach --name "$container_name" \
    -e POSTGRES_PASSWORD=zarrar-restore-rehearsal-only \
    -v "$BACKUP_DIR:/backup:ro" \
    postgres:17.10-alpine >/dev/null

for _ in {1..60}; do
    if docker exec "$container_name" pg_isready --username postgres --dbname postgres >/dev/null 2>&1; then
        break
    fi
    sleep 1
done
docker exec "$container_name" pg_isready --username postgres --dbname postgres >/dev/null
docker exec "$container_name" createdb --username postgres zarrar_rehearsal
docker exec "$container_name" pg_restore --username postgres --dbname zarrar_rehearsal \
    --no-owner --exit-on-error /backup/postgresql.dump

table_count="$(docker exec "$container_name" psql --username postgres --dbname zarrar_rehearsal \
    --tuples-only --no-align --command \
    "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public';")"
[[ "$table_count" =~ ^[0-9]+$ && "$table_count" -ge 20 ]] || {
    printf 'Rehearsed database contains only %s public tables.\n' "$table_count" >&2
    exit 65
}

printf 'Restore rehearsal passed: PostgreSQL restored into an isolated disposable instance (%s tables).\n' \
    "$table_count"

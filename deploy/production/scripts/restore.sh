#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "$SCRIPT_DIR/common.sh"

if [[ $# -ne 1 || ! -d "$1" ]]; then
    printf 'Usage: %s /absolute/path/to/verified-buraq-backup\n' "$0" >&2
    exit 64
fi

BACKUP_DIR="$(cd -- "$1" && pwd)"
"$SCRIPT_DIR/verify-backup.sh" "$BACKUP_DIR"

printf '%s\n' \
    'WARNING: this replaces the configured PostgreSQL database, object bucket, and BURAQ files.' \
    'No Docker volume is deleted. Take a separate pre-restore backup first.'
printf 'Environment: %s\nDatabase: %s\nObject bucket: %s\nData network: %s\nFiles volume: %s\n' \
    "$ENV_FILE" "$POSTGRES_DB" "$MINIO_BUCKET" "$(data_network)" "$(volume_name files)"
read -r -p 'Type RESTORE BURAQ to continue: ' confirmation
[[ "$confirmation" == 'RESTORE BURAQ' ]] || {
    printf 'Restore cancelled.\n'
    exit 0
}

compose stop proxy buraq

compose exec -T postgres psql --username "$POSTGRES_USER" --dbname postgres \
    --set ON_ERROR_STOP=1 \
    --command "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$POSTGRES_DB' AND pid <> pg_backend_pid();"
compose exec -T postgres dropdb --username "$POSTGRES_USER" --if-exists "$POSTGRES_DB"
compose exec -T postgres createdb --username "$POSTGRES_USER" --owner "$POSTGRES_USER" "$POSTGRES_DB"
compose exec -T postgres pg_restore --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
    --no-owner --exit-on-error <"$BACKUP_DIR/postgresql.dump"

docker run --rm \
    --user "$(id -u):$(id -g)" \
    --network "$(data_network)" \
    -e MC_CONFIG_DIR=/tmp/mc \
    -e MINIO_ROOT_USER -e MINIO_ROOT_PASSWORD -e MINIO_BUCKET \
    -v "$BACKUP_DIR/object-storage:/backup:ro" \
    --entrypoint /bin/sh \
    "$BURAQ_MC_IMAGE" -ec '
        mc alias set buraq http://minio:9000 "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD" >/dev/null
        mc mb --ignore-existing "buraq/$MINIO_BUCKET" >/dev/null
        mc rm --recursive --force "buraq/$MINIO_BUCKET" >/dev/null
        mc mirror --overwrite "/backup/$MINIO_BUCKET" "buraq/$MINIO_BUCKET"
    '

docker run --rm \
    -v "$(volume_name files):/target" \
    -v "$BACKUP_DIR:/backup:ro" \
    alpine:3.22 sh -ec '
        find /target -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +
        tar -xzf /backup/buraq-files.tar.gz -C /target
    '

compose exec -T redis redis-cli --no-auth-warning -a "$REDIS_PASSWORD" FLUSHALL SYNC >/dev/null

compose up -d buraq proxy
printf '%s\n' \
    'Restore complete. Verify /healthz, login, an asset download, and protected-key operations.' \
    'config.env and caddy-data.tar.gz were not applied automatically; compare/restore them deliberately.'

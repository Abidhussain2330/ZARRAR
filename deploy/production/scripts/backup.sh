#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "$SCRIPT_DIR/common.sh"

if [[ $# -ne 1 || "$1" != /* ]]; then
    printf 'Usage: %s /absolute/encrypted-backup-root\n' "$0" >&2
    exit 64
fi

BACKUP_ROOT="$1"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
BACKUP_DIR="$BACKUP_ROOT/buraq-$STAMP"
LOCK_DIR="$BACKUP_ROOT/.buraq-backup.lock"
APP_STOPPED=false

mkdir -p -- "$BACKUP_ROOT"
mkdir -- "$LOCK_DIR" 2>/dev/null || {
    printf 'Another BURAQ backup appears to be running: %s\n' "$LOCK_DIR" >&2
    exit 75
}

cleanup() {
    if [[ "$APP_STOPPED" == true ]]; then
        compose up -d buraq proxy >/dev/null || true
    fi
    rmdir -- "$LOCK_DIR" 2>/dev/null || true
}
trap cleanup EXIT INT TERM

mkdir -p -- "$BACKUP_DIR/object-storage"
printf 'Backup destination: %s\n' "$BACKUP_DIR"
df -h -- "$BACKUP_ROOT"

compose stop proxy buraq
APP_STOPPED=true

compose exec -T postgres pg_dump \
    --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
    --format custom --create --no-owner >"$BACKUP_DIR/postgresql.dump"

docker run --rm \
    --user "$(id -u):$(id -g)" \
    --network "$(data_network)" \
    -e MC_CONFIG_DIR=/tmp/mc \
    -e MINIO_ROOT_USER -e MINIO_ROOT_PASSWORD -e MINIO_BUCKET \
    -v "$BACKUP_DIR/object-storage:/backup" \
    --entrypoint /bin/sh \
    "$BURAQ_MC_IMAGE" -ec '
        mc alias set buraq http://minio:9000 "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD" >/dev/null
        mc mirror --overwrite "buraq/$MINIO_BUCKET" "/backup/$MINIO_BUCKET"
    '

docker run --rm \
    -v "$(volume_name files):/source:ro" \
    -v "$BACKUP_DIR:/backup" \
    alpine:3.22 tar -czf /backup/buraq-files.tar.gz -C /source .

docker run --rm \
    -v "$(volume_name caddy_data):/source:ro" \
    -v "$BACKUP_DIR:/backup" \
    alpine:3.22 tar -czf /backup/caddy-data.tar.gz -C /source .

install -m 0600 -- "$ENV_FILE" "$BACKUP_DIR/config.env"
printf '%s\n' \
    'PostgreSQL includes the Data Protection key ring.' \
    'config.env includes XorKey and credentials; encrypt this backup off-host.' \
    'Redis is intentionally excluded because it is non-authoritative cache/backplane state.' \
    >"$BACKUP_DIR/RECOVERY_NOTES.txt"

(
    cd -- "$BACKUP_DIR"
    find . -type f ! -name SHA256SUMS -print0 | sort -z | xargs -0 sha256sum >SHA256SUMS
)

compose up -d buraq proxy
APP_STOPPED=false
printf 'Backup complete: %s\n' "$BACKUP_DIR"

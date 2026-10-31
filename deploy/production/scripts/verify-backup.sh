#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 || ! -d "$1" ]]; then
    printf 'Usage: %s /absolute/path/to/zarrar-backup\n' "$0" >&2
    exit 64
fi

BACKUP_DIR="$(cd -- "$1" && pwd)"
cd -- "$BACKUP_DIR"

required=(SHA256SUMS postgresql.dump zarrar-files.tar.gz caddy-data.tar.gz config.env RECOVERY_NOTES.txt)
for item in "${required[@]}"; do
    [[ -f "$item" ]] || {
        printf 'Required backup item is missing: %s\n' "$item" >&2
        exit 65
    }
done

sha256sum --check SHA256SUMS
docker run --rm -v "$BACKUP_DIR:/backup:ro" postgres:17.10-alpine \
    pg_restore --list /backup/postgresql.dump >/dev/null
docker run --rm -v "$BACKUP_DIR:/backup:ro" alpine:3.22 \
    tar -tzf /backup/zarrar-files.tar.gz >/dev/null
docker run --rm -v "$BACKUP_DIR:/backup:ro" alpine:3.22 \
    tar -tzf /backup/caddy-data.tar.gz >/dev/null

[[ -d "$BACKUP_DIR/object-storage" ]] || {
    printf 'Object-storage snapshot is missing.\n' >&2
    exit 65
}

config_mode="$(stat -c '%a' "$BACKUP_DIR/config.env")"
[[ "$config_mode" == 600 || "$config_mode" == 400 ]] || {
    printf 'Backup config.env permissions are %s; expected 0600 or 0400.\n' "$config_mode" >&2
    exit 77
}

printf 'Backup structure, checksums, PostgreSQL, application files, Caddy state, object snapshot, and protected configuration are valid.\n'


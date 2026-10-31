#!/usr/bin/env bash
set -euo pipefail

umask 077

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="$(cd -- "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="${ZARRAR_ENV_FILE:-$DEPLOY_DIR/.env.production}"
COMPOSE_FILE="$DEPLOY_DIR/compose.yaml"

[[ -f "$ENV_FILE" ]] || {
    printf 'Missing production environment file: %s\n' "$ENV_FILE" >&2
    exit 64
}

set -a
# shellcheck disable=SC1090
source "$ENV_FILE"
set +a

: "${COMPOSE_PROJECT_NAME:=zarrar}"
: "${ZARRAR_VOLUME_PREFIX:=zarrar}"
: "${ZARRAR_MC_IMAGE:=zarrar-minio-client:77f82e1}"
: "${POSTGRES_DB:?POSTGRES_DB is required}"
: "${POSTGRES_USER:?POSTGRES_USER is required}"
: "${REDIS_PASSWORD:?REDIS_PASSWORD is required}"
: "${MINIO_ROOT_USER:?MINIO_ROOT_USER is required}"
: "${MINIO_ROOT_PASSWORD:?MINIO_ROOT_PASSWORD is required}"
: "${MINIO_BUCKET:?MINIO_BUCKET is required}"

[[ "$POSTGRES_DB" =~ ^[A-Za-z0-9_]+$ ]] || {
    printf 'POSTGRES_DB may contain only letters, digits, and underscores.\n' >&2
    exit 64
}

[[ "$POSTGRES_USER" =~ ^[A-Za-z0-9_]+$ ]] || {
    printf 'POSTGRES_USER may contain only letters, digits, and underscores.\n' >&2
    exit 64
}

[[ "$COMPOSE_PROJECT_NAME" =~ ^[a-z0-9][a-z0-9_-]*$ ]] || {
    printf 'COMPOSE_PROJECT_NAME must be a lowercase Docker-safe name.\n' >&2
    exit 64
}

[[ "$ZARRAR_VOLUME_PREFIX" =~ ^[A-Za-z0-9][A-Za-z0-9_.-]*$ ]] || {
    printf 'ZARRAR_VOLUME_PREFIX contains unsafe characters.\n' >&2
    exit 64
}

compose() {
    docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" "$@"
}

data_network() {
    printf '%s_data' "$COMPOSE_PROJECT_NAME"
}

volume_name() {
    printf '%s_%s' "$ZARRAR_VOLUME_PREFIX" "$1"
}

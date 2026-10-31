#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "$SCRIPT_DIR/common.sh"

compose ps
compose exec -T postgres pg_isready --username "$POSTGRES_USER" --dbname "$POSTGRES_DB"
compose exec -T redis sh -ec 'redis-cli --no-auth-warning -a "$REDIS_PASSWORD" ping | grep -q PONG'
compose exec -T minio curl --fail --silent http://127.0.0.1:9000/minio/health/ready >/dev/null
compose exec -T zarrar wget --quiet --tries=1 --spider \
    --header="Host: $ZARRAR_ALLOWED_HOSTS" http://127.0.0.1:3000/healthz

for service in zarrar postgres redis minio; do
    container_id="$(compose ps --quiet "$service")"
    [[ -n "$container_id" ]] || {
        printf 'Service %s has no running container.\n' "$service" >&2
        exit 69
    }
    bindings="$(docker inspect --format '{{json .HostConfig.PortBindings}}' "$container_id")"
    [[ "$bindings" == '{}' || "$bindings" == 'null' ]] || {
        printf 'Internal service %s publishes a host port: %s\n' "$service" "$bindings" >&2
        exit 77
    }
done

printf 'PostgreSQL, Redis, MinIO, and ZARRAR health checks passed; internal service ports are unpublished.\n'

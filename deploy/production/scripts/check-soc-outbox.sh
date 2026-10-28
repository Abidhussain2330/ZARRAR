#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "$SCRIPT_DIR/common.sh"

printf 'Checking BURAQ SOC event outbox health...\n'

compose exec -T postgres psql \
  --username "$POSTGRES_USER" \
  --dbname "$POSTGRES_DB" \
  --set ON_ERROR_STOP=1 \
  --no-align \
  --field-separator='|' \
  --command '
    SELECT
      COUNT(*) FILTER (WHERE "ProcessedAtUtc" IS NULL AND "AttemptCount" < 10) AS pending,
      COUNT(*) FILTER (WHERE "ProcessedAtUtc" IS NULL AND "AttemptCount" > 0 AND "AttemptCount" < 10) AS retrying,
      COUNT(*) FILTER (WHERE "ProcessedAtUtc" IS NULL AND "AttemptCount" >= 10) AS dead_lettered,
      MIN("OccurredAtUtc") FILTER (WHERE "ProcessedAtUtc" IS NULL) AS oldest_pending,
      MAX("ProcessedAtUtc") AS last_processed
    FROM "SocEventOutbox";
  '

DEAD_LETTERED="$(compose exec -T postgres psql \
  --username "$POSTGRES_USER" \
  --dbname "$POSTGRES_DB" \
  --tuples-only \
  --no-align \
  --command 'SELECT COUNT(*) FROM "SocEventOutbox" WHERE "ProcessedAtUtc" IS NULL AND "AttemptCount" >= 10;')"

if [[ "${DEAD_LETTERED//[[:space:]]/}" != "0" ]]; then
  printf 'SOC outbox has %s dead-lettered event(s). Investigate /api/soc/outbox and LastError before deployment continues.\n' \
    "${DEAD_LETTERED//[[:space:]]/}" >&2
  exit 1
fi

printf 'SOC event outbox has no dead-lettered records.\n'

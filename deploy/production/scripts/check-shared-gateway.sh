#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "$SCRIPT_DIR/common.sh"

printf 'Checking shared Web/API gateway invariants...\n'
if ! compose ps --status running postgres | grep -q postgres; then
    printf 'PostgreSQL service is not running.\n' >&2
    exit 69
fi

result="$(compose exec -T postgres \
    psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -AtF '|' <<'SQL'
SELECT
    (SELECT COUNT(*) FROM (
        SELECT "ChallengeId" FROM "SharedRuntimeConfigurations"
        GROUP BY "ChallengeId" HAVING COUNT(*) > 1
    ) duplicate_configs),
    (SELECT COUNT(*) FROM "SharedRuntimeConfigurations" c
        WHERE (SELECT COUNT(*) FROM "SharedRuntimeSessions" s
               WHERE s."RuntimeConfigurationId" = c."Id"
                 AND s."RevokedAtUtc" IS NULL AND s."ExpiresAtUtc" > NOW())
              > c."MaxConcurrentSessions"),
    (SELECT COUNT(*) FROM "SharedRuntimeConfigurations"
        WHERE "IsEnabled" AND "HealthStatus" IN ('Unavailable', 'Misconfigured'));
SQL
)"

IFS='|' read -r duplicate_configs over_capacity unhealthy <<<"$result"
printf 'Duplicate configurations: %s\n' "$duplicate_configs"
printf 'Runtimes over capacity: %s\n' "$over_capacity"
printf 'Enabled unhealthy runtimes: %s\n' "$unhealthy"

if (( duplicate_configs > 0 || over_capacity > 0 || unhealthy > 0 )); then
    printf 'Shared gateway invariant check FAILED.\n' >&2
    exit 1
fi
printf 'Shared gateway invariant check PASSED.\n'

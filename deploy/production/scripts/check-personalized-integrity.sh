#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "$SCRIPT_DIR/common.sh"

printf 'Checking personalized challenge ownership integrity...\n'

if ! compose ps --status running postgres | grep -q postgres; then
    printf 'PostgreSQL service is not running. Start the production database before this check.\n' >&2
    exit 69
fi

duplicate_assignments="$(compose exec -T postgres \
    psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atqc '
        SELECT COUNT(*)
        FROM (
            SELECT "FlagId"
            FROM "GameInstances"
            WHERE "FlagId" IS NOT NULL
            GROUP BY "FlagId"
            HAVING COUNT(*) > 1
        ) AS duplicates;
    ')"

duplicate_flags="$(compose exec -T postgres \
    psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atqc '
        SELECT COUNT(*)
        FROM (
            SELECT "ChallengeId", "Flag"
            FROM "FlagContexts"
            WHERE "ChallengeId" IS NOT NULL
            GROUP BY "ChallengeId", "Flag"
            HAVING COUNT(*) > 1
        ) AS duplicates;
    ')"

printf 'Duplicate assigned FlagId groups: %s\n' "$duplicate_assignments"
printf 'Duplicate (ChallengeId, Flag) groups: %s\n' "$duplicate_flags"

if (( duplicate_assignments > 0 || duplicate_flags > 0 )); then
    cat >&2 <<'EOF'
Integrity check FAILED.
Do not apply HardenPersonalizedChallengeUniqueness yet.
Investigate the duplicate rows and reconcile ownership deliberately; do not delete evidence blindly.
EOF
    exit 1
fi

printf 'Integrity check PASSED. No legacy duplicates block the personalized challenge uniqueness migration.\n'

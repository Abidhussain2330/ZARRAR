#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "$SCRIPT_DIR/common.sh"

printf 'Checking personalized artifact assignments...\n'

if ! compose ps --status running postgres | grep -q postgres; then
    printf 'PostgreSQL service is not running.\n' >&2
    exit 69
fi

result="$(compose exec -T postgres \
    psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -AtF '|' <<'SQL'
SELECT
    COUNT(*) FILTER (WHERE va."Id" IS NULL) AS missing_assignments,
    COUNT(*) FILTER (WHERE va."ArtifactHash" IS NOT NULL AND length(va."ArtifactHash") <> 64) AS invalid_hashes,
    COUNT(*) FILTER (WHERE va."DownloadCount" < 0) AS invalid_download_counts
FROM "GameInstances" gi
JOIN "GameChallenges" c ON c."Id" = gi."ChallengeId" AND c."Type" = 2
LEFT JOIN "VariantAssignments" va
    ON va."ChallengeId" = gi."ChallengeId"
    AND va."ParticipationId" = gi."ParticipationId";
SQL
)"

IFS='|' read -r missing_assignments invalid_hashes invalid_download_counts <<<"$result"
printf 'Missing assignments: %s\n' "$missing_assignments"
printf 'Invalid artifact hashes: %s\n' "$invalid_hashes"
printf 'Invalid download counts: %s\n' "$invalid_download_counts"

if (( missing_assignments > 0 || invalid_hashes > 0 || invalid_download_counts > 0 )); then
    printf 'Variant assignment integrity check FAILED.\n' >&2
    exit 1
fi

printf 'Variant assignment integrity check PASSED.\n'

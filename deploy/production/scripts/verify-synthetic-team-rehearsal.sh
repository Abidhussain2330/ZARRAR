#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "$SCRIPT_DIR/common.sh"

: "${ZARRAR_REHEARSAL_GAME_ID:?ZARRAR_REHEARSAL_GAME_ID is required}"
: "${ZARRAR_REHEARSAL_PARTICIPATION_A_ID:?ZARRAR_REHEARSAL_PARTICIPATION_A_ID is required}"
: "${ZARRAR_REHEARSAL_PARTICIPATION_B_ID:?ZARRAR_REHEARSAL_PARTICIPATION_B_ID is required}"
: "${ZARRAR_REHEARSAL_SINCE_UTC:?ZARRAR_REHEARSAL_SINCE_UTC is required, for example 2026-09-13T00:00:00Z}"

for value in "$ZARRAR_REHEARSAL_GAME_ID" "$ZARRAR_REHEARSAL_PARTICIPATION_A_ID" \
    "$ZARRAR_REHEARSAL_PARTICIPATION_B_ID"; do
    [[ "$value" =~ ^[1-9][0-9]*$ ]] || {
        printf 'Game and participation identifiers must be positive integers.\n' >&2
        exit 64
    }
done
[[ "$ZARRAR_REHEARSAL_PARTICIPATION_A_ID" != "$ZARRAR_REHEARSAL_PARTICIPATION_B_ID" ]] || {
    printf 'Synthetic teams must use different participations.\n' >&2
    exit 64
}
[[ "$ZARRAR_REHEARSAL_SINCE_UTC" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}T ]] || {
    printf 'ZARRAR_REHEARSAL_SINCE_UTC must be an ISO-8601 UTC timestamp.\n' >&2
    exit 64
}

scalar() {
    compose exec -T postgres psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
        --tuples-only --no-align --set ON_ERROR_STOP=1 \
        --set game_id="$ZARRAR_REHEARSAL_GAME_ID" \
        --set team_a="$ZARRAR_REHEARSAL_PARTICIPATION_A_ID" \
        --set team_b="$ZARRAR_REHEARSAL_PARTICIPATION_B_ID" \
        --set since_utc="$ZARRAR_REHEARSAL_SINCE_UTC" \
        --command "$1"
}

require_count() {
    local label="$1"
    local minimum="$2"
    local query="$3"
    local count
    count="$(scalar "$query")"
    [[ "$count" =~ ^[0-9]+$ && "$count" -ge "$minimum" ]] || {
        printf '%s failed: found %s, expected at least %s.\n' "$label" "$count" "$minimum" >&2
        exit 65
    }
    printf '%-38s %s\n' "$label" "$count"
}

require_count 'Accepted synthetic participations' 2 \
    'SELECT COUNT(*) FROM "Participations" WHERE "GameId" = :'"'"'game_id'"'"'::int AND "Status" = 1 AND "Id" IN (:'"'"'team_a'"'"'::int, :'"'"'team_b'"'"'::int);'
require_count 'Recent submissions from both teams' 2 \
    'SELECT COUNT(DISTINCT "ParticipationId") FROM "Submissions" WHERE "GameId" = :'"'"'game_id'"'"'::int AND "ParticipationId" IN (:'"'"'team_a'"'"'::int, :'"'"'team_b'"'"'::int) AND "SubmitTimeUtc" >= :'"'"'since_utc'"'"'::timestamptz;'
require_count 'SOC-processed rehearsal submissions' 2 \
    'SELECT COUNT(DISTINCT s."ParticipationId") FROM "SocDetectionJobs" j JOIN "Submissions" s ON s."Id" = j."SubmissionId" WHERE s."GameId" = :'"'"'game_id'"'"'::int AND s."ParticipationId" IN (:'"'"'team_a'"'"'::int, :'"'"'team_b'"'"'::int) AND s."SubmitTimeUtc" >= :'"'"'since_utc'"'"'::timestamptz AND j."Status" = 2;'

dynamic_count="$(scalar 'SELECT COUNT(*) FROM "GameChallenges" WHERE "GameId" = :'"'"'game_id'"'"'::int AND "IsEnabled" AND "Type" = 2;')"
if [[ "$dynamic_count" -gt 0 ]]; then
    require_count 'Personalized assignments' 2 \
        'SELECT COUNT(DISTINCT "ParticipationId") FROM "VariantAssignments" WHERE "ParticipationId" IN (:'"'"'team_a'"'"'::int, :'"'"'team_b'"'"'::int) AND "AssignedAtUtc" >= :'"'"'since_utc'"'"'::timestamptz AND "ChallengeId" IN (SELECT "Id" FROM "GameChallenges" WHERE "GameId" = :'"'"'game_id'"'"'::int AND "IsEnabled" AND "Type" = 2);'
fi

shared_count="$(scalar 'SELECT COUNT(*) FROM "SharedRuntimeConfigurations" r JOIN "GameChallenges" c ON c."Id" = r."ChallengeId" WHERE c."GameId" = :'"'"'game_id'"'"'::int AND r."IsEnabled";')"
if [[ "$shared_count" -gt 0 ]]; then
    require_count 'Shared-runtime team sessions' 2 \
        'SELECT COUNT(DISTINCT s."ParticipationId") FROM "SharedRuntimeSessions" s JOIN "SharedRuntimeConfigurations" r ON r."Id" = s."RuntimeConfigurationId" JOIN "GameChallenges" c ON c."Id" = r."ChallengeId" WHERE c."GameId" = :'"'"'game_id'"'"'::int AND s."ParticipationId" IN (:'"'"'team_a'"'"'::int, :'"'"'team_b'"'"'::int) AND s."LastAccessAtUtc" >= :'"'"'since_utc'"'"'::timestamptz;'
fi

container_count="$(scalar 'SELECT COUNT(*) FROM "GameChallenges" WHERE "GameId" = :'"'"'game_id'"'"'::int AND "IsEnabled" AND "Type" IN (1, 3);')"
if [[ "$container_count" -gt 0 ]]; then
    require_count 'Container runtime team instances' 2 \
        'SELECT COUNT(DISTINCT i."ParticipationId") FROM "GameInstances" i JOIN "Containers" c ON c."Id" = i."ContainerId" WHERE i."ParticipationId" IN (:'"'"'team_a'"'"'::int, :'"'"'team_b'"'"'::int) AND c."StartedAt" >= :'"'"'since_utc'"'"'::timestamptz AND i."ChallengeId" IN (SELECT "Id" FROM "GameChallenges" WHERE "GameId" = :'"'"'game_id'"'"'::int AND "IsEnabled" AND "Type" IN (1, 3));'
fi

require_count 'Cross-team risk evidence' 1 \
    'SELECT COUNT(*) FROM "SocRiskEvidence" e JOIN "SocRiskCases" r ON r."Id" = e."RiskCaseId" JOIN "Participations" a ON a."TeamId" = r."PrimaryTeamId" LEFT JOIN "Participations" b ON b."TeamId" = r."RelatedTeamId" AND b."GameId" = a."GameId" WHERE r."GameId" = :'"'"'game_id'"'"'::int AND e."OccurredAtUtc" >= :'"'"'since_utc'"'"'::timestamptz AND a."Id" IN (:'"'"'team_a'"'"'::int, :'"'"'team_b'"'"'::int) AND b."Id" IN (:'"'"'team_a'"'"'::int, :'"'"'team_b'"'"'::int);'

printf 'Synthetic-team rehearsal passed for game %s.\n' "$ZARRAR_REHEARSAL_GAME_ID"

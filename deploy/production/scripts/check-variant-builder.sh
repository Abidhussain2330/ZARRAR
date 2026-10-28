#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "$SCRIPT_DIR/common.sh"

printf 'Checking deterministic variant builder invariants...\n'
if ! compose ps --status running postgres | grep -q postgres; then
    printf 'PostgreSQL service is not running.\n' >&2
    exit 69
fi

result="$(compose exec -T postgres \
    psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -AtF '|' <<'SQL'
SELECT
    (SELECT COUNT(*) FROM (
        SELECT "TemplateId", "ParticipationId" FROM "VariantBuildJobs"
        GROUP BY "TemplateId", "ParticipationId" HAVING COUNT(*) > 1
    ) duplicates),
    (SELECT COUNT(*) FROM "VariantBuildJobs"
        WHERE "Status" = 1 AND "StartedAtUtc" < NOW() - INTERVAL '15 minutes'),
    (SELECT COUNT(*) FROM "VariantBuildJobs"
        WHERE "Status" = 2 AND ("ArtifactHash" IS NULL OR length("ArtifactHash") <> 64)),
    (SELECT COUNT(*) FROM "VariantDatasetItems" d
        JOIN "FlagContexts" f ON f."Id" = d."FlagContextId"
        WHERE (d."SelectedAtUtc" IS NULL) <> (d."SelectedParticipationId" IS NULL)
           OR (d."SelectedAtUtc" IS NOT NULL AND NOT f."IsOccupied")),
    (SELECT COUNT(*) FROM "VariantTemplates"
        WHERE "Status" = 1 AND "BuilderImageDigest" !~ '@sha256:[0-9a-f]{64}$');
SQL
)"

IFS='|' read -r duplicates stale invalid_artifacts inconsistent_dataset unpinned <<<"$result"
printf 'Duplicate jobs: %s\n' "$duplicates"
printf 'Stale running jobs: %s\n' "$stale"
printf 'Invalid successful artifacts: %s\n' "$invalid_artifacts"
printf 'Inconsistent dataset selections: %s\n' "$inconsistent_dataset"
printf 'Unpinned validated templates: %s\n' "$unpinned"

if (( duplicates > 0 || stale > 0 || invalid_artifacts > 0 || inconsistent_dataset > 0 || unpinned > 0 )); then
    printf 'Variant builder invariant check FAILED.\n' >&2
    exit 1
fi
printf 'Variant builder invariant check PASSED.\n'

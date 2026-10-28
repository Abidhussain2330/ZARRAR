#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "$SCRIPT_DIR/common.sh"

printf 'Checking durable SOC correlation invariants...\n'
if ! compose ps --status running postgres | grep -q postgres; then
    printf 'PostgreSQL service is not running.\n' >&2
    exit 69
fi

result="$(compose exec -T postgres \
    psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -AtF '|' <<'SQL'
SELECT
    (SELECT COUNT(*) FROM (
        SELECT "SubmissionId" FROM "SocDetectionJobs"
        GROUP BY "SubmissionId" HAVING COUNT(*) > 1
    ) duplicate_jobs),
    (SELECT COUNT(*) FROM "SocDetectionJobs"
        WHERE "Status" = 1 AND "StartedAtUtc" < NOW() - INTERVAL '10 minutes'),
    (SELECT COUNT(*) FROM "SocDetectionJobs" WHERE "Status" = 3),
    (SELECT COUNT(*) FROM "SocRiskCases" c
        LEFT JOIN LATERAL (
            SELECT COUNT(*)::integer AS evidence_count,
                   COUNT(DISTINCT e."Kind")::integer AS rule_count,
                   LEAST(100, COALESCE(SUM(GREATEST(0, LEAST(100, e."Weight"))), 0))::integer AS risk_score,
                   LEAST(100, COALESCE(MAX(GREATEST(0, LEAST(100, e."Confidence"))), 0)
                       + LEAST(15, GREATEST(0, COUNT(DISTINCT e."Kind") - 1) * 5))::integer AS confidence
            FROM "SocRiskEvidence" e WHERE e."RiskCaseId" = c."Id"
        ) calculated ON TRUE
        WHERE c."EvidenceCount" <> calculated.evidence_count
           OR c."RuleHitCount" <> calculated.rule_count
           OR c."RiskScore" <> calculated.risk_score
           OR c."Confidence" <> calculated.confidence),
    (SELECT COUNT(*) FROM "SocDetectionJobs" j
        JOIN "Submissions" s ON s."Id" = j."SubmissionId" AND s."Status" = 'CheatDetected'
        JOIN "CheatInfo" ci ON ci."SubmissionId" = s."Id"
        JOIN "Participations" submitter ON submitter."Id" = ci."SubmitTeamId"
        JOIN "Participations" source ON source."Id" = ci."SourceTeamId"
        LEFT JOIN "SocRiskCases" c ON c."CaseKey" =
            'pair:' || s."GameId"::text || ':' || LEAST(submitter."TeamId", source."TeamId")::text || ':' ||
            GREATEST(submitter."TeamId", source."TeamId")::text
        WHERE j."Status" = 2 AND c."Id" IS NULL),
    (SELECT COUNT(*) FROM "SocRiskEvidence" e
        LEFT JOIN "Submissions" s ON s."Id" = e."SubmissionId"
        WHERE (e."Fingerprint" IS NOT NULL AND e."Fingerprint" NOT LIKE 'fp:%')
           OR (s."Answer" IS NOT NULL AND length(s."Answer") >= 4
               AND e."MetadataJson" LIKE '%' || s."Answer" || '%'));
SQL
)"

IFS='|' read -r duplicate_jobs stale_jobs failed_jobs score_mismatches missing_foreign_cases exposed_secrets <<<"$result"
printf 'Duplicate detection jobs: %s\n' "$duplicate_jobs"
printf 'Stale running jobs: %s\n' "$stale_jobs"
printf 'Failed detection jobs: %s\n' "$failed_jobs"
printf 'Case score/count mismatches: %s\n' "$score_mismatches"
printf 'Completed foreign-flag jobs without case: %s\n' "$missing_foreign_cases"
printf 'Unsafe evidence values: %s\n' "$exposed_secrets"

if (( duplicate_jobs > 0 || stale_jobs > 0 || failed_jobs > 0 || score_mismatches > 0 || missing_foreign_cases > 0 || exposed_secrets > 0 )); then
    printf 'SOC correlation invariant check FAILED.\n' >&2
    exit 1
fi
printf 'SOC correlation invariant check PASSED.\n'

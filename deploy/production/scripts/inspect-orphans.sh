#!/usr/bin/env bash
set -euo pipefail

printf '%s\n' \
    'ZARRAR-labelled Docker challenge containers (read-only inventory):' \
    'Compare these IDs with the Administration > Containers view before any manual removal.'

docker ps -a \
    --filter 'label=ChallengeId' \
    --format 'table {{.ID}}\t{{.Names}}\t{{.Status}}\t{{.Label "TeamId"}}\t{{.Label "ChallengeId"}}'

printf '\nZARRAR challenge networks (read-only inventory):\n'
docker network ls --filter 'name=^zarrar-(open|isolated|custom)$'


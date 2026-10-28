#!/usr/bin/env bash
set -euo pipefail

printf '%s\n' \
    'BURAQ-labelled Docker challenge containers (read-only inventory):' \
    'Compare these IDs with the Administration > Containers view before any manual removal.'

docker ps -a \
    --filter 'label=ChallengeId' \
    --format 'table {{.ID}}\t{{.Names}}\t{{.Status}}\t{{.Label "TeamId"}}\t{{.Label "ChallengeId"}}'

printf '\nBURAQ challenge networks (read-only inventory):\n'
docker network ls --filter 'name=^buraq-(open|isolated|custom)$'


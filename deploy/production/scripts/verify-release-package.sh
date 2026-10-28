#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="$(cd -- "$SCRIPT_DIR/.." && pwd)"
REPO_DIR="$(cd -- "$DEPLOY_DIR/../.." && pwd)"

required_files=(
    .dockerignore
    global.json
    Directory.Packages.props
    src/BURAQ/BURAQ.csproj
    src/BURAQ/ClientApp/package.json
    src/BURAQ/ClientApp/pnpm-lock.yaml
    deploy/production/compose.yaml
    deploy/production/compose.local.yaml
    deploy/production/compose.packet-capture.yaml
    deploy/production/.env.production.example
    deploy/production/Caddyfile
    deploy/production/Caddyfile.local
    deploy/production/Dockerfile
    deploy/production/Dockerfile.minio
    deploy/production/Dockerfile.mc
    deploy/production/entrypoint.sh
    deploy/production/minio-init.sh
)

for relative in "${required_files[@]}"; do
    [[ -s "$REPO_DIR/$relative" ]] || {
        printf 'Required release asset is missing or empty: %s\n' "$relative" >&2
        exit 66
    }
done

for script in "$DEPLOY_DIR"/scripts/*.sh; do
    [[ -x "$script" ]] || {
        printf 'Operator script is not executable: %s\n' "${script#"$REPO_DIR/"}" >&2
        exit 77
    }
done

bash -n "$DEPLOY_DIR/entrypoint.sh" "$DEPLOY_DIR/minio-init.sh" "$DEPLOY_DIR"/scripts/*.sh

for dockerfile in Dockerfile Dockerfile.minio Dockerfile.mc; do
    grep -q '^FROM ' "$DEPLOY_DIR/$dockerfile" || {
        printf '%s contains no build stage.\n' "$dockerfile" >&2
        exit 65
    }
done

printf 'Release package structure and shell syntax passed.\n'

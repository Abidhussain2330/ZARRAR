#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "$SCRIPT_DIR/common.sh"

required=(
    BURAQ_DOMAIN BURAQ_PUBLIC_URL BURAQ_ALLOWED_HOSTS BURAQ_XOR_KEY BURAQ_DYNAMIC_FLAG_HMAC_KEY
    BURAQ_SHARED_GATEWAY_SIGNING_KEY BURAQ_VARIANT_SEED_KEY
    POSTGRES_PASSWORD REDIS_PASSWORD MINIO_ROOT_USER MINIO_ROOT_PASSWORD
    MINIO_ACCESS_KEY MINIO_SECRET_KEY MINIO_BUCKET
    ACME_EMAIL
)

for name in "${required[@]}"; do
    value="${!name:-}"
    [[ -n "$value" ]] || {
        printf '%s is required.\n' "$name" >&2
        exit 64
    }
    [[ "$value" != *REQUIRED* && "$value" != *REPLACE_WITH* ]] || {
        printf '%s still contains a template placeholder.\n' "$name" >&2
        exit 64
    }
done

for name in BURAQ_XOR_KEY BURAQ_DYNAMIC_FLAG_HMAC_KEY BURAQ_SHARED_GATEWAY_SIGNING_KEY BURAQ_VARIANT_SEED_KEY POSTGRES_PASSWORD REDIS_PASSWORD MINIO_ROOT_PASSWORD MINIO_SECRET_KEY; do
    value="${!name}"
    [[ ${#value} -ge 43 ]] || {
        printf '%s must contain at least 43 characters (generate 48 random bytes).\n' "$name" >&2
        exit 64
    }
done

for name in BURAQ_VARIANT_BUILDER_ENABLED BURAQ_REQUIRE_SMTP BURAQ_REQUEST_LOGGING BURAQ_ENABLE_TRAFFIC_CAPTURE; do
    value="${!name:-false}"
    [[ "$value" =~ ^(true|false)$ ]] || {
        printf '%s must be exactly true or false.\n' "$name" >&2
        exit 64
    }
done

[[ "${BURAQ_MAX_REQUEST_BODY_BYTES:-536870912}" =~ ^[0-9]+$ &&
   "${BURAQ_MAX_REQUEST_BODY_BYTES:-536870912}" -ge 1024 &&
   "${BURAQ_MAX_REQUEST_BODY_BYTES:-536870912}" -le 1073741824 ]] || {
    printf 'BURAQ_MAX_REQUEST_BODY_BYTES must be an integer from 1024 through 1073741824.\n' >&2
    exit 64
}
[[ "${BURAQ_PROXY_BODY_LIMIT:-512MB}" =~ ^[1-9][0-9]*(B|KB|MB|GB)$ ]] || {
    printf 'BURAQ_PROXY_BODY_LIMIT must be a positive Caddy byte size such as 512MB.\n' >&2
    exit 64
}

if [[ -n "${BURAQ_SHARED_GATEWAY_ALLOWED_ORIGIN:-}" ]]; then
    [[ "$BURAQ_SHARED_GATEWAY_ALLOWED_ORIGIN" == https://*/ &&
       "$BURAQ_SHARED_GATEWAY_ALLOWED_ORIGIN" != *'?'* &&
       "$BURAQ_SHARED_GATEWAY_ALLOWED_ORIGIN" != *'#'* &&
       "$BURAQ_SHARED_GATEWAY_ALLOWED_ORIGIN" != *'@'* &&
       "$BURAQ_SHARED_GATEWAY_ALLOWED_ORIGIN" != *[[:space:]]* ]] || {
        printf 'BURAQ_SHARED_GATEWAY_ALLOWED_ORIGIN must be a credential-free HTTPS root origin ending in /.\n' >&2
        exit 64
    }
fi

case "${BURAQ_VARIANT_BUILDER_ENABLED:-false}" in
    true|TRUE|1)
        [[ -n "${BURAQ_VARIANT_BUILDER_ORIGIN:-}" &&
           "$BURAQ_VARIANT_BUILDER_ORIGIN" == https://*/ &&
           "$BURAQ_VARIANT_BUILDER_ORIGIN" != *'?'* &&
           "$BURAQ_VARIANT_BUILDER_ORIGIN" != *'#'* &&
           "$BURAQ_VARIANT_BUILDER_ORIGIN" != *'@'* ]] || {
            printf 'BURAQ_VARIANT_BUILDER_ORIGIN must be an HTTPS origin ending in /.\n' >&2
            exit 64
        }
        builder_token="${BURAQ_VARIANT_BUILDER_TOKEN:-}"
        [[ ${#builder_token} -ge 32 && "$builder_token" != *REQUIRED* &&
           "$builder_token" != *REPLACE_WITH* && "$builder_token" != *[[:space:]]* ]] || {
            printf 'BURAQ_VARIANT_BUILDER_TOKEN must contain at least 32 non-whitespace, non-placeholder characters.\n' >&2
            exit 64
        }
        ;;
    false|FALSE|0) ;;
    *)
        printf 'BURAQ_VARIANT_BUILDER_ENABLED must be true or false.\n' >&2
        exit 64
        ;;
esac

builder_parallelism="${BURAQ_VARIANT_BUILDER_PARALLELISM:-2}"
[[ "$builder_parallelism" =~ ^[0-9]+$ && "$builder_parallelism" -ge 1 && "$builder_parallelism" -le 16 ]] || {
    printf 'BURAQ_VARIANT_BUILDER_PARALLELISM must be an integer from 1 through 16.\n' >&2
    exit 64
}
builder_max_bytes="${BURAQ_VARIANT_MAX_ARTIFACT_BYTES:-67108864}"
[[ "$builder_max_bytes" =~ ^[0-9]+$ && "$builder_max_bytes" -ge 1024 && "$builder_max_bytes" -le 536870912 ]] || {
    printf 'BURAQ_VARIANT_MAX_ARTIFACT_BYTES must be an integer from 1024 through 536870912.\n' >&2
    exit 64
}

[[ "$BURAQ_PUBLIC_URL" == "https://$BURAQ_DOMAIN" ]] || {
    printf 'BURAQ_PUBLIC_URL must exactly equal https://BURAQ_DOMAIN.\n' >&2
    exit 64
}
[[ "$BURAQ_DOMAIN" =~ ^[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?$ ]] || {
    printf 'BURAQ_DOMAIN must be a plain DNS host name.\n' >&2
    exit 64
}
[[ "$BURAQ_ALLOWED_HOSTS" == "$BURAQ_DOMAIN" ]] || {
    printf 'BURAQ_ALLOWED_HOSTS must exactly equal BURAQ_DOMAIN for this package.\n' >&2
    exit 64
}
[[ "$BURAQ_ALLOWED_HOSTS" != *'*'* ]] || {
    printf 'Wildcard AllowedHosts is forbidden.\n' >&2
    exit 64
}
[[ "$MINIO_ROOT_USER" != minioadmin && "$MINIO_ROOT_PASSWORD" != minioadmin ]] || {
    printf 'MinIO default credentials are forbidden.\n' >&2
    exit 64
}
[[ "$MINIO_ACCESS_KEY" != "$MINIO_ROOT_USER" ]] || {
    printf 'MINIO_ACCESS_KEY must be separate from MINIO_ROOT_USER.\n' >&2
    exit 64
}
[[ "$MINIO_BUCKET" =~ ^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$ && "$MINIO_BUCKET" != *..* ]] || {
    printf 'MINIO_BUCKET must be a lowercase 3-63 character DNS-style S3 bucket name.\n' >&2
    exit 64
}
if [[ "${BURAQ_REQUIRE_SMTP:-false}" == true ]]; then
    [[ -n "${SMTP_HOST:-}" && -n "${SMTP_PORT:-}" && -n "${SMTP_SENDER_ADDRESS:-}" ]] || {
        printf 'SMTP host, port, and sender are required when BURAQ_REQUIRE_SMTP=true.\n' >&2
        exit 64
    }
fi
case "${BURAQ_RUNTIME_PROVIDER:-Docker}" in
    Docker|docker)
        [[ "${DOCKER_GID:-}" =~ ^[0-9]+$ ]] || {
            printf 'DOCKER_GID must be numeric for the Docker provider.\n' >&2
            exit 64
        }
        [[ -S /var/run/docker.sock ]] || {
            printf '/var/run/docker.sock is not available.\n' >&2
            exit 69
        }
        socket_gid="$(stat -c '%g' /var/run/docker.sock)"
        [[ "$DOCKER_GID" == "$socket_gid" ]] || {
            printf 'DOCKER_GID=%s does not match Docker socket GID %s.\n' "$DOCKER_GID" "$socket_gid" >&2
            exit 77
        }
        ;;
    MicroVm|microvm|MICROVM)
        [[ "${BURAQ_RUNTIME_SOCKET_SOURCE:-}" == /dev/null ]] || {
            printf 'MicroVm deployments must set BURAQ_RUNTIME_SOCKET_SOURCE=/dev/null.\n' >&2
            exit 77
        }
        [[ "${BURAQ_MICROVM_ORIGIN:-}" == https://*/ &&
           "$BURAQ_MICROVM_ORIGIN" != *'?'* && "$BURAQ_MICROVM_ORIGIN" != *'#'* &&
           "$BURAQ_MICROVM_ORIGIN" != *'@'* ]] || {
            printf 'BURAQ_MICROVM_ORIGIN must be a credential-free HTTPS origin ending in /.\n' >&2
            exit 64
        }
        microvm_token="${BURAQ_MICROVM_TOKEN:-}"
        [[ ${#microvm_token} -ge 32 && "$microvm_token" != *REQUIRED* && "$microvm_token" != *REPLACE_WITH* &&
           "$microvm_token" != *[[:space:]]* ]] || {
            printf 'BURAQ_MICROVM_TOKEN must contain at least 32 characters.\n' >&2
            exit 64
        }
        [[ "${BURAQ_MICROVM_NODE:-microvm-primary}" =~ ^[A-Za-z0-9._:-]{1,80}$ ]] || {
            printf 'BURAQ_MICROVM_NODE contains unsafe characters.\n' >&2
            exit 64
        }
        [[ "${BURAQ_MICROVM_HEALTH_PATH:-v1/health}" =~ ^[A-Za-z0-9._/-]{1,128}$ &&
           "${BURAQ_MICROVM_HEALTH_PATH:-v1/health}" != *..* ]] || {
            printf 'BURAQ_MICROVM_HEALTH_PATH must be a safe relative path.\n' >&2
            exit 64
        }
        microvm_timeout="${BURAQ_MICROVM_TIMEOUT_SECONDS:-30}"
        [[ "$microvm_timeout" =~ ^[0-9]+$ && "$microvm_timeout" -ge 5 && "$microvm_timeout" -le 120 ]] || {
            printf 'BURAQ_MICROVM_TIMEOUT_SECONDS must be an integer from 5 through 120.\n' >&2
            exit 64
        }
        ;;
    *)
        printf 'BURAQ_RUNTIME_PROVIDER must be Docker or MicroVm.\n' >&2
        exit 64
        ;;
esac

[[ "${BURAQ_RUNTIME_MAX_INSTANCES:-0}" =~ ^[0-9]+$ ]] || {
    printf 'BURAQ_RUNTIME_MAX_INSTANCES must be a non-negative integer.\n' >&2
    exit 64
}

mode="$(stat -c '%a' "$ENV_FILE")"
[[ "$mode" == 600 || "$mode" == 400 ]] || {
    printf 'Environment file permissions are %s; set them to 0600.\n' "$mode" >&2
    exit 77
}

compose config --quiet

if [[ "${BURAQ_ENABLE_TRAFFIC_CAPTURE:-false}" == true ]]; then
    printf '%s\n' \
        'Traffic capture is enabled. Start with compose.packet-capture.yaml so only BURAQ receives NET_RAW/NET_ADMIN.'
fi

printf 'Production configuration preflight passed.\n'

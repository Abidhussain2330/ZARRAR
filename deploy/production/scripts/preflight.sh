#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "$SCRIPT_DIR/common.sh"

required=(
    ZARRAR_DOMAIN ZARRAR_PUBLIC_URL ZARRAR_ALLOWED_HOSTS ZARRAR_XOR_KEY ZARRAR_DYNAMIC_FLAG_HMAC_KEY
    ZARRAR_SHARED_GATEWAY_SIGNING_KEY ZARRAR_VARIANT_SEED_KEY
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

for name in ZARRAR_XOR_KEY ZARRAR_DYNAMIC_FLAG_HMAC_KEY ZARRAR_SHARED_GATEWAY_SIGNING_KEY ZARRAR_VARIANT_SEED_KEY POSTGRES_PASSWORD REDIS_PASSWORD MINIO_ROOT_PASSWORD MINIO_SECRET_KEY; do
    value="${!name}"
    [[ ${#value} -ge 43 ]] || {
        printf '%s must contain at least 43 characters (generate 48 random bytes).\n' "$name" >&2
        exit 64
    }
done

for name in ZARRAR_VARIANT_BUILDER_ENABLED ZARRAR_REQUIRE_SMTP ZARRAR_REQUEST_LOGGING ZARRAR_ENABLE_TRAFFIC_CAPTURE; do
    value="${!name:-false}"
    [[ "$value" =~ ^(true|false)$ ]] || {
        printf '%s must be exactly true or false.\n' "$name" >&2
        exit 64
    }
done

[[ "${ZARRAR_MAX_REQUEST_BODY_BYTES:-536870912}" =~ ^[0-9]+$ &&
   "${ZARRAR_MAX_REQUEST_BODY_BYTES:-536870912}" -ge 1024 &&
   "${ZARRAR_MAX_REQUEST_BODY_BYTES:-536870912}" -le 1073741824 ]] || {
    printf 'ZARRAR_MAX_REQUEST_BODY_BYTES must be an integer from 1024 through 1073741824.\n' >&2
    exit 64
}
[[ "${ZARRAR_PROXY_BODY_LIMIT:-512MB}" =~ ^[1-9][0-9]*(B|KB|MB|GB)$ ]] || {
    printf 'ZARRAR_PROXY_BODY_LIMIT must be a positive Caddy byte size such as 512MB.\n' >&2
    exit 64
}

if [[ -n "${ZARRAR_SHARED_GATEWAY_ALLOWED_ORIGIN:-}" ]]; then
    [[ "$ZARRAR_SHARED_GATEWAY_ALLOWED_ORIGIN" == https://*/ &&
       "$ZARRAR_SHARED_GATEWAY_ALLOWED_ORIGIN" != *'?'* &&
       "$ZARRAR_SHARED_GATEWAY_ALLOWED_ORIGIN" != *'#'* &&
       "$ZARRAR_SHARED_GATEWAY_ALLOWED_ORIGIN" != *'@'* &&
       "$ZARRAR_SHARED_GATEWAY_ALLOWED_ORIGIN" != *[[:space:]]* ]] || {
        printf 'ZARRAR_SHARED_GATEWAY_ALLOWED_ORIGIN must be a credential-free HTTPS root origin ending in /.\n' >&2
        exit 64
    }
fi

case "${ZARRAR_VARIANT_BUILDER_ENABLED:-false}" in
    true|TRUE|1)
        [[ -n "${ZARRAR_VARIANT_BUILDER_ORIGIN:-}" &&
           "$ZARRAR_VARIANT_BUILDER_ORIGIN" == https://*/ &&
           "$ZARRAR_VARIANT_BUILDER_ORIGIN" != *'?'* &&
           "$ZARRAR_VARIANT_BUILDER_ORIGIN" != *'#'* &&
           "$ZARRAR_VARIANT_BUILDER_ORIGIN" != *'@'* ]] || {
            printf 'ZARRAR_VARIANT_BUILDER_ORIGIN must be an HTTPS origin ending in /.\n' >&2
            exit 64
        }
        builder_token="${ZARRAR_VARIANT_BUILDER_TOKEN:-}"
        [[ ${#builder_token} -ge 32 && "$builder_token" != *REQUIRED* &&
           "$builder_token" != *REPLACE_WITH* && "$builder_token" != *[[:space:]]* ]] || {
            printf 'ZARRAR_VARIANT_BUILDER_TOKEN must contain at least 32 non-whitespace, non-placeholder characters.\n' >&2
            exit 64
        }
        ;;
    false|FALSE|0) ;;
    *)
        printf 'ZARRAR_VARIANT_BUILDER_ENABLED must be true or false.\n' >&2
        exit 64
        ;;
esac

builder_parallelism="${ZARRAR_VARIANT_BUILDER_PARALLELISM:-2}"
[[ "$builder_parallelism" =~ ^[0-9]+$ && "$builder_parallelism" -ge 1 && "$builder_parallelism" -le 16 ]] || {
    printf 'ZARRAR_VARIANT_BUILDER_PARALLELISM must be an integer from 1 through 16.\n' >&2
    exit 64
}
builder_max_bytes="${ZARRAR_VARIANT_MAX_ARTIFACT_BYTES:-67108864}"
[[ "$builder_max_bytes" =~ ^[0-9]+$ && "$builder_max_bytes" -ge 1024 && "$builder_max_bytes" -le 536870912 ]] || {
    printf 'ZARRAR_VARIANT_MAX_ARTIFACT_BYTES must be an integer from 1024 through 536870912.\n' >&2
    exit 64
}

[[ "$ZARRAR_PUBLIC_URL" == "https://$ZARRAR_DOMAIN" ]] || {
    printf 'ZARRAR_PUBLIC_URL must exactly equal https://ZARRAR_DOMAIN.\n' >&2
    exit 64
}
[[ "$ZARRAR_DOMAIN" =~ ^[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?$ ]] || {
    printf 'ZARRAR_DOMAIN must be a plain DNS host name.\n' >&2
    exit 64
}
[[ "$ZARRAR_ALLOWED_HOSTS" == "$ZARRAR_DOMAIN" ]] || {
    printf 'ZARRAR_ALLOWED_HOSTS must exactly equal ZARRAR_DOMAIN for this package.\n' >&2
    exit 64
}
[[ "$ZARRAR_ALLOWED_HOSTS" != *'*'* ]] || {
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
if [[ "${ZARRAR_REQUIRE_SMTP:-false}" == true ]]; then
    [[ -n "${SMTP_HOST:-}" && -n "${SMTP_PORT:-}" && -n "${SMTP_SENDER_ADDRESS:-}" ]] || {
        printf 'SMTP host, port, and sender are required when ZARRAR_REQUIRE_SMTP=true.\n' >&2
        exit 64
    }
fi
case "${ZARRAR_RUNTIME_PROVIDER:-Docker}" in
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
        [[ "${ZARRAR_RUNTIME_SOCKET_SOURCE:-}" == /dev/null ]] || {
            printf 'MicroVm deployments must set ZARRAR_RUNTIME_SOCKET_SOURCE=/dev/null.\n' >&2
            exit 77
        }
        [[ "${ZARRAR_MICROVM_ORIGIN:-}" == https://*/ &&
           "$ZARRAR_MICROVM_ORIGIN" != *'?'* && "$ZARRAR_MICROVM_ORIGIN" != *'#'* &&
           "$ZARRAR_MICROVM_ORIGIN" != *'@'* ]] || {
            printf 'ZARRAR_MICROVM_ORIGIN must be a credential-free HTTPS origin ending in /.\n' >&2
            exit 64
        }
        microvm_token="${ZARRAR_MICROVM_TOKEN:-}"
        [[ ${#microvm_token} -ge 32 && "$microvm_token" != *REQUIRED* && "$microvm_token" != *REPLACE_WITH* &&
           "$microvm_token" != *[[:space:]]* ]] || {
            printf 'ZARRAR_MICROVM_TOKEN must contain at least 32 characters.\n' >&2
            exit 64
        }
        [[ "${ZARRAR_MICROVM_NODE:-microvm-primary}" =~ ^[A-Za-z0-9._:-]{1,80}$ ]] || {
            printf 'ZARRAR_MICROVM_NODE contains unsafe characters.\n' >&2
            exit 64
        }
        [[ "${ZARRAR_MICROVM_HEALTH_PATH:-v1/health}" =~ ^[A-Za-z0-9._/-]{1,128}$ &&
           "${ZARRAR_MICROVM_HEALTH_PATH:-v1/health}" != *..* ]] || {
            printf 'ZARRAR_MICROVM_HEALTH_PATH must be a safe relative path.\n' >&2
            exit 64
        }
        microvm_timeout="${ZARRAR_MICROVM_TIMEOUT_SECONDS:-30}"
        [[ "$microvm_timeout" =~ ^[0-9]+$ && "$microvm_timeout" -ge 5 && "$microvm_timeout" -le 120 ]] || {
            printf 'ZARRAR_MICROVM_TIMEOUT_SECONDS must be an integer from 5 through 120.\n' >&2
            exit 64
        }
        ;;
    *)
        printf 'ZARRAR_RUNTIME_PROVIDER must be Docker or MicroVm.\n' >&2
        exit 64
        ;;
esac

[[ "${ZARRAR_RUNTIME_MAX_INSTANCES:-0}" =~ ^[0-9]+$ ]] || {
    printf 'ZARRAR_RUNTIME_MAX_INSTANCES must be a non-negative integer.\n' >&2
    exit 64
}

mode="$(stat -c '%a' "$ENV_FILE")"
[[ "$mode" == 600 || "$mode" == 400 ]] || {
    printf 'Environment file permissions are %s; set them to 0600.\n' "$mode" >&2
    exit 77
}

compose config --quiet

if [[ "${ZARRAR_ENABLE_TRAFFIC_CAPTURE:-false}" == true ]]; then
    printf '%s\n' \
        'Traffic capture is enabled. Start with compose.packet-capture.yaml so only ZARRAR receives NET_RAW/NET_ADMIN.'
fi

printf 'Production configuration preflight passed.\n'

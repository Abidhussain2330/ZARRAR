#!/bin/sh
set -eu

fatal() {
    printf '%s\n' "[ZARRAR] FATAL: $*" >&2
    exit 64
}

require_var() {
    value="$(printenv "$1" 2>/dev/null || true)"
    [ -n "$value" ] || fatal "$1 is required"
}

require_length() {
    value="$(printenv "$1" 2>/dev/null || true)"
    [ "${#value}" -ge "$2" ] || fatal "$1 must contain at least $2 characters"
}

require_integer_range() {
    value="$(printenv "$1" 2>/dev/null || true)"
    case "$value" in
        ''|*[!0-9]*) fatal "$1 must be an integer from $2 through $3" ;;
    esac
    [ "$value" -ge "$2" ] && [ "$value" -le "$3" ] || \
        fatal "$1 must be an integer from $2 through $3"
}

for name in ZARRAR_PUBLIC_URL ZARRAR_ALLOWED_HOSTS ZARRAR_XOR_KEY ZARRAR_DYNAMIC_FLAG_HMAC_KEY \
    ZARRAR_SHARED_GATEWAY_SIGNING_KEY \
    ZARRAR_VARIANT_SEED_KEY \
    POSTGRES_DB POSTGRES_USER POSTGRES_PASSWORD REDIS_PASSWORD \
    MINIO_ACCESS_KEY MINIO_SECRET_KEY MINIO_BUCKET ZARRAR_PROXY_IP; do
    require_var "$name"
done

require_length ZARRAR_XOR_KEY 43
require_length ZARRAR_DYNAMIC_FLAG_HMAC_KEY 43
require_length ZARRAR_SHARED_GATEWAY_SIGNING_KEY 43
require_length ZARRAR_VARIANT_SEED_KEY 43
require_length POSTGRES_PASSWORD 43
require_length REDIS_PASSWORD 43
require_length MINIO_SECRET_KEY 43

for name in ZARRAR_XOR_KEY ZARRAR_DYNAMIC_FLAG_HMAC_KEY ZARRAR_SHARED_GATEWAY_SIGNING_KEY ZARRAR_VARIANT_SEED_KEY POSTGRES_PASSWORD REDIS_PASSWORD MINIO_ACCESS_KEY MINIO_SECRET_KEY; do
    value="$(printenv "$name" 2>/dev/null || true)"
    case "$value" in
        *REQUIRED*|*REPLACE_WITH*) fatal "$name still contains a template placeholder" ;;
    esac
done

case "$ZARRAR_PUBLIC_URL" in
    https://*) ;;
    *) fatal "ZARRAR_PUBLIC_URL must be an https:// origin" ;;
esac

case "$ZARRAR_PUBLIC_URL" in
    *"?"*|*"#"*) fatal "ZARRAR_PUBLIC_URL must not contain a query or fragment" ;;
esac

case "$ZARRAR_ALLOWED_HOSTS" in
    *"*"*|*" "*|"") fatal "ZARRAR_ALLOWED_HOSTS must contain explicit host names and no wildcard" ;;
esac

case "${SMTP_TLS_BYPASS:-false}" in
    true|TRUE|1) fatal "SMTP certificate validation cannot be bypassed in Production" ;;
esac

case "${ZARRAR_MAX_REQUEST_BODY_BYTES:-536870912}" in
    ''|*[!0-9]*) fatal "ZARRAR_MAX_REQUEST_BODY_BYTES must be numeric" ;;
esac
require_integer_range ZARRAR_MAX_REQUEST_BODY_BYTES 1024 1073741824

if [ -n "${ZARRAR_SHARED_GATEWAY_ALLOWED_ORIGIN:-}" ]; then
    case "$ZARRAR_SHARED_GATEWAY_ALLOWED_ORIGIN" in
        https://*/) ;;
        *) fatal "ZARRAR_SHARED_GATEWAY_ALLOWED_ORIGIN must be an https:// root origin ending in /" ;;
    esac
    case "$ZARRAR_SHARED_GATEWAY_ALLOWED_ORIGIN" in
        *"?"*|*"#"*|*"@"*|*[[:space:]]*) fatal "ZARRAR_SHARED_GATEWAY_ALLOWED_ORIGIN contains unsafe origin characters" ;;
    esac
fi

if [ -n "${ZARRAR_BOOTSTRAP_ADMIN_PASSWORD:-}" ]; then
    [ "${#ZARRAR_BOOTSTRAP_ADMIN_PASSWORD}" -ge 14 ] || \
        fatal "ZARRAR_BOOTSTRAP_ADMIN_PASSWORD must contain at least 14 characters"
    export ADMIN_PASSWORD="$ZARRAR_BOOTSTRAP_ADMIN_PASSWORD"
else
    unset ADMIN_PASSWORD 2>/dev/null || true
fi

export ZARRAR_XorKey="$ZARRAR_XOR_KEY"
export ZARRAR_DynamicFlagHmacKey="$ZARRAR_DYNAMIC_FLAG_HMAC_KEY"
export ZARRAR_SharedGatewayOptions__SigningKey="$ZARRAR_SHARED_GATEWAY_SIGNING_KEY"
if [ -n "${ZARRAR_SHARED_GATEWAY_ALLOWED_ORIGIN:-}" ]; then
    export ZARRAR_SharedGatewayOptions__AllowedOrigins__0="$ZARRAR_SHARED_GATEWAY_ALLOWED_ORIGIN"
fi
export ZARRAR_VariantBuilderOptions__SeedKey="$ZARRAR_VARIANT_SEED_KEY"
export ZARRAR_VariantBuilderOptions__Enabled="${ZARRAR_VARIANT_BUILDER_ENABLED:-false}"
export ZARRAR_VariantBuilderOptions__Parallelism="${ZARRAR_VARIANT_BUILDER_PARALLELISM:-2}"
export ZARRAR_VariantBuilderOptions__MaxArtifactBytes="${ZARRAR_VARIANT_MAX_ARTIFACT_BYTES:-67108864}"
require_integer_range ZARRAR_VARIANT_BUILDER_PARALLELISM 1 16
require_integer_range ZARRAR_VARIANT_MAX_ARTIFACT_BYTES 1024 536870912
case "${ZARRAR_VARIANT_BUILDER_ENABLED:-false}" in
    true|TRUE|1)
        require_var ZARRAR_VARIANT_BUILDER_ORIGIN
        require_var ZARRAR_VARIANT_BUILDER_TOKEN
        require_length ZARRAR_VARIANT_BUILDER_TOKEN 32
        case "$ZARRAR_VARIANT_BUILDER_TOKEN" in
            *REQUIRED*|*REPLACE_WITH*|*[[:space:]]*) fatal "ZARRAR_VARIANT_BUILDER_TOKEN contains a placeholder or whitespace" ;;
        esac
        case "$ZARRAR_VARIANT_BUILDER_ORIGIN" in
            https://*/) ;;
            *) fatal "ZARRAR_VARIANT_BUILDER_ORIGIN must be an https:// origin ending in /" ;;
        esac
        case "$ZARRAR_VARIANT_BUILDER_ORIGIN" in
            *"?"*|*"#"*|*"@"*) fatal "ZARRAR_VARIANT_BUILDER_ORIGIN must not contain credentials, query or fragment" ;;
        esac
        export ZARRAR_VariantBuilderOptions__ControlPlaneOrigin="$ZARRAR_VARIANT_BUILDER_ORIGIN"
        export ZARRAR_VariantBuilderOptions__ApiToken="$ZARRAR_VARIANT_BUILDER_TOKEN"
        ;;
    false|FALSE|0) ;;
    *) fatal "ZARRAR_VARIANT_BUILDER_ENABLED must be true or false" ;;
esac
export ZARRAR_PublicUrl="$ZARRAR_PUBLIC_URL"
export ZARRAR_AllowedHosts="$ZARRAR_ALLOWED_HOSTS"
export ZARRAR_ConnectionStrings__Database="Host=postgres;Port=5432;Database=$POSTGRES_DB;Username=$POSTGRES_USER;Password=$POSTGRES_PASSWORD;Pooling=true;SSL Mode=Disable"
export ZARRAR_ConnectionStrings__redis="redis:6379,password=$REDIS_PASSWORD,abortConnect=false"
export ZARRAR_ConnectionStrings__Storage="minio.s3://bucket=$MINIO_BUCKET;endpoint=http://minio:9000;accessKey=$MINIO_ACCESS_KEY;secretKey=$MINIO_SECRET_KEY;useHttp=true;forcePathStyle=true"
export ZARRAR_ForwardedOptions__ForwardedHeaders="XForwardedFor,XForwardedProto"
export ZARRAR_ForwardedOptions__ForwardLimit="1"
export ZARRAR_ForwardedOptions__KnownProxies__0="$ZARRAR_PROXY_IP"
export ZARRAR_MetricsBindAddress="loopback"
export ZARRAR_DisableRateLimit="false"
export ZARRAR_Kestrel__Limits__MaxRequestBodySize="${ZARRAR_MAX_REQUEST_BODY_BYTES:-536870912}"
export ZARRAR_RequestLogging="${ZARRAR_REQUEST_LOGGING:-false}"
export ZARRAR_ContainerProvider__PortMappingType="${ZARRAR_CONTAINER_PORT_MAPPING:-PlatformProxy}"
export ZARRAR_ContainerProvider__PublicEntry="${ZARRAR_CONTAINER_PUBLIC_ENTRY:-$ZARRAR_ALLOWED_HOSTS}"
export ZARRAR_ContainerProvider__DefaultNetworkMode="Isolated"
export ZARRAR_ContainerProvider__EnableTrafficCapture="${ZARRAR_ENABLE_TRAFFIC_CAPTURE:-false}"
export ZARRAR_ContainerProvider__MaxConcurrentInstances="${ZARRAR_RUNTIME_MAX_INSTANCES:-0}"
case "${ZARRAR_RUNTIME_MAX_INSTANCES:-0}" in
    ''|*[!0-9]*) fatal "ZARRAR_RUNTIME_MAX_INSTANCES must be a non-negative integer" ;;
esac
case "${ZARRAR_RUNTIME_PROVIDER:-Docker}" in
    Docker|docker)
        export ZARRAR_ContainerProvider__Type="Docker"
        # ZARRAR_DOCKER_URI lets the app reach Docker through a filtering socket proxy
        # (compose.socket-proxy.yaml) instead of the raw socket.
        export ZARRAR_ContainerProvider__DockerConfig__Uri="${ZARRAR_DOCKER_URI:-unix:///var/run/docker.sock}"
        export ZARRAR_ContainerProvider__DockerConfig__ChallengeNetwork="${ZARRAR_CHALLENGE_NETWORK_PREFIX:-zarrar}"
        ;;
    MicroVm|microvm|MICROVM)
        require_var ZARRAR_MICROVM_ORIGIN
        require_var ZARRAR_MICROVM_TOKEN
        require_length ZARRAR_MICROVM_TOKEN 32
        case "$ZARRAR_MICROVM_TOKEN" in
            *REQUIRED*|*REPLACE_WITH*) fatal "ZARRAR_MICROVM_TOKEN still contains a template placeholder" ;;
        esac
        case "$ZARRAR_MICROVM_ORIGIN" in
            https://*/) ;;
            *) fatal "ZARRAR_MICROVM_ORIGIN must be an https:// origin ending in /" ;;
        esac
        case "$ZARRAR_MICROVM_ORIGIN" in
            *"?"*|*"#"*|*"@"*) fatal "ZARRAR_MICROVM_ORIGIN must not contain credentials, query or fragment" ;;
        esac
        export ZARRAR_ContainerProvider__Type="MicroVm"
        export ZARRAR_ContainerProvider__MicroVmConfig__ControlPlaneOrigin="$ZARRAR_MICROVM_ORIGIN"
        export ZARRAR_ContainerProvider__MicroVmConfig__ApiToken="$ZARRAR_MICROVM_TOKEN"
        export ZARRAR_ContainerProvider__MicroVmConfig__NodeName="${ZARRAR_MICROVM_NODE:-microvm-primary}"
        export ZARRAR_ContainerProvider__MicroVmConfig__HealthPath="${ZARRAR_MICROVM_HEALTH_PATH:-v1/health}"
        export ZARRAR_ContainerProvider__MicroVmConfig__RequestTimeoutSeconds="${ZARRAR_MICROVM_TIMEOUT_SECONDS:-30}"
        ;;
    *) fatal "ZARRAR_RUNTIME_PROVIDER must be Docker or MicroVm" ;;
esac
export ZARRAR_ContainerProvider__Sandbox__Enabled="true"
export ZARRAR_ContainerProvider__Sandbox__DropCapabilities__0="ALL"
export ZARRAR_ContainerProvider__Sandbox__AddCapabilities__0="CHOWN"
export ZARRAR_ContainerProvider__Sandbox__AddCapabilities__1="DAC_OVERRIDE"
export ZARRAR_ContainerProvider__Sandbox__AddCapabilities__2="FOWNER"
export ZARRAR_ContainerProvider__Sandbox__AddCapabilities__3="SETGID"
export ZARRAR_ContainerProvider__Sandbox__AddCapabilities__4="SETUID"
export ZARRAR_ContainerProvider__Sandbox__AddCapabilities__5="KILL"
export ZARRAR_ContainerProvider__Sandbox__AddCapabilities__6="NET_BIND_SERVICE"
export ZARRAR_ContainerProvider__Sandbox__PidsLimit="${ZARRAR_CHALLENGE_PIDS_LIMIT:-512}"
export ZARRAR_ContainerProvider__Sandbox__SeccompProfile="RuntimeDefault"

case "${ZARRAR_REQUIRE_SMTP:-false}" in
    true|TRUE|1)
        require_var SMTP_HOST
        require_var SMTP_SENDER_ADDRESS
        ;;
esac

if [ -n "${SMTP_HOST:-}" ]; then
    require_var SMTP_SENDER_ADDRESS
    require_var SMTP_PORT
    export ZARRAR_EmailConfig__SenderAddress="$SMTP_SENDER_ADDRESS"
    export ZARRAR_EmailConfig__SenderName="${SMTP_SENDER_NAME:-ZARRAR}"
    export ZARRAR_EmailConfig__Smtp__Host="$SMTP_HOST"
    export ZARRAR_EmailConfig__Smtp__Port="$SMTP_PORT"
    export ZARRAR_EmailConfig__Smtp__BypassCertVerify="false"

    if [ -n "${SMTP_USERNAME:-}" ] || [ -n "${SMTP_PASSWORD:-}" ]; then
        require_var SMTP_USERNAME
        require_var SMTP_PASSWORD
        export ZARRAR_EmailConfig__UserName="$SMTP_USERNAME"
        export ZARRAR_EmailConfig__Password="$SMTP_PASSWORD"
    fi
fi

exec dotnet ZARRAR.dll

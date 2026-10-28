#!/bin/sh
set -eu

fatal() {
    printf '%s\n' "[BURAQ] FATAL: $*" >&2
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

for name in BURAQ_PUBLIC_URL BURAQ_ALLOWED_HOSTS BURAQ_XOR_KEY BURAQ_DYNAMIC_FLAG_HMAC_KEY \
    BURAQ_SHARED_GATEWAY_SIGNING_KEY \
    BURAQ_VARIANT_SEED_KEY \
    POSTGRES_DB POSTGRES_USER POSTGRES_PASSWORD REDIS_PASSWORD \
    MINIO_ACCESS_KEY MINIO_SECRET_KEY MINIO_BUCKET BURAQ_PROXY_IP; do
    require_var "$name"
done

require_length BURAQ_XOR_KEY 43
require_length BURAQ_DYNAMIC_FLAG_HMAC_KEY 43
require_length BURAQ_SHARED_GATEWAY_SIGNING_KEY 43
require_length BURAQ_VARIANT_SEED_KEY 43
require_length POSTGRES_PASSWORD 43
require_length REDIS_PASSWORD 43
require_length MINIO_SECRET_KEY 43

for name in BURAQ_XOR_KEY BURAQ_DYNAMIC_FLAG_HMAC_KEY BURAQ_SHARED_GATEWAY_SIGNING_KEY BURAQ_VARIANT_SEED_KEY POSTGRES_PASSWORD REDIS_PASSWORD MINIO_ACCESS_KEY MINIO_SECRET_KEY; do
    value="$(printenv "$name" 2>/dev/null || true)"
    case "$value" in
        *REQUIRED*|*REPLACE_WITH*) fatal "$name still contains a template placeholder" ;;
    esac
done

case "$BURAQ_PUBLIC_URL" in
    https://*) ;;
    *) fatal "BURAQ_PUBLIC_URL must be an https:// origin" ;;
esac

case "$BURAQ_PUBLIC_URL" in
    *"?"*|*"#"*) fatal "BURAQ_PUBLIC_URL must not contain a query or fragment" ;;
esac

case "$BURAQ_ALLOWED_HOSTS" in
    *"*"*|*" "*|"") fatal "BURAQ_ALLOWED_HOSTS must contain explicit host names and no wildcard" ;;
esac

case "${SMTP_TLS_BYPASS:-false}" in
    true|TRUE|1) fatal "SMTP certificate validation cannot be bypassed in Production" ;;
esac

case "${BURAQ_MAX_REQUEST_BODY_BYTES:-536870912}" in
    ''|*[!0-9]*) fatal "BURAQ_MAX_REQUEST_BODY_BYTES must be numeric" ;;
esac
require_integer_range BURAQ_MAX_REQUEST_BODY_BYTES 1024 1073741824

if [ -n "${BURAQ_SHARED_GATEWAY_ALLOWED_ORIGIN:-}" ]; then
    case "$BURAQ_SHARED_GATEWAY_ALLOWED_ORIGIN" in
        https://*/) ;;
        *) fatal "BURAQ_SHARED_GATEWAY_ALLOWED_ORIGIN must be an https:// root origin ending in /" ;;
    esac
    case "$BURAQ_SHARED_GATEWAY_ALLOWED_ORIGIN" in
        *"?"*|*"#"*|*"@"*|*[[:space:]]*) fatal "BURAQ_SHARED_GATEWAY_ALLOWED_ORIGIN contains unsafe origin characters" ;;
    esac
fi

if [ -n "${BURAQ_BOOTSTRAP_ADMIN_PASSWORD:-}" ]; then
    [ "${#BURAQ_BOOTSTRAP_ADMIN_PASSWORD}" -ge 14 ] || \
        fatal "BURAQ_BOOTSTRAP_ADMIN_PASSWORD must contain at least 14 characters"
    export ADMIN_PASSWORD="$BURAQ_BOOTSTRAP_ADMIN_PASSWORD"
else
    unset ADMIN_PASSWORD 2>/dev/null || true
fi

export BURAQ_XorKey="$BURAQ_XOR_KEY"
export BURAQ_DynamicFlagHmacKey="$BURAQ_DYNAMIC_FLAG_HMAC_KEY"
export BURAQ_SharedGatewayOptions__SigningKey="$BURAQ_SHARED_GATEWAY_SIGNING_KEY"
if [ -n "${BURAQ_SHARED_GATEWAY_ALLOWED_ORIGIN:-}" ]; then
    export BURAQ_SharedGatewayOptions__AllowedOrigins__0="$BURAQ_SHARED_GATEWAY_ALLOWED_ORIGIN"
fi
export BURAQ_VariantBuilderOptions__SeedKey="$BURAQ_VARIANT_SEED_KEY"
export BURAQ_VariantBuilderOptions__Enabled="${BURAQ_VARIANT_BUILDER_ENABLED:-false}"
export BURAQ_VariantBuilderOptions__Parallelism="${BURAQ_VARIANT_BUILDER_PARALLELISM:-2}"
export BURAQ_VariantBuilderOptions__MaxArtifactBytes="${BURAQ_VARIANT_MAX_ARTIFACT_BYTES:-67108864}"
require_integer_range BURAQ_VARIANT_BUILDER_PARALLELISM 1 16
require_integer_range BURAQ_VARIANT_MAX_ARTIFACT_BYTES 1024 536870912
case "${BURAQ_VARIANT_BUILDER_ENABLED:-false}" in
    true|TRUE|1)
        require_var BURAQ_VARIANT_BUILDER_ORIGIN
        require_var BURAQ_VARIANT_BUILDER_TOKEN
        require_length BURAQ_VARIANT_BUILDER_TOKEN 32
        case "$BURAQ_VARIANT_BUILDER_TOKEN" in
            *REQUIRED*|*REPLACE_WITH*|*[[:space:]]*) fatal "BURAQ_VARIANT_BUILDER_TOKEN contains a placeholder or whitespace" ;;
        esac
        case "$BURAQ_VARIANT_BUILDER_ORIGIN" in
            https://*/) ;;
            *) fatal "BURAQ_VARIANT_BUILDER_ORIGIN must be an https:// origin ending in /" ;;
        esac
        case "$BURAQ_VARIANT_BUILDER_ORIGIN" in
            *"?"*|*"#"*|*"@"*) fatal "BURAQ_VARIANT_BUILDER_ORIGIN must not contain credentials, query or fragment" ;;
        esac
        export BURAQ_VariantBuilderOptions__ControlPlaneOrigin="$BURAQ_VARIANT_BUILDER_ORIGIN"
        export BURAQ_VariantBuilderOptions__ApiToken="$BURAQ_VARIANT_BUILDER_TOKEN"
        ;;
    false|FALSE|0) ;;
    *) fatal "BURAQ_VARIANT_BUILDER_ENABLED must be true or false" ;;
esac
export BURAQ_PublicUrl="$BURAQ_PUBLIC_URL"
export BURAQ_AllowedHosts="$BURAQ_ALLOWED_HOSTS"
export BURAQ_ConnectionStrings__Database="Host=postgres;Port=5432;Database=$POSTGRES_DB;Username=$POSTGRES_USER;Password=$POSTGRES_PASSWORD;Pooling=true;SSL Mode=Disable"
export BURAQ_ConnectionStrings__redis="redis:6379,password=$REDIS_PASSWORD,abortConnect=false"
export BURAQ_ConnectionStrings__Storage="minio.s3://bucket=$MINIO_BUCKET;endpoint=http://minio:9000;accessKey=$MINIO_ACCESS_KEY;secretKey=$MINIO_SECRET_KEY;useHttp=true;forcePathStyle=true"
export BURAQ_ForwardedOptions__ForwardedHeaders="XForwardedFor,XForwardedProto"
export BURAQ_ForwardedOptions__ForwardLimit="1"
export BURAQ_ForwardedOptions__KnownProxies__0="$BURAQ_PROXY_IP"
export BURAQ_MetricsBindAddress="loopback"
export BURAQ_DisableRateLimit="false"
export BURAQ_Kestrel__Limits__MaxRequestBodySize="${BURAQ_MAX_REQUEST_BODY_BYTES:-536870912}"
export BURAQ_RequestLogging="${BURAQ_REQUEST_LOGGING:-false}"
export BURAQ_ContainerProvider__PortMappingType="${BURAQ_CONTAINER_PORT_MAPPING:-PlatformProxy}"
export BURAQ_ContainerProvider__PublicEntry="${BURAQ_CONTAINER_PUBLIC_ENTRY:-$BURAQ_ALLOWED_HOSTS}"
export BURAQ_ContainerProvider__DefaultNetworkMode="Isolated"
export BURAQ_ContainerProvider__EnableTrafficCapture="${BURAQ_ENABLE_TRAFFIC_CAPTURE:-false}"
export BURAQ_ContainerProvider__MaxConcurrentInstances="${BURAQ_RUNTIME_MAX_INSTANCES:-0}"
case "${BURAQ_RUNTIME_MAX_INSTANCES:-0}" in
    ''|*[!0-9]*) fatal "BURAQ_RUNTIME_MAX_INSTANCES must be a non-negative integer" ;;
esac
case "${BURAQ_RUNTIME_PROVIDER:-Docker}" in
    Docker|docker)
        export BURAQ_ContainerProvider__Type="Docker"
        # BURAQ_DOCKER_URI lets the app reach Docker through a filtering socket proxy
        # (compose.socket-proxy.yaml) instead of the raw socket.
        export BURAQ_ContainerProvider__DockerConfig__Uri="${BURAQ_DOCKER_URI:-unix:///var/run/docker.sock}"
        export BURAQ_ContainerProvider__DockerConfig__ChallengeNetwork="${BURAQ_CHALLENGE_NETWORK_PREFIX:-buraq}"
        ;;
    MicroVm|microvm|MICROVM)
        require_var BURAQ_MICROVM_ORIGIN
        require_var BURAQ_MICROVM_TOKEN
        require_length BURAQ_MICROVM_TOKEN 32
        case "$BURAQ_MICROVM_TOKEN" in
            *REQUIRED*|*REPLACE_WITH*) fatal "BURAQ_MICROVM_TOKEN still contains a template placeholder" ;;
        esac
        case "$BURAQ_MICROVM_ORIGIN" in
            https://*/) ;;
            *) fatal "BURAQ_MICROVM_ORIGIN must be an https:// origin ending in /" ;;
        esac
        case "$BURAQ_MICROVM_ORIGIN" in
            *"?"*|*"#"*|*"@"*) fatal "BURAQ_MICROVM_ORIGIN must not contain credentials, query or fragment" ;;
        esac
        export BURAQ_ContainerProvider__Type="MicroVm"
        export BURAQ_ContainerProvider__MicroVmConfig__ControlPlaneOrigin="$BURAQ_MICROVM_ORIGIN"
        export BURAQ_ContainerProvider__MicroVmConfig__ApiToken="$BURAQ_MICROVM_TOKEN"
        export BURAQ_ContainerProvider__MicroVmConfig__NodeName="${BURAQ_MICROVM_NODE:-microvm-primary}"
        export BURAQ_ContainerProvider__MicroVmConfig__HealthPath="${BURAQ_MICROVM_HEALTH_PATH:-v1/health}"
        export BURAQ_ContainerProvider__MicroVmConfig__RequestTimeoutSeconds="${BURAQ_MICROVM_TIMEOUT_SECONDS:-30}"
        ;;
    *) fatal "BURAQ_RUNTIME_PROVIDER must be Docker or MicroVm" ;;
esac
export BURAQ_ContainerProvider__Sandbox__Enabled="true"
export BURAQ_ContainerProvider__Sandbox__DropCapabilities__0="ALL"
export BURAQ_ContainerProvider__Sandbox__AddCapabilities__0="CHOWN"
export BURAQ_ContainerProvider__Sandbox__AddCapabilities__1="DAC_OVERRIDE"
export BURAQ_ContainerProvider__Sandbox__AddCapabilities__2="FOWNER"
export BURAQ_ContainerProvider__Sandbox__AddCapabilities__3="SETGID"
export BURAQ_ContainerProvider__Sandbox__AddCapabilities__4="SETUID"
export BURAQ_ContainerProvider__Sandbox__AddCapabilities__5="KILL"
export BURAQ_ContainerProvider__Sandbox__AddCapabilities__6="NET_BIND_SERVICE"
export BURAQ_ContainerProvider__Sandbox__PidsLimit="${BURAQ_CHALLENGE_PIDS_LIMIT:-512}"
export BURAQ_ContainerProvider__Sandbox__SeccompProfile="RuntimeDefault"

case "${BURAQ_REQUIRE_SMTP:-false}" in
    true|TRUE|1)
        require_var SMTP_HOST
        require_var SMTP_SENDER_ADDRESS
        ;;
esac

if [ -n "${SMTP_HOST:-}" ]; then
    require_var SMTP_SENDER_ADDRESS
    require_var SMTP_PORT
    export BURAQ_EmailConfig__SenderAddress="$SMTP_SENDER_ADDRESS"
    export BURAQ_EmailConfig__SenderName="${SMTP_SENDER_NAME:-BURAQ}"
    export BURAQ_EmailConfig__Smtp__Host="$SMTP_HOST"
    export BURAQ_EmailConfig__Smtp__Port="$SMTP_PORT"
    export BURAQ_EmailConfig__Smtp__BypassCertVerify="false"

    if [ -n "${SMTP_USERNAME:-}" ] || [ -n "${SMTP_PASSWORD:-}" ]; then
        require_var SMTP_USERNAME
        require_var SMTP_PASSWORD
        export BURAQ_EmailConfig__UserName="$SMTP_USERNAME"
        export BURAQ_EmailConfig__Password="$SMTP_PASSWORD"
    fi
fi

exec dotnet BURAQ.dll

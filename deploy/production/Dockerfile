# syntax=docker/dockerfile:1.7
FROM node:24-alpine AS frontend
WORKDIR /src/BURAQ/ClientApp
RUN corepack enable && corepack prepare pnpm@10.14.0 --activate
COPY src/BURAQ/ClientApp/package.json src/BURAQ/ClientApp/pnpm-lock.yaml src/BURAQ/ClientApp/pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile
COPY src/BURAQ/ClientApp/ ./
RUN pnpm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS backend
WORKDIR /src
COPY global.json BURAQ.sln Directory.Packages.props ./
COPY src/BURAQ/BURAQ.csproj src/BURAQ/BURAQ.csproj
RUN dotnet restore src/BURAQ/BURAQ.csproj
COPY src/BURAQ/ src/BURAQ/
RUN dotnet publish src/BURAQ/BURAQ.csproj --configuration Release --no-restore \
    -p:SkipFrontendBuild=true --output /out
COPY --from=frontend /src/BURAQ/ClientApp/build/ /out/wwwroot/admin/

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
RUN apk add --no-cache icu-libs krb5-libs libgcc libpcap libstdc++ tzdata \
    && addgroup -S -g 10001 buraq \
    && adduser -S -D -H -u 10001 -G buraq buraq \
    && mkdir -p /app/files \
    && chown -R 10001:10001 /app
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_EnableDiagnostics=0 \
    ASPNETCORE_ENVIRONMENT=Production
WORKDIR /app
COPY --from=backend --chown=10001:10001 /out/ ./
# Plain COPY + chmod: COPY --chmod has produced an empty file from Windows build contexts.
COPY deploy/production/entrypoint.sh /app/entrypoint.sh
RUN sed -i 's/$//' /app/entrypoint.sh && chmod 0555 /app/entrypoint.sh && test -s /app/entrypoint.sh
USER 10001:10001
EXPOSE 8080
ENTRYPOINT ["/app/entrypoint.sh"]

# syntax=docker/dockerfile:1.7
FROM golang:1.26.5-alpine3.24 AS build
ARG MC_COMMIT=77f82e18b5401a65958f1619df6ebb994634bd88
RUN apk add --no-cache ca-certificates git
WORKDIR /src
RUN git clone https://github.com/minio/mc.git . \
    && git checkout --detach "$MC_COMMIT" \
    && test "$(git rev-parse HEAD)" = "$MC_COMMIT"
RUN LDFLAGS="$(go run buildscripts/gen-ldflags.go)" \
    && CGO_ENABLED=0 go build -tags kqueue -trimpath -ldflags "$LDFLAGS" -o /out/mc .

FROM alpine:3.24
RUN apk add --no-cache ca-certificates \
    && addgroup -S -g 10001 minio \
    && adduser -S -D -H -u 10001 -G minio minio
COPY --from=build --chmod=0555 /out/mc /usr/local/bin/mc
USER 10001:10001
ENTRYPOINT ["/usr/local/bin/mc"]

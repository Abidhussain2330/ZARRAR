# Security

ZARRAR runs untrusted challenge workloads and handles participant credentials, flags, artifacts, and operational telemetry. Treat the host, Docker or MicroVM authority, database, object store, cryptographic keys, and backups as one high-value security boundary.

## Secret rules

- Generate every password, HMAC key, seed, and provider token independently with a cryptographically secure generator.
- Store Development secrets in .NET user-secrets. Store production values in a protected host secret mechanism or the mode-`0600` `.env.production` file required by the supplied Compose runbook.
- Never commit `.env.production`, user-secrets, credentials, cookies, browser state, raw flags, private keys, certificates, database dumps, upload data, or packet captures.
- Do not log or place raw dynamic flags in labels, image metadata, SOC payloads, error responses, or evidence.
- Rotate a leaked value and assess all data protected or authenticated by it. Changing `XorKey` or a dynamic-flag authority without a coordinated migration can make persisted data unusable.

## Network and origin boundaries

- Production exposes only Caddy on the public HTTP/HTTPS ports.
- Keep PostgreSQL, Redis, MinIO, ZARRAR `8080`, management `3000`, and Docker TCP ports private.
- Use exact trusted origins. Do not use wildcard CORS or disable CSRF/origin checks to work around a client configuration error.
- Keep secure cookie, forwarded-header, and HTTPS behavior under the production proxy.
- Shared-runtime and builder origins must use approved HTTPS authorities. Reject authority changes, origin confusion, encoded path traversal, and redirects outside the approved origin.

## Workload isolation

- Never grant privileged mode, host PID/network namespaces, arbitrary host paths, or Docker-socket access to challenge containers.
- Enforce CPU, memory, PID, network, lifetime, and global/per-participation instance limits.
- Scope Docker discovery and lifecycle actions to ZARRAR ownership/project labels. Inspect before cleanup and never delete unrelated containers.
- In MicroVM mode, mount `/dev/null` in place of the Docker socket and use only a scoped control-plane credential.
- Treat challenge images as untrusted release artifacts requiring review and immutable tags/digests.

## Authorization and data access

Participant, administrator, and SOC authorization must be enforced server-side. UI visibility is not an authorization control. Maintain team/event ownership checks for attachments, personalized variants, container instances, proxy routes, submissions, and artifact access. Preserve no-store behavior for personalized downloads.

Use safe `400`, `401`, `403`, `404`, `409`, and `429` responses without stack traces or sensitive payloads. Keep rate limiting enabled and preserve `Retry-After` behavior.

## Telemetry and SOC

SOC and SignalR events may include safe entity IDs and correlation context, but must not include raw flags, wrong answers, passwords, tokens, cookies, HMAC keys, seed keys, database credentials, storage secrets, or private headers. Apply the same rule to request logs, queue payloads, screenshots, and exported diagnostics.

## Optional integrations

SMTP, WSRX, shared runtime, deterministic builder, and MicroVM control are external trust relationships. Leave them disabled or clearly unavailable until their deployment-specific authority, credentials, TLS, capacity, failure behavior, and auditability are verified. Never install an untrusted WSRX executable or disable TLS verification in production.

## Reporting

Do not place vulnerabilities or proof material containing credentials in public issues. Provide a minimal reproduction with secrets removed and rotate any value that may have been exposed.

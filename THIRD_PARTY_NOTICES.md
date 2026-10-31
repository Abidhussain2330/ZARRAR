# Third-Party Notices

ZARRAR is a product of THE ARZENS. This file is separate from LICENSE.txt because third-party dependencies are not owned or relicensed by THE ARZENS.

The frontend and backend use external packages whose licenses remain applicable to those packages. Their package metadata is retained in the project manifests and canonical pnpm lockfile. Before distributing a production build, generate and attach the complete notice bundle for the exact resolved dependency graph, including pnpm and NuGet packages, fonts, icons, images, and container/base-image components used by that build.

Nothing in the Custom THE ARZENS License changes the terms of a third-party component. Where a dependency license requires attribution, source availability, notice retention, or other conditions, those conditions continue to apply to that dependency.

## Release dependency snapshot

The pnpm lockfile and central NuGet package manifest are authoritative. The current application release resolves these major components:

| Component | Resolved version or source |
|---|---|
| React / React DOM | `19.2.7` |
| Mantine | `9.4.1` |
| TypeScript | `6.0.3` |
| Vite | `8.1.3` |
| SignalR JavaScript client | `10.0.0` |
| DOMPurify | `3.4.15` |
| EF Core | `10.0.9` |
| Npgsql EF provider | `10.0.2` |
| PostgreSQL image | `17.10-alpine` |
| Redis image | `8.2.8-alpine` |
| Caddy image | `2.11.4-alpine` |
| MinIO server | source commit `7aac2a2c5b7c882e68c1ce017d8256be2feea27f` |
| MinIO client | source commit `77f82e18b5401a65958f1619df6ebb994634bd88` |

Before redistribution, produce a machine-generated software bill of materials and license/notice bundle from the exact CI-built dependency graph and resolved container digests. This summary is not a substitute for the licenses distributed by each dependency.

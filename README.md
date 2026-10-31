# ZARRAR — 1v1 Cyber Showdown

ZARRAR by The Arzens is a live head-to-head CTF platform: two players (or two tag teams) race for the same challenges while their screens stream to a projector stage, with the admin releasing targets one at a time. It is built on ZARRAR, a self-hosted CTF platform (challenge containers, dynamic flags, SOC and anti-cheat).

## One-click start (Windows)

Needs only **Docker Desktop**. Double-click **`Zarrar.exe`**. It:

1. starts Docker Desktop and waits for it
2. on the first run, writes `deploy/production/.env.zarrar` with fresh random secrets
3. builds the app image (first run only, 5–15 min) and starts Caddy, the app, PostgreSQL, Redis and MinIO
4. trusts the local HTTPS certificate for your Windows user (Windows asks once — click **Yes**)
5. creates the demo accounts once and lists them in `DEMO_ACCOUNTS.txt`
6. opens `https://localhost`

| File | Does |
|---|---|
| `Zarrar.exe` | start (or restart) the platform |
| `Zarrar-Stop.exe` | stop it; data stays in the `zarrar_*` Docker volumes |
| `Zarrar.exe -Rebuild` | rebuild the image after changing the source code |
| `launcher/ZARRAR-Launcher.ps1` | the script both executables run |
| `launcher/build-exe.cmd` | recompile the two executables from `launcher/Launcher.cs` |

To start completely fresh, stop the platform, delete `deploy/production/.env.zarrar`, `DEMO_ACCOUNTS.txt` and the `zarrar_*` volumes in Docker Desktop, then run `Zarrar.exe` again.

| Surface | URL |
|---|---|
| Website / players | `https://localhost` |
| Admin | `https://localhost/admin` |
| Projector stage (monitor) | `https://localhost/monitor/login` |

## Match day and venue

**[ZARRAR_SETUP.md](ZARRAR_SETUP.md)** is the runbook: setting up a match, venue HTTPS for other laptops (§4 — set `ZARRAR_DOMAIN`, `ZARRAR_PUBLIC_URL` and `ZARRAR_ALLOWED_HOSTS` in `.env.zarrar` to the server's LAN IP), tag teams, recordings and the smoke test. [SECURITY.md](SECURITY.md) covers trust boundaries and secrets.

## Development (optional)

.NET SDK `10.0.300`, Node 24 and pnpm `10.14.0`. Backend + data services: `dotnet run --project src/ZARRAR.AppHost/ZARRAR.AppHost.csproj --launch-profile http` (needs the AppHost user secrets `Parameters:postgres-password`, `XorKey`, `DynamicFlagHmacKey`); frontend: `pnpm dev` in `src/ZARRAR/ClientApp`, then open `http://localhost:63000`.

## License

See [LICENSE.txt](LICENSE.txt) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). The upstream ZARRAR attribution must stay with any copy or build.

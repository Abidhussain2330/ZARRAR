# ZARRAR — 1v1 Showdown on BURAQ

ZARRAR turns any BURAQ game into a live 1v1 match:

- Players keep the normal challenge page, rebranded to ZARRAR red, silver and black.
- Each player shares their entire screen for the whole match.
- The admin releases challenges one at a time. The first correct flag captures a challenge, and it disappears for both players.
- The monitor laptop shows the **Stage** on the auditorium screen: both live screens, scores, the timer, and full-screen FLAG CAPTURED / ACCESS DENIED animations with sound.
- The admin sees the same Stage, kept in sync, in **Admin → ZARRAR Live**, next to the match controls.

Flag checking, SOC, cheat detection and containers are unchanged.

---

## 1. Build (first time after copying these files)

These changes were written without a compiler. Build before anything else, and fix anything the compiler reports before testing.

```powershell
# backend (and the unit tests, which include the SOC decay and ledger checks)
dotnet build BURAQ.sln --no-incremental
dotnet test src/BURAQ.Test

# frontend (type-check + bundle)
cd src/BURAQ/ClientApp
pnpm install --frozen-lockfile
pnpm build
```

**No database migration is needed.** The match state is stored as JSON in the existing `Configs` table, under the key `ZARRAR_DUEL_<gameId>`.

The production Docker build (`deploy/production`) picks the changes up the normal way. **No new ports and no Caddy change:** the video travels over WebSockets through the existing HTTPS origin.

## 2. One-time setup

1. **Platform title.** The default title is now `ZARRAR`. If your database already has a title saved, change it in **Admin → Settings → Platform title**.
2. **Monitor account.** Create a normal account. Then in **Admin → Users**, set its role to **Monitor**. A Monitor can open the stage but cannot administer the platform.
3. **Two-step login for staff.** Sign in as each admin and monitor, open **Account → Settings → Two-step login**, add the key to an authenticator app (Google/Microsoft Authenticator, Aegis, 1Password…), type the code, and **save the ten recovery codes somewhere offline**. From then on, signing in asks for the 6-digit code. Pre-flight lists staff accounts that still don't have it.
4. **Players.** Every player registers with their own account. A side can be one person (a one-person team) or a **tag team**: one captain creates the team and the others join it. See §5a.

## 3. Setting up a match (one game = one match)

1. **Admin → Events → New event.** In **Info**:
   - team member limit: **1** for solo sides, or the roster size for tag teams (e.g. 3). `0` means unlimited.
   - practice mode **off**, so nothing is accepted after the end
   - start and end times
2. **Challenges.**
   - Add them in the order you want to release them. The release order is creation order.
   - **Points are fixed automatically.** Only the first correct flag on a challenge scores, so dynamic scoring never lowers it. The one thing that changes points is the **first-blood bonus**: turn it off on each challenge, or set it to 0 in Info. Pre-flight warns you if it is on.
   - Leave them all **disabled**. "Release" enables them during the match.
   - Prefer **dynamic flags** (dynamic container or dynamic attachment), so a flag seen on the big screen is useless to the other player.
3. **Review.** Accept exactly **two** players. The first accepted is Player 1 (blue, left) and the second is Player 2 (green, right).
4. **Admin → ZARRAR Live → (the game) → turn on "1v1 showdown mode".**
5. **Check the Pre-flight panel** at the top right of that page. Every item should be green before you start:

   | Check | Blocking? | Fix |
   |---|---|---|
   | Showdown mode on | yes | the switch under MATCH |
   | Exactly two players | yes | the game's Review tab |
   | Both screens live | yes | each player clicks **Share entire screen** |
   | Game is open | yes | start and end time in the game's Info tab |
   | Challenges added | yes | the game's Challenges tab |
   | Challenges hidden until released | warning | disable them in the Challenges tab |
   | Projector stage open | warning | log in as the monitor and open the match |
   | Player on the keyboard per team | warning | tag someone in for every team with more than one member (§5a) |
   | Practice mode off | warning | the game's Info tab |
   | Points match what is announced | warning | turn off the first-blood bonus |
   | SOC pipeline healthy | warning | no dead-lettered SOC events, no failed detection jobs in the last day, nothing pending for over 2 min (Admin → SOC → Infrastructure) |
   | Staff use two-step login | warning | the listed admin/monitor accounts turn it on (§2) |

   While any **blocking** item is red, **Start match** and **Release** are disabled. The override checkbox under the list lets you go anyway, for example to release a test challenge before the players are sharing.

## 4. Venue network: HTTPS without internet

**Chrome and Edge only allow screen sharing and the video decoder on a secure (`https://`) address.** `localhost` counts as secure, which is why everything works on your own machine. A player laptop opening `http://192.168.x.x` does **not**: the share button stays disabled, and the monitor shows an amber "LIVE FEEDS NEED HTTPS" banner. Every laptop must therefore use the `https://` address.

The venue has no public domain, so use the local-CA proxy that is already in `deploy/production`:

1. **Give the server a fixed LAN IP** (a DHCP reservation on the venue router), for example `192.168.1.50`.
2. **In `.env.production`, set `BURAQ_DOMAIN=192.168.1.50`** (the IP, no `https://`).
3. **Start the stack with the local override:**
   ```bash
   cd deploy/production
   docker compose --env-file .env.production -f compose.yaml -f compose.local.yaml up -d
   ```
   Caddy issues a certificate for that IP from its own built-in CA. Port 80 redirects to HTTPS automatically.
4. **Export Caddy's root certificate once:**
   ```bash
   docker compose --env-file .env.production -f compose.yaml -f compose.local.yaml cp proxy:/data/caddy/pki/authorities/local/root.crt ./zarrar-root.crt
   ```
   Keep it: the CA is stored in the `caddy_data` volume and survives restarts. It only changes if you delete that volume.
5. **Trust it on every laptop** (both players, the monitor laptop and the admin laptop). On Windows, run this in an **administrator** PowerShell:
   ```powershell
   certutil -addstore -f Root zarrar-root.crt
   ```
   Or double-click the file → Install Certificate → Local Machine → "Trusted Root Certification Authorities". Then restart Chrome.
6. **Check:** open `https://192.168.1.50` on each laptop. It should show a padlock and no warning.

**Fallback if a laptop cannot install the certificate:** on the warning page, click Advanced → Proceed. Pages then still count as `https://`, and sharing should still work. **Confirm this during the smoke test, not on the day.**

**Network for the live feeds.** Each player's screen is sent at up to about 3–4 Mbps (it steps down automatically if the network can't keep up, and back up when it can). The server sends a copy to every open stage and admin preview, so two players and three viewers is roughly 8 Mbps in and 24 Mbps out of the server. Wire the server and, if possible, the two player laptops and the projector laptop. On Wi-Fi, use 5 GHz and keep the audience off that network.

**Don't** hand out `http://<ip>:8080`. The local override publishes the app's debug port on the server itself only (`127.0.0.1:8080`), so the LAN can reach the platform through Caddy's `https://` and nothing else.

## 4a. Security settings (optional)

All optional; the defaults are safe. Set them in `.env.production` as `BURAQ_Soc__<Name>` (or in `appsettings.json` under `"Soc"`).

| Setting | Default | What it does |
|---|---|---|
| `Soc:RiskHalfLifeHours` | `6` | SOC case risk halves every this many hours, so old low-grade signals cool down. Proof (a foreign dynamic flag, a two-way exchange) never fades. `0` turns decay off. |
| `Soc:FingerprintKey` | the platform `XorKey` | Separate secret for hashing wrong answers before comparing them across teams. Set it to a long random string. Changing it mid-event makes earlier wrong answers stop matching new ones. |
| `Soc:LedgerKey` | the platform `XorKey` | Secret for the sealed SOC ledger. Set it to a long random string **before** the event and never change it afterwards: a different key makes every earlier seal fail to verify. |
| `Soc:EventRetentionDays` | `0` (keep everything) | Deletes SOC timeline events older than this, in whole sealed ranges, so the ledger still verifies. Leave at 0 until after the event's review. |

**Docker socket proxy.** Normally the app container mounts the Docker socket to start challenge containers. `compose.socket-proxy.yaml` puts a filtering proxy in between, so a compromised app can't exec into containers, touch volumes or build images:

```bash
docker compose --env-file .env.production -f compose.yaml -f compose.local.yaml -f compose.socket-proxy.yaml up -d
```

It needs Docker Compose 2.24 or newer. It still allows creating containers (the platform needs that), so it reduces the damage rather than removing it. Run the whole smoke test (§8) with it on before relying on it.

## 5. Match day

| Who | Where | What |
|---|---|---|
| Players | Chrome or Edge, `https://<server>/games/<id>/challenges` | Click **Share entire screen** and pick "Entire screen". The challenges unlock once the server is receiving their screen. A LIVE badge stays in the corner. |
| Monitor | `https://<server>/monitor/login`, then pick the match | Click **Enter the arena** (full screen and sound). Share or mirror this display to the projector. Otherwise leave it alone. The only control is **T**: the hidden TAG TEAM drawer for swapping players (§5a). |
| Admin | **Admin → ZARRAR Live → match**, or the **LIVE** tab inside the game's admin page | Press **Start match** once Pre-flight is green. Then release the next challenge, pause or resume, switch the layout and end the match. The preview on the left is exactly what the auditorium sees. |

## 5a. Tag team (swapping who plays for a side)

A side can bring several players and swap them mid-match — like Ali (team 1) vs Mustafa (team 2), then Ali goes back and Shafey tags in for team 1. Only one member per side is **on the keyboard** at a time: that player's screen is on the projector and only that player can open challenges, start containers and submit flags.

**Before the match**
- Every member who might play must **sign in with their own account, open the game and join it with the team** (so they appear in the roster). Set the team member limit high enough (§3).
- On the monitor stage press **T** (or move the mouse and click the small **TAG** button bottom-right). The TAG TEAM drawer lists each side's roster. Click who starts. The same control sits on the admin LIVE page under PLAYERS.

**Swapping mid-match**
1. Shafey signs in on his laptop and opens the challenges page. He sees **On the bench — ali is on the keyboard**.
2. The monitor presses **T** and clicks **shafey → TAG IN** under team 1.
3. The stage plays a full-screen **TAG IN — ALI ⇄ SHAFEY**. Ali's laptop stops sharing and shows the bench screen. Team 1's panel reads **TAG IN — shafey is taking the keyboard** until Shafey shares.
4. Shafey's screen says **You're tagged in** — he clicks **Share entire screen**. The projector switches to his screen on the same panel; the handle under the team name becomes `@shafey`.

Notes
- A swap does **not** pause the match (unlike a lost feed). Team 1 simply can't submit until Shafey is live, so make swaps quick.
- If Shafey's stream drops *after* he is live, that's a normal FEED LOST → auto-pause.
- Benched players are refused by the server if they try to submit, so a swap can't be used to play from two laptops.
- With nobody tagged in, whoever on the team shares first holds the keyboard; a teammate can't grab the stream until the monitor tags them in or that stream ends. **Clear tag** goes back to that behaviour.
- Tag-ins appear in the timeline and the CSV export. Recordings are per player: `…-p<id>-<team>-<player>.ivf`.

**Start match** plays a full-screen **3 – 2 – 1 – FIGHT** countdown on the stage, then releases the first challenge automatically. After that, release each next challenge with **Release next**.

**Keyboard shortcuts** on the admin Live page (ignored while typing or while a dialog is open):

| Key | Does |
|---|---|
| `1` `2` `3` `4` | layout: both screens / focus P1 / focus P2 / scoreboard |
| `5` `6` | layout: player 1 / player 2 full screen |
| `P` | pause or resume |
| `Shift` + `N` | release the next challenge |
| `Shift` + `S` | start the match |

**Clocks.** The stage shows how long the current target has been live, next to its title. FLAG CAPTURED shows how long the capture took. Both clocks stop while the match is paused.

**Stage layouts** (switched by the admin):

- **Both screens** — the default, split view
- **Focus player 1** / **Focus player 2** — one big feed and one small one
- **Full screen P1** / **Full screen P2** — one player's screen fills the projector edge to edge, with a slim name and score strip. On a 1920×1080 projector a 1080p laptop screen is shown pixel for pixel, so the audience can read the terminal. Use it for the interesting moments, then go back to **Both screens**.
- **Scoreboard** — mascots, scores and the target list; shown automatically when the match ends

**Getting the sharpest picture:**

- Set the projector (or the monitor laptop's second display) to **1920×1080** and put the stage on it in full screen. The stage draws each feed at exactly the size it occupies, so a lower output resolution throws detail away.
- Players with very high-resolution screens (a 4K or 2880×1800 laptop) should set their display to 1920×1080, or raise Windows scaling, so text is large enough to survive being shrunk.
- In **Both screens**, each feed is shown at about half size; terminal text at default size will be small. Ask players to use a larger terminal font (16 px+) and switch to **Full screen** when something worth reading happens.
- While a player is sharing, their own ZARRAR pages stop the background animations so the stream spends its bandwidth on their text, not on falling effects.

**If a player's screen share stops** (they click "Stop sharing", reload, close the tab, the laptop dies or the Wi-Fi drops):

1. After about 4 seconds the stage shows FEED LOST and the match **pauses**.
2. Flag submission and new containers are blocked for both players.
3. The admin presses **Resume** once the player is sharing again. The game's end time moves back by the paused time.

**End match** sets the game end time to now, freezes the scoreboard and shows the winner on the stage. **Reopen** undoes the stage state. To restore the clock afterwards, edit the end time in the game's Info tab.

**Withdraw** (on a live challenge) takes it away with no winner. Use it when nobody can solve it. The stage shows TARGET WITHDRAWN.

**Timeline.** Below the preview, the admin page lists every start, release, wrong flag, capture (with its time), withdrawal, pause and lost feed, newest first. **Export CSV** downloads all of it. The timeline is kept in the database, so it survives a server restart.

## 6. Recordings

Every player's feed is recorded to `files/zarrar-recordings/<gameId>/<time>-p<participation>-<team>.ivf`. In Docker this is the `buraq_files` volume, at `/app/files`.

- **Download:** on the admin Live page, under **Recordings**. Each file is labelled with its player. A new file starts every time a player starts sharing, and when a player's laptop falls back from VP9 to VP8, so one player can have a few files. Quality changes within a codec stay in the same file.
- **Play:** open the file in VLC, or convert it with `ffmpeg -i in.ivf -c copy out.webm`.
- **Turn off:** set `Zarrar__Record=false`.
- **Move:** set `Zarrar__RecordingPath`.

At 2–4 Mbps, a one-hour match is about 1–1.8 GB per player. Check free disk before the event; recording stops by itself below 1 GB free.

## 7. Sound effects

Drop MP3 files into `src/BURAQ/wwwroot/assets/zarrar/sfx/`:

| File | Plays when |
|---|---|
| `accepted.mp3` | a flag is captured |
| `denied.mp3` | a wrong flag is submitted |
| `released.mp3` | a new challenge is released |
| `alert.mp3` | a feed is lost / the match is paused |
| `resume.mp3` | the match resumes |
| `winner.mp3` | the match ends |
| `count.mp3` | each 3, 2, 1 of the start countdown |
| `fight.mp3` | FIGHT at the end of the countdown |

A missing file falls back to a built-in synthesized placeholder.

## 8. Smoke test before the event (two laptops, venue Wi-Fi)

1. Set the server up as in §4 and use its `https://` address on every laptop. Create a test game with two challenges, two player accounts and a monitor account. Leave the first-blood bonus on and the challenges enabled, on purpose.
2. **Pre-flight (M2).** Open Admin → ZARRAR Live → the game.
   - "Exactly two players", "Both screens live" and "Showdown mode on" are red and marked BLOCKING. Start match is disabled.
   - "Challenges hidden until released" and "Points match what is announced" show amber warnings.
   - Fix each one (accept players, turn on the mode, disable the challenges, turn off the bonus) and watch it go green within a few seconds.
3. Player A opens the challenges page. The share gate appears. Share a **window** instead of the entire screen: it must be refused.
4. Share the entire screen. The challenges unlock, and the admin preview shows A's screen within about a second. Repeat with Player B. "Both screens live" turns green.
5. Log in as the monitor on the projector laptop, open the match and click Enter the arena. "Projector stage open" turns green.
6. **Start (M3).** Press **Start match** (or `Shift`+`S`). The stage plays 3 – 2 – 1 – FIGHT with sound, then NEW TARGET for challenge 1. Both players see it, and the target clock next to its title starts counting.
7. Player B submits a wrong flag: ACCESS DENIED. Player A submits the right flag: FLAG CAPTURED, with "IN mm:ss". The challenge disappears for both players, A's score goes up, and B's container for it stops.
8. Press `Shift`+`N` to release challenge 2. Press `2`, `3`, `4`, `5`, `6` and `1` to cycle the layouts; in `5` / `6` the player's screen fills the projector and its text should be readable from the back of the room. Press `P` to pause: the target clock freezes. Press `P` again to resume.
8a. **Feed resilience.** Turn Player A's Wi-Fi off for 10 seconds and back on: the feed freezes, then the stage shows FEED LOST; it comes back on its own within a few seconds of the Wi-Fi returning. Put the projector laptop to sleep for 30 seconds and wake it: its feeds reconnect without a reload.
9. Player B stops sharing: about 4 seconds later, FEED LOST, then MATCH PAUSED, and B's flag submit is refused. B shares again, and the admin resumes.
10. Withdraw challenge 2: TARGET WITHDRAWN on the stage.
10a. **Tag team.** Join the game with a second account on team A (Player C). C sees **On the bench**. On the monitor stage press **T** and tag C in: TAG IN — A ⇄ C on the stage, A's laptop goes to the bench and no pause happens. C shares and the projector shows C's screen with `@C`. A tries to submit on another tab: refused ("You are on the bench").
11. End the match: the winner screen appears.
11a. **SOC (S1–S4).** Sign in as an admin and open **Admin → SOC**.
    - **Two-step login:** sign out and back in as the admin. The code box appears after the password, a wrong code is refused, and a recovery code works once.
    - **Throttle + toast:** on a player laptop, type wrong passwords for **five different accounts** (any names). Within a few seconds the admin sees a "Password Spraying" toast with a chime, on any admin page. Then fail the monitor's password 9 times from that laptop: the 9th is refused even with the right password, while the real monitor still signs in normally from the projector laptop.
    - **Case actions:** open a case (the earlier wrong flag or a fast capture usually creates one). Under **Respond**, add a note, then suspend team B with a reason. B's challenges page shows it suspended; **Reinstate** brings it back. Each action appears in the case log and the SOC timeline.
    - **Bulk triage:** on **Alerts**, filter to Open and press **Resolve shown**.
    - **Ledger:** on **Infrastructure**, wait 5 minutes after the first SOC events and press **Verify now**. It should say *Intact* with a count of sealed events.
    - **Sign out everywhere:** in Account → Settings on a player, press it. Any other tab or laptop signed in as that player is signed out within a minute.
12. **After the match (M4).** On the admin page:
    - The **Timeline** lists the start, both releases, the wrong flag, the capture with its time, the pause/resume, the lost feed and the withdrawal.
    - **Export CSV** opens in Excel.
    - **Recordings** lists the files for both players (three, because B re-shared). Download them, and they play in VLC.

## 8b. Copying or zipping the platform

Don't zip the working folder as it is: build output (`bin`, `obj`), the frontend's `node_modules` and test recordings make it several GB, and all of that is recreated by building. From the ZARRAR folder, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\zarrar-package.ps1
```

It writes `ZARRAR-<date>-<time>.zip` (about 10–20 MB of source) next to the ZARRAR folder, leaving out build output, `node_modules`, recordings, logs, `.env` secrets and the backup zips. Options:

- `-Clean` also deletes `bin`, `obj` and the frontend `build` folder here to free disk (stop the AppHost first). The next `dotnet build` recreates them.
- `-CleanAll` also deletes `node_modules` and the test recordings and logs (`pnpm install` before the next `pnpm dev`).
- `-IncludeRecordings` puts the recordings in the zip.

On the new machine: unzip, then follow `README.md` (secrets, `dotnet build`, `pnpm install`, run).

## 9. Known limits

- **Chrome or Edge only for players.** Screen streaming uses WebCodecs and MediaStreamTrackProcessor. The monitor laptop should also use Chrome or Edge.
- **One server instance.** Live feeds and match state live in that server's memory, with the state persisted to the database. The venue setup is a single server, so this is fine; a multi-node setup would need a shared relay.
- **Reloading the player's tab ends the share.** The browser warns before leaving, and the match pauses until they share again.
- **Players can keep working on a challenge while the match is paused.** They just cannot submit.
- **Wrong guesses are never shown as text.** The ACCESS DENIED animation shows the player and the challenge only, following `SECURITY.md`.
- **The stage uses the team name**, with the player on the keyboard as the `@handle` underneath. For solo players, name the team after the player.
- **The server cannot prove a stream is the real screen.** The "entire screen" rule is enforced by the browser, and a modified client could send something else. The monitor watching the feeds is the real check — a frozen or odd-looking feed is grounds to pause.
- **Role changes apply to new connections.** A monitor demoted mid-match keeps the feeds already open until they reload; sign them out (Admin → Users) to cut them off immediately.
- **Benched players can still talk to their teammate.** The server stops them streaming, opening challenges and submitting, but can't stop advice from the bench — that's a house rule for the referee.
- **Sign-outs and bans reach open sessions within a minute,** not instantly: the session check runs once a minute.
- **The SOC ledger proves the timeline wasn't edited, not that it is complete.** Someone with database access and the platform secret could still forge it; keep `Soc:LedgerKey` out of the database host. The newest few minutes are unsealed until the next seal.
- **The Docker socket proxy narrows but doesn't close the container path** (see §4a). For full separation run challenges on a separate Docker host.
- **Feed quality depends on the player's laptop and the network.** The stream adapts (VP9 → VP8 on a slow CPU, lower bitrate and size on a congested network), so on a weak laptop or busy Wi-Fi the picture gets softer rather than stalling.
- **Tag-in needs a click from the new player.** Browsers only start a screen share from a user click, so the tagged-in player must press **Share entire screen**.

## 10. What changed (for reviewers)

**Backend (C#)**
- New:
  - `Services/Zarrar/LiveRelay.cs` — WebSocket fan-out of the encoded screen frames, IVF recording, stale-feed watchdog
  - `Services/Zarrar/ZarrarDuelService.cs` — match state, pause/resume/end, capture handling, stage snapshot
  - `Controllers/ZarrarController.cs` — `/api/zarrar/*`, including `live/publish` and `live/watch/{participation}`
  - `Hubs/ZarrarHub.cs` (`/hub/zarrar`) and `Hubs/Clients/IZarrarClient.cs`
  - Tag team: `PUT /api/zarrar/{id}/operator` (Monitor or Admin); `DuelStateModel.Operators`; relay tracks who streams and closes a benched stream with code 4001; `ZarrarGuard` refuses benched players
  - `Models/Request/Zarrar/ZarrarModels.cs`
- Hooks in existing code:
  - `GameController` — captured challenges hidden from players; screen-share, pause and ended checks on opening a challenge, starting a container and submitting a flag
  - `FlagChecker` — stage events and capture after each judged flag
  - `GameInstanceRepository.VerifyAnswer` — only the first correct flag on a showdown challenge scores
- Hardening pass (19 Oct):
  - `CookieOriginValidationMiddleware` also checks Origin on WebSocket upgrades (screen publish/watch, SignalR hubs, container proxy) — cross-site WebSocket hijacking
  - `EntityConfigurationProvider` ignores `ZARRAR_*` rows, so match state and the timeline never enter `IConfiguration` or its periodic hash/reload
  - timeline writes are batched (one DB write per ~2 s, flushed on shutdown) instead of a full rewrite per event
  - resume gives back the paused time even when the pause ran past the scheduled end
  - relay: 2 MB frame cap, 4 KB control-message cap, recordings capped at 4 GB per file and skipped below 1 GB free disk, one file per stream session (not per config message), "feed restored" only when a feed was actually down, viewer queue safe for two readers
  - publish/watch sockets rate-limited; browsers back off when reconnecting (viewer up to 15 s, player up to 6 s)
  - the challenge guard requires the caller's *own* screen to be the team's live feed
  - admin mutations validate the game (and the challenge, for withdraw); hub pushes can't fail a saved change; players' state pushes carry no user IDs; banned users can't join the player hub
  - admin LIVE page and projector coalesce event-driven reloads, and the projector keeps at most one pending ACCESS DENIED per player (spam can't lock the admin out through the rate limiter or hold the projector hostage)
  - timeline CSV export neutralises spreadsheet formulas (`= + - @`) in player-supplied names
  - post-login `?from=` redirects only to paths on this site
- Security & SOC pass (19 Oct, afternoon):
  - **Login guessing** is throttled per (account, source address) — 8 failures per 15 min — with a 60-per-15-min ceiling per account, instead of Identity's per-account lockout. An opponent can no longer lock a rival out before the match; a guesser only locks out themselves (`Services/Soc/AuthGuard.cs`).
  - SOC alerts for **password spraying** (one address failing on 5+ accounts) and **brute force** (20 / 60 failures on one account); every auth event now records the source address and user agent.
  - Failed logins no longer overwrite the account's last address (it used to poison the SOC's shared-network rule).
  - New SOC signals (`Services/Soc/SecuritySignalSink.cs`, throttled to one row per source per 5 min with a repeat count): rate-limit hits, cross-origin (CSRF / WebSocket hijack) attempts, benched players acting or streaming, off-stream flag/container attempts, showdown feed drops; tag-ins are audited with who did them.
  - SOC robustness: the 7-day backfill scan runs every 30 s instead of before every job; a failed analysis can no longer save half-written evidence; one poison outbox row no longer dead-letters its whole batch; shared-wrong-answer matching ignores case and spacing; alert updates validate the assignee and audit the from→to status.
  - `deploy/production/compose.local.yaml`: the debug port is bound to loopback.
- SOC response & trust pass (20 Oct, S1–S4):
  - **Respond from the case** (`POST /api/soc/cases/{id}/actions`, `GET …/targets`): suspend/reinstate team, ban user, sign a user out everywhere, void a submission (removes its capture), add a note. A reason is required; staff accounts can't be banned from here; every action is logged on the case and the timeline.
  - **Triage**: case risk decays with age (`SocRiskScoring.CalculateAt`, refreshed every 5 min by `SocRiskMaintenanceService`); resolved cases reopen only for High/Critical signals or risk ≥ 50; repeat alerts of the same kind on one case within 30 min are folded into the case; bulk alert status (`PATCH /api/soc/alerts/bulk`); a case's status change carries to its open alerts; the Overview respects the chosen time range.
  - **Critical notifications**: `SocAlertWatcher` shows High/Critical alerts as toasts with a chime on every admin page.
  - **Wider detection**: impossibly fast solves (from first interaction to the flag), captures under 20 s into a release, one account used from two addresses at once, registration bursts from one address, container egress spikes (> 2 MB/s).
  - **Two-step login** (TOTP + recovery codes) and **sign out everywhere** (`/api/account/twofactor*`, `/api/account/signouteverywhere`); the login pages ask for the code; staff signing in without it are logged. Session checks every minute so bans and sign-outs apply quickly.
  - **Sealed SOC ledger** (`Services/Soc/SocLedger.cs`): every 2 min new SOC events are folded into an HMAC chain and a seal event is written; `GET /api/soc/ledger` verifies it and raises a Critical alert if it is broken.
  - **Ops**: optional Docker socket proxy (`compose.socket-proxy.yaml`, `BURAQ_DOCKER_URI`), separate fingerprint and ledger keys, SOC event retention by sealed range, and SOC health + staff two-step login in Pre-flight.
  - New tests: `src/BURAQ.Test/SocHardeningTests.cs`.
- Clean, robust feeds (20 Oct, F1):
  - stage: the falling scan beam and per-feed sweep are gone; scanlines and vignette sit under the player panels; nothing is drawn over the picture (LIVE badge moved to the panel header)
  - new layouts `f1` / `f2` (keys 5 / 6): one screen full size, pixel for pixel on a 1080p projector
  - viewer draws each feed at its on-screen size in device pixels with high-quality downscaling and letterboxing (16:10 screens are no longer stretched), keeps the last picture during layout changes, asks for a key frame when it skips, and reconnects by itself after 12 s of silence (sleeping laptop, Wi-Fi hop)
  - publisher: up to 1920×1080, VP9 when the browser can encode it (VP8 otherwise), screen-text content hint, high-quality scaling; adapts bitrate and size to the network and falls back VP9 → VP8 → smaller frames when the CPU can't keep up; send backlog capped at 0.75 s
  - relay: keepalive pings to viewers every 4 s, a viewer that accepts nothing for 10 s is dropped, viewers can request key frames, recordings use the right IVF codec tag (VP90 for VP9) and start a new file when the codec changes
  - player pages: the backdrop stops its animations while the screen is shared
- `SecurityHeadersMiddleware`: `display-capture` and `autoplay` are now allowed for the site's own origin. They are required for screen sharing and the stage sounds.
- `Configs.cs`: the default platform title is now ZARRAR.
- `Resources/favicon.webp`: the ZARRAR shield.

**Frontend**
- New:
  - `utils/zarrar/*` — API client, publisher worker, viewer, hub hook, sound effects
  - `components/zarrar/*` — Stage, PlayerLiveGate, MonitorGuard
  - `pages/monitor/*` — login, event picker, stage
  - `pages/admin/live/*` — admin control room
  - `styles/zarrar/*`
- Rebrand:
  - player tokens and palette switched from violet to ZARRAR red/silver/carbon
  - BURAQ text replaced with ZARRAR on player-facing pages
  - new logo, emblem and landing art in `wwwroot/assets/zarrar/`
- Navigation:
  - Admin rail: **ZARRAR Live** (admins) and **ZARRAR Stage** (monitors)
  - Game admin tabs: **LIVE**
- Login sends Monitor accounts to `/monitor`.
- Security (S1–S4): Account → Settings → **Two-step login** and **Sign out everywhere** (`components/account/AccountSecurityPanel.tsx`); the code step on both login pages; SOC case **Respond** panel (`SocCaseDetailModal.tsx`); alert status filter and **Resolve shown**; **Ledger** panel on the SOC Infrastructure tab; alert toasts (`components/admin/SocAlertWatcher.tsx`). The SOC page loads only the open tab's data and throttles live refreshes.
- Match readiness (M2):
  - a pre-flight checklist in the stage snapshot, for admins only (`ZarrarDuelService.BuildReadiness`)
  - the hub counts open projector stages (`/hub/zarrar?screen=1`)
- Showtime (M3):
  - `POST /api/zarrar/{id}/start` and the countdown stage event
  - a match clock in the duel state that excludes pauses (`ClockSeconds`, `ClockSinceUtc`, `ReleasedAtClock`)
  - time to capture on the `accepted` and `captured` events
  - admin keyboard shortcuts
- Player GUI re-imagined (M6):
  - `styles/player/fx.css` + `components/player/CyberBackdrop.tsx`: the living background behind every player page
    - key art, haze, hex mesh, data rain, grid floor, scan beam, glitch bands and HUD corners
    - transform/opacity animations only, so it stays light on laptops that are also streaming
  - New look applied to:
    - the shell: HUD nav, status strip, energy rail
    - every page stylesheet
    - Mantine popups and controls
  - `utils/zarrar/flagHit.ts`: full-screen FLAG CAPTURED / ACCESS DENIED on the player's own page after each flag
  - The landing page is a versus poster; the 404 page glitches
  - Turning on the OS setting "reduce motion" stops all of the effects
- After the match (M4):
  - the timeline, stored as JSON in `Configs` under `ZARRAR_LOG_<gameId>` and served at `GET /api/zarrar/{id}/timeline`
  - recordings at `GET /api/zarrar/{id}/recordings` and `/recordings/{file}`, admin only, file names validated
- The player share gate and the stage explain an `http://` (insecure) address explicitly, instead of reporting an unsupported browser.
- Fonts: Orbitron, Chakra Petch and Rajdhani are added to the build-time web-font download in `vite.config.mts`.

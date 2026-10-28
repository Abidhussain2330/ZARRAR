import dayjs from 'dayjs'
import { FC, useEffect, useState } from 'react'
import { Link } from 'react-router'
import useSWR from 'swr'
import { Empty, ErrorState, Loading, PageHead, arena } from '@Components/player/Arena'
import { PlayerShell } from '@Components/player/PlayerShell'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useUser } from '@Hooks/useUser'
import api, { ParticipationStatus } from '@Api'
import classes from '@Styles/player/CommandCentre.module.css'

/**
 * The player's command centre.
 *
 * Deliberately NOT at /command-centre: that route belongs to the existing platform-wide page,
 * which renders in the admin shell and is linked from the admin sidebar. Overwriting it would
 * silently repoint an admin nav item at a player page.
 */

interface TeamInstance {
  challengeId: number
  challengeTitle: string
  category?: string | null
  entry: string
  startedAt: string
  expectStopAt: string
}

interface TeamInstances {
  instances: TeamInstance[]
  limit: number
}

const fetcher = async (url: string) => {
  const res = await fetch(url, { credentials: 'include' })
  if (!res.ok) throw Object.assign(new Error('Request failed'), { status: res.status })
  return res.json()
}

/** Re-renders once a second so container countdowns stay live without refetching. */
const useTick = (active: boolean) => {
  const [, force] = useState(0)
  useEffect(() => {
    if (!active) return
    const t = window.setInterval(() => force((n) => n + 1), 1000)
    return () => window.clearInterval(t)
  }, [active])
}

const remaining = (until: string) => {
  const secs = dayjs(until).diff(dayjs(), 'second')
  if (secs <= 0) return { text: 'expired', urgent: true }
  const h = Math.floor(secs / 3600)
  const m = Math.floor((secs % 3600) / 60)
  const s = secs % 60
  return {
    // Under five minutes is where a player must extend or lose the container.
    urgent: secs < 300,
    text: h > 0 ? `${h}h ${m}m` : `${m}m ${String(s).padStart(2, '0')}s`,
  }
}

const Dashboard: FC = () => {
  const { user } = useUser()
  usePageTitle('Command centre')

  const { data: state, error: stateError, mutate: retryState } = api.dashboard.useDashboardCurrentUser(undefined, {
    // Refreshes with the containers panel so rank and score don't sit stale beside a live clock.
    refreshInterval: 60 * 1000,
  })

  const gameId = state?.gameId ?? null
  const joined = state?.status === ParticipationStatus.Accepted

  const {
    data: containers,
    error: containersError,
    mutate: refreshContainers,
  } = useSWR<TeamInstances>(joined && gameId ? `/api/game/${gameId}/myinstances` : null, fetcher, {
    refreshInterval: 30 * 1000,
  })

  useTick(!!containers?.instances.length)

  const running = containers?.instances ?? []
  const limit = containers?.limit ?? 0

  return (
    <PlayerShell>
      <PageHead
        eyebrow="ZARRAR / COMMAND CENTRE"
        title={`Welcome back, ${user?.userName ?? 'operator'}`}
        subtitle="Your standing, your team, and what you have running right now."
      />

      {stateError ? (
        <ErrorState
          title="Couldn't load your status"
          body="The server didn't answer. Check your connection and try again."
          action={
            <button type="button" className={`${arena.btn} ${arena.btnGhost} bqFocusRing`} onClick={() => retryState()}>
              Retry
            </button>
          }
        />
      ) : !state ? (
        <Loading label="Loading your command centre…" />
      ) : !state.gameId ? (
        <Empty
          title="No active event"
          body="Once you join an event, your standing and running containers appear here."
          action={
            <Link to="/games" className={`${arena.btn} ${arena.btnPrimary} bqFocusRing`}>
              Browse events
            </Link>
          }
        />
      ) : (
        <>
          <section className={classes.statRow} aria-label="Your standing">
            <div className={classes.stat}>
              <span className={classes.statLabel}>Rank</span>
              <strong className={classes.statValue}>{state.ranked ? `#${state.rank}` : '—'}</strong>
              <span className={classes.statNote}>{state.gameTitle}</span>
            </div>
            <div className={classes.stat}>
              <span className={classes.statLabel}>Score</span>
              <strong className={classes.statValue}>{state.ranked ? state.score.toLocaleString() : '—'}</strong>
              <span className={classes.statNote}>Points earned</span>
            </div>
            <div className={classes.stat}>
              <span className={classes.statLabel}>Solves</span>
              <strong className={classes.statValue}>{state.ranked ? state.solvedCount : '—'}</strong>
              <span className={classes.statNote}>Challenges cleared</span>
            </div>
            <div className={classes.stat}>
              <span className={classes.statLabel}>Containers</span>
              <strong className={classes.statValue}>
                {running.length}
                {limit > 0 && <span className={classes.statOf}> / {limit}</span>}
              </strong>
              <span className={classes.statNote}>{limit > 0 ? 'Team limit' : 'Running now'}</span>
            </div>
          </section>

          <div className={classes.columns}>
            <section className={classes.panel} aria-label="Running containers">
              <div className={classes.panelHead}>
                <h2 className={classes.panelTitle}>Active containers</h2>
                {/* Shared instances are the reason this panel exists: a teammate's container is
                    your container, and the limit counts against the team, not the individual. */}
                <span className={classes.panelNote}>Shared across your team</span>
              </div>

              {!joined ? (
                <p className={classes.note}>Your registration isn&rsquo;t accepted yet, so containers are unavailable.</p>
              ) : containersError ? (
                <p className={classes.note}>Container status is unavailable right now.</p>
              ) : !containers ? (
                <Loading label="Checking containers…" />
              ) : running.length === 0 ? (
                <p className={classes.note}>
                  Nothing running. Start a container from a challenge and it appears here.
                </p>
              ) : (
                <ul className={classes.instanceList}>
                  {running.map((inst) => {
                    const left = remaining(inst.expectStopAt)
                    return (
                      <li key={inst.challengeId} className={classes.instance}>
                        <div className={classes.instanceMain}>
                          <Link
                            to={`/games/${gameId}/challenges#${inst.challengeId}`}
                            className={classes.instanceName}
                          >
                            {inst.challengeTitle}
                          </Link>
                          <code className={classes.entry}>{inst.entry}</code>
                        </div>

                        <div className={classes.instanceMeta}>
                          <span className={`${classes.ttl} ${left.urgent ? classes.ttlUrgent : ''}`}>{left.text}</span>
                          <span className={classes.ttlLabel}>REMAINING</span>
                        </div>

                        {/* Extend and destroy live in the challenge modal, which already owns the
                            container lifecycle and its rate limiting. Linking there keeps one
                            code path for those operations rather than duplicating them here. */}
                        <Link
                          to={`/games/${gameId}/challenges#${inst.challengeId}`}
                          className={`${arena.btn} ${arena.btnGhost} ${classes.manageBtn} bqFocusRing`}
                        >
                          Manage
                        </Link>
                      </li>
                    )
                  })}
                </ul>
              )}

              {running.length > 0 && (
                <button
                  type="button"
                  className={`${arena.btn} ${arena.btnGhost} ${classes.refresh} bqFocusRing`}
                  onClick={() => refreshContainers()}
                >
                  Refresh
                </button>
              )}
            </section>

            <aside className={classes.side}>
              <div className={classes.panel}>
                <h2 className={classes.panelTitle}>Your team</h2>
                {state.teamName ? (
                  <div className={classes.teamRow}>
                    {state.teamAvatar ? (
                      <img className={classes.teamAvatar} src={state.teamAvatar} alt="" />
                    ) : (
                      <span className={classes.teamAvatarFallback}>{state.teamName.slice(0, 2).toUpperCase()}</span>
                    )}
                    <div>
                      <p className={classes.teamName}>{state.teamName}</p>
                      <p className={classes.teamNote}>
                        {state.status === ParticipationStatus.Accepted ? 'Competing' : 'Registration pending'}
                      </p>
                    </div>
                  </div>
                ) : (
                  <p className={classes.note}>
                    You&rsquo;re not on a team for this event. <Link to="/teams">Create or join one</Link>.
                  </p>
                )}
              </div>

              <div className={classes.panel}>
                <h2 className={classes.panelTitle}>Jump back in</h2>
                <div className={classes.quickLinks}>
                  <Link to={`/games/${gameId}/challenges`} className={classes.quickLink}>
                    Challenges →
                  </Link>
                  <Link to={`/games/${gameId}/scoreboard`} className={classes.quickLink}>
                    Leaderboard →
                  </Link>
                  <Link to={`/games/${gameId}`} className={classes.quickLink}>
                    Event overview →
                  </Link>
                </div>
              </div>
            </aside>
          </div>
        </>
      )}
    </PlayerShell>
  )
}

export default Dashboard

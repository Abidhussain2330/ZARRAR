import dayjs from 'dayjs'
import { FC, lazy, Suspense, useEffect, useMemo, useRef, useState } from 'react'
import { useParams } from 'react-router'
import useSWR from 'swr'
import { Empty, ErrorState, Loading, arena } from '@Components/player/Arena'
import { GameSubNav } from '@Components/player/GameSubNav'
import { PlayerShell } from '@Components/player/PlayerShell'
import { useGame } from '@Hooks/useGame'
import { usePageTitle } from '@Hooks/usePageTitle'
import { TopTimeLine } from '@Api'
import classes from '@Styles/player/Leaderboard.module.css'

const TABLE_SIZE = 20
const GRAPH_TEAMS = 10

const LeaderboardChart = lazy(() =>
  import('@Components/player/LeaderboardChart').then(({ LeaderboardChart: Chart }) => ({ default: Chart }))
)

interface LeaderboardEntry {
  id: number
  name: string
  bio?: string | null
  avatar?: string | null
  divisionId?: number | null
  score: number
  rank: number
  divisionRank?: number | null
  solvedCount: number
  lastSubmissionTime: string
}

interface LeaderboardPayload {
  entries: LeaderboardEntry[]
  currentTeam?: LeaderboardEntry | null
  totalCount: number
  updateTimeUtc: string
}

const fetcher = async (url: string) => {
  const res = await fetch(url, { credentials: 'include' })
  if (!res.ok) throw Object.assign(new Error('Request failed'), { status: res.status })
  return res.json()
}

const MEDALS = ['#ffd166', '#c7ccd8', '#cd7f4e']

const initials = (name: string) => name.slice(0, 2).toUpperCase()

const Avatar: FC<{ entry: LeaderboardEntry; podium?: boolean }> = ({ entry, podium }) =>
  entry.avatar ? (
    <img
      className={podium ? classes.podiumAvatar : classes.rowAvatar}
      src={entry.avatar}
      alt=""
      loading="lazy"
    />
  ) : (
    <span className={podium ? classes.podiumAvatarFallback : classes.rowAvatarFallback} aria-hidden="true">
      {initials(entry.name)}
    </span>
  )

const Row: FC<{ entry: LeaderboardEntry; own?: boolean }> = ({ entry, own }) => (
  <tr className={own ? classes.ownRow : undefined}>
    <td className={classes.rankCell}>#{entry.rank}</td>
    <td>
      <div className={classes.teamCell}>
        <Avatar entry={entry} />
        <div>
          <div className={classes.rowName}>
            {entry.name}
            {own && <span className={classes.ownTag}>YOUR TEAM</span>}
          </div>
          {entry.bio && <p className={classes.rowBio}>{entry.bio}</p>}
        </div>
      </div>
    </td>
    <td className={`${classes.numeric} ${classes.hideSm}`}>{entry.solvedCount}</td>
    <td className={`${classes.numeric} ${classes.hideSm}`}>
      {entry.lastSubmissionTime ? dayjs(entry.lastSubmissionTime).format('D MMM HH:mm') : '—'}
    </td>
    <td className={`${classes.numeric} ${classes.scoreCell}`}>{entry.score.toLocaleString()}</td>
  </tr>
)

const Leaderboard: FC = () => {
  const { id } = useParams()
  const numId = Number.parseInt(id ?? '', 10)
  const { game } = useGame(numId)

  const [view, setView] = useState<'podium' | 'graph'>('podium')
  const [menuOpen, setMenuOpen] = useState(false)
  const switchRef = useRef<HTMLDivElement>(null)

  usePageTitle(game?.title)

  const {
    data,
    error,
    mutate: retry,
  } = useSWR<LeaderboardPayload>(
    Number.isInteger(numId) && numId > 0 ? `/api/game/${numId}/leaderboard?count=${TABLE_SIZE}` : null,
    fetcher,
    { refreshInterval: 30 * 1000 }
  )

  // The timeline is requested only once the graph view is selected — that laziness is the
  // whole point of the split endpoint. SWR keeps it cached afterwards, so toggling back and
  // forth doesn't refetch.
  const { data: timeline, error: timelineError } = useSWR<TopTimeLine[]>(
    view === 'graph' && Number.isInteger(numId) && numId > 0
      ? `/api/game/${numId}/leaderboard/timeline?count=${GRAPH_TEAMS}`
      : null,
    fetcher,
    { refreshInterval: 60 * 1000, revalidateOnFocus: false }
  )

  useEffect(() => {
    if (!menuOpen) return
    const onDown = (e: MouseEvent) => {
      if (!switchRef.current?.contains(e.target as Node)) setMenuOpen(false)
    }
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setMenuOpen(false)
    document.addEventListener('mousedown', onDown)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [menuOpen])

  const entries = data?.entries ?? []
  const podium = entries.slice(0, 3)

  // The podium already shows ranks 1–3, so the table starts at 4 to avoid showing them twice.
  // In graph view there's no podium, so the table carries the full ranking from 1.
  const tableRows = view === 'podium' ? entries.slice(3) : entries

  const ownTeam = data?.currentTeam ?? null
  const hasGap = ownTeam !== null && ownTeam.rank > (entries.at(-1)?.rank ?? 0) + 1

  const body = (() => {
    if (error) {
      return (
        <ErrorState
          title="Leaderboard unavailable"
          body={
            error.status === 400
              ? "This event hasn't started yet, so there are no standings."
              : "The standings couldn't be loaded."
          }
          action={
            <button type="button" className={`${arena.btn} ${arena.btnGhost} bqFocusRing`} onClick={() => retry()}>
              Retry
            </button>
          }
        />
      )
    }

    if (!data) return <Loading label="Loading standings…" />

    if (entries.length === 0) {
      return <Empty title="No standings yet" body="Rankings appear once teams start solving challenges." />
    }

    return (
      <>
        {view === 'podium' ? (
          <div className={classes.podium}>
            {podium.map((entry, i) => (
              <div key={entry.id} className={`${classes.podiumSlot} ${classes[`rank${i + 1}`]}`}>
                <span className={classes.medal} style={{ background: MEDALS[i] }}>
                  {i + 1}
                </span>
                <Avatar entry={entry} podium />
                <p className={classes.podiumName} title={entry.name}>
                  {entry.name}
                </p>
                <p className={classes.podiumScore}>{entry.score.toLocaleString()}</p>
                <p className={classes.podiumSub}>
                  {entry.solvedCount} SOLVE{entry.solvedCount === 1 ? '' : 'S'}
                </p>
              </div>
            ))}
          </div>
        ) : (
          <div className={classes.graphPanel}>
            <div className={classes.graphHead}>
              <h2 className={classes.graphTitle}>Score progression</h2>
              <p className={classes.graphNote}>Top {GRAPH_TEAMS} teams</p>
            </div>
            <div className={classes.graphBody}>
              {timelineError ? (
                <ErrorState title="Progression unavailable" body="The score history couldn't be loaded." />
              ) : !timeline ? (
                <Loading label="Loading progression…" />
              ) : timeline.length === 0 ? (
                <Empty title="No progression yet" body="Scored solves will draw the progression here." />
              ) : (
                <Suspense fallback={<Loading label="Loading graph…" />}>
                  <LeaderboardChart teams={timeline} start={game?.start ?? 0} end={game?.end ?? 0} />
                </Suspense>
              )}
            </div>
          </div>
        )}

        <div className={classes.tableWrap}>
          <table className={classes.table}>
            <thead>
              <tr>
                <th>Rank</th>
                <th>Team</th>
                <th className={`${classes.numeric} ${classes.hideSm}`}>Solves</th>
                <th className={`${classes.numeric} ${classes.hideSm}`}>Last solve</th>
                <th className={classes.numeric}>Score</th>
              </tr>
            </thead>
            <tbody>
              {tableRows.map((entry) => (
                <Row key={entry.id} entry={entry} own={entry.id === ownTeam?.id} />
              ))}

              {ownTeam && (
                <>
                  {hasGap && (
                    <tr className={classes.gapRow}>
                      <td colSpan={5}>· · ·</td>
                    </tr>
                  )}
                  <Row entry={ownTeam} own />
                </>
              )}
            </tbody>
          </table>
        </div>
      </>
    )
  })()

  return (
    <PlayerShell wide allowAdmin>
      <GameSubNav gameId={numId} title={game?.title} active="scoreboard" />

      <div className={classes.head}>
        <div className={classes.headText}>
          <span className={`${classes.eyebrow} zr-cursor`}>// ZARRAR / STANDINGS</span>
          <h1 className={classes.title}>
            <span className="zr-glitch" data-text="LEADERBOARD">
              LEADERBOARD
            </span>
          </h1>
          {data && (
            <p className={classes.meta}>
              TOP {Math.min(TABLE_SIZE, data.totalCount)} OF {data.totalCount} TEAMS
              {data.updateTimeUtc && ` · UPDATED ${dayjs(data.updateTimeUtc).format('HH:mm:ss')}`}
            </p>
          )}
        </div>

        <div className={classes.viewSwitch} ref={switchRef}>
          <button
            type="button"
            className={`${classes.viewButton} bqFocusRing`}
            aria-haspopup="menu"
            aria-expanded={menuOpen}
            onClick={() => setMenuOpen((v) => !v)}
          >
            {view === 'podium' ? 'Podium' : 'Graph'}
            <span className={`${classes.caret} ${menuOpen ? classes.caretOpen : ''}`} aria-hidden="true">
              ▼
            </span>
          </button>

          {menuOpen && (
            <div className={classes.menu} role="menu">
              <button
                type="button"
                role="menuitem"
                className={`${classes.menuItem} ${view === 'podium' ? classes.menuItemOn : ''}`}
                onClick={() => {
                  setView('podium')
                  setMenuOpen(false)
                }}
              >
                <span className={classes.menuLabel}>Podium</span>
                <span className={classes.menuHint}>Top 3, table from rank 4</span>
              </button>
              <button
                type="button"
                role="menuitem"
                className={`${classes.menuItem} ${view === 'graph' ? classes.menuItemOn : ''}`}
                onClick={() => {
                  setView('graph')
                  setMenuOpen(false)
                }}
              >
                <span className={classes.menuLabel}>Score progression</span>
                <span className={classes.menuHint}>Top 10 graph, table from rank 1</span>
              </button>
            </div>
          )}
        </div>
      </div>

      {body}
    </PlayerShell>
  )
}

export default Leaderboard

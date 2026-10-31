import { FC, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { Empty, ErrorState, Loading, PageHead, Pager, arena } from '@Components/player/Arena'
import { EventTimeline, TimelineEvent } from '@Components/player/EventTimeline'
import { PlayerShell } from '@Components/player/PlayerShell'
import dayjs from 'dayjs'
import { useLanguage } from '@Utils/I18n'
import { GameStatus, getGameStatus, toLimitTag } from '@Hooks/useGame'
import { usePageTitle } from '@Hooks/usePageTitle'
import api, { BasicGameInfoModel } from '@Api'
import classes from '@Styles/player/Events.module.css'

const ITEM_PER_PAGE = 12

type Filter = 'all' | GameStatus

const FILTERS: { key: Filter; label: string }[] = [
  { key: 'all', label: 'ALL' },
  { key: GameStatus.OnGoing, label: 'LIVE' },
  { key: GameStatus.Coming, label: 'UPCOMING' },
  { key: GameStatus.Ended, label: 'ENDED' },
]

const STATUS_STYLE: Record<GameStatus, { label: string; color: string }> = {
  [GameStatus.OnGoing]: { label: 'LIVE', color: 'var(--bq-live)' },
  [GameStatus.Coming]: { label: 'UPCOMING', color: 'var(--bq-solved)' },
  [GameStatus.Ended]: { label: 'ENDED', color: 'var(--bq-locked)' },
}

const EventCard: FC<{ game: BasicGameInfoModel }> = ({ game }) => {
  const { t } = useTranslation()
  const { locale } = useLanguage()
  const { startTime, endTime, status, progress } = getGameStatus(game)
  const duration = Math.max(1, endTime.diff(startTime, 'hours'))
  const style = STATUS_STYLE[status]

  return (
    <Link to={`/games/${game.id}`} className={`${classes.card} bqFocusRing`}>
      <span className={classes.accent} style={{ background: style.color }} aria-hidden="true" />

      <div className={classes.poster}>
        {game.poster ? (
          <img src={game.poster} alt="" loading="lazy" />
        ) : (
          <span className={classes.posterFallback} aria-hidden="true">
            {(game.title ?? '?').slice(0, 2).toUpperCase()}
          </span>
        )}
        <span
          className={classes.statusBadge}
          style={{
            color: style.color,
            borderColor: `color-mix(in srgb, ${style.color} 35%, transparent)`,
            background: `color-mix(in srgb, ${style.color} 14%, rgb(4 6 14 / 85%))`,
          }}
        >
          {style.label}
        </span>
      </div>

      <div className={classes.body}>
        <div className={classes.tagRow}>
          <span className={classes.metaTag}>{toLimitTag(t, game.limit)}</span>
          <span className={classes.metaTag}>{t('game.content.duration', { hours: duration })}</span>
        </div>

        <h2 className={classes.title}>{game.title}</h2>

        {game.summary && <p className={classes.summary}>{game.summary}</p>}

        <div className={classes.times}>
          <span>{startTime.locale(locale).format('D MMM YYYY, HH:mm')}</span>
          <span className={classes.arrow} aria-hidden="true">
            →
          </span>
          <span>{endTime.locale(locale).format('D MMM YYYY, HH:mm')}</span>
        </div>

        {/* Progress only means something while a game is running. */}
        {status === GameStatus.OnGoing && (
          <div className={classes.progressTrack}>
            <span className={classes.progressFill} style={{ width: `${Math.min(100, progress)}%` }} />
          </div>
        )}
      </div>
    </Link>
  )
}

const Events: FC = () => {
  const { t } = useTranslation()
  const [page, setPage] = useState(1)
  const [filter, setFilter] = useState<Filter>('all')
  const [query, setQuery] = useState('')

  const {
    data: games,
    error,
    mutate: retry,
  } = api.game.useGameGames(
    { count: ITEM_PER_PAGE, skip: (page - 1) * ITEM_PER_PAGE },
    { refreshInterval: 5 * 60 * 1000 }
  )

  usePageTitle(t('game.title.index'))

  /**
   * Filtering runs on the current page only. The list endpoint pages server-side
   * and takes no status or search parameter, so a client-side filter can't reach
   * rows it hasn't fetched — the count label says "on this page" to be honest
   * about that rather than implying a whole-catalogue search.
   */
  const visible = useMemo(() => {
    const rows = games?.data ?? []
    const q = query.trim().toLowerCase()

    return rows.filter((game) => {
      const { status } = getGameStatus(game)
      if (filter !== 'all' && status !== filter) return false
      if (!q) return true
      return (game.title ?? '').toLowerCase().includes(q) || (game.summary ?? '').toLowerCase().includes(q)
    })
  }, [games, filter, query])

  // The timeline draws from the unfiltered page: it's an overview of what's scheduled, so
  // hiding bars because of a status chip or search term would misrepresent the calendar.
  const timelineEvents = useMemo<TimelineEvent[]>(
    () =>
      (games?.data ?? []).map((game) => {
        const { status } = getGameStatus(game)
        const palette =
          status === GameStatus.OnGoing
            ? { color: '#1fa968', colorHi: '#2ee08a' }
            : status === GameStatus.Coming
              ? { color: '#3f9fd0', colorHi: '#7fd3ff' }
              : { color: '#5a6480', colorHi: '#8b93ad' }

        return {
          id: game.id!,
          title: game.title ?? 'Untitled event',
          start: dayjs(game.start),
          end: dayjs(game.end),
          ...palette,
        }
      }),
    [games]
  )

  const pageCount = Math.ceil((games?.total ?? 0) / ITEM_PER_PAGE)
  const filtering = filter !== 'all' || query.trim().length > 0

  return (
    <PlayerShell>
      <PageHead
        eyebrow="ZARRAR / MATCHES"
        title="Events"
        subtitle="Every competition on the platform. Join one to unlock its challenges and scoreboard."
        actions={
          <>
            <input
              type="search"
              className={`${arena.search} bqFocusRing`}
              placeholder="search events…"
              aria-label="Search events"
              value={query}
              onChange={(e) => setQuery(e.currentTarget.value)}
            />
            <span className={arena.count}>
              {filtering ? `${visible.length} ON THIS PAGE` : `${games?.total ?? 0} TOTAL`}
            </span>
          </>
        }
      />

      {games && timelineEvents.length > 0 && <EventTimeline events={timelineEvents} />}

      <div className={arena.chipRow} role="group" aria-label="Filter by status">
        {FILTERS.map((f) => (
          <button
            key={f.key}
            type="button"
            className={`${arena.chip} bqFocusRing ${filter === f.key ? arena.chipOn : ''}`}
            aria-pressed={filter === f.key}
            onClick={() => setFilter(f.key)}
          >
            {f.label}
          </button>
        ))}
      </div>

      {error ? (
        <ErrorState
          title="Couldn't load events"
          body="The server didn't answer. Check your connection and try again."
          action={
            <button type="button" className={`${arena.btn} ${arena.btnGhost} bqFocusRing`} onClick={() => retry()}>
              Retry
            </button>
          }
        />
      ) : !games ? (
        <Loading label="Loading events…" />
      ) : visible.length === 0 ? (
        <Empty
          title={filtering ? 'Nothing matches those filters' : 'No events scheduled'}
          body={
            filtering
              ? 'Try a different status, or clear the search box.'
              : 'Organisers haven\u2019t published an event yet. Announcements will appear on the home page.'
          }
          action={
            filtering ? (
              <button
                type="button"
                className={`${arena.btn} ${arena.btnGhost} bqFocusRing`}
                onClick={() => {
                  setFilter('all')
                  setQuery('')
                }}
              >
                Clear filters
              </button>
            ) : undefined
          }
        />
      ) : (
        <div className={classes.grid}>
          {visible.map((game) => (
            <EventCard key={game.id} game={game} />
          ))}
        </div>
      )}

      <Pager page={page} pageCount={pageCount} onChange={setPage} />
    </PlayerShell>
  )
}

export default Events

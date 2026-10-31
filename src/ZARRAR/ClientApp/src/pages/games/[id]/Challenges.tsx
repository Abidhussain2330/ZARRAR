import { useLocalStorage } from '@mantine/hooks'
import dayjs from 'dayjs'
import { FC, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation, useParams } from 'react-router'
import { GameChallengeModal } from '@Components/GameChallengeModal'
import { GameNoticePanel } from '@Components/GameNoticePanel'
import { TeamRank } from '@Components/TeamRank'
import { WriteupSubmitModal } from '@Components/WriteupSubmitModal'
import { WithRole } from '@Components/WithRole'
import { PlayerLiveGate } from '@Components/zarrar/PlayerLiveGate'
import { Empty, ErrorState, Loading, arena, categoryColor } from '@Components/player/Arena'
import { GameSubNav } from '@Components/player/GameSubNav'
import { DIFFICULTY, Difficulty, PlayerChallengeCard, ViewMode, toDifficulty } from '@Components/player/PlayerChallengeCard'
import { PlayerShell } from '@Components/player/PlayerShell'
import { useChallengeCategoryLabelMap } from '@Utils/Shared'
import { useGameTeamInfo } from '@Hooks/useGame'
import { usePageTitle } from '@Hooks/usePageTitle'
import { ChallengeCategory, ChallengeInfo, ChallengeType, Role, SubmissionType } from '@Api'
import classes from '@Styles/player/Challenges.module.css'

type SolvedFilter = 'all' | 'solved' | 'unsolved'
type SortKey = 'score-desc' | 'score-asc' | 'solves-desc' | 'difficulty-desc' | 'title-asc'

const DIFFICULTY_KEYS: Difficulty[] = ['easy', 'medium', 'hard', 'expert', 'insane']

const isContainer = (c: ChallengeInfo) =>
  c.type === ChallengeType.StaticContainer || c.type === ChallengeType.DynamicContainer

const Challenges: FC = () => {
  const { hash } = useLocation()
  const { id } = useParams()
  const numId = Number.parseInt(id ?? '-1', 10)
  const { t } = useTranslation()

  const { teamInfo, game, error, mutate } = useGameTeamInfo(numId)
  const categoryLabels = useChallengeCategoryLabelMap()

  const [query, setQuery] = useState('')
  const [category, setCategory] = useState<string>('all')
  const [difficulty, setDifficulty] = useState<string>('all')
  const [solvedFilter, setSolvedFilter] = useState<SolvedFilter>('all')
  const [sort, setSort] = useState<SortKey>('score-desc')
  const [viewMode, setViewMode] = useLocalStorage<ViewMode>({
    key: 'zarrar-challenge-view',
    defaultValue: 'grid',
    getInitialValueInEffect: false,
  })

  const [challenge, setChallenge] = useState<ChallengeInfo | null>(null)
  const [detailOpened, setDetailOpened] = useState(false)
  const [writeupOpened, setWriteupOpened] = useState(false)

  usePageTitle(game?.title)

  const challenges = teamInfo?.challenges
  const allChallenges = useMemo(() => Object.values(challenges ?? {}).flat(), [challenges])
  const solvedChallenges = teamInfo?.rank?.solvedChallenges

  const solvedIds = useMemo(
    () =>
      new Set(
        (solvedChallenges ?? []).filter((item) => item.type !== SubmissionType.Unaccepted).map((item) => item.id)
      ),
    [solvedChallenges]
  )

  const categoryOptions = useMemo(
    () =>
      Object.entries(challenges ?? {})
        .map(([c, list]) => ({
          value: c,
          label: categoryLabels.get(c as ChallengeCategory)?.name ?? c,
          count: list.length,
        }))
        .sort((a, b) => a.label.localeCompare(b.label)),
    [categoryLabels, challenges]
  )

  const gameEnded = dayjs().isAfter(dayjs(game?.end))

  // A per-challenge deadline can close a challenge while the event is still live.
  // Practice mode on a finished game reopens everything.
  const isLocked = (item: ChallengeInfo) =>
    !!item.deadline && dayjs().isAfter(dayjs(item.deadline)) && !(gameEnded && game?.practiceMode)

  const filtered = useMemo(() => {
    const q = query.trim().toLocaleLowerCase()

    const rows = allChallenges.filter((c) => {
      const solved = solvedIds.has(c.id)
      if (q && !c.title.toLocaleLowerCase().includes(q)) return false
      if (category !== 'all' && c.category !== category) return false
      if (difficulty !== 'all' && toDifficulty(c.difficulty) !== difficulty) return false
      if (solvedFilter === 'solved' && !solved) return false
      if (solvedFilter === 'unsolved' && solved) return false
      return true
    })

    return rows.sort((a, b) => {
      switch (sort) {
        case 'score-asc':
          return a.score - b.score || a.title.localeCompare(b.title)
        case 'solves-desc':
          return b.solved - a.solved || b.score - a.score
        case 'difficulty-desc':
          return b.difficulty - a.difficulty || b.score - a.score
        case 'title-asc':
          return a.title.localeCompare(b.title)
        default:
          return b.score - a.score || a.title.localeCompare(b.title)
      }
    })
  }, [allChallenges, category, difficulty, query, solvedFilter, solvedIds, sort])

  // Deep links carry `#<id>-<slug>`, so a shared challenge link opens the modal.
  useEffect(() => {
    const challengeId = Number.parseInt(hash.slice(1).split('-')[0], 10)
    if (!Number.isFinite(challengeId) || challengeId < 0 || challenge?.id === challengeId) return

    const selected = allChallenges.find((item) => item.id === challengeId)
    if (selected) {
      setChallenge(selected)
      setDetailOpened(true)
    }
  }, [allChallenges, challenge?.id, hash])

  const clearFilters = () => {
    setQuery('')
    setCategory('all')
    setDifficulty('all')
    setSolvedFilter('all')
    setSort('score-desc')
  }

  const hasFilters =
    query.trim().length > 0 || category !== 'all' || difficulty !== 'all' || solvedFilter !== 'all'

  const openChallenge = (item: ChallengeInfo) => {
    setChallenge(item)
    setDetailOpened(true)
    window.location.hash = `#${item.id}-${encodeURIComponent(item.title.replace(/ /g, '-'))}`
  }

  const body = (() => {
    if (error) {
      return (
        <ErrorState
          title="Challenges unavailable"
          body="The event's challenge data couldn't be loaded."
          action={
            <button type="button" className={`${arena.btn} ${arena.btnGhost} bqFocusRing`} onClick={() => mutate()}>
              Retry
            </button>
          }
        />
      )
    }

    if (!teamInfo) return <Loading label="Loading challenges…" />

    if (allChallenges.length === 0) {
      return <Empty title="No challenges published" body={t('game.content.no_challenge')} />
    }

    if (!teamInfo.rank?.divisionId && !teamInfo.rank?.rank) {
      return (
        <Empty
          title={t('game.content.scoreboard_not_ready.title')}
          body={t('game.content.scoreboard_not_ready.comment')}
        />
      )
    }

    if (filtered.length === 0) {
      return (
        <Empty
          title="No matching challenges"
          body="Adjust the search or filters to see what's available to your team."
          action={
            <button type="button" className={`${arena.btn} ${arena.btnGhost} bqFocusRing`} onClick={clearFilters}>
              Clear filters
            </button>
          }
        />
      )
    }

    return (
      <div className={`${classes.grid} ${viewMode === 'list' ? classes.gridList : ''}`}>
        {filtered.map((item) => (
          <PlayerChallengeCard
            key={item.id}
            challenge={item}
            solved={solvedIds.has(item.id)}
            locked={isLocked(item)}
            onClick={() => openChallenge(item)}
          />
        ))}
      </div>
    )
  })()

  return (
    <PlayerShell wide score={teamInfo?.rank?.score}>
      <WithRole requiredRole={Role.User}>
        <GameSubNav gameId={numId} title={game?.title} active="challenges" />
        {/* ZARRAR showdown: screen-share gate, LIVE strip and pause banner (no-op in normal games). */}
        <PlayerLiveGate gameId={numId} onChange={() => mutate()} />

        <div className={classes.layout}>
          <div>
            <div className={classes.toolbar} role="search" aria-label="Filter challenges">
              <input
                type="search"
                className={`${arena.search} bqFocusRing`}
                placeholder="search challenges…"
                aria-label="Search challenges by title"
                value={query}
                onChange={(e) => setQuery(e.currentTarget.value)}
              />

              {/* Sort stays a select: it is one-of-many with no useful at-a-glance state.
                  Category and status are pills below, per the approved mockup — a dropdown
                  hides which categories even exist until you open it. */}
              <span className={classes.selectWrap}>
                <select
                  className={`${classes.select} bqFocusRing`}
                  aria-label="Sort by"
                  value={sort}
                  onChange={(e) => setSort(e.currentTarget.value as SortKey)}
                >
                  <option value="score-desc">Points: high to low</option>
                  <option value="score-asc">Points: low to high</option>
                  <option value="solves-desc">Most solved</option>
                  <option value="difficulty-desc">Difficulty</option>
                  <option value="title-asc">Title A–Z</option>
                </select>
                <span className={classes.selectArrow} aria-hidden="true">
                  ▼
                </span>
              </span>

              <span className={classes.viewToggle} role="group" aria-label="Layout">
                <button
                  type="button"
                  className={`${classes.viewBtn} ${viewMode === 'grid' ? classes.viewBtnOn : ''}`}
                  aria-pressed={viewMode === 'grid'}
                  aria-label="Grid view"
                  onClick={() => setViewMode('grid')}
                >
                  ▦
                </button>
                <button
                  type="button"
                  className={`${classes.viewBtn} ${viewMode === 'list' ? classes.viewBtnOn : ''}`}
                  aria-pressed={viewMode === 'list'}
                  aria-label="List view"
                  onClick={() => setViewMode('list')}
                >
                  ☰
                </button>
              </span>

              {game?.writeupRequired && (
                <button
                  type="button"
                  className={`${arena.btn} ${arena.btnGhost} bqFocusRing`}
                  onClick={() => setWriteupOpened(true)}
                >
                  {t('game.button.submit_writeup')}
                </button>
              )}
            </div>

            {/* Category pills carry their own colour, so the palette doubles as a legend for
                the card accents. */}
            <div className={classes.pillRow} role="group" aria-label="Filter by category">
              <button
                type="button"
                className={`${classes.pill} bqFocusRing ${category === 'all' ? classes.pillOn : ''}`}
                aria-pressed={category === 'all'}
                onClick={() => setCategory('all')}
              >
                ALL
                <span className={classes.pillCount}>{allChallenges.length}</span>
              </button>
              {categoryOptions.map((c) => {
                const tint = categoryColor(c.value)
                const on = category === c.value
                return (
                  <button
                    key={c.value}
                    type="button"
                    className={`${classes.pill} bqFocusRing ${on ? classes.pillOn : ''}`}
                    aria-pressed={on}
                    onClick={() => setCategory(c.value)}
                    style={
                      on
                        ? { color: tint, borderColor: tint, background: `color-mix(in srgb, ${tint} 18%, transparent)` }
                        : { color: tint }
                    }
                  >
                    <span className={classes.pillDot} style={{ background: tint }} aria-hidden="true" />
                    {c.label.toUpperCase()}
                    <span className={classes.pillCount}>{c.count}</span>
                  </button>
                )
              })}
            </div>

            <div className={classes.pillRow} role="group" aria-label="Filter by status">
              {(
                [
                  ['all', 'ALL'],
                  ['unsolved', 'UNSOLVED'],
                  ['solved', 'SOLVED'],
                ] as const
              ).map(([key, label]) => (
                <button
                  key={key}
                  type="button"
                  className={`${classes.pillSm} bqFocusRing ${solvedFilter === key ? classes.pillOn : ''}`}
                  aria-pressed={solvedFilter === key}
                  onClick={() => setSolvedFilter(key)}
                >
                  {label}
                </button>
              ))}

              <span className={classes.pillDivider} aria-hidden="true" />

              {(['all', ...DIFFICULTY_KEYS] as const).map((key) => (
                <button
                  key={key}
                  type="button"
                  className={`${classes.pillSm} bqFocusRing ${difficulty === key ? classes.pillOn : ''}`}
                  aria-pressed={difficulty === key}
                  onClick={() => setDifficulty(key)}
                  style={key !== 'all' && difficulty === key ? { color: DIFFICULTY[key].color } : undefined}
                >
                  {key === 'all' ? 'ANY DIFFICULTY' : DIFFICULTY[key].label}
                </button>
              ))}
            </div>

            <div className={classes.resultBar} aria-live="polite">
              <p className={classes.resultCount}>
                <strong>{filtered.length}</strong> OF {allChallenges.length} CHALLENGES
              </p>
              <div className={classes.resultMetaRow}>
                <p className={classes.resultMeta}>{solvedIds.size} SOLVED</p>
                <p className={classes.resultMeta}>{allChallenges.filter(isContainer).length} CONTAINER</p>
                {hasFilters && (
                  <button type="button" className={classes.clearBtn} onClick={clearFilters}>
                    Clear filters
                  </button>
                )}
              </div>
            </div>

            {body}
          </div>

          <aside className={classes.aside} aria-label="Team and event updates">
            <TeamRank />
            <GameNoticePanel />
          </aside>
        </div>

        {game?.writeupRequired && teamInfo && (
          <WriteupSubmitModal
            opened={writeupOpened}
            onClose={() => setWriteupOpened(false)}
            withCloseButton={false}
            size="40%"
            gameId={numId}
            writeupDeadline={teamInfo.writeupDeadline}
          />
        )}

        {challenge?.id && (
          <GameChallengeModal
            gameId={numId}
            gameTitle={game?.title ?? ''}
            opened={detailOpened}
            onClose={() => {
              window.location.hash = ''
              setDetailOpened(false)
            }}
            gameEnded={gameEnded}
            practiceMode={game?.practiceMode}
            status={solvedChallenges?.find((item) => item.id === challenge.id)?.type}
            cateData={categoryLabels.get(challenge.category ?? ChallengeCategory.Misc)!}
            title={challenge.title}
            score={challenge.score}
            solveCount={challenge.solved}
            difficulty={toDifficulty(challenge.difficulty)}
            locked={isLocked(challenge)}
            challengeId={challenge.id}
          />
        )}
      </WithRole>
    </PlayerShell>
  )
}

export default Challenges

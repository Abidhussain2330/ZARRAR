import { Anchor, Button, Group, Select } from '@mantine/core'
import {
  mdiAccountCogOutline,
  mdiAccountGroupOutline,
  mdiCogOutline,
  mdiFileDocumentOutline,
  mdiFlagOutline,
  mdiPackageVariantClosed,
  mdiPuzzleOutline,
  mdiRefresh,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import {
  AdminCellMeta,
  AdminCellStack,
  AdminLayout,
  AdminMetricGrid,
  AdminMonoValue,
  AdminOutageNotice,
  AdminStatusBadge,
  AdminTable,
} from '@Components/admin/shell'
import { CategoryBadge, DataTableColumn, EmptyState, ErrorState, Panel, StatCard } from '@Components/zarrar'
import { TaskStatusColorMap } from '@Utils/Shared'
import api, { CategoryStatisticsModel, DashboardOverviewModel, LiveSolveModel, LogMessageModel, TaskStatus, TopParticipantModel } from '@Api'
import classes from '@Styles/admin/AdminOverview.module.css'

const RECENT_LOG_COUNT = 8
const LIVE_SOLVE_COUNT = 10
const LIVE_OPTIONS = { refreshInterval: 15_000, revalidateOnFocus: true, shouldRetryOnError: false } as const

interface AdminCounts {
  users?: number
  teams?: number
  games?: number
  instances?: number
  files?: number
}

const STATUS_TONE: Record<string, 'success' | 'warning' | 'critical' | 'neutral'> = {
  green: 'success',
  lime: 'success',
  yellow: 'warning',
  orange: 'warning',
  red: 'critical',
  alert: 'critical',
}

/**
 * Admin Command Center overview.
 *
 * Every tile is a real count read from an endpoint that already existed — the
 * `total` an `ArrayResponse` reports, the live container list, and the public
 * dashboard counters. Nothing here is synthesised: a counter whose request
 * fails renders as unavailable rather than as a zero, because a fabricated
 * zero on an operations dashboard is worse than an honest gap.
 */
const AdminOverview: FC = () => {
  const { t } = useTranslation()

  const [counts, setCounts] = useState<AdminCounts>({})
  const [degraded, setDegraded] = useState<string[]>([])
  const [logs, setLogs] = useState<LogMessageModel[]>()
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<unknown>(null)
  const [refreshing, setRefreshing] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    const failures: string[] = []
    const next: AdminCounts = {}

    /*
     * The counters come from five independent endpoints. One of them failing
     * (a degraded blob store, say) must not blank the whole page, so each is
     * settled separately and only the failed ones are reported as unavailable.
     */
    const results = await Promise.allSettled([
      api.admin.adminUsers({ count: 1, skip: 0 }),
      api.admin.adminTeams({ count: 1, skip: 0 }),
      api.edit.editGetGames({ count: 1, skip: 0 }),
      api.admin.adminInstances(),
      api.admin.adminFiles({ count: 1, skip: 0 }),
    ])

    const labels = [
      t('admin.tab.users'),
      t('admin.tab.teams'),
      t('admin.tab.games.index'),
      t('admin.tab.instances'),
      t('admin.nav.files', { defaultValue: 'Files' }),
    ]
    const keys: (keyof AdminCounts)[] = ['users', 'teams', 'games', 'instances', 'files']

    results.forEach((result, index) => {
      if (result.status === 'rejected') {
        failures.push(labels[index])
        return
      }

      const payload = result.value.data
      next[keys[index]] = payload.total ?? payload.length
    })

    setCounts(next)
    setDegraded(failures)

    if (failures.length === results.length) {
      setError(new Error('all counters failed'))
    }

    try {
      const res = await api.admin.adminLogs({ level: 'All', count: RECENT_LOG_COUNT, skip: 0 })
      setLogs(res.data)
    } catch {
      setLogs([])
      failures.push(t('admin.tab.logs'))
      setDegraded([...failures])
    } finally {
      setLoading(false)
    }
  }, [t])

  useEffect(() => {
    load()
  }, [load])

  const {
    data: overview,
    error: overviewErr,
    isLoading: overviewLoading,
    mutate: refreshOverview,
  } = api.dashboard.useDashboardOverview(LIVE_OPTIONS)

  const {
    data: liveSolves,
    error: liveErr,
    isLoading: liveLoading,
    mutate: refreshLive,
  } = api.dashboard.useDashboardLiveSolves({ count: LIVE_SOLVE_COUNT }, LIVE_OPTIONS)

  const pulseMetric = (overview: DashboardOverviewModel | undefined, value: number | undefined) =>
    !overview || value === undefined
      ? t('admin.state.unavailable', { defaultValue: 'Unavailable' })
      : value.toLocaleString()

  const metric = (value: number | undefined) =>
    value === undefined ? t('admin.state.unavailable', { defaultValue: 'Unavailable' }) : value.toLocaleString()

  const liveSolveColumns: DataTableColumn<LiveSolveModel>[] = [
    {
      key: 'time',
      header: t('common.label.time'),
      width: '10rem',
      nowrap: true,
      render: (solve) => <AdminCellMeta>{dayjs(solve.time).format('HH:mm:ss')}</AdminCellMeta>,
    },
    {
      key: 'team',
      header: t('admin.tab.teams'),
      rowHeader: true,
      render: (solve) => (
        <AdminCellStack>
          <AdminCellMeta title={solve.team}>{solve.team}</AdminCellMeta>
          {solve.user && <AdminMonoValue value={solve.user} />}
        </AdminCellStack>
      ),
    },
    {
      key: 'challenge',
      header: t('admin.label.challenge', { defaultValue: 'Challenge' }),
      render: (solve) => (
        <AdminCellStack>
          <AdminCellMeta title={solve.challenge}>{solve.challenge}</AdminCellMeta>
          <CategoryBadge category={solve.category} size="sm" />
        </AdminCellStack>
      ),
    },
    {
      key: 'game',
      header: t('admin.tab.games.index'),
      render: (solve) => <AdminCellMeta title={solve.gameTitle}>{solve.gameTitle}</AdminCellMeta>,
    },
    {
      key: 'score',
      header: t('admin.label.score', { defaultValue: 'Score' }),
      width: '8rem',
      numeric: true,
      nowrap: true,
      render: (solve) => (
        <AdminCellStack>
          <span>{solve.score.toLocaleString()}</span>
          {solve.isFirstBlood && (
            <AdminStatusBadge solid tone="critical" label={t('admin.label.first_blood', { defaultValue: 'First blood' })} />
          )}
        </AdminCellStack>
      ),
    },
  ]

  const logColumns: DataTableColumn<LogMessageModel>[] = [
    {
      key: 'time',
      header: t('common.label.time'),
      width: '11rem',
      nowrap: true,
      render: (log) => <AdminCellMeta>{dayjs(log.time).format('YYYY-MM-DD HH:mm:ss')}</AdminCellMeta>,
    },
    {
      key: 'who',
      header: t('common.label.user'),
      width: '13rem',
      rowHeader: true,
      render: (log) => (
        <AdminCellStack>
          <AdminCellMeta title={log.name ?? undefined}>{log.name || '—'}</AdminCellMeta>
          <AdminMonoValue value={log.ip} placeholder="—" />
        </AdminCellStack>
      ),
    },
    {
      key: 'message',
      header: t('admin.label.logs.message'),
      render: (log) => <span style={{ overflowWrap: 'anywhere' }}>{log.msg}</span>,
    },
    {
      key: 'status',
      header: t('admin.label.logs.status'),
      width: '8rem',
      render: (log) =>
        log.status ? (
          <AdminStatusBadge
            solid
            label={log.status}
            tone={STATUS_TONE[TaskStatusColorMap.get(log.status as TaskStatus) ?? 'gray'] ?? 'neutral'}
          />
        ) : null,
    },
  ]

  /* ------------------------------------------------------- event breakdown */

  const { data: gamesRes, mutate: refreshGames } = api.edit.useEditGetGames({ count: 100, skip: 0 })
  const now = Date.now()
  const pickableGames = useMemo(
    () =>
      (gamesRes?.data ?? [])
        .filter((g) => g.id !== undefined && g.end > now)
        .sort((a, b) => a.start - b.start),
    // `now` deliberately excluded — recomputing on every tick would reorder the list under the user
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [gamesRes]
  )

  const [selectedGameId, setSelectedGameId] = useState<number | null>(null)

  useEffect(() => {
    if (selectedGameId !== null || pickableGames.length === 0) return
    const active = pickableGames.find((g) => g.start <= now && g.end > now)
    setSelectedGameId((active ?? pickableGames[0]).id ?? null)
    // `now` deliberately excluded — this only needs to run once the list first arrives
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pickableGames, selectedGameId])

  const selectedGame = pickableGames.find((g) => g.id === selectedGameId)
  const selectedIsLive = Boolean(selectedGame && selectedGame.start <= now && selectedGame.end > now)

  const {
    data: categories,
    error: categoriesErr,
    isLoading: categoriesLoading,
    mutate: refreshCategories,
  } = api.dashboard.useDashboardCategoryStatistics(
    { gameId: selectedGameId ?? undefined },
    LIVE_OPTIONS,
    selectedGameId !== null
  )
  const {
    data: topParticipants,
    error: participantsErr,
    isLoading: participantsLoading,
    mutate: refreshParticipants,
  } = api.dashboard.useDashboardTopParticipants(
    { gameId: selectedGameId ?? undefined, count: 8 },
    LIVE_OPTIONS,
    selectedGameId !== null
  )

  const refreshDashboard = async () => {
    setRefreshing(true)
    try {
      await Promise.allSettled([
        load(),
        refreshOverview(),
        refreshLive(),
        refreshGames(),
        refreshCategories(),
        refreshParticipants(),
      ])
    } finally {
      setRefreshing(false)
    }
  }

  const categoryColumns: DataTableColumn<CategoryStatisticsModel>[] = [
    {
      key: 'category',
      header: t('admin.label.category', { defaultValue: 'Category' }),
      rowHeader: true,
      render: (item) => <CategoryBadge category={item.category} />,
    },
    {
      key: 'challenges',
      header: t('admin.label.challenges', { defaultValue: 'Challenges' }),
      numeric: true,
      render: (item) => item.challengeCount.toLocaleString(),
    },
    {
      key: 'solves',
      header: t('admin.label.solves', { defaultValue: 'Solves' }),
      numeric: true,
      render: (item) => item.solvedCount.toLocaleString(),
    },
  ]

  const participantColumns: DataTableColumn<TopParticipantModel>[] = [
    {
      key: 'rank',
      header: t('admin.label.rank', { defaultValue: 'Rank' }),
      width: '4rem',
      numeric: true,
      render: (p) => `#${p.rank}`,
    },
    {
      key: 'team',
      header: t('admin.tab.teams'),
      rowHeader: true,
      render: (p) => <AdminCellMeta title={p.name}>{p.name}</AdminCellMeta>,
    },
    {
      key: 'score',
      header: t('admin.label.score', { defaultValue: 'Score' }),
      numeric: true,
      render: (p) => p.score.toLocaleString(),
    },
  ]

  return (
    <AdminLayout
      title={t('admin.nav.dashboard', { defaultValue: 'Dashboard' })}
      description={t('admin.nav.dashboard_description', {
        defaultValue: 'Platform totals and the most recent system activity.',
      })}
      actions={
        <Group gap="sm">
          <Button
            variant="default"
            loading={refreshing}
            onClick={refreshDashboard}
            leftSection={<Icon path={mdiRefresh} size={0.8} />}
          >
            {t('common.button.refresh', { defaultValue: 'Refresh' })}
          </Button>
          <Button component={Link} to="/admin/games" leftSection={<Icon path={mdiFlagOutline} size={0.8} />}>
            {t('admin.tab.games.index')}
          </Button>
        </Group>
      }
    >
      {degraded.length > 0 && (
        <AdminOutageNotice
          onRetry={refreshDashboard}
          retryLabel={t('common.state.error.retry', { defaultValue: 'Try again' })}
          message={t('admin.state.partial_outage', {
            defaultValue: 'Some data could not be loaded: {{areas}}. The rest of this page is current.',
            areas: degraded.join(', '),
          })}
        />
      )}

      {error ? (
        <ErrorState
          message={t('admin.state.load_failed_overview', { defaultValue: 'Could not load platform totals.' })}
          onRetry={refreshDashboard}
        />
      ) : (
        <AdminMetricGrid label={t('admin.nav.dashboard', { defaultValue: 'Dashboard' })}>
          <StatCard
            label={t('admin.tab.users')}
            value={metric(counts.users)}
            loading={loading}
            icon={<Icon path={mdiAccountCogOutline} size={0.8} />}
          />
          <StatCard
            label={t('admin.tab.teams')}
            value={metric(counts.teams)}
            loading={loading}
            icon={<Icon path={mdiAccountGroupOutline} size={0.8} />}
          />
          <StatCard
            label={t('admin.tab.games.index')}
            value={metric(counts.games)}
            loading={loading}
            icon={<Icon path={mdiFlagOutline} size={0.8} />}
          />
          <StatCard
            label={t('admin.tab.instances')}
            value={metric(counts.instances)}
            loading={loading}
            tone={counts.instances ? 'accent' : 'neutral'}
            icon={<Icon path={mdiPackageVariantClosed} size={0.8} />}
          />
          <StatCard
            label={t('admin.nav.files', { defaultValue: 'Files' })}
            value={metric(counts.files)}
            loading={loading}
            icon={<Icon path={mdiPuzzleOutline} size={0.8} />}
          />
          <StatCard
            label={t('admin.label.solves_24h', { defaultValue: 'Solves (24h)' })}
            value={pulseMetric(overview, overview?.recentSolveCount)}
            loading={overviewLoading}
            tone="success"
            icon={<Icon path={mdiPuzzleOutline} size={0.8} />}
          />
        </AdminMetricGrid>
      )}
      {overviewErr && !overviewLoading && (
        <ErrorState compact message={t('admin.state.load_failed_pulse', { defaultValue: 'Could not load live counters.' })} />
      )}

      <div className={classes.dashRowWide}>
        <Panel
          bare
          title={t('admin.section.live_solves', { defaultValue: 'Live solve feed' })}
          icon={<Icon path={mdiFlagOutline} size={0.8} />}
        >
          {liveErr ? (
            <ErrorState
              compact
              message={t('admin.state.load_failed_live', { defaultValue: 'Could not load the live solve feed.' })}
              onRetry={() => refreshLive()}
            />
          ) : (
            <AdminTable
              caption={t('admin.section.live_solves', { defaultValue: 'Live solve feed' })}
              columns={liveSolveColumns}
              rows={liveSolves ?? []}
              rowKey={(solve, index) => `${solve.time}-${solve.challengeId}-${index}`}
              loading={liveLoading && !liveSolves}
              maxHeight="22rem"
              emptyTitle={t('admin.state.no_solves', { defaultValue: 'No solves yet' })}
            />
          )}
        </Panel>

        <Panel
          bare
          title={t('admin.section.system_pulse', { defaultValue: 'System pulse' })}
          icon={<Icon path={mdiCogOutline} size={0.8} />}
        >
          <div className={classes.pulseRows}>
            <div className={classes.pulseRow}>
              <span>{t('admin.label.active_games', { defaultValue: 'Active games' })}</span>
              <strong>{pulseMetric(overview, overview?.activeGameCount)}</strong>
            </div>
            <div className={classes.pulseRow}>
              <span>{t('admin.label.upcoming_games', { defaultValue: 'Upcoming games' })}</span>
              <strong>{pulseMetric(overview, overview?.upcomingGameCount)}</strong>
            </div>
            <div className={classes.pulseRow}>
              <span>{t('admin.tab.instances')}</span>
              <strong>{metric(counts.instances)}</strong>
            </div>
          </div>
          <div className={classes.pulseActions}>
            <Button component={Link} to="/admin/games" variant="light" fullWidth>
              {t('admin.hero.action.manage_games', { defaultValue: 'Manage games' })}
            </Button>
            <Button component={Link} to="/admin/settings" variant="light" fullWidth>
              {t('admin.nav.settings', { defaultValue: 'Settings' })}
            </Button>
          </div>
        </Panel>
      </div>

      <div className={classes.dashRowEven}>
        <Panel
          bare
          title={t('admin.section.event_breakdown', { defaultValue: 'Event breakdown' })}
          icon={<Icon path={mdiFlagOutline} size={0.8} />}
          actions={
            <div className={classes.breakdownPicker}>
              {selectedGame && (
                <AdminStatusBadge
                  tone={selectedIsLive ? 'success' : 'neutral'}
                  label={selectedIsLive ? t('common.label.live', { defaultValue: 'Live' }) : t('common.label.upcoming', { defaultValue: 'Upcoming' })}
                />
              )}
              <Select
                placeholder={t('admin.label.select_event', { defaultValue: 'Select an event' })}
                data={pickableGames.map((g) => ({ value: String(g.id), label: g.title }))}
                value={selectedGameId !== null ? String(selectedGameId) : null}
                onChange={(value) => setSelectedGameId(value ? Number(value) : null)}
                w={200}
                size="sm"
                disabled={pickableGames.length === 0}
              />
            </div>
          }
        >
          {pickableGames.length === 0 ? (
            <EmptyState
              title={t('admin.empty.no_events.title', { defaultValue: 'No active or upcoming events' })}
              description={t('admin.empty.no_events.description', {
                defaultValue: 'Event breakdown appears here once a game is scheduled.',
              })}
              compact
            />
          ) : (
            <div className={classes.breakdownPanes}>
              <div>
                {participantsErr ? (
                  <ErrorState compact message={t('admin.state.load_failed_participants', { defaultValue: 'Could not load top participants.' })} />
                ) : (
                  <AdminTable
                    caption={t('admin.label.top_participants', { defaultValue: 'Top participants' })}
                    columns={participantColumns}
                    rows={topParticipants ?? []}
                    rowKey={(p) => p.teamId}
                    loading={participantsLoading && !topParticipants}
                    emptyTitle={t('admin.state.no_participants', { defaultValue: 'No scored teams yet' })}
                  />
                )}
              </div>
              <div>
                {categoriesErr ? (
                  <ErrorState compact message={t('admin.state.load_failed_categories', { defaultValue: 'Could not load category stats.' })} />
                ) : (
                  <AdminTable
                    caption={t('admin.label.category_stats', { defaultValue: 'Category stats' })}
                    columns={categoryColumns}
                    rows={categories ?? []}
                    rowKey={(item) => item.category}
                    loading={categoriesLoading && !categories}
                    emptyTitle={t('admin.state.no_categories', { defaultValue: 'No categories yet' })}
                  />
                )}
              </div>
            </div>
          )}
        </Panel>

        <Panel
          bare
          title={t('admin.tab.logs')}
          icon={<Icon path={mdiFileDocumentOutline} size={0.8} />}
          actions={
            <Anchor component={Link} to="/admin/logs" size="sm">
              {t('admin.nav.view_all', { defaultValue: 'View all' })}
            </Anchor>
          }
        >
          <AdminTable
            caption={t('admin.tab.logs')}
            columns={logColumns}
            rows={logs ?? []}
            rowKey={(log, index) => `${log.time}-${index}`}
            loading={loading && !logs}
            maxHeight="22rem"
            emptyTitle={t('admin.state.no_logs', { defaultValue: 'No log entries' })}
          />
        </Panel>
      </div>
    </AdminLayout>
  )
}

export default AdminOverview

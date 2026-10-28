import { ActionIcon, Avatar, Button, FileButton, Progress, Select, Switch, Tooltip } from '@mantine/core'
import { mdiAccountMultipleOutline, mdiFileDocumentOutline, mdiKeyOutline, mdiPencilOutline, mdiPlus, mdiSleep, mdiUpload } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router'
import { GameStatus } from '@Hooks/useGame'
import { GameCreateModal } from '@Components/admin/GameCreateModal'
import {
  AdminCellActions,
  AdminCellIdentity,
  AdminCellMeta,
  AdminCellName,
  AdminCellStack,
  AdminLayout,
  AdminMetricGrid,
  AdminPagination,
  AdminResultCount,
  AdminSearch,
  AdminStatusBadge,
  AdminTable,
  AdminToolbar,
  showAdminSuccess,
} from '@Components/admin/shell'
import type { AdminTone } from '@Components/admin/shell'
import { DataTableColumn, ErrorState, Panel, StatCard } from '@Components/buraq'
import { showErrorMsg } from '@Utils/Shared'
import { useArrayResponse } from '@Hooks/useArrayResponse'
import { getGameStatus } from '@Hooks/useGame'
import api, { GameInfoModel } from '@Api'
import classes from '@Styles/admin/AdminControls.module.css'
import uploadClasses from '@Styles/Upload.module.css'

const ITEM_COUNT_PER_PAGE = 30

const STATUS_TONE = new Map<GameStatus, AdminTone>([
  [GameStatus.OnGoing, 'success'],
  [GameStatus.Coming, 'info'],
  [GameStatus.Ended, 'neutral'],
])

const Games: FC = () => {
  const [page, setPage] = useState(1)
  const [reload, setReload] = useState(0)
  const [createOpened, setCreateOpened] = useState(false)
  const [busy, setBusy] = useState(false)
  const [progress, setProgress] = useState(0)
  const [query, setQuery] = useState('')
  const [statusFilter, setStatusFilter] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<unknown>(null)

  const { data: games, total, setData: setGames, updateData: updateGames } = useArrayResponse<GameInfoModel>()

  const navigate = useNavigate()
  const { t } = useTranslation()

  const fetchGames = useCallback(async () => {
    setLoading(true)
    setError(null)

    try {
      const res = await api.edit.editGetGames({
        count: ITEM_COUNT_PER_PAGE,
        skip: (page - 1) * ITEM_COUNT_PER_PAGE,
      })
      setGames(res.data)
    } catch (err) {
      setError(err)
      showErrorMsg(err, t)
    } finally {
      setLoading(false)
    }
  }, [page, t])

  useEffect(() => {
    fetchGames()
  }, [fetchGames, reload])

  const onToggleHidden = async (game: GameInfoModel) => {
    if (!game.id) return
    setBusy(true)

    try {
      await api.edit.editUpdateGame(game.id, { ...game, hidden: !game.hidden })
      updateGames((games ?? []).map((item) => (item.id === game.id ? { ...item, hidden: !item.hidden } : item)))
      showAdminSuccess(t('admin.notification.games.info.info_updated'))
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setBusy(false)
    }
  }

  const onImportGame = async (file: File | null) => {
    if (!file) return

    setProgress(0)
    setBusy(true)

    try {
      const res = await api.edit.editImportGame(
        { file },
        { onUploadProgress: (e) => setProgress((e.loaded / (e.total ?? 1)) * 100) }
      )

      if (res.data) {
        showAdminSuccess(t('admin.notification.games.import.imported', { defaultValue: 'Event imported' }))
        navigate(`/admin/games/${res.data}/info`)
      }
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setProgress(0)
      setBusy(false)
      setReload((value) => value + 1)
    }
  }

  const visible = useMemo(() => {
    const needle = query.trim().toLowerCase()

    return (games ?? []).filter((game) => {
      const { status } = getGameStatus(game)
      if (statusFilter && status !== statusFilter) return false
      if (!needle) return true
      return (game.title ?? '').toLowerCase().includes(needle) || (game.summary ?? '').toLowerCase().includes(needle)
    })
  }, [games, query, statusFilter])

  const columns: DataTableColumn<GameInfoModel>[] = [
    {
      key: 'game',
      header: t('common.label.game'),
      rowHeader: true,
      width: '22rem',
      render: (game) => (
        <AdminCellIdentity>
          <Avatar alt="" src={game.poster} radius="sm" size="sm">
            {game.title?.slice(0, 1)}
          </Avatar>
          <AdminCellStack>
            <AdminCellName title={game.title}>{game.title}</AdminCellName>
            <AdminCellMeta title={game.summary}>{game.summary || '—'}</AdminCellMeta>
          </AdminCellStack>
        </AdminCellIdentity>
      ),
    },
    {
      key: 'status',
      header: t('admin.label.games.status', { defaultValue: 'Status' }),
      width: '13rem',
      render: (game) => {
        const { status } = getGameStatus(game)

        return (
          <AdminCellStack>
            <AdminStatusBadge solid tone={STATUS_TONE.get(status) ?? 'neutral'} label={status} />
            {game.hidden && (
              <AdminStatusBadge tone="warning" label={t('admin.label.games.hidden', { defaultValue: 'Hidden' })} />
            )}
          </AdminCellStack>
        )
      },
    },
    {
      key: 'schedule',
      header: t('common.label.time'),
      width: '15rem',
      nowrap: true,
      render: (game) => {
        const { startTime, endTime } = getGameStatus(game)

        return (
          <AdminCellStack>
            <AdminCellMeta>{startTime.format('YYYY-MM-DD HH:mm')}</AdminCellMeta>
            <AdminCellMeta>{endTime.format('YYYY-MM-DD HH:mm')}</AdminCellMeta>
          </AdminCellStack>
        )
      },
    },
    {
      key: 'policy',
      header: t('admin.label.games.policy', { defaultValue: 'Policy' }),
      width: '15rem',
      render: (game) => (
        <AdminCellStack>
          <AdminCellMeta>
            {t('admin.label.games.limits', {
              defaultValue: '{{team}} / team · {{container}} containers',
              team: game.teamMemberCountLimit ? game.teamMemberCountLimit : t('admin.label.unlimited', { defaultValue: 'No limit' }),
              container: game.containerCountLimit ?? '—',
            })}
          </AdminCellMeta>
          <div style={{ display: 'flex', gap: '0.35rem', flexWrap: 'wrap' }}>
            {game.practiceMode && (
              <Tooltip label={t('admin.label.games.practice_mode', { defaultValue: 'Practice mode' })}>
                <Icon path={mdiSleep} size={0.7} />
              </Tooltip>
            )}
            {game.inviteCode && (
              <Tooltip label={t('admin.label.games.invite_only', { defaultValue: 'Invite-only' })}>
                <Icon path={mdiKeyOutline} size={0.7} />
              </Tooltip>
            )}
            {game.writeupRequired && (
              <Tooltip label={t('admin.label.games.writeup_required', { defaultValue: 'Writeup required' })}>
                <Icon path={mdiFileDocumentOutline} size={0.7} />
              </Tooltip>
            )}
            {game.acceptWithoutReview && (
              <Tooltip label={t('admin.label.games.auto_accept', { defaultValue: 'Auto-accepts teams' })}>
                <Icon path={mdiAccountMultipleOutline} size={0.7} />
              </Tooltip>
            )}
          </div>
        </AdminCellStack>
      ),
    },
    {
      key: 'visible',
      header: t('admin.label.games.hide'),
      width: '7rem',
      render: (game) => (
        <Switch
          size="sm"
          disabled={busy}
          checked={game.hidden}
          aria-label={t('admin.label.games.hide')}
          onChange={() => onToggleHidden(game)}
        />
      ),
    },
    {
      key: 'actions',
      header: '',
      headerLabel: t('admin.table.row_actions', { defaultValue: 'Row actions' }),
      align: 'right',
      width: '5rem',
      render: (game) => (
        <AdminCellActions>
          <Tooltip label={t('admin.button.games.edit', { defaultValue: 'Edit event' })} withArrow>
            <ActionIcon
              variant="subtle"
              color="blue"
              component={Link}
              to={`/admin/games/${game.id}/info`}
              aria-label={t('admin.button.games.edit', { defaultValue: 'Edit event' })}
            >
              <Icon path={mdiPencilOutline} size={0.85} />
            </ActionIcon>
          </Tooltip>
        </AdminCellActions>
      ),
    },
  ]

  const isFiltered = Boolean(query.trim() || statusFilter)

  return (
    <AdminLayout
      title={t('admin.tab.games.index')}
      description={t('admin.nav.games_description', {
        defaultValue: 'Create, import, export and configure competitions.',
      })}
      actions={
        <>
          <FileButton onChange={onImportGame} accept="application/zip">
            {(props) => (
              <Button
                {...props}
                variant="light"
                className={uploadClasses.button}
                disabled={busy}
                leftSection={<Icon path={mdiUpload} size={0.8} />}
              >
                <div className={uploadClasses.label}>
                  {progress !== 0 ? t('admin.notification.games.import.importing') : t('admin.button.games.import')}
                </div>
                {progress !== 0 && <Progress value={progress} className={uploadClasses.progress} radius="sm" />}
              </Button>
            )}
          </FileButton>
          <Button leftSection={<Icon path={mdiPlus} size={0.8} />} onClick={() => setCreateOpened(true)}>
            {t('admin.button.games.new')}
          </Button>
        </>
      }
    >
      <AdminMetricGrid label={t('admin.tab.games.index')}>
        <StatCard
          label={t('admin.label.games.total', { defaultValue: 'Total events' })}
          value={total !== undefined ? total.toLocaleString() : '—'}
          loading={loading && !games}
        />
      </AdminMetricGrid>

      <AdminToolbar
        filters={
          <Select
            className={classes.filter}
            clearable
            value={statusFilter}
            onChange={setStatusFilter}
            aria-label={t('admin.label.games.status', { defaultValue: 'Status' })}
            placeholder={t('admin.content.show_all')}
            data={Object.values(GameStatus).map((status) => ({ value: status, label: status }))}
          />
        }
        trailing={
          <>
            <AdminResultCount shown={visible.length} total={total} filtered={isFiltered} />
            <AdminPagination page={page} onChange={setPage} total={total} pageSize={ITEM_COUNT_PER_PAGE} />
          </>
        }
      >
        <AdminSearch
          value={query}
          onChange={setQuery}
          placeholder={t('admin.placeholder.games.search', { defaultValue: 'Filter this page by title' })}
        />
      </AdminToolbar>

      <Panel bare>
        {error && !games ? (
          <ErrorState
            message={t('admin.state.load_failed_games', { defaultValue: 'Could not load events.' })}
            onRetry={() => setReload((value) => value + 1)}
          />
        ) : (
          <AdminTable
            caption={t('admin.tab.games.index')}
            columns={columns}
            rows={visible}
            rowKey={(game) => game.id ?? 0}
            loading={loading && !games}
            maxHeight="calc(100vh - 22rem)"
            emptyTitle={
              isFiltered
                ? t('admin.state.no_results', { defaultValue: 'No matching events' })
                : t('admin.state.no_games', { defaultValue: 'No events yet' })
            }
            emptyDescription={
              isFiltered
                ? t('admin.state.no_results_hint', { defaultValue: 'Adjust the search or clear the filters.' })
                : t('admin.state.no_games_hint', {
                    defaultValue: 'Create an event, or import one from a BURAQ archive.',
                  })
            }
          />
        )}
      </Panel>

      <GameCreateModal
        opened={createOpened}
        onClose={() => setCreateOpened(false)}
        onAddGame={(game) => updateGames([...(games ?? []), game])}
      />
    </AdminLayout>
  )
}

export default Games

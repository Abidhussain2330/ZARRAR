import { ActionIcon, Button, Select, Tooltip } from '@mantine/core'
import { useClipboard } from '@mantine/hooks'
import {
  mdiAccountGroupOutline,
  mdiPackageVariantClosedRemove,
  mdiPuzzleOutline,
  mdiRefresh,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  AdminBulkBar,
  AdminCellActions,
  AdminCellMeta,
  AdminCellName,
  AdminCellStack,
  AdminLayout,
  AdminMetricGrid,
  AdminMonoValue,
  AdminResultCount,
  AdminStatusBadge,
  AdminTable,
  AdminToolbar,
  openAdminConfirm,
  showAdminSuccess,
  useAdminSelection,
} from '@Components/admin/shell'
import { CategoryBadge, DataTableColumn, ErrorState, Panel, StatCard } from '@Components/zarrar'
import { useLanguage } from '@Utils/I18n'
import { getProxyUrl, showErrorMsg } from '@Utils/Shared'
import api, { ChallengeCategory, ContainerInstanceModel } from '@Api'
import classes from '@Styles/admin/AdminControls.module.css'

/**
 * Container instance control.
 *
 * The listing refreshes on an interval because instances expire on their own;
 * a stale table here leads an operator to destroy something that already died.
 */
const REFRESH_INTERVAL = 30 * 1000

const Instances: FC = () => {
  const {
    data: instances,
    error,
    isLoading,
    mutate,
  } = api.admin.useAdminInstances({
    refreshInterval: REFRESH_INTERVAL,
    revalidateOnFocus: false,
  })

  const [teamFilter, setTeamFilter] = useState<string | null>(null)
  const [challengeFilter, setChallengeFilter] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const clipboard = useClipboard()
  const { t } = useTranslation()
  const { locale } = useLanguage()
  const selection = useAdminSelection<string>()

  const rows = useMemo(() => instances?.data ?? [], [instances])

  const teamOptions = useMemo(
    () =>
      [...new Map(rows.map((row) => [row.team?.id, row.team])).values()]
        .filter((team) => team?.id !== undefined)
        .map((team) => ({ value: String(team!.id), label: team!.name ?? `#${team!.id}` })),
    [rows]
  )

  const challengeOptions = useMemo(
    () =>
      [...new Map(rows.map((row) => [row.challenge?.id, row.challenge])).values()]
        .filter((challenge) => challenge?.id !== undefined)
        .map((challenge) => ({ value: String(challenge!.id), label: challenge!.title ?? `#${challenge!.id}` })),
    [rows]
  )

  const visible = useMemo(
    () =>
      rows.filter(
        (row) =>
          (!teamFilter || row.team?.id === Number(teamFilter)) &&
          (!challengeFilter || row.challenge?.id === Number(challengeFilter))
      ),
    [rows, teamFilter, challengeFilter]
  )

  const destroy = async (targets: ContainerInstanceModel[]) => {
    setBusy(true)
    let destroyed = 0

    try {
      for (const instance of targets) {
        if (!instance.containerGuid) continue
        await api.admin.adminDestroyInstance(instance.containerGuid)
        destroyed += 1
      }

      showAdminSuccess(
        targets.length === 1
          ? t('admin.notification.instances.destroyed')
          : t('admin.notification.instances.destroyed_many', {
              defaultValue: '{{count}} containers destroyed',
              count: destroyed,
            })
      )
      selection.clear()
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setBusy(false)
      // Always refetch: the orchestrator is the source of truth, not this table.
      mutate()
    }
  }

  const confirmDestroy = (targets: ContainerInstanceModel[]) =>
    openAdminConfirm({
      t,
      title: t('admin.button.instances.destroy', { defaultValue: 'Destroy container' }),
      message:
        targets.length === 1
          ? t('admin.content.instances.destroy', { name: targets[0].containerGuid?.slice(0, 8) })
          : t('admin.content.instances.destroy_many', {
              defaultValue: 'Destroy {{count}} selected containers?',
              count: targets.length,
            }),
      subject: targets.length === 1 ? targets[0].containerGuid : undefined,
      consequences: [
        t('admin.content.instances.destroy_consequence', {
          defaultValue: 'The team loses access immediately and any unsaved work inside the container is lost.',
        }),
      ],
      onConfirm: () => destroy(targets),
    })

  const columns: DataTableColumn<ContainerInstanceModel>[] = [
    {
      key: 'challenge',
      header: t('common.label.challenge'),
      rowHeader: true,
      width: '18rem',
      render: (instance) => (
        <AdminCellStack>
          <AdminCellName title={instance.challenge?.title}>{instance.challenge?.title ?? '—'}</AdminCellName>
          <CategoryBadge category={instance.challenge?.category ?? ChallengeCategory.Misc} size="sm" />
        </AdminCellStack>
      ),
    },
    {
      key: 'team',
      header: t('common.label.team'),
      width: '14rem',
      render: (instance) => (
        <AdminCellName title={instance.team?.name}>{instance.team?.name ?? '—'}</AdminCellName>
      ),
    },
    {
      key: 'lifecycle',
      header: t('admin.label.instances.life_cycle'),
      width: '14rem',
      nowrap: true,
      render: (instance) => {
        const expiring = instance.expectStopAt ? dayjs(instance.expectStopAt).diff(dayjs(), 'minute') <= 10 : false

        return (
          <AdminCellStack>
            <AdminStatusBadge
              solid
              tone={expiring ? 'warning' : 'success'}
              label={
                expiring
                  ? t('admin.content.instances.expiring', { defaultValue: 'Expiring' })
                  : t('admin.content.instances.running', { defaultValue: 'Running' })
              }
            />
            <AdminCellMeta>
              {dayjs(instance.startedAt).locale(locale).format('SL HH:mm')}
              {' → '}
              {dayjs(instance.expectStopAt).locale(locale).format('SL HH:mm')}
            </AdminCellMeta>
          </AdminCellStack>
        )
      },
    },
    {
      key: 'container',
      header: t('admin.label.instances.container_id'),
      width: '22rem',
      render: (instance) => (
        <AdminCellStack>
          <AdminMonoValue
            wide
            value={instance.containerGuid}
            onCopy={(value) => {
              clipboard.copy(getProxyUrl(value))
              showAdminSuccess(
                t('admin.notification.instances.url_copied.message'),
                t('admin.notification.instances.url_copied.title')
              )
            }}
            copyLabel={t('admin.notification.instances.url_copied.title')}
          />
          <AdminCellMeta title={instance.image}>{instance.image ?? '—'}</AdminCellMeta>
        </AdminCellStack>
      ),
    },
    {
      key: 'entry',
      header: t('admin.label.instances.entry'),
      width: '12rem',
      render: (instance) => (
        <AdminMonoValue
          value={instance.ip ? `${instance.ip}:${instance.port ?? ''}` : undefined}
          placeholder="—"
          onCopy={(value) => {
            clipboard.copy(value)
            showAdminSuccess(t('admin.notification.instances.entry_copied'))
          }}
        />
      ),
    },
    {
      key: 'actions',
      header: '',
      headerLabel: t('admin.table.row_actions', { defaultValue: 'Row actions' }),
      align: 'right',
      width: '5rem',
      render: (instance) => (
        <AdminCellActions>
          <Tooltip label={t('admin.button.instances.destroy', { defaultValue: 'Destroy container' })} withArrow>
            <ActionIcon
              variant="subtle"
              color="red"
              disabled={busy}
              aria-label={t('admin.button.instances.destroy', { defaultValue: 'Destroy container' })}
              onClick={() => confirmDestroy([instance])}
            >
              <Icon path={mdiPackageVariantClosedRemove} size={0.85} />
            </ActionIcon>
          </Tooltip>
        </AdminCellActions>
      ),
    },
  ]

  const selected = rows.filter((row) => row.containerGuid && selection.isSelected(row.containerGuid))
  const isFiltered = Boolean(teamFilter || challengeFilter)

  const expiringCount = rows.filter((row) => row.expectStopAt && dayjs(row.expectStopAt).diff(dayjs(), 'minute') <= 10).length
  const uniqueTeams = new Set(rows.map((row) => row.team?.id).filter((id) => id !== undefined)).size
  const uniqueChallenges = new Set(rows.map((row) => row.challenge?.id).filter((id) => id !== undefined)).size

  return (
    <AdminLayout
      title={t('admin.tab.instances')}
      description={t('admin.content.instances.note')}
      breadcrumbs={[{ label: t('admin.nav.group.infrastructure', { defaultValue: 'Infrastructure' }) }]}
      actions={
        <Button
          variant="light"
          leftSection={<Icon path={mdiRefresh} size={0.8} />}
          onClick={() => mutate()}
          disabled={busy}
        >
          {t('admin.button.refresh', { defaultValue: 'Refresh' })}
        </Button>
      }
    >
      <AdminMetricGrid label={t('admin.tab.instances')}>
        <StatCard label={t('admin.label.instances.running', { defaultValue: 'Running' })} value={rows.length.toLocaleString()} loading={isLoading} tone={rows.length ? 'accent' : 'neutral'} icon={<Icon path={mdiPackageVariantClosedRemove} size={0.8} />} />
        <StatCard label={t('admin.label.instances.expiring_soon', { defaultValue: 'Expiring soon' })} value={expiringCount.toLocaleString()} loading={isLoading} tone={expiringCount ? 'warning' : 'neutral'} icon={<Icon path={mdiRefresh} size={0.8} />} />
        <StatCard label={t('admin.label.instances.teams_using', { defaultValue: 'Teams with containers' })} value={uniqueTeams.toLocaleString()} loading={isLoading} icon={<Icon path={mdiAccountGroupOutline} size={0.8} />} />
        <StatCard label={t('admin.label.instances.challenges_using', { defaultValue: 'Challenges in use' })} value={uniqueChallenges.toLocaleString()} loading={isLoading} icon={<Icon path={mdiPuzzleOutline} size={0.8} />} />
      </AdminMetricGrid>

      <AdminToolbar
        filters={
          <>
            <Select
              className={classes.filter}
              searchable
              clearable
              value={teamFilter}
              onChange={setTeamFilter}
              aria-label={t('common.label.team')}
              placeholder={t('admin.placeholder.instances.teams.select')}
              nothingFoundMessage={t('admin.placeholder.instances.teams.not_found')}
              leftSection={<Icon path={mdiAccountGroupOutline} size={0.8} />}
              data={teamOptions}
            />
            <Select
              className={classes.filter}
              searchable
              clearable
              value={challengeFilter}
              onChange={setChallengeFilter}
              aria-label={t('common.label.challenge')}
              placeholder={t('admin.placeholder.instances.challenges.select')}
              nothingFoundMessage={t('admin.placeholder.instances.challenges.not_found')}
              leftSection={<Icon path={mdiPuzzleOutline} size={0.8} />}
              data={challengeOptions}
            />
          </>
        }
        trailing={<AdminResultCount shown={visible.length} total={instances?.total ?? rows.length} filtered={isFiltered} />}
      />

      {selection.count > 0 && (
        <AdminBulkBar count={selection.count} onClear={selection.clear}>
          <Button
            size="xs"
            color="red"
            variant="light"
            disabled={busy || selected.length === 0}
            leftSection={<Icon path={mdiPackageVariantClosedRemove} size={0.7} />}
            onClick={() => confirmDestroy(selected)}
          >
            {t('admin.button.instances.destroy', { defaultValue: 'Destroy container' })}
          </Button>
        </AdminBulkBar>
      )}

      <Panel bare>
        {error && !instances ? (
          <ErrorState
            message={t('admin.state.load_failed_instances', { defaultValue: 'Could not load container instances.' })}
            onRetry={() => mutate()}
          />
        ) : (
          <AdminTable
            caption={t('admin.tab.instances')}
            columns={columns}
            rows={visible}
            rowKey={(instance) => instance.containerGuid ?? ''}
            loading={isLoading && !instances}
            maxHeight="calc(100vh - 22rem)"
            selection={selection}
            selectionKey={(instance) => instance.containerGuid ?? ''}
            selectionDisabled={(instance) => !instance.containerGuid}
            selectionLabel={(instance) =>
              t('admin.table.select_named', {
                defaultValue: 'Select {{name}}',
                name: instance.containerGuid?.slice(0, 8) ?? '',
              })
            }
            emptyTitle={
              isFiltered
                ? t('admin.state.no_results', { defaultValue: 'No matching containers' })
                : t('admin.state.no_instances', { defaultValue: 'No containers running' })
            }
            emptyDescription={
              isFiltered
                ? t('admin.state.no_results_hint', { defaultValue: 'Adjust the search or clear the filters.' })
                : t('admin.state.no_instances_hint', {
                    defaultValue: 'Instances appear here as soon as a team starts one.',
                  })
            }
          />
        )}
      </Panel>
    </AdminLayout>
  )
}

export default Instances

import { ActionIcon, Avatar, Button, Select, Tooltip } from '@mantine/core'
import { mdiAccountPlusOutline, mdiDeleteOutline, mdiLockOpenVariantOutline, mdiLockOutline, mdiPencilOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { TeamEditModal } from '@Components/admin/TeamEditModal'
import { TeamCreateModal } from '@Components/admin/TeamCreateModal'
import {
  AdminBulkBar,
  AdminCellActions,
  AdminCellIdentity,
  AdminCellMeta,
  AdminCellName,
  AdminCellStack,
  AdminMonoValue,
  AdminLayout,
  AdminPagination,
  AdminResultCount,
  AdminSearch,
  AdminStatusBadge,
  AdminTable,
  AdminToolbar,
  openAdminConfirm,
  showAdminSuccess,
  useAdminSelection,
} from '@Components/admin/shell'
import { DataTableColumn, ErrorState, Panel } from '@Components/buraq'
import { showErrorMsg } from '@Utils/Shared'
import { useArrayResponse } from '@Hooks/useArrayResponse'
import api, { TeamInfoModel } from '@Api'
import classes from '@Styles/admin/AdminControls.module.css'

const ITEM_COUNT_PER_PAGE = 30

const Teams: FC = () => {
  const [page, setPage] = useState(1)
  const [reload, setReload] = useState(0)
  const { data: teams, total, setData: setTeams, updateData: updateTeams } = useArrayResponse<TeamInfoModel>()

  const [hint, setHint] = useState('')
  const [appliedHint, setAppliedHint] = useState('')
  const [lockFilter, setLockFilter] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  const [isEditModalOpen, setIsEditModalOpen] = useState(false)
  const [isCreateModalOpen, setIsCreateModalOpen] = useState(false)
  const [activeTeam, setActiveTeam] = useState<TeamInfoModel>({})

  const { t } = useTranslation()
  const selection = useAdminSelection<number>()

  const fetchTeams = useCallback(async () => {
    setLoading(true)
    setError(null)

    try {
      const res = appliedHint
        ? await api.admin.adminSearchTeams({ hint: appliedHint })
        : await api.admin.adminTeams({ count: ITEM_COUNT_PER_PAGE, skip: (page - 1) * ITEM_COUNT_PER_PAGE })

      setTeams(res.data)
    } catch (err) {
      setError(err)
      showErrorMsg(err, t)
    } finally {
      setLoading(false)
    }
  }, [appliedHint, page, t])

  useEffect(() => {
    fetchTeams()
  }, [fetchTeams, reload])

  useEffect(() => {
    selection.clear()
  }, [page, appliedHint, lockFilter, selection.clear])

  const visible = useMemo(
    () =>
      (teams ?? []).filter((team) => {
        if (!lockFilter) return true
        return lockFilter === 'locked' ? !!team.locked : !team.locked
      }),
    [teams, lockFilter]
  )

  const onToggleLock = async (team: TeamInfoModel) => {
    if (!team.id) return
    setBusy(true)

    try {
      await api.admin.adminUpdateTeam(team.id, { locked: !team.locked })
      updateTeams((teams ?? []).map((item) => (item.id === team.id ? { ...item, locked: !item.locked } : item)))
      showAdminSuccess(t('team.notification.updated'))
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setBusy(false)
    }
  }

  const deleteTeams = async (targets: TeamInfoModel[]) => {
    setBusy(true)
    let removed = 0

    try {
      for (const team of targets) {
        if (!team.id) continue
        await api.admin.adminDeleteTeam(team.id)
        removed += 1
      }

      showAdminSuccess(
        targets.length === 1
          ? t('admin.notification.teams.deleted', { name: targets[0].name })
          : t('admin.notification.teams.deleted_many', {
              defaultValue: '{{count}} teams deleted',
              count: removed,
            })
      )
      selection.clear()
      setReload((value) => value + 1)
    } catch (err) {
      showErrorMsg(err, t)
      setReload((value) => value + 1)
    } finally {
      setBusy(false)
    }
  }

  const confirmDelete = (targets: TeamInfoModel[]) =>
    openAdminConfirm({
      t,
      title: t('admin.button.teams.delete', { defaultValue: 'Delete team' }),
      message:
        targets.length === 1
          ? t('admin.content.teams.delete', { name: targets[0].name })
          : t('admin.content.teams.delete_many', {
              defaultValue: 'Delete {{count}} selected teams?',
              count: targets.length,
            }),
      subject: targets.length === 1 ? (targets[0].name ?? undefined) : undefined,
      consequences: targets.length > 1 ? targets.slice(0, 8).map((team) => team.name ?? `#${team.id}`) : undefined,
      onConfirm: () => deleteTeams(targets),
    })

  const columns: DataTableColumn<TeamInfoModel>[] = [
    {
      key: 'team',
      header: t('common.label.team'),
      rowHeader: true,
      width: '20rem',
      render: (team) => (
        <AdminCellIdentity>
          <Avatar alt="" src={team.avatar} radius="xl" size="sm">
            {team.name?.slice(0, 1) ?? 'T'}
          </Avatar>
          <AdminCellStack>
            <AdminCellName title={team.name ?? undefined}>{team.name ?? `#${team.id}`}</AdminCellName>
            {team.bio ? (
              <AdminCellMeta title={team.bio}>{team.bio}</AdminCellMeta>
            ) : (
              <AdminMonoValue value={team.id !== undefined ? `#${team.id}` : undefined} placeholder="—" />
            )}
          </AdminCellStack>
        </AdminCellIdentity>
      ),
    },
    {
      key: 'status',
      header: t('admin.label.teams.status', { defaultValue: 'Status' }),
      width: '9rem',
      render: (team) => (
        <AdminStatusBadge
          solid
          tone={team.locked ? 'warning' : 'neutral'}
          label={team.locked ? t('admin.content.teams.locked') : t('admin.content.teams.unlocked')}
        />
      ),
    },
    {
      key: 'members',
      header: t('admin.label.teams.members'),
      width: '16rem',
      render: (team) => {
        const members = [...(team.members ?? [])].sort((a, b) => (a.captain ? (b.captain ? 0 : -1) : 1))
        const overflow = members.slice(5)

        return (
          <Avatar.Group spacing="sm">
            {members.slice(0, 5).map((member) => (
              <Tooltip key={member.id} label={member.captain ? `${member.userName} · ${t('team.label.captain', { defaultValue: 'Captain' })}` : member.userName}>
                <Avatar
                  alt=""
                  radius="xl"
                  size="sm"
                  src={member.avatar}
                  style={member.captain ? { boxShadow: '0 0 0 2px var(--buraq-primary)' } : undefined}
                >
                  {member.userName?.slice(0, 1) ?? 'U'}
                </Avatar>
              </Tooltip>
            ))}
            {overflow.length > 0 && (
              <Tooltip multiline withArrow maw="16rem" label={overflow.map((m) => m.userName).join(', ')}>
                <Avatar alt="" radius="xl" size="sm">
                  +{overflow.length}
                </Avatar>
              </Tooltip>
            )}
            {members.length === 0 && <AdminCellMeta>{t('admin.placeholder.empty')}</AdminCellMeta>}
          </Avatar.Group>
        )
      },
    },
    {
      key: 'count',
      header: t('admin.label.teams.member_count', { defaultValue: 'Size' }),
      width: '5rem',
      numeric: true,
      render: (team) => team.members?.length ?? 0,
    },
    {
      key: 'actions',
      header: '',
      headerLabel: t('admin.table.row_actions', { defaultValue: 'Row actions' }),
      align: 'right',
      width: '8rem',
      render: (team) => (
        <AdminCellActions>
          <Tooltip label={t('admin.button.teams.edit')} withArrow>
            <ActionIcon
              variant="subtle"
              color="blue"
              aria-label={t('admin.button.teams.edit')}
              onClick={() => {
                setActiveTeam(team)
                setIsEditModalOpen(true)
              }}
            >
              <Icon path={mdiPencilOutline} size={0.85} />
            </ActionIcon>
          </Tooltip>
          <Tooltip
            withArrow
            label={team.locked ? t('admin.button.teams.do_unlock') : t('admin.button.teams.do_lock')}
          >
            <ActionIcon
              variant="subtle"
              color={team.locked ? 'gray' : 'yellow'}
              disabled={busy}
              aria-label={team.locked ? t('admin.button.teams.do_unlock') : t('admin.button.teams.do_lock')}
              onClick={() =>
                openAdminConfirm({
                  t,
                  severity: 'caution',
                  title: team.locked ? t('admin.button.teams.do_unlock') : t('admin.button.teams.do_lock'),
                  message: t('admin.content.teams.lock', {
                    name: team.name,
                    action: team.locked ? t('admin.button.teams.do_unlock') : t('admin.button.teams.do_lock'),
                  }),
                  subject: team.name ?? undefined,
                  onConfirm: () => onToggleLock(team),
                })
              }
            >
              <Icon path={team.locked ? mdiLockOpenVariantOutline : mdiLockOutline} size={0.85} />
            </ActionIcon>
          </Tooltip>
          <Tooltip label={t('admin.button.teams.delete', { defaultValue: 'Delete team' })} withArrow>
            <ActionIcon
              variant="subtle"
              color="red"
              disabled={busy}
              aria-label={t('admin.button.teams.delete', { defaultValue: 'Delete team' })}
              onClick={() => confirmDelete([team])}
            >
              <Icon path={mdiDeleteOutline} size={0.85} />
            </ActionIcon>
          </Tooltip>
        </AdminCellActions>
      ),
    },
  ]

  const selectedTeams = (teams ?? []).filter((team) => team.id !== undefined && selection.isSelected(team.id))
  const isFiltered = Boolean(appliedHint || lockFilter)

  return (
    <AdminLayout
      title={t('admin.tab.teams')}
      description={t('admin.nav.teams_description', {
        defaultValue: 'Team registrations, membership, locking and deletion.',
      })}
      breadcrumbs={[{ label: t('admin.nav.group.people', { defaultValue: 'People' }) }]}
      actions={
        <Button leftSection={<Icon path={mdiAccountPlusOutline} size={0.75} />} onClick={() => setIsCreateModalOpen(true)}>
          {t('admin.button.teams.create', { defaultValue: 'New team' })}
        </Button>
      }
    >
      <AdminToolbar
        filters={
          <Select
            className={classes.filter}
            clearable
            value={lockFilter}
            onChange={setLockFilter}
            aria-label={t('admin.label.teams.status', { defaultValue: 'Status' })}
            placeholder={t('admin.content.show_all')}
            data={[
              { value: 'locked', label: t('admin.content.teams.locked') },
              { value: 'unlocked', label: t('admin.content.teams.unlocked') },
            ]}
          />
        }
        trailing={
          <>
            <AdminResultCount shown={visible.length} total={appliedHint ? undefined : total} filtered={isFiltered} />
            {!appliedHint && (
              <AdminPagination page={page} onChange={setPage} total={total} pageSize={ITEM_COUNT_PER_PAGE} />
            )}
          </>
        }
      >
        <AdminSearch
          value={hint}
          onChange={setHint}
          onSubmit={() => {
            setPage(1)
            setAppliedHint(hint.trim())
          }}
          placeholder={t('admin.placeholder.teams.search')}
        />
      </AdminToolbar>

      {selection.count > 0 && (
        <AdminBulkBar count={selection.count} onClear={selection.clear}>
          <Button
            size="xs"
            color="red"
            variant="light"
            disabled={busy || selectedTeams.length === 0}
            leftSection={<Icon path={mdiDeleteOutline} size={0.7} />}
            onClick={() => confirmDelete(selectedTeams)}
          >
            {t('admin.button.teams.delete', { defaultValue: 'Delete team' })}
          </Button>
        </AdminBulkBar>
      )}

      <Panel bare>
        {error && !teams ? (
          <ErrorState
            message={t('admin.state.load_failed_teams', { defaultValue: 'Could not load teams.' })}
            onRetry={() => setReload((value) => value + 1)}
          />
        ) : (
          <AdminTable
            caption={t('admin.tab.teams')}
            columns={columns}
            rows={visible}
            rowKey={(team) => team.id ?? 0}
            loading={loading && !teams}
            maxHeight="calc(100vh - 22rem)"
            selection={selection}
            selectionKey={(team) => team.id ?? 0}
            selectionDisabled={(team) => team.id === undefined}
            selectionLabel={(team) =>
              t('admin.table.select_named', { defaultValue: 'Select {{name}}', name: team.name ?? '' })
            }
            emptyTitle={
              isFiltered
                ? t('admin.state.no_results', { defaultValue: 'No matching teams' })
                : t('admin.state.no_teams', { defaultValue: 'No teams yet' })
            }
            emptyDescription={
              isFiltered
                ? t('admin.state.no_results_hint', { defaultValue: 'Adjust the search or clear the filters.' })
                : undefined
            }
          />
        )}
      </Panel>

      <TeamEditModal
        size="min(34rem, 92vw)"
        title={t('admin.button.teams.edit')}
        team={activeTeam}
        opened={isEditModalOpen}
        onClose={() => setIsEditModalOpen(false)}
        mutateTeam={(team: TeamInfoModel) =>
          updateTeams(
            [team, ...((teams ?? []).filter((n) => n.id !== team.id))].sort((a, b) => (a.id! < b.id! ? -1 : 1))
          )
        }
      />
      <TeamCreateModal
        size="min(34rem, 92vw)"
        opened={isCreateModalOpen}
        onClose={() => setIsCreateModalOpen(false)}
        onCreated={() => {
          setPage(1)
          setAppliedHint('')
          setHint('')
          setReload((value) => value + 1)
        }}
      />
    </AdminLayout>
  )
}

export default Teams

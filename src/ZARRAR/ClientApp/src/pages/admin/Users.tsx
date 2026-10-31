import { ActionIcon, Avatar, Button, Select, Stack, Switch, Text, Tooltip } from '@mantine/core'
import { useClipboard } from '@mantine/hooks'
import { modals } from '@mantine/modals'
import { mdiAccountPlusOutline, mdiDeleteOutline, mdiLockReset, mdiPencilOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { UserEditModal } from '@Components/admin/UserEditModal'
import { UserCreateModal } from '@Components/admin/UserCreateModal'
import {
  AdminBulkBar,
  AdminCellIdentity,
  AdminCellActions,
  AdminCellMeta,
  AdminCellName,
  AdminCellStack,
  AdminLayout,
  AdminMonoValue,
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
import type { AdminTone } from '@Components/admin/shell'
import { DataTableColumn, ErrorState, Panel } from '@Components/zarrar'
import { showErrorMsg } from '@Utils/Shared'
import { useArrayResponse } from '@Hooks/useArrayResponse'
import { useUser } from '@Hooks/useUser'
import api, { Role, UserInfoModel } from '@Api'
import classes from '@Styles/admin/AdminControls.module.css'

const ITEM_COUNT_PER_PAGE = 30

export const RoleToneMap = new Map<Role, AdminTone>([
  [Role.Admin, 'accent'],
  [Role.Monitor, 'info'],
  [Role.User, 'neutral'],
  [Role.Banned, 'critical'],
])

const Users: FC = () => {
  const [page, setPage] = useState(1)
  const [reload, setReload] = useState(0)
  const [editModalOpened, setEditModalOpened] = useState(false)
  const [createModalOpened, setCreateModalOpened] = useState(false)
  const [activeUser, setActiveUser] = useState<UserInfoModel>({})
  const { data: users, total, setData: setUsers, updateData: updateUsers } = useArrayResponse<UserInfoModel>()

  const [hint, setHint] = useState('')
  const [appliedHint, setAppliedHint] = useState('')
  const [roleFilter, setRoleFilter] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  const { user: currentUser } = useUser()
  const clipboard = useClipboard()
  const { t } = useTranslation()
  const selection = useAdminSelection<string>()

  /*
   * Search is a server call (`adminSearchUsers`) and is unpaged, so applying one
   * resets to page 1 and hides the pager rather than pretending page 2 exists.
   */
  const fetchUsers = useCallback(async () => {
    setLoading(true)
    setError(null)

    try {
      const res = appliedHint
        ? await api.admin.adminSearchUsers({ hint: appliedHint })
        : await api.admin.adminUsers({ count: ITEM_COUNT_PER_PAGE, skip: (page - 1) * ITEM_COUNT_PER_PAGE })

      setUsers(res.data)
    } catch (err) {
      setError(err)
      showErrorMsg(err, t)
    } finally {
      setLoading(false)
    }
  }, [appliedHint, page, t])

  useEffect(() => {
    fetchUsers()
  }, [fetchUsers, reload])

  useEffect(() => {
    selection.clear()
  }, [page, appliedHint, roleFilter, selection.clear])

  const onSearch = () => {
    setPage(1)
    setAppliedHint(hint.trim())
  }

  const visible = useMemo(
    () => (users ?? []).filter((user) => !roleFilter || (user.role ?? Role.User) === roleFilter),
    [users, roleFilter]
  )

  const onToggleActive = async (user: UserInfoModel) => {
    setBusy(true)

    try {
      await api.admin.adminUpdateUserInfo(user.id!, { emailConfirmed: !user.emailConfirmed })
      updateUsers((users ?? []).map((u) => (u.id === user.id ? { ...u, emailConfirmed: !u.emailConfirmed } : u)))
      showAdminSuccess(t('admin.notification.users.updated'))
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setBusy(false)
    }
  }

  const onResetPassword = async (user: UserInfoModel) => {
    setBusy(true)

    try {
      const res = await api.admin.adminResetPassword(user.id!)

      /*
       * The generated password is shown once and never persisted client-side.
       * It is rendered as plain text in a modal the operator must dismiss —
       * deliberately not written to a notification, which would linger on
       * screen and in the notification history.
       */
      modals.open({
        title: t('admin.content.users.reset.title', { name: user.userName }),
        centered: true,
        children: (
          <Stack>
            <Text size="sm">{t('admin.content.users.reset.content')}</Text>
            <Text fw="bold" ff="monospace" style={{ overflowWrap: 'anywhere' }}>
              {res.data}
            </Text>
            <Button
              onClick={() => {
                clipboard.copy(res.data)
                showAdminSuccess(t('admin.notification.users.password_copied'))
              }}
            >
              {t('common.button.copy')}
            </Button>
          </Stack>
        ),
      })
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setBusy(false)
    }
  }

  const deleteUsers = async (targets: UserInfoModel[]) => {
    setBusy(true)
    let removed = 0

    try {
      for (const user of targets) {
        if (!user.id || user.id === currentUser?.userId) continue
        await api.admin.adminDeleteUser(user.id)
        removed += 1
      }

      showAdminSuccess(
        targets.length === 1
          ? t('admin.notification.users.deleted', { name: targets[0].userName })
          : t('admin.notification.users.deleted_many', {
              defaultValue: '{{count}} users deleted',
              count: removed,
            })
      )
      selection.clear()
      setReload((value) => value + 1)
    } catch (err) {
      showErrorMsg(err, t)
      // A partial batch still changed the server; refetch so the table is truthful.
      setReload((value) => value + 1)
    } finally {
      setBusy(false)
    }
  }

  const confirmDelete = (targets: UserInfoModel[]) =>
    openAdminConfirm({
      t,
      title: t('admin.button.users.delete', { defaultValue: 'Delete user' }),
      message:
        targets.length === 1
          ? t('admin.content.users.delete', { name: targets[0].userName })
          : t('admin.content.users.delete_many', {
              defaultValue: 'Delete {{count}} selected users?',
              count: targets.length,
            }),
      subject: targets.length === 1 ? (targets[0].userName ?? undefined) : undefined,
      consequences:
        targets.length > 1 ? targets.slice(0, 8).map((user) => user.userName ?? user.id ?? '') : undefined,
      confirmLabel: t('common.modal.confirm'),
      onConfirm: () => deleteUsers(targets),
    })

  const columns: DataTableColumn<UserInfoModel>[] = [
    {
      key: 'user',
      header: t('common.label.user'),
      rowHeader: true,
      width: '20rem',
      render: (user) => (
        <AdminCellIdentity>
          <Avatar alt="" src={user.avatar} radius="xl" size="sm">
            {user.userName?.slice(0, 1) ?? 'U'}
          </Avatar>
          <AdminCellStack>
            <AdminCellName title={user.userName ?? undefined}>{user.userName}</AdminCellName>
            <AdminCellMeta title={user.email ?? undefined}>{user.email}</AdminCellMeta>
          </AdminCellStack>
        </AdminCellIdentity>
      ),
    },
    {
      key: 'role',
      header: t('admin.label.users.role'),
      width: '8rem',
      render: (user) => (
        <AdminStatusBadge
          label={user.role ?? Role.User}
          tone={RoleToneMap.get(user.role ?? Role.User) ?? 'neutral'}
          solid
        />
      ),
    },
    {
      key: 'active',
      header: t('admin.label.users.active'),
      width: '7rem',
      render: (user) => (
        <Switch
          size="sm"
          disabled={busy}
          checked={user.emailConfirmed ?? false}
          aria-label={t('admin.label.users.active')}
          onChange={() => onToggleActive(user)}
        />
      ),
    },
    {
      key: 'identity',
      header: t('account.label.real_name'),
      width: '12rem',
      render: (user) => (
        <AdminCellStack>
          <AdminCellName title={user.realName ?? undefined}>
            {user.realName || t('admin.placeholder.users.real_name')}
          </AdminCellName>
          <AdminCellMeta>{user.stdNumber || t('admin.placeholder.users.student_id')}</AdminCellMeta>
        </AdminCellStack>
      ),
    },
    {
      key: 'seen',
      header: t('admin.label.users.activity', { defaultValue: 'Activity' }),
      width: '13rem',
      nowrap: true,
      render: (user) => (
        <AdminCellStack>
          <AdminCellMeta>
            {t('admin.label.users.last_seen', { defaultValue: 'Last seen' })}{' '}
            {user.lastVisitedUtc ? dayjs(user.lastVisitedUtc).format('YYYY-MM-DD HH:mm') : '—'}
          </AdminCellMeta>
          <AdminCellMeta title={user.registerTimeUtc ? dayjs(user.registerTimeUtc).format('YYYY-MM-DD HH:mm') : undefined}>
            {t('admin.label.users.joined', { defaultValue: 'Joined' })}{' '}
            {user.registerTimeUtc ? dayjs(user.registerTimeUtc).format('YYYY-MM-DD') : '—'}
          </AdminCellMeta>
          <AdminMonoValue value={user.ip} placeholder="—" />
        </AdminCellStack>
      ),
    },
    {
      key: 'actions',
      header: '',
      headerLabel: t('admin.table.row_actions', { defaultValue: 'Row actions' }),
      align: 'right',
      width: '8rem',
      render: (user) => (
        <AdminCellActions>
          <Tooltip label={t('admin.button.users.edit')} withArrow>
            <ActionIcon
              variant="subtle"
              color="blue"
              aria-label={t('admin.button.users.edit')}
              onClick={() => {
                setActiveUser(user)
                setEditModalOpened(true)
              }}
            >
              <Icon path={mdiPencilOutline} size={0.85} />
            </ActionIcon>
          </Tooltip>
          <Tooltip label={t('admin.button.users.reset', { defaultValue: 'Reset password' })} withArrow>
            <ActionIcon
              variant="subtle"
              color="orange"
              disabled={busy}
              aria-label={t('admin.button.users.reset', { defaultValue: 'Reset password' })}
              onClick={() =>
                openAdminConfirm({
                  t,
                  severity: 'caution',
                  title: t('admin.button.users.reset', { defaultValue: 'Reset password' }),
                  message: t('admin.content.users.reset.message', { name: user.userName }),
                  subject: user.userName ?? undefined,
                  onConfirm: () => onResetPassword(user),
                })
              }
            >
              <Icon path={mdiLockReset} size={0.85} />
            </ActionIcon>
          </Tooltip>
          <Tooltip label={t('admin.button.users.delete', { defaultValue: 'Delete user' })} withArrow>
            <ActionIcon
              variant="subtle"
              color="red"
              disabled={busy || user.id === currentUser?.userId}
              aria-label={t('admin.button.users.delete', { defaultValue: 'Delete user' })}
              onClick={() => confirmDelete([user])}
            >
              <Icon path={mdiDeleteOutline} size={0.85} />
            </ActionIcon>
          </Tooltip>
        </AdminCellActions>
      ),
    },
  ]

  const selectedUsers = (users ?? []).filter((user) => user.id && selection.isSelected(user.id))
  const isFiltered = Boolean(appliedHint || roleFilter)

  return (
    <AdminLayout
      title={t('admin.tab.users')}
      description={t('admin.nav.users_description', {
        defaultValue: 'Accounts, roles, activation and password resets.',
      })}
      breadcrumbs={[{ label: t('admin.nav.group.people', { defaultValue: 'People' }) }]}
      actions={
        <Button
          leftSection={<Icon path={mdiAccountPlusOutline} size={0.75} />}
          onClick={() => setCreateModalOpened(true)}
        >
          {t('admin.button.users.create', { defaultValue: 'New user' })}
        </Button>
      }
    >
      <AdminToolbar
        filters={
          <Select
            className={classes.filter}
            clearable
            value={roleFilter}
            onChange={setRoleFilter}
            aria-label={t('admin.label.users.role')}
            placeholder={t('admin.content.show_all')}
            data={Object.values(Role).map((role) => ({ value: role, label: role }))}
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
          onSubmit={onSearch}
          placeholder={t('admin.placeholder.users.search')}
        />
      </AdminToolbar>

      {selection.count > 0 && (
        <AdminBulkBar count={selection.count} onClear={selection.clear}>
          <Button
            size="xs"
            color="red"
            variant="light"
            disabled={busy || selectedUsers.length === 0}
            leftSection={<Icon path={mdiDeleteOutline} size={0.7} />}
            onClick={() => confirmDelete(selectedUsers)}
          >
            {t('admin.button.users.delete', { defaultValue: 'Delete user' })}
          </Button>
        </AdminBulkBar>
      )}

      <Panel bare>
        {error && !users ? (
          <ErrorState
            message={t('admin.state.load_failed', { defaultValue: 'Could not load users.' })}
            onRetry={() => setReload((value) => value + 1)}
          />
        ) : (
          <AdminTable
            caption={t('admin.tab.users')}
            columns={columns}
            rows={visible}
            rowKey={(user) => user.id ?? ''}
            loading={loading && !users}
            maxHeight="calc(100vh - 22rem)"
            selection={selection}
            selectionKey={(user) => user.id ?? ''}
            selectionDisabled={(user) => !user.id || user.id === currentUser?.userId}
            selectionLabel={(user) =>
              t('admin.table.select_named', { defaultValue: 'Select {{name}}', name: user.userName ?? '' })
            }
            emptyTitle={
              isFiltered
                ? t('admin.state.no_results', { defaultValue: 'No matching users' })
                : t('admin.state.no_users', { defaultValue: 'No users yet' })
            }
            emptyDescription={
              isFiltered
                ? t('admin.state.no_results_hint', { defaultValue: 'Adjust the search or clear the filters.' })
                : undefined
            }
          />
        )}
      </Panel>

      <UserEditModal
        size="min(34rem, 92vw)"
        title={t('admin.button.users.edit')}
        user={activeUser}
        opened={editModalOpened}
        onClose={() => setEditModalOpened(false)}
        mutateUser={(user: UserInfoModel) =>
          updateUsers(
            [user, ...((users ?? []).filter((n) => n.id !== user.id))].sort((a, b) => (a.id! < b.id! ? -1 : 1))
          )
        }
      />
      <UserCreateModal
        size="min(34rem, 92vw)"
        opened={createModalOpened}
        onClose={() => setCreateModalOpened(false)}
        onCreated={() => {
          setPage(1)
          setReload((value) => value + 1)
        }}
      />
    </AdminLayout>
  )
}

export default Users

import { Accordion, ActionIcon, Avatar, Box, Group, Select, Stack, Text } from '@mantine/core'
import { mdiClose, mdiPencil } from '@mdi/js'
import { Icon } from '@mdi/react'
import { showNotification } from '@mantine/notifications'
import { FC, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useParams } from 'react-router'
import { AdminGameLayout } from '@Components/admin/AdminGameLayout'
import { ParticipationDivisionEditModal } from '@Components/admin/ParticipationDivisionEditModal'
import { ParticipationStatusControl } from '@Components/admin/ParticipationStatusControl'
import {
  AdminCellMeta,
  AdminCellName,
  AdminCellStack,
  AdminPagination,
  AdminResultCount,
  AdminSearch,
  AdminStatusBadge,
  AdminToolbar,
  showAdminSuccess,
} from '@Components/admin/shell'
import type { AdminTone } from '@Components/admin/shell'
import { EmptyState, ErrorState, LoadingState, Panel } from '@Components/buraq'
import { showErrorMsg, useParticipationStatusMap } from '@Utils/Shared'
import { OnceSWRConfig } from '@Hooks/useConfig'
import api, { ParticipationEditModel, ParticipationInfoModel, ParticipationStatus, ProfileUserInfoModel } from '@Api'
import accordionClasses from '@Styles/Accordion.module.css'
import classes from '@Styles/admin/AdminControls.module.css'

const PART_NUM_PER_PAGE = 10

const STATUS_TONE = new Map<ParticipationStatus, AdminTone>([
  [ParticipationStatus.Pending, 'warning'],
  [ParticipationStatus.Accepted, 'success'],
  [ParticipationStatus.Rejected, 'neutral'],
  [ParticipationStatus.Suspended, 'critical'],
])

interface MemberRowProps {
  user: ProfileUserInfoModel
  isRegistered: boolean
  isCaptain: boolean
}

/**
 * One team member inside an expanded participation.
 *
 * Rendered as a definition-style grid rather than a fixed 500px-minimum row, so
 * the reviewer's checklist — real name, student number, e-mail, phone — reflows
 * onto a phone instead of forcing the page sideways.
 */
const MemberRow: FC<MemberRowProps> = ({ user, isCaptain, isRegistered }) => {
  const { t } = useTranslation()

  return (
    <Group wrap="nowrap" align="flex-start" gap="sm">
      <Avatar alt="" src={user.avatar} size="sm" radius="xl">
        {user.userName?.slice(0, 1) ?? 'U'}
      </Avatar>
      <div className={classes.formGrid} style={{ flex: 1 }}>
        <AdminCellStack>
          <AdminCellName title={user.userName ?? undefined}>{user.userName}</AdminCellName>
          <AdminCellMeta>{user.realName || t('admin.placeholder.empty')}</AdminCellMeta>
        </AdminCellStack>
        <AdminCellStack>
          <AdminCellMeta title={user.email ?? undefined}>{user.email || t('admin.placeholder.empty')}</AdminCellMeta>
          <AdminCellMeta>{user.phone || t('admin.placeholder.empty')}</AdminCellMeta>
        </AdminCellStack>
        <AdminCellStack>
          <AdminCellMeta>{user.stdNumber || t('admin.placeholder.empty')}</AdminCellMeta>
          <Group gap="xs">
            {isCaptain && (
              <AdminStatusBadge tone="warning" label={t('team.content.role.captain')} />
            )}
            <AdminStatusBadge
              tone={isRegistered ? 'success' : 'neutral'}
              label={
                isRegistered
                  ? t('admin.content.games.review.participation.joined')
                  : t('admin.content.games.review.participation.not_joined')
              }
            />
          </Group>
        </AdminCellStack>
      </div>
    </Group>
  )
}

const GameTeamReview: FC = () => {
  const navigate = useNavigate()
  const { id } = useParams()
  const numId = parseInt(id ?? '-1', 10)

  const [disabled, setDisabled] = useState(false)
  const [selectedStatus, setSelectedStatus] = useState<string | null>(null)
  const [selectedDivisionId, setSelectedDivisionId] = useState<string | null>(null)
  const [participations, setParticipations] = useState<ParticipationInfoModel[]>()
  const [error, setError] = useState<unknown>(null)
  const [reload, setReload] = useState(0)
  const [search, setSearch] = useState('')
  const [activePage, setPage] = useState(1)

  const [divModalOpened, setDivModalOpened] = useState(false)
  const [curParticipation, setCurParticipation] = useState<ParticipationInfoModel | null>(null)

  const participationStatusMap = useParticipationStatusMap()
  const { t } = useTranslation()

  const { data: divisions } = api.edit.useEditGetDivisions(numId, OnceSWRConfig, numId > 0)

  const divisionNameMap = useMemo(() => {
    const map = new Map<number, string>()
    divisions?.forEach((division) =>
      map.set(division.id, division.name?.trim() ? division.name : `#${division.id}`)
    )
    return map
  }, [divisions])

  const divisionSelectOptions = useMemo(() => {
    const optionMap = new Map<string, { value: string; label: string }>()

    divisions?.forEach((division) => {
      const value = division.id.toString()
      optionMap.set(value, { value, label: divisionNameMap.get(division.id) ?? `#${division.id}` })
    })

    participations?.forEach((participation) => {
      if (participation.divisionId === undefined || participation.divisionId === null) return
      const value = participation.divisionId.toString()
      if (!optionMap.has(value)) optionMap.set(value, { value, label: `#${participation.divisionId}` })
    })

    return [...optionMap.values()].sort((a, b) => a.label.localeCompare(b.label))
  }, [divisions, divisionNameMap, participations])

  const hasDivisions = (divisions?.length ?? 0) > 0

  const setParticipation = async (participationId: number, model: ParticipationEditModel) => {
    setDisabled(true)

    try {
      await api.admin.adminParticipation(participationId, model)
      setParticipations((prev) =>
        prev?.map((value) => {
          if (value.id !== participationId) return value

          const next: ParticipationInfoModel = { ...value }
          if (model.status) next.status = model.status
          if (model.divisionId !== undefined) next.divisionId = model.divisionId
          if (model.status === ParticipationStatus.Rejected) next.divisionId = null

          return next
        })
      )
      showAdminSuccess(t('admin.notification.games.participation.updated'))
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setDisabled(false)
    }
  }

  useEffect(() => {
    setPage(1)
  }, [selectedStatus, selectedDivisionId, search])

  useEffect(() => {
    if (numId < 0) {
      showNotification({
        color: 'red',
        message: t('common.error.param_error'),
        icon: <Icon path={mdiClose} size={1} />,
      })
      navigate('/admin/games')
      return
    }

    const fetchData = async () => {
      setError(null)

      try {
        const res = await api.game.gameParticipations(numId)
        setParticipations(res.data)
      } catch (err) {
        setError(err)
        showErrorMsg(err, t)
      }
    }

    fetchData()
  }, [navigate, numId, t, reload])

  const filtered = participations?.filter((participation) => {
    const matchesStatus = !selectedStatus || participation.status === selectedStatus
    const matchesDivision =
      !selectedDivisionId ||
      (participation.divisionId !== undefined &&
        participation.divisionId !== null &&
        participation.divisionId.toString() === selectedDivisionId)
    const matchesSearch = !search || (participation.team?.name?.toLowerCase().includes(search.toLowerCase()) ?? false)

    return matchesStatus && matchesDivision && matchesSearch
  })

  const paged = filtered?.slice((activePage - 1) * PART_NUM_PER_PAGE, activePage * PART_NUM_PER_PAGE)
  const isFiltered = Boolean(selectedStatus || selectedDivisionId || search)

  return (
    <AdminGameLayout
      tab="review"
      description={t('admin.nav.review_description', {
        defaultValue: 'Approve, reject or suspend team registrations and assign divisions.',
      })}
    >
      <AdminToolbar
        filters={
          <>
            {divisionSelectOptions.length > 0 && (
              <Select
                className={classes.filter}
                clearable
                value={selectedDivisionId}
                onChange={setSelectedDivisionId}
                aria-label={t('admin.tab.games.divisions')}
                placeholder={t('admin.content.show_all')}
                data={divisionSelectOptions}
              />
            )}
            <Select
              className={classes.filter}
              clearable
              value={selectedStatus}
              onChange={setSelectedStatus}
              aria-label={t('admin.tab.games.review')}
              placeholder={t('admin.content.show_all')}
              data={[...participationStatusMap].map(([value, meta]) => ({ value, label: meta.title }))}
            />
          </>
        }
        trailing={
          <>
            <AdminResultCount
              shown={filtered?.length ?? 0}
              total={participations?.length}
              filtered={isFiltered}
            />
            <AdminPagination
              page={activePage}
              onChange={setPage}
              total={filtered?.length}
              pageSize={PART_NUM_PER_PAGE}
            />
          </>
        }
      >
        <AdminSearch value={search} onChange={setSearch} placeholder={t('admin.placeholder.teams.search')} />
      </AdminToolbar>

      {error && !participations ? (
        <ErrorState
          message={t('admin.state.load_failed_review', { defaultValue: 'Could not load registrations.' })}
          onRetry={() => setReload((value) => value + 1)}
        />
      ) : !participations ? (
        <Panel>
          <LoadingState label={t('admin.tab.games.review')} />
        </Panel>
      ) : !paged?.length ? (
        <Panel>
          <EmptyState
            title={
              isFiltered
                ? t('admin.state.no_results', { defaultValue: 'No matching registrations' })
                : t('admin.content.games.review.empty.title')
            }
            description={
              isFiltered
                ? t('admin.state.no_results_hint', { defaultValue: 'Adjust the search or clear the filters.' })
                : t('admin.content.games.review.empty.description')
            }
          />
        </Panel>
      ) : (
        <Accordion variant="contained" chevronPosition="left" classNames={accordionClasses}>
          {paged.map((participation) => {
            const status = participationStatusMap.get(participation.status!)

            return (
              <Accordion.Item key={participation.id} value={String(participation.id)}>
                <Group gap={0} wrap="nowrap" align="stretch">
                  <Accordion.Control style={{ flex: 1, minWidth: 0 }}>
                    <Group justify="space-between" wrap="wrap" gap="sm">
                      <Group wrap="nowrap" gap="sm" style={{ minWidth: 0, flex: '1 1 14rem' }}>
                        <Avatar alt="" src={participation.team?.avatar} size="sm">
                          {participation.team?.name?.slice(0, 1) ?? 'T'}
                        </Avatar>
                        <AdminCellStack>
                          <AdminCellName title={participation.team?.name ?? undefined}>
                            {participation.team?.name ?? t('admin.placeholder.games.participation.team')}
                          </AdminCellName>
                          <AdminCellMeta title={participation.team?.bio ?? undefined}>
                            {participation.team?.bio ?? t('admin.placeholder.games.participation.bio')}
                          </AdminCellMeta>
                        </AdminCellStack>
                      </Group>

                      <Group wrap="wrap" gap="sm" justify="flex-end">
                        {hasDivisions && participation.status !== ParticipationStatus.Rejected && (
                          <Text size="xs" fw="bold" truncate maw="9rem">
                            {participation.divisionId
                              ? (divisionNameMap.get(participation.divisionId) ?? `#${participation.divisionId}`)
                              : t('admin.content.games.review.participation.no_division')}
                          </Text>
                        )}

                        <AdminCellMeta>
                          {t('admin.content.games.review.participation.stats', {
                            count: participation.registeredMembers?.length ?? 0,
                            total: participation.team?.members?.length ?? 0,
                          })}
                        </AdminCellMeta>

                        {status && (
                          <AdminStatusBadge
                            solid
                            label={status.title}
                            tone={STATUS_TONE.get(participation.status!) ?? 'neutral'}
                          />
                        )}
                      </Group>
                    </Group>
                  </Accordion.Control>

                  <Group gap={4} wrap="nowrap" pr="sm" onClick={(event) => event.stopPropagation()}>
                    {hasDivisions && participation.status !== ParticipationStatus.Rejected && (
                      <ActionIcon
                        size="sm"
                        variant="subtle"
                        disabled={disabled}
                        aria-label={t('admin.content.games.review.edit_division')}
                        onClick={() => {
                          setCurParticipation(participation)
                          setDivModalOpened(true)
                        }}
                      >
                        <Icon path={mdiPencil} size={0.6} />
                      </ActionIcon>
                    )}
                    <Box>
                      <ParticipationStatusControl
                        disabled={disabled}
                        participation={participation}
                        setParticipation={setParticipation}
                      />
                    </Box>
                  </Group>
                </Group>

                <Accordion.Panel>
                  <Stack gap="md">
                    {participation.team?.members?.map((user) => (
                      <MemberRow
                        key={user.userId}
                        user={user}
                        isRegistered={participation.registeredMembers?.some((id) => id === user.userId) ?? false}
                        isCaptain={participation.team?.captainId === user.userId}
                      />
                    ))}
                  </Stack>
                </Accordion.Panel>
              </Accordion.Item>
            )
          })}
        </Accordion>
      )}

      {hasDivisions && curParticipation && (
        <ParticipationDivisionEditModal
          title={t('admin.content.games.review.edit_division')}
          opened={divModalOpened}
          divisions={divisions ?? []}
          participateId={curParticipation.id ?? -1}
          currentDivisionId={curParticipation.divisionId ?? null}
          setParticipation={setParticipation}
          onClose={() => {
            setDivModalOpened(false)
            setCurParticipation(null)
          }}
        />
      )}
    </AdminGameLayout>
  )
}

export default GameTeamReview

import { Accordion, Avatar, Box, Group, SegmentedControl, Stack } from '@mantine/core'
import { useLocalStorage } from '@mantine/hooks'
import { mdiKeyAlert, mdiTarget } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { RequireRole } from '@Components/WithRole'
import { AdminMonitorLayout } from '@Components/admin/AdminMonitorLayout'
import { ParticipationStatusControl } from '@Components/admin/ParticipationStatusControl'
import {
  AdminCellMeta,
  AdminCellName,
  AdminCellStack,
  AdminMonoValue,
  AdminResultCount,
  AdminStatusBadge,
  AdminTable,
  AdminToolbar,
  showAdminSuccess,
} from '@Components/admin/shell'
import type { AdminTone } from '@Components/admin/shell'
import { DataTableColumn, EmptyState, ErrorState, Panel } from '@Components/zarrar'
import { useLanguage } from '@Utils/I18n'
import { showErrorMsg, useParticipationStatusMap } from '@Utils/Shared'
import { OnceSWRConfig } from '@Hooks/useConfig'
import { useUserRole } from '@Hooks/useUser'
import api, { CheatInfoModel, ParticipationEditModel, ParticipationStatus, Role } from '@Api'
import accordionClasses from '@Styles/Accordion.module.css'

const STATUS_TONE = new Map<ParticipationStatus, AdminTone>([
  [ParticipationStatus.Pending, 'warning'],
  [ParticipationStatus.Accepted, 'success'],
  [ParticipationStatus.Rejected, 'neutral'],
  [ParticipationStatus.Suspended, 'critical'],
])

enum CheatType {
  Submit = 'Submit',
  Owned = 'Owned',
}

interface CheatSubmissionInfo {
  time?: dayjs.Dayjs
  fingerprint?: string
  user?: string
  challenge?: string
  relatedTeam?: string
  cheatType: CheatType
}

interface CheatTeamInfo {
  name?: string
  avatar?: string | null
  teamId?: number
  status?: ParticipationStatus
  lastSubmitTime?: dayjs.Dayjs
  participateId?: number
  division?: string | null
  divisionId?: number | null
  submissionInfo: CheatSubmissionInfo[]
}

/**
 * Pivots the flat cheat record list into a per-team view.
 *
 * Each record ties the team that *owns* a flag to the team that *submitted* it,
 * so one record contributes an entry to both teams — that is why the source
 * data cannot simply be grouped.
 */
const toCheatTeamInfo = (cheatInfo: CheatInfoModel[]): Map<number, CheatTeamInfo> => {
  const teams = new Map<number, CheatTeamInfo>()

  for (const info of cheatInfo) {
    const { ownedTeam, submitTeam, submission } = info
    if (!ownedTeam || !submitTeam || !submission) continue

    const time = dayjs(submission.time)

    for (const part of [ownedTeam, submitTeam]) {
      if (!teams.has(part.id ?? -1)) {
        teams.set(part.id ?? -1, {
          name: part.team?.name,
          avatar: part.team?.avatar,
          teamId: part.team?.id,
          status: part.status,
          participateId: part.id,
          divisionId: part.divisionId,
          division: part.division,
          lastSubmitTime: time,
          submissionInfo: [],
        })
      }
    }

    const owned = teams.get(ownedTeam.id ?? -1)
    const submitted = teams.get(submitTeam.id ?? -1)
    if (!owned || !submitted) continue

    if (owned.lastSubmitTime?.isBefore(time)) owned.lastSubmitTime = time
    if (submitted.lastSubmitTime?.isBefore(time)) submitted.lastSubmitTime = time

    const base: CheatSubmissionInfo = {
      time,
      fingerprint: submission.fingerprint,
      user: submission.user,
      challenge: submission.challenge,
      cheatType: CheatType.Owned,
      relatedTeam: submitTeam.team?.name,
    }

    owned.submissionInfo.push(base)
    submitted.submissionInfo.push({ ...base, cheatType: CheatType.Submit, relatedTeam: ownedTeam.team?.name })
  }

  return teams
}

const CheatInfo: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1', 10)

  const { data: cheatInfo, error, mutate } = api.game.useGameCheatInfo(numId, OnceSWRConfig)

  const [disabled, setDisabled] = useState(false)
  const [teamInfo, setTeamInfo] = useState<Map<number, CheatTeamInfo>>()
  const [view, setView] = useLocalStorage({
    key: 'cheat-info-team-view',
    defaultValue: 'team',
    getInitialValueInEffect: false,
  })

  const { role } = useUserRole()
  const { t } = useTranslation()
  const { locale } = useLanguage()
  const participationStatusMap = useParticipationStatusMap()

  useEffect(() => {
    if (cheatInfo) setTeamInfo(toCheatTeamInfo(cheatInfo))
  }, [cheatInfo])

  const setParticipation = async (participationId: number, model: ParticipationEditModel) => {
    setDisabled(true)

    try {
      await api.admin.adminParticipation(participationId, model)
      setTeamInfo((current) => {
        if (!current) return current
        const existing = current.get(participationId)
        if (!existing) return current

        const next = new Map(current)
        next.set(participationId, { ...existing, status: model.status ?? existing.status })
        return next
      })
      showAdminSuccess(t('admin.notification.games.participation.updated'))
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setDisabled(false)
    }
  }

  const teams = useMemo(
    () =>
      [...(teamInfo?.values() ?? [])].sort((a, b) => (b.lastSubmitTime?.unix() ?? 0) - (a.lastSubmitTime?.unix() ?? 0)),
    [teamInfo]
  )

  const records = useMemo(
    () =>
      [...(cheatInfo ?? [])].sort(
        (a, b) => dayjs(b.submission?.time).unix() - dayjs(a.submission?.time).unix()
      ),
    [cheatInfo]
  )

  const recordColumns: DataTableColumn<CheatInfoModel>[] = [
    {
      key: 'time',
      header: t('common.label.time'),
      width: '9rem',
      nowrap: true,
      render: (item) => (
        <AdminCellMeta>{dayjs(item.submission?.time).locale(locale).format('SL HH:mm:ss')}</AdminCellMeta>
      ),
    },
    {
      key: 'owned',
      header: t('game.label.cheat_info.owned_team'),
      width: '13rem',
      rowHeader: true,
      render: (item) => (
        <AdminCellName title={item.ownedTeam?.team?.name}>{item.ownedTeam?.team?.name ?? '—'}</AdminCellName>
      ),
    },
    {
      key: 'submit',
      header: t('game.label.cheat_info.submit_team'),
      width: '13rem',
      render: (item) => (
        <AdminCellStack>
          <AdminCellName title={item.submitTeam?.team?.name}>{item.submitTeam?.team?.name ?? '—'}</AdminCellName>
          <AdminCellMeta title={item.submission?.user ?? undefined}>{item.submission?.user ?? '—'}</AdminCellMeta>
        </AdminCellStack>
      ),
    },
    {
      key: 'challenge',
      header: t('common.label.challenge'),
      width: '13rem',
      render: (item) => (
        <AdminCellName title={item.submission?.challenge ?? undefined}>
          {item.submission?.challenge ?? '—'}
        </AdminCellName>
      ),
    },
    {
      key: 'fingerprint',
      header: t('common.label.flag'),
      render: (item) => <AdminMonoValue wide value={item.submission?.fingerprint} placeholder="—" />,
    },
  ]

  return (
    <AdminMonitorLayout
      tab="cheatinfo"
      isLoading={!cheatInfo && !error}
      description={t('game.content.monitor.cheat_description', {
        defaultValue: 'Flags solved by one team and submitted by another, grouped by team or listed as records.',
      })}
    >
      <AdminToolbar
        filters={
          <SegmentedControl
            value={view}
            onChange={setView}
            data={[
              { value: 'team', label: t('game.content.team_view.label') },
              { value: 'record', label: t('game.label.cheat_info.records', { defaultValue: 'Records' }) },
            ]}
          />
        }
        trailing={<AdminResultCount shown={view === 'team' ? teams.length : records.length} />}
      />

      {error ? (
        <ErrorState
          message={t('admin.state.load_failed_cheat', { defaultValue: 'Could not load cheat records.' })}
          onRetry={() => mutate()}
        />
      ) : teams.length === 0 ? (
        <Panel>
          <EmptyState title={t('game.content.no_cheat.title')} description={t('game.content.no_cheat.comment')} />
        </Panel>
      ) : view === 'team' ? (
        <Accordion multiple variant="contained" chevronPosition="left" classNames={accordionClasses}>
          {teams.map((team) => {
            const status = team.status ? participationStatusMap.get(team.status) : undefined

            return (
              <Accordion.Item key={team.participateId} value={String(team.participateId)}>
                <Accordion.Control>
                  <Group justify="space-between" wrap="wrap" gap="sm">
                    <Group wrap="nowrap" gap="sm" style={{ minWidth: 0, flex: '1 1 14rem' }}>
                      <Avatar alt="" src={team.avatar} size="sm">
                        {team.name?.slice(0, 1) ?? 'T'}
                      </Avatar>
                      <AdminCellStack>
                        <AdminCellName title={team.name}>
                          {team.name ?? t('admin.placeholder.games.participation.team')}
                        </AdminCellName>
                        <AdminCellMeta>
                          {dayjs(team.lastSubmitTime).locale(locale).format('SL LTS')}
                        </AdminCellMeta>
                      </AdminCellStack>
                    </Group>

                    <Group wrap="wrap" gap="sm" justify="flex-end">
                      {team.division && <AdminStatusBadge tone="info" label={team.division} />}
                      <AdminStatusBadge
                        tone="critical"
                        label={t('game.label.cheat_info.record_count', {
                          defaultValue: '{{count}} records',
                          count: team.submissionInfo.length,
                        })}
                      />
                      {status && (
                        <AdminStatusBadge solid label={status.title} tone={STATUS_TONE.get(team.status!) ?? 'neutral'} />
                      )}
                      {RequireRole(Role.Admin, role) && team.participateId !== undefined && team.status && (
                        <Box onClick={(event) => event.stopPropagation()}>
                          <ParticipationStatusControl
                            disabled={disabled}
                            participation={{
                              id: team.participateId,
                              divisionId: team.divisionId,
                              status: team.status,
                            }}
                            setParticipation={setParticipation}
                          />
                        </Box>
                      )}
                    </Group>
                  </Group>
                </Accordion.Control>

                <Accordion.Panel>
                  <Stack gap="sm">
                    {[...team.submissionInfo]
                      .sort((a, b) => (b.time?.unix() ?? 0) - (a.time?.unix() ?? 0))
                      .map((record, index) => (
                        <Group key={`${record.time?.unix()}-${index}`} wrap="wrap" gap="sm" align="flex-start">
                          <AdminStatusBadge
                            tone={record.cheatType === CheatType.Owned ? 'critical' : 'warning'}
                            glyph={
                              <Icon path={record.cheatType === CheatType.Owned ? mdiKeyAlert : mdiTarget} size={0.6} />
                            }
                            label={record.cheatType}
                          />
                          <AdminCellMeta>{dayjs(record.time).locale(locale).format('SL HH:mm:ss')}</AdminCellMeta>
                          <AdminCellStack>
                            <AdminCellName title={record.relatedTeam}>{record.relatedTeam}</AdminCellName>
                            <AdminCellMeta>{record.user}</AdminCellMeta>
                          </AdminCellStack>
                          <AdminCellStack>
                            <AdminCellName title={record.challenge}>{record.challenge}</AdminCellName>
                            <AdminMonoValue wide value={record.fingerprint} />
                          </AdminCellStack>
                        </Group>
                      ))}
                  </Stack>
                </Accordion.Panel>
              </Accordion.Item>
            )
          })}
        </Accordion>
      ) : (
        <Panel bare>
          <AdminTable
            caption={t('game.tab.monitor.cheatinfo')}
            columns={recordColumns}
            rows={records}
            rowKey={(item, index) => `${item.submission?.time}-${index}`}
            maxHeight="calc(100vh - 22rem)"
            emptyTitle={t('game.content.no_cheat.title')}
            emptyDescription={t('game.content.no_cheat.comment')}
          />
        </Panel>
      )}
    </AdminMonitorLayout>
  )
}

export default CheatInfo

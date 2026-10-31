import { ActionIcon, Divider, Group, ScrollArea, Text, Tooltip } from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiClose, mdiDeleteForeverOutline, mdiDownloadMultiple } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { ScrollSelect } from '@Components/ScrollSelect'
import { ChallengeItem, FileItem, TeamItem } from '@Components/TrafficItems'
import { AdminMonitorLayout } from '@Components/admin/AdminMonitorLayout'
import { openAdminConfirm, showAdminSuccess } from '@Components/admin/shell'
import { EmptyState, ErrorState, Panel } from '@Components/zarrar'
import { useLanguage } from '@Utils/I18n'
import { HunamizeSize, showErrorMsg } from '@Utils/Shared'
import api, { FileRecord } from '@Api'
import classes from '@Styles/admin/AdminTraffic.module.css'

const SWROptions = {
  refreshInterval: 0,
  shouldRetryOnError: false,
  revalidateOnFocus: false,
}

/**
 * Packet-capture browser: challenge → team → captured files.
 *
 * The three panes are a responsive grid rather than a fixed 3/3/6 `Grid`: below
 * the desktop breakpoint they stack, so the file list keeps a usable width on a
 * tablet instead of being compressed into a quarter of the viewport.
 */
const Traffic: FC = () => {
  const { id } = useParams()
  const gameId = parseInt(id ?? '-1', 10)

  const [challengeId, setChallengeId] = useState<number | null>(null)
  const [participationId, setParticipationId] = useState<number | null>(null)
  const [disabled, setDisabled] = useState(false)

  const { t } = useTranslation()
  const { locale } = useLanguage()

  const {
    data: challengeTraffic,
    error,
    mutate: mutateChallenges,
  } = api.game.useGameGetChallengesWithTrafficCapturing(gameId, SWROptions)
  const { data: teamTraffic, mutate: mutateTeams } = api.game.useGameGetChallengeTraffic(
    challengeId ?? 0,
    SWROptions,
    !!challengeId
  )
  const { data: fileRecords, mutate: mutateTraffic } = api.game.useGameGetTeamTrafficAll(
    challengeId ?? 0,
    participationId ?? 0,
    SWROptions,
    !!challengeId && !!participationId
  )

  const onDownload = (item: FileRecord) => {
    if (!challengeId || !participationId || !item.fileName) return
    window.open(`/api/game/captures/${challengeId}/${participationId}/${item.fileName}`, '_blank', 'noopener,noreferrer')
  }

  const onDownloadAll = () => {
    if (!challengeId || !participationId) {
      showNotification({
        color: 'red',
        title: t('common.error.encountered'),
        message: t('game.notification.select_chal_and_part'),
        icon: <Icon path={mdiClose} size={1} />,
      })
      return
    }

    window.open(`/api/game/captures/${challengeId}/${participationId}/all`, '_blank', 'noopener,noreferrer')
  }

  const onDelete = async (item: FileRecord) => {
    if (!challengeId || !participationId || !item.fileName) return
    setDisabled(true)

    try {
      await api.game.gameDeleteTeamTraffic(challengeId, participationId, item.fileName)
      showAdminSuccess(t('game.notification.traffic.deleted'))
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      mutateTeams()
      mutateTraffic()
      setDisabled(false)
    }
  }

  const onDeleteAll = async () => {
    if (!challengeId || !participationId) return
    setDisabled(true)

    try {
      await api.game.gameDeleteAllTeamTraffic(challengeId, participationId)
      showAdminSuccess(t('game.notification.traffic.deleted'))
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      mutateTraffic()
      mutateTeams()
      mutateChallenges()
      setDisabled(false)
    }
  }

  const sortedChallenges = useMemo(
    () => [...(challengeTraffic ?? [])].sort((a, b) => a.category?.localeCompare(b.category ?? '') ?? 0),
    [challengeTraffic]
  )

  const sortedTeams = useMemo(
    () => [...(teamTraffic ?? [])].sort((a, b) => (a.teamId ?? 0) - (b.teamId ?? 0)),
    [teamTraffic]
  )

  const orderedFileRecords = useMemo(
    () => [...(fileRecords ?? [])].sort((a, b) => dayjs(b.updateTime).diff(dayjs(a.updateTime))),
    [fileRecords]
  )

  const totalFileSize = orderedFileRecords.reduce((sum, record) => sum + (record?.size ?? 0), 0)

  return (
    <AdminMonitorLayout
      tab="traffic"
      isLoading={!challengeTraffic && !error}
      description={t('game.content.monitor.traffic_description', {
        defaultValue: 'Captured traffic for challenges with packet capture enabled.',
      })}
    >
      {error ? (
        <ErrorState
          message={t('admin.state.load_failed_traffic', { defaultValue: 'Could not load captured traffic.' })}
          onRetry={() => mutateChallenges()}
        />
      ) : !sortedChallenges.length ? (
        <Panel>
          <EmptyState title={t('game.content.no_traffic.title')} description={t('game.content.no_traffic.comment')} />
        </Panel>
      ) : (
        <div className={classes.panes}>
          <Panel bare className={classes.pane}>
            <div className={classes.paneHead}>
              <Text size="sm" fw="bold">
                {t('common.label.challenge')}
              </Text>
            </div>
            <Divider />
            <ScrollSelect
              itemComponent={ChallengeItem}
              items={sortedChallenges}
              selectedId={challengeId}
              onSelect={setChallengeId}
              h="min(60vh, 34rem)"
            />
          </Panel>

          <Panel bare className={classes.pane}>
            <div className={classes.paneHead}>
              <Text size="sm" fw="bold">
                {t('common.label.team')}
              </Text>
            </div>
            <Divider />
            <ScrollSelect
              itemComponent={TeamItem}
              items={sortedTeams}
              selectedId={participationId}
              onSelect={setParticipationId}
              h="min(60vh, 34rem)"
            />
          </Panel>

          <Panel bare className={classes.paneWide}>
            <div className={classes.paneHead}>
              <Group gap="sm" wrap="nowrap">
                <Text size="sm" fw="bold">
                  {t('game.label.traffic')}
                </Text>
                <Text size="xs" c="dimmed" fw="bold">
                  {HunamizeSize(totalFileSize)}
                </Text>
              </Group>
              <Group gap="xs" wrap="nowrap">
                <Tooltip label={t('game.button.download.all_traffic')} withArrow position="left">
                  <ActionIcon
                    variant="subtle"
                    disabled={!challengeId || !participationId}
                    aria-label={t('game.button.download.all_traffic')}
                    onClick={onDownloadAll}
                  >
                    <Icon path={mdiDownloadMultiple} size={0.85} />
                  </ActionIcon>
                </Tooltip>
                <Tooltip label={t('game.button.delete.all_traffic')} withArrow position="left">
                  <ActionIcon
                    variant="subtle"
                    color="red"
                    disabled={disabled || !challengeId || !participationId}
                    aria-label={t('game.button.delete.all_traffic')}
                    onClick={() =>
                      openAdminConfirm({
                        t,
                        title: t('game.button.delete.all_traffic'),
                        message: t('game.content.traffic.deleted_all_confirm'),
                        onConfirm: onDeleteAll,
                      })
                    }
                  >
                    <Icon path={mdiDeleteForeverOutline} size={0.85} />
                  </ActionIcon>
                </Tooltip>
              </Group>
            </div>
            <Divider />
            {orderedFileRecords.length === 0 ? (
              <ScrollArea h="min(60vh, 34rem)">
                <EmptyState
                  compact
                  title={t('game.notification.select_chal_and_part')}
                  description={t('game.content.no_traffic.comment')}
                />
              </ScrollArea>
            ) : (
              <ScrollSelect
                itemComponent={FileItem}
                itemComponentProps={{ onDownload, onDelete, disabled, t, locale }}
                items={orderedFileRecords}
                h="min(60vh, 34rem)"
              />
            )}
          </Panel>
        </div>
      )}
    </AdminMonitorLayout>
  )
}

export default Traffic

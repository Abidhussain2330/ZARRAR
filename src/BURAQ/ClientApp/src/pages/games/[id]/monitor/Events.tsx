import { SegmentedControl, Switch } from '@mantine/core'
import { useLocalStorage } from '@mantine/hooks'
import { mdiCircleMedium, mdiExclamationThick, mdiFlag, mdiLightningBolt, mdiToggleSwitchOffOutline, mdiToggleSwitchOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import * as signalR from '@microsoft/signalr'
import dayjs from 'dayjs'
import { TFunction } from 'i18next'
import { FC, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { AdminMonitorLayout } from '@Components/admin/AdminMonitorLayout'
import { SwitchLabel } from '@Components/admin/SwitchLabel'
import {
  AdminCellMeta,
  AdminCellName,
  AdminCellStack,
  AdminOutageNotice,
  AdminPagination,
  AdminResultCount,
  AdminStatusBadge,
  AdminTable,
  AdminToolbar,
} from '@Components/admin/shell'
import type { AdminTone } from '@Components/admin/shell'
import { DataTableColumn, ErrorState, Panel } from '@Components/buraq'
import { useLanguage } from '@Utils/I18n'
import { showErrorMsg } from '@Utils/Shared'
import { useGame } from '@Hooks/useGame'
import api, { AnswerResult, EventType, GameEventInfoModel } from '@Api'

const ITEM_COUNT_PER_PAGE = 30

const EVENT_META = new Map<EventType, { icon: string; tone: AdminTone }>([
  [EventType.Normal, { icon: mdiLightningBolt, tone: 'neutral' }],
  [EventType.FlagSubmit, { icon: mdiFlag, tone: 'accent' }],
  [EventType.ContainerStart, { icon: mdiToggleSwitchOutline, tone: 'success' }],
  [EventType.ContainerDestroy, { icon: mdiToggleSwitchOffOutline, tone: 'critical' }],
  [EventType.CheatDetected, { icon: mdiExclamationThick, tone: 'warning' }],
])

const formatAnswer = (t: TFunction, res: AnswerResult) => {
  switch (res) {
    case AnswerResult.Accepted:
      return t('game.event.answer.accepted')
    case AnswerResult.WrongAnswer:
      return t('game.event.answer.wrong')
    case AnswerResult.CheatDetected:
      return t('game.event.answer.cheat')
    case AnswerResult.FlagSubmitted:
      return t('game.event.answer.submitted')
    case AnswerResult.NotFound:
      return t('game.event.answer.not_found')
    default:
      return ''
  }
}

const formatEvent = (t: TFunction, event: GameEventInfoModel) => {
  switch (event.type) {
    case EventType.Normal:
      return event.values.at(-1) || ''
    case EventType.FlagSubmit:
      return t('game.event.flag_submit', {
        status: formatAnswer(t, event.values.at(0) as AnswerResult),
        flag: event.values.at(1),
        chal: event.values.at(2),
        id: event.values.at(3),
      })
    case EventType.CheatDetected:
      return t('game.event.cheat_detected', {
        chal: event.values.at(0),
        team: event.values.at(1),
        steam: event.values.at(2),
      })
    case EventType.ContainerStart:
      return t('game.event.container.start', { id: event.values.at(0), chal: event.values.at(1) })
    case EventType.ContainerDestroy:
      return t('game.event.container.destroy', { id: event.values.at(0), chal: event.values.at(1) })
    default:
      return event.values.at(-1) || ''
  }
}

type StreamState = 'connecting' | 'live' | 'down' | 'ended'

/**
 * Live game event feed.
 *
 * Presented as a table rather than a card stack: an operator scanning a running
 * event is comparing timestamps, teams and event kinds across rows, which a
 * column layout supports and a stack of cards does not.
 */
const Events: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1', 10)

  const [hideContainerEvents, setHideContainerEvents] = useLocalStorage({
    key: 'hide-container-events',
    defaultValue: false,
    getInitialValueInEffect: false,
  })

  const [page, setPage] = useState(1)
  const [events, setEvents] = useState<GameEventInfoModel[]>()
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<unknown>(null)
  const [reload, setReload] = useState(0)
  const [typeFilter, setTypeFilter] = useState<string>('All')
  const [streamState, setStreamState] = useState<StreamState>('connecting')

  const [, forceUpdate] = useState(0)
  const liveEvents = useRef<GameEventInfoModel[]>([])

  const { game } = useGame(numId)
  const { t } = useTranslation()
  const { locale } = useLanguage()

  const fetchEvents = useCallback(async () => {
    setLoading(true)
    setError(null)

    try {
      const res = await api.game.gameEvents(numId, {
        hideContainer: hideContainerEvents,
        count: ITEM_COUNT_PER_PAGE,
        skip: (page - 1) * ITEM_COUNT_PER_PAGE,
      })
      setEvents(res.data)
    } catch (err) {
      setError(err)
      showErrorMsg(err, t)
    } finally {
      setLoading(false)
    }
  }, [numId, page, hideContainerEvents, t])

  useEffect(() => {
    fetchEvents()
    if (page === 1) liveEvents.current = []
  }, [fetchEvents, reload])

  useEffect(() => {
    if (!game?.end || new Date() >= new Date(game.end)) {
      setStreamState('ended')
      return
    }

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hub/monitor?game=${numId}`)
      .withHubProtocol(new signalR.JsonHubProtocol())
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.None)
      .build()

    connection.serverTimeoutInMilliseconds = 60 * 1000 * 60 * 2

    connection.on('ReceivedGameEvent', (message: GameEventInfoModel) => {
      liveEvents.current = [message, ...liveEvents.current]
      forceUpdate((value) => value + 1)
    })

    connection.onreconnecting(() => setStreamState('connecting'))
    connection.onreconnected(() => setStreamState('live'))
    connection.onclose(() => setStreamState('down'))

    connection
      .start()
      .then(() => setStreamState('live'))
      .catch(() => setStreamState('down'))

    return () => {
      connection.stop().catch(() => setStreamState('down'))
    }
  }, [game, numId, reload])

  const rows = useMemo(() => {
    const live =
      page === 1
        ? liveEvents.current.filter(
            (event) =>
              !hideContainerEvents ||
              (event.type !== EventType.ContainerStart && event.type !== EventType.ContainerDestroy)
          )
        : []

    return [...live, ...(events ?? [])].filter((event) => typeFilter === 'All' || event.type === typeFilter)
  }, [events, page, hideContainerEvents, typeFilter, liveEvents.current.length])

  const columns: DataTableColumn<GameEventInfoModel>[] = [
    {
      key: 'type',
      header: t('admin.label.logs.status'),
      width: '10rem',
      render: (event) => {
        const meta = EVENT_META.get(event.type)
        return (
          <AdminStatusBadge
            solid
            tone={meta?.tone ?? 'neutral'}
            glyph={meta ? <Icon path={meta.icon} size={0.6} /> : undefined}
            label={event.type}
          />
        )
      },
    },
    {
      key: 'time',
      header: t('common.label.time'),
      width: '9rem',
      nowrap: true,
      render: (event) => <AdminCellMeta>{dayjs(event.time).locale(locale).format('SL LTS')}</AdminCellMeta>,
    },
    {
      key: 'who',
      header: t('common.label.team'),
      width: '14rem',
      rowHeader: true,
      render: (event) => (
        <AdminCellStack>
          <AdminCellName title={event.team ?? undefined}>{event.team ?? '—'}</AdminCellName>
          <AdminCellMeta title={event.user ?? undefined}>{event.user ?? '—'}</AdminCellMeta>
        </AdminCellStack>
      ),
    },
    {
      key: 'message',
      header: t('admin.label.logs.message'),
      render: (event) => <span style={{ overflowWrap: 'anywhere' }}>{formatEvent(t, event)}</span>,
    },
  ]

  return (
    <AdminMonitorLayout
      tab="events"
      description={t('game.content.monitor.events_description', {
        defaultValue: 'Submissions, container lifecycle and cheat detections as they happen.',
      })}
      actions={
        <AdminStatusBadge
          solid
          glyph={<Icon path={mdiCircleMedium} size={0.6} />}
          tone={streamState === 'live' ? 'success' : streamState === 'down' ? 'critical' : 'neutral'}
          label={
            streamState === 'live'
              ? t('admin.state.stream_live', { defaultValue: 'Live' })
              : streamState === 'connecting'
                ? t('admin.state.stream_connecting', { defaultValue: 'Connecting' })
                : streamState === 'ended'
                  ? t('admin.state.stream_ended', { defaultValue: 'Event ended' })
                  : t('admin.state.stream_down', { defaultValue: 'Stream offline' })
          }
        />
      }
    >
      {streamState === 'down' && (
        <AdminOutageNotice
          onRetry={() => setReload((value) => value + 1)}
          retryLabel={t('common.state.error.retry', { defaultValue: 'Try again' })}
          message={t('admin.state.stream_down_detail', {
            defaultValue:
              'The live stream is disconnected. Stored entries below are still current; new entries will not appear until it reconnects.',
          })}
        />
      )}

      <AdminToolbar
        filters={
          <>
            <SegmentedControl
              value={typeFilter}
              onChange={setTypeFilter}
              data={[{ value: 'All', label: 'All' }, ...Object.values(EventType).map((value) => ({ value, label: value }))]}
            />
            <Switch
              checked={hideContainerEvents}
              onChange={(e) => setHideContainerEvents(e.currentTarget.checked)}
              label={SwitchLabel(
                t('game.content.hide_container_events.label'),
                t('game.content.hide_container_events.description')
              )}
            />
          </>
        }
        trailing={
          <>
            <AdminResultCount shown={rows.length} filtered={typeFilter !== 'All' || hideContainerEvents} />
            <AdminPagination
              page={page}
              onChange={setPage}
              pageSize={ITEM_COUNT_PER_PAGE}
              hasNext={(events?.length ?? 0) >= ITEM_COUNT_PER_PAGE}
            />
          </>
        }
      />

      <Panel bare>
        {error && !events ? (
          <ErrorState
            message={t('game.notification.fetch_failed.event')}
            onRetry={() => setReload((value) => value + 1)}
          />
        ) : (
          <AdminTable
            caption={t('game.tab.monitor.events')}
            columns={columns}
            rows={rows}
            rowKey={(event, index) => `${event.time}-${index}`}
            loading={loading && !events}
            maxHeight="calc(100vh - 22rem)"
            emptyTitle={t('admin.state.no_events', { defaultValue: 'No events' })}
          />
        )}
      </Panel>
    </AdminMonitorLayout>
  )
}

export default Events

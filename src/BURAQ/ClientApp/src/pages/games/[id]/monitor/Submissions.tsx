import { ActionIcon, SegmentedControl, Tooltip } from '@mantine/core'
import { mdiCircleMedium, mdiDownload } from '@mdi/js'
import { Icon } from '@mdi/react'
import * as signalR from '@microsoft/signalr'
import dayjs from 'dayjs'
import { FC, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { AdminMonitorLayout } from '@Components/admin/AdminMonitorLayout'
import {
  AdminCellMeta,
  AdminCellName,
  AdminCellStack,
  AdminMonoValue,
  AdminOutageNotice,
  AdminPagination,
  AdminResultCount,
  AdminStatusBadge,
  AdminTable,
  AdminToolbar,
} from '@Components/admin/shell'
import type { AdminTone } from '@Components/admin/shell'
import { DataTableColumn, ErrorState, Panel } from '@Components/buraq'
import { downloadBlob } from '@Utils/ApiHelper'
import { useLanguage } from '@Utils/I18n'
import { showErrorMsg } from '@Utils/Shared'
import { useGame } from '@Hooks/useGame'
import api, { AnswerResult, SubmissionInfoModel } from '@Api'

const ITEM_COUNT_PER_PAGE = 50

const RESULT_LABEL = new Map<AnswerResult, string>([
  [AnswerResult.Accepted, 'AC'],
  [AnswerResult.WrongAnswer, 'WA'],
  [AnswerResult.CheatDetected, 'CD'],
  [AnswerResult.NotFound, 'NF'],
  [AnswerResult.FlagSubmitted, '…'],
])

const RESULT_TONE = new Map<AnswerResult, AdminTone>([
  [AnswerResult.Accepted, 'success'],
  [AnswerResult.WrongAnswer, 'critical'],
  [AnswerResult.CheatDetected, 'warning'],
  [AnswerResult.NotFound, 'neutral'],
  [AnswerResult.FlagSubmitted, 'neutral'],
])

type StreamState = 'connecting' | 'live' | 'down' | 'ended'

/**
 * Live submission feed.
 *
 * The flag column shows `fingerprint` — the HMAC the API returns — and never a
 * submitted flag value. That is a deliberate property of the DTO, not a display
 * choice, and it is why this screen can be left open on a projector.
 */
const Submissions: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1', 10)

  const [page, setPage] = useState(1)
  const [type, setType] = useState<AnswerResult | 'All'>('All')
  const [submissions, setSubmissions] = useState<SubmissionInfoModel[]>()
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<unknown>(null)
  const [reload, setReload] = useState(0)
  const [downloading, setDownloading] = useState(false)
  const [streamState, setStreamState] = useState<StreamState>('connecting')

  const [, forceUpdate] = useState(0)
  const liveSubmissions = useRef<SubmissionInfoModel[]>([])

  const { game } = useGame(numId)
  const { t } = useTranslation()
  const { locale } = useLanguage()

  const fetchSubmissions = useCallback(async () => {
    setLoading(true)
    setError(null)

    try {
      const res = await api.game.gameSubmissions(numId, {
        type: type === 'All' ? undefined : type,
        count: ITEM_COUNT_PER_PAGE,
        skip: (page - 1) * ITEM_COUNT_PER_PAGE,
      })
      setSubmissions(res.data)
    } catch (err) {
      setError(err)
      showErrorMsg(err, t)
    } finally {
      setLoading(false)
    }
  }, [numId, page, type, t])

  useEffect(() => {
    fetchSubmissions()
    if (page === 1) liveSubmissions.current = []
  }, [fetchSubmissions, reload])

  useEffect(() => {
    // Nothing new can arrive after an event ends, so no socket is opened and
    // the badge says "ended" rather than pretending to be a broken live feed.
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

    connection.on('ReceivedSubmissions', (message: SubmissionInfoModel) => {
      liveSubmissions.current = [message, ...liveSubmissions.current]
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
    const live = page === 1 ? liveSubmissions.current.filter((item) => type === 'All' || item.status === type) : []
    return [...live, ...(submissions ?? [])]
  }, [submissions, page, type, liveSubmissions.current.length])

  const columns: DataTableColumn<SubmissionInfoModel>[] = [
    {
      key: 'result',
      header: t('admin.label.logs.status'),
      width: '7rem',
      render: (item) => {
        const status = item.status ?? AnswerResult.FlagSubmitted
        return (
          <AdminStatusBadge
            solid
            tone={RESULT_TONE.get(status) ?? 'neutral'}
            label={RESULT_LABEL.get(status) ?? status}
            title={status}
          />
        )
      },
    },
    {
      key: 'time',
      header: t('common.label.time'),
      width: '9rem',
      nowrap: true,
      render: (item) => <AdminCellMeta>{dayjs(item.time).locale(locale).format('SL HH:mm:ss')}</AdminCellMeta>,
    },
    {
      key: 'who',
      header: t('common.label.team'),
      width: '14rem',
      rowHeader: true,
      render: (item) => (
        <AdminCellStack>
          <AdminCellName title={item.team ?? undefined}>{item.team ?? '—'}</AdminCellName>
          <AdminCellMeta title={item.user ?? undefined}>{item.user ?? '—'}</AdminCellMeta>
        </AdminCellStack>
      ),
    },
    {
      key: 'challenge',
      header: t('common.label.challenge'),
      width: '14rem',
      render: (item) => <AdminCellName title={item.challenge ?? undefined}>{item.challenge ?? '—'}</AdminCellName>,
    },
    {
      key: 'fingerprint',
      header: t('common.label.flag'),
      render: (item) => <AdminMonoValue wide value={item.fingerprint} placeholder="—" />,
    },
  ]

  return (
    <AdminMonitorLayout
      tab="submissions"
      description={t('game.content.monitor.submissions_description', {
        defaultValue: 'Every flag submission, identified by HMAC fingerprint rather than by value.',
      })}
      actions={
        <>
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
          <Tooltip label={t('game.button.download.submissionsheet')} withArrow>
            <ActionIcon
              variant="default"
              size="lg"
              disabled={downloading}
              aria-label={t('game.button.download.submissionsheet')}
              onClick={() =>
                downloadBlob(
                  api.game.gameSubmissionSheet(numId, { format: 'blob' }),
                  setDownloading,
                  t,
                  `Submission_${numId}_${Date.now()}.xlsx`
                )
              }
            >
              <Icon path={mdiDownload} size={0.9} />
            </ActionIcon>
          </Tooltip>
        </>
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
          <SegmentedControl
            value={type}
            onChange={(value) => {
              setType(value as AnswerResult | 'All')
              setPage(1)
            }}
            data={[
              { value: 'All', label: 'All' },
              ...Object.values(AnswerResult)
                .filter((value) => value !== AnswerResult.FlagSubmitted)
                .map((value) => ({ value, label: RESULT_LABEL.get(value) ?? value })),
            ]}
          />
        }
        trailing={
          <>
            <AdminResultCount shown={rows.length} filtered={type !== 'All'} />
            <AdminPagination
              page={page}
              onChange={setPage}
              pageSize={ITEM_COUNT_PER_PAGE}
              hasNext={(submissions?.length ?? 0) >= ITEM_COUNT_PER_PAGE}
            />
          </>
        }
      />

      <Panel bare>
        {error && !submissions ? (
          <ErrorState
            message={t('game.notification.fetch_failed.submission')}
            onRetry={() => setReload((value) => value + 1)}
          />
        ) : (
          <AdminTable
            caption={t('game.tab.monitor.submissions')}
            columns={columns}
            rows={rows}
            rowKey={(item, index) => `${item.time}-${index}`}
            loading={loading && !submissions}
            maxHeight="calc(100vh - 22rem)"
            emptyTitle={t('admin.state.no_submissions', { defaultValue: 'No submissions' })}
          />
        )}
      </Panel>
    </AdminMonitorLayout>
  )
}

export default Submissions

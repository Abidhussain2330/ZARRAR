import { SegmentedControl } from '@mantine/core'
import { mdiCircleMedium } from '@mdi/js'
import { Icon } from '@mdi/react'
import * as signalR from '@microsoft/signalr'
import dayjs from 'dayjs'
import { FC, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  AdminCellMeta,
  AdminCellStack,
  AdminLayout,
  AdminMonoValue,
  AdminOutageNotice,
  AdminPagination,
  AdminResultCount,
  AdminStatusBadge,
  AdminTable,
  AdminToolbar,
} from '@Components/admin/shell'
import { DataTableColumn, ErrorState, Panel } from '@Components/zarrar'
import { useLanguage } from '@Utils/I18n'
import { showErrorMsg, TaskStatusColorMap } from '@Utils/Shared'
import api, { LogMessageModel, TaskStatus } from '@Api'

const ITEM_COUNT_PER_PAGE = 50
const MAX_RETAINED_LOGS = 300

enum LogLevel {
  All = 'All',
  Info = 'Information',
  Warn = 'Warning',
  Error = 'Error',
}

const LEVEL_TONE: Record<string, 'neutral' | 'info' | 'warning' | 'critical'> = {
  Information: 'info',
  Warning: 'warning',
  Error: 'critical',
}

const STATUS_TONE: Record<string, 'success' | 'warning' | 'critical' | 'neutral'> = {
  green: 'success',
  lime: 'success',
  yellow: 'warning',
  orange: 'warning',
  red: 'critical',
  alert: 'critical',
  violet: 'neutral',
  gray: 'neutral',
}

type StreamState = 'connecting' | 'live' | 'down'

/**
 * System log viewer.
 *
 * Two sources feed one table: a paged REST fetch for history and a SignalR hub
 * for entries arriving now. Live entries are only merged on page 1 — appending
 * them while the operator is reading page 4 would silently shift every row.
 *
 * The stream's health is surfaced rather than hidden. The previous screen
 * swallowed a failed handshake into `console.error`, so a viewer sitting on a
 * dead socket had no way to tell it apart from a quiet system.
 */
const Logs: FC = () => {
  const [level, setLevel] = useState<LogLevel>(LogLevel.Info)
  const [page, setPage] = useState(1)
  const [logs, setLogs] = useState<LogMessageModel[]>()
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<unknown>(null)
  const [reload, setReload] = useState(0)
  const [streamState, setStreamState] = useState<StreamState>('connecting')

  const [, forceUpdate] = useState(0)
  const liveLogs = useRef<LogMessageModel[]>([])

  const { t } = useTranslation()
  const { locale } = useLanguage()

  const fetchLogs = useCallback(async () => {
    setLoading(true)
    setError(null)

    try {
      const res = await api.admin.adminLogs({
        level,
        count: ITEM_COUNT_PER_PAGE,
        skip: (page - 1) * ITEM_COUNT_PER_PAGE,
      })
      setLogs(res.data)
    } catch (err) {
      setError(err)
      showErrorMsg(err, t)
    } finally {
      setLoading(false)
    }
  }, [level, page, t])

  useEffect(() => {
    fetchLogs()
    if (page === 1) liveLogs.current = []
  }, [fetchLogs, reload])

  useEffect(() => {
    setPage(1)
  }, [level])

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hub/admin')
      .withHubProtocol(new signalR.JsonHubProtocol())
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.None)
      .build()

    connection.serverTimeoutInMilliseconds = 60 * 1000 * 60 * 24

    connection.on('ReceivedLog', (message: LogMessageModel) => {
      liveLogs.current = [message, ...liveLogs.current].slice(0, MAX_RETAINED_LOGS)
      forceUpdate((value) => value + 1)
    })
    // SOC updates share the typed admin hub but are consumed by the SOC page.
    connection.on('ReceivedSocUpdate', () => undefined)

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
  }, [reload])

  const rows = useMemo(() => {
    const live = page === 1 ? liveLogs.current : []
    return [...live, ...(logs ?? [])].filter((item) => level === LogLevel.All || item.level === level)
    // `liveLogs.current` is a ref; `forceUpdate` is what actually re-runs this.
  }, [logs, level, page, liveLogs.current.length])

  const columns: DataTableColumn<LogMessageModel>[] = [
    {
      key: 'time',
      header: t('common.label.time'),
      width: '10rem',
      nowrap: true,
      render: (log) => (
        <AdminCellStack>
          <AdminCellMeta>{dayjs(log.time).locale(locale).format('SL HH:mm:ss')}</AdminCellMeta>
          {log.level && (
            <AdminStatusBadge tone={LEVEL_TONE[log.level] ?? 'neutral'} label={log.level.slice(0, 4)} />
          )}
        </AdminCellStack>
      ),
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

  return (
    <AdminLayout
      title={t('admin.tab.logs')}
      description={t('admin.nav.logs_description', {
        defaultValue: 'Audit trail and system events, with live entries streamed as they occur.',
      })}
      breadcrumbs={[{ label: t('admin.nav.group.observability', { defaultValue: 'Observability' }) }]}
      actions={
        <AdminStatusBadge
          solid
          glyph={<Icon path={mdiCircleMedium} size={0.6} />}
          tone={streamState === 'live' ? 'success' : streamState === 'connecting' ? 'warning' : 'critical'}
          label={
            streamState === 'live'
              ? t('admin.state.stream_live', { defaultValue: 'Live' })
              : streamState === 'connecting'
                ? t('admin.state.stream_connecting', { defaultValue: 'Connecting' })
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
              'The live log stream is disconnected. Stored entries below are still current; new entries will not appear until it reconnects.',
          })}
        />
      )}

      <AdminToolbar
        filters={
          <SegmentedControl
            value={level}
            onChange={(value) => setLevel(value as LogLevel)}
            data={Object.entries(LogLevel).map(([label, value]) => ({ value, label }))}
          />
        }
        trailing={
          <>
            <AdminResultCount shown={rows.length} filtered={level !== LogLevel.All} />
            <AdminPagination
              page={page}
              onChange={setPage}
              pageSize={ITEM_COUNT_PER_PAGE}
              hasNext={(logs?.length ?? 0) >= ITEM_COUNT_PER_PAGE}
            />
          </>
        }
      />

      <Panel bare>
        {error && !logs ? (
          <ErrorState
            message={t('admin.state.load_failed_logs', { defaultValue: 'Could not load log entries.' })}
            onRetry={() => setReload((value) => value + 1)}
          />
        ) : (
          <AdminTable
            caption={t('admin.tab.logs')}
            columns={columns}
            rows={rows}
            rowKey={(log, index) => `${log.time}-${index}`}
            loading={loading && !logs}
            maxHeight="calc(100vh - 22rem)"
            emptyTitle={t('admin.state.no_logs', { defaultValue: 'No log entries' })}
            emptyDescription={t('admin.state.no_logs_hint', {
              defaultValue: 'Nothing matched this level on this page.',
            })}
          />
        )}
      </Panel>
    </AdminLayout>
  )
}

export default Logs

import { ActionIcon, Button, Code, Group, Modal, Select, SegmentedControl, Stack, Tabs, Text, TextInput, Tooltip } from '@mantine/core'
import {
  mdiAlertOctagonOutline,
  mdiAlertOutline,
  mdiCheckCircleOutline,
  mdiCpu64Bit,
  mdiDeleteOutline,
  mdiHarddisk,
  mdiLan,
  mdiMemory,
  mdiPackageVariantClosed,
  mdiRefresh,
  mdiShieldSearch,
  mdiTimerPlusOutline,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import { HubConnectionBuilder, LogLevel as SignalRLogLevel } from '@microsoft/signalr'
import type { EChartsOption } from 'echarts'
import dayjs from 'dayjs'
import { FC, useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import api, { ContentType, LogMessageModel, TaskStatus } from '@Api'
import {
  AdminCellMeta,
  AdminCellName,
  AdminCellStack,
  AdminLayout,
  AdminMetricGrid,
  AdminMonoValue,
  AdminOutageNotice,
  AdminPagination,
  AdminResultCount,
  AdminStatusBadge,
  AdminTable,
  AdminToolbar,
  openAdminConfirm,
  showAdminSuccess,
} from '@Components/admin/shell'
import type { AdminTone } from '@Components/admin/shell'
import { Panel, StatCard, zarrarChartBase, zarrarXAxis, zarrarYAxis, getZarrarChartTheme } from '@Components/zarrar'
import type { DataTableColumn } from '@Components/zarrar'
import { EchartsContainer } from '@Components/charts/EchartsContainer'
import { SocCaseDetailModal } from '@Components/admin/SocCaseDetailModal'
import { SocOperations } from '@Components/admin/SocOperations'
import { showErrorMsg, TaskStatusColorMap } from '@Utils/Shared'
import { socFlagRowKey } from '@Utils/SocRowKeys'
import classes from '@Styles/admin/Soc.module.css'

type Range = '15m' | '1h' | '6h' | '24h' | '7d'
interface MetricPoint { time: number; cpuPercent: number; memoryPercent: number; storagePercent: number; runningInstances: number }
interface ContainerMetric { containerGuid: string; cpuPercent?: number; memoryUsedBytes?: number; memoryLimitBytes?: number; networkRxBytes: number; networkTxBytes: number; restartCount: number; runtimeStatus: string }
interface HostSnapshot { node: string; status: string; sampledAt: number; cpuCores: number; cpuPercent?: number; loadAverage?: number; memoryUsedBytes?: number; memoryTotalBytes?: number; memoryPercent?: number; storageUsedBytes?: number; storageTotalBytes?: number; storagePercent?: number; services: Record<string, string> }
interface ResourceSnapshot { host: HostSnapshot; point: MetricPoint; containerMetrics: ContainerMetric[] }
interface ProviderSnapshot { provider: string; node: string; status: string; capacity?: number; running: number; pending: number; unavailable: number; isolation: string; version?: string; sampledAt: number }
interface Overview { activeTeams: number; onlineUsers: number; activeInstances: number; totalSubmissions: number; correctSolves: number; failedAttempts: number; openAlerts: number; criticalAlerts: number; openRiskCases: number; failedInstances: number; provider: ProviderSnapshot; host: HostSnapshot; history: MetricPoint[]; containerMetrics: ContainerMetric[] }
interface Instance { instanceId: string; containerId: string; teamId: number; team: string; gameId: number; game: string; challengeId: number; challenge: string; node: string; createdAt: number; expiresAt: number; status: string; networkMode: string; cpuPercent?: number; memoryUsedBytes?: number; memoryLimitBytes?: number; networkRxBytes: number; networkTxBytes: number; restartCount: number; cpuLimitTenths?: number; configuredMemoryMb?: number }
interface Alert { id: number; createdAt: number; severity: string; category: string; event: string; description: string; teamId?: number; team?: string; relatedTeam?: string; game?: string; challenge?: string; instanceId?: string; riskImpact: number; status: string; evidenceFingerprint?: string }
interface RiskCase { id: string; caseKey: string; title: string; summary: string; status: string; riskScore: number; confidence: number; ruleHitCount: number; evidenceCount: number; firstSeenAt: number; lastSeenAt: number; updatedAt: number; gameId: number; game: string; primaryTeamId: number; primaryTeam: string; relatedTeamId?: number; relatedTeam?: string; assignedAdminId?: string; notes?: string }
interface TeamRisk { teamId: number; team: string; riskScore: number; classification: string; openAlerts: number; criticalAlerts: number; submissions: number; solves: number; runningInstances: number; reasons: string[] }
interface FlagInfo { fingerprint: string; teamId: number; team: string; game: string; challenge: string; instanceId?: string; generatedAt?: number; status: string; assignmentId?: string; artifactHash?: string; assignedAt?: number; firstDownloadedAt?: number; lastDownloadedAt?: number; downloadCount: number; immutable: boolean }
interface Timeline { id: number; timestamp: number; severity: string; category: string; event: string; team?: string; relatedTeam?: string; game?: string; challenge?: string; instanceId?: string; details: string; eventId?: string; source?: string; correlationId?: string; actorUserId?: string; userId?: string }
interface TimelineDetail extends Timeline { payloadJson?: string; teamId?: number; relatedTeamId?: number; gameId?: number; challengeId?: number; submissionId?: number }
interface OutboxStatus { pending: number; retrying: number; deadLettered: number; oldestPending?: number; lastProcessed?: number }
interface DetectionQueueStatus { pending: number; running: number; failed: number; oldestPending?: number; lastCompleted?: number }
interface SharedRuntime { challengeId: number; challenge: string; origin: string; audience: string; healthPath: string; stateMode: string; isEnabled: boolean; activeSessions: number; maxConcurrentSessions: number; healthStatus: string; lastHealthCheckUtc?: number; lastHealthError?: string; updatedAtUtc: number }
interface VariantBuilderStatus { enabled: boolean; configured: boolean; validatedTemplates: number; datasetAvailable: number; pending: number; running: number; succeeded: number; failed: number; oldestPendingAtUtc?: number }
interface ArrayResponse<T> { data: T[]; total: number; length: number }
interface LedgerStatus { intact: boolean; verifiedSeals: number; verifiedEvents: number; unsealedEvents: number; brokenAtSealId?: number; problem?: string; lastSealAt?: number; prunedSeals: number }

const PAGE_SIZE = 30
const MAX_SOC_LOGS = 300
const MAX_RESOURCE_HISTORY = 7 * 24 * 60 * 6
type SystemLogLevel = 'All' | 'Information' | 'Warning' | 'Error'
const SYSTEM_LOG_LEVELS: { label: string; value: SystemLogLevel }[] = [
  { label: 'All', value: 'All' },
  { label: 'Info', value: 'Information' },
  { label: 'Warn', value: 'Warning' },
  { label: 'Error', value: 'Error' },
]
const LOG_LEVEL_TONE: Record<string, 'neutral' | 'info' | 'warning' | 'critical'> = {
  Information: 'info',
  Warning: 'warning',
  Error: 'critical',
}
const LOG_STATUS_TONE: Record<string, 'success' | 'warning' | 'critical' | 'neutral'> = {
  green: 'success',
  lime: 'success',
  yellow: 'warning',
  orange: 'warning',
  red: 'critical',
  alert: 'critical',
  violet: 'neutral',
  gray: 'neutral',
}
const bytes = (value?: number | null) => value == null ? '—' : value >= 1024 ** 3 ? `${(value / 1024 ** 3).toFixed(1)} GB` : `${Math.round(value / 1024 ** 2)} MB`
const percent = (value?: number | null) => value == null ? '—' : `${value.toFixed(1)}%`
const duration = (target: number) => {
  const seconds = Math.max(0, Math.floor((target - Date.now()) / 1000))
  const hours = Math.floor(seconds / 3600)
  const minutes = Math.floor((seconds % 3600) / 60)
  return hours > 0 ? `${hours}h ${minutes}m` : `${minutes}m ${seconds % 60}s`
}
const tone = (value: string): Exclude<AdminTone, 'info'> => {
  const v = value.toLowerCase()
  if (v.includes('critical') || v.includes('failed') || v.includes('offline')) return 'critical'
  if (v.includes('high') || v.includes('warning') || v.includes('degraded') || v.includes('pending')) return 'warning'
  if (v.includes('healthy') || v.includes('running') || v.includes('resolved') || v.includes('clean')) return 'success'
  if (v.includes('medium') || v.includes('investigating')) return 'accent'
  return 'neutral'
}
const arrayData = <T,>(value: unknown): ArrayResponse<T> => value as ArrayResponse<T>
const normalizeSocPayload = (value: unknown): unknown => {
  if (Array.isArray(value)) return value.map(normalizeSocPayload)
  if (!value || typeof value !== 'object') return value
  return Object.fromEntries(
    Object.entries(value).map(([key, nested]) => [
      key ? key[0].toLowerCase() + key.slice(1) : key,
      normalizeSocPayload(nested),
    ])
  )
}

const Soc: FC = () => {
  const { t } = useTranslation()
  const [active, setActive] = useState<string | null>('overview')
  const [range, setRange] = useState<Range>('1h')
  const [overview, setOverview] = useState<Overview>()
  const [instances, setInstances] = useState<ArrayResponse<Instance>>()
  const [alerts, setAlerts] = useState<ArrayResponse<Alert>>()
  const [cases, setCases] = useState<ArrayResponse<RiskCase>>()
  const [teams, setTeams] = useState<ArrayResponse<TeamRisk>>()
  const [flags, setFlags] = useState<ArrayResponse<FlagInfo>>()
  const [timeline, setTimeline] = useState<ArrayResponse<Timeline>>()
  const [outbox, setOutbox] = useState<OutboxStatus>()
  const [detectionQueue, setDetectionQueue] = useState<DetectionQueueStatus>()
  const [sharedRuntimes, setSharedRuntimes] = useState<SharedRuntime[]>([])
  const [variantBuilder, setVariantBuilder] = useState<VariantBuilderStatus>()
  const [systemLogs, setSystemLogs] = useState<LogMessageModel[]>()
  const [systemLogLevel, setSystemLogLevel] = useState<SystemLogLevel>('All')
  const [logsLoading, setLogsLoading] = useState(true)
  const [logsError, setLogsError] = useState<unknown>()
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<string | null>(null)
  const [category, setCategory] = useState<string | null>(null)
  const [severity, setSeverity] = useState<string | null>(null)
  const [gameFilter, setGameFilter] = useState('')
  const [eventType, setEventType] = useState('')
  const [source, setSource] = useState('')
  const [artifactHash, setArtifactHash] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<unknown>()
  const [live, setLive] = useState(false)
  const [reload, setReload] = useState(0)
  const [selectedCaseId, setSelectedCaseId] = useState<string>()
  const [selectedTimelineId, setSelectedTimelineId] = useState<number>()
  const [timelineDetail, setTimelineDetail] = useState<TimelineDetail>()
  const [ledger, setLedger] = useState<LedgerStatus>()
  const [ledgerBusy, setLedgerBusy] = useState(false)

  // S3 — re-check the timeline's hash-chained seals on demand (it reads every sealed row).
  const verifyLedger = async () => {
    setLedgerBusy(true)
    try {
      const response = await api.request<LedgerStatus>({ path: '/api/soc/ledger', method: 'GET', format: 'json' })
      setLedger(response.data)
    } catch (err) { showErrorMsg(err, t) } finally { setLedgerBusy(false) }
  }

  const updateAlert = async (id: number, nextStatus: string) => {
    try {
      await api.request({ path: `/api/soc/alerts/${id}`, method: 'PATCH', body: { status: nextStatus }, type: ContentType.Json })
      showAdminSuccess(`Alert marked ${nextStatus}`)
      setReload(x => x + 1)
    } catch (err) { showErrorMsg(err, t) }
  }
  const resolveShownAlerts = (nextStatus: 'Resolved' | 'Clean') => {
    const ids = (alerts?.data ?? []).filter(x => x.status === 'Open' || x.status === 'Investigating').map(x => x.id)
    if (ids.length === 0) return
    openAdminConfirm({
      t,
      severity: 'danger',
      title: `Mark ${ids.length} alert(s) ${nextStatus}`,
      message: 'Every open or investigating alert on this page changes status. The change is recorded in the SOC timeline.',
      subject: `${ids.length} alerts`,
      onConfirm: async () => {
        try {
          await api.request({ path: '/api/soc/alerts/bulk', method: 'PATCH', body: { ids, status: nextStatus }, type: ContentType.Json })
          showAdminSuccess(`${ids.length} alert(s) marked ${nextStatus}`)
          setReload(x => x + 1)
        } catch (err) { showErrorMsg(err, t) }
      },
    })
  }
  const updateCase = async (id: string, nextStatus: string) => {
    try {
      await api.request({ path: `/api/soc/cases/${id}`, method: 'PATCH', body: { status: nextStatus }, type: ContentType.Json })
      showAdminSuccess(`Risk case marked ${nextStatus}`)
      setReload(x => x + 1)
    } catch (err) { showErrorMsg(err, t) }
  }
  const extendInstance = async (id: string) => {
    try {
      await api.request({ path: `/api/soc/instances/${id}/extend`, method: 'POST' })
      showAdminSuccess('Instance lifetime extended')
      setReload(x => x + 1)
    } catch (err) { showErrorMsg(err, t) }
  }
  const destroyInstance = (id: string) => openAdminConfirm({
    t,
    severity: 'danger',
    title: 'Destroy instance',
    message: 'Permanently stop and remove this challenge container?',
    subject: id.slice(0, 12),
    onConfirm: async () => {
      try {
        await api.request({ path: `/api/soc/instances/${id}`, method: 'DELETE' })
        showAdminSuccess('Instance destroyed')
        setReload(x => x + 1)
      } catch (err) { showErrorMsg(err, t) }
    },
  })
  const checkSharedHealth = async (challengeId: number) => {
    try {
      await api.request({ path: `/api/admin/shared-runtimes/${challengeId}/health`, method: 'POST' })
      showAdminSuccess('Shared runtime health refreshed')
      setReload(x => x + 1)
    } catch (err) { showErrorMsg(err, t) }
  }

  const load = useCallback(async () => {
    setLoading(true); setError(undefined)
    try {
      const query = { count: PAGE_SIZE, skip: (page - 1) * PAGE_SIZE }
      const parsedGameId = /^\d+$/.test(gameFilter.trim()) ? Number(gameFilter.trim()) : undefined
      // Only what the open tab shows. Fetching all eleven endpoints on every refresh (and a
      // refresh on every SOC event) used to burn the admin's per-minute request budget during a
      // busy match, after which the rate limiter refused Pause / Release elsewhere.
      const tab = active ?? 'overview'
      const want = (...tabs: string[]) => tabs.includes(tab)
      const skip = Promise.resolve(undefined)
      const [o, i, a, c, tm, f, tl, ob, dq, sr, vb] = await Promise.allSettled([
        want('overview', 'infrastructure') ? api.request<Overview>({ path: '/api/soc/overview', method: 'GET', query: { range, gameId: parsedGameId }, format: 'json' }) : skip,
        want('instances') ? api.request<ArrayResponse<Instance>>({ path: '/api/soc/instances', method: 'GET', query: { ...query, search: search || undefined, status: status || undefined, gameId: parsedGameId }, format: 'json' }) : skip,
        want('alerts') ? api.request<ArrayResponse<Alert>>({ path: '/api/soc/alerts', method: 'GET', query: { ...query, category: category || undefined, severity: severity || undefined, status: status || undefined, gameId: parsedGameId }, format: 'json' }) : skip,
        want('cases') ? api.request<ArrayResponse<RiskCase>>({ path: '/api/soc/cases', method: 'GET', query: { ...query, status: status || undefined, gameId: parsedGameId }, format: 'json' }) : skip,
        want('teams') ? api.request<ArrayResponse<TeamRisk>>({ path: '/api/soc/teams', method: 'GET', query: { ...query, gameId: parsedGameId }, format: 'json' }) : skip,
        want('flags') ? api.request<ArrayResponse<FlagInfo>>({ path: '/api/soc/flags', method: 'GET', query: { ...query, gameId: parsedGameId, artifactHash: /^[0-9a-f]{64}$/.test(artifactHash.trim()) ? artifactHash.trim() : undefined }, format: 'json' }) : skip,
        want('timeline') ? api.request<ArrayResponse<Timeline>>({ path: '/api/soc/timeline', method: 'GET', query: { ...query, category: category || undefined, severity: severity || undefined, gameId: parsedGameId, eventType: eventType || undefined, source: source || undefined }, format: 'json' }) : skip,
        want('infrastructure') ? api.request<OutboxStatus>({ path: '/api/soc/outbox', method: 'GET', format: 'json' }) : skip,
        want('infrastructure') ? api.request<DetectionQueueStatus>({ path: '/api/soc/detectionqueue', method: 'GET', format: 'json' }) : skip,
        want('infrastructure') ? api.request<SharedRuntime[]>({ path: '/api/admin/shared-runtimes', method: 'GET', format: 'json' }) : skip,
        want('infrastructure') ? api.request<VariantBuilderStatus>({ path: '/api/admin/variant-builder/status', method: 'GET', format: 'json' }) : skip,
      ])
      if (o.status === 'fulfilled' && o.value) setOverview(o.value.data)
      if (i.status === 'fulfilled' && i.value) setInstances(arrayData<Instance>(i.value.data))
      if (a.status === 'fulfilled' && a.value) setAlerts(arrayData<Alert>(a.value.data))
      if (c.status === 'fulfilled' && c.value) setCases(arrayData<RiskCase>(c.value.data))
      if (tm.status === 'fulfilled' && tm.value) setTeams(arrayData<TeamRisk>(tm.value.data))
      if (f.status === 'fulfilled' && f.value) setFlags(arrayData<FlagInfo>(f.value.data))
      if (tl.status === 'fulfilled' && tl.value) setTimeline(arrayData<Timeline>(tl.value.data))
      if (ob.status === 'fulfilled' && ob.value) setOutbox(ob.value.data)
      if (dq.status === 'fulfilled' && dq.value) setDetectionQueue(dq.value.data)
      if (sr.status === 'fulfilled' && sr.value) setSharedRuntimes(sr.value.data)
      if (vb.status === 'fulfilled' && vb.value) setVariantBuilder(vb.value.data)

      const failed = [o, i, a, c, tm, f, tl, ob, dq, sr, vb]
        .find(result => result.status === 'rejected')
      if (failed?.status === 'rejected') setError(failed.reason)
    } catch (err) { setError(err); showErrorMsg(err, t) } finally { setLoading(false) }
  }, [active, artifactHash, category, eventType, gameFilter, page, range, search, severity, source, status, t])

  const loadSystemLogs = useCallback(async () => {
    setLogsLoading(true)
    setLogsError(undefined)
    try {
      const response = await api.admin.adminLogs({ level: 'All', count: MAX_SOC_LOGS, skip: 0 })
      setSystemLogs(response.data)
    } catch (err) {
      setLogsError(err)
      showErrorMsg(err, t)
    } finally {
      setLogsLoading(false)
    }
  }, [t])

  useEffect(() => { load() }, [load, reload])
  useEffect(() => { if (active === 'logs') void loadSystemLogs() }, [active, loadSystemLogs, reload])
  useEffect(() => {
    if (selectedTimelineId === undefined) return
    setTimelineDetail(undefined)
    api.request<TimelineDetail>({ path: `/api/soc/timeline/${selectedTimelineId}`, method: 'GET', format: 'json' })
      .then(response => setTimelineDetail(response.data))
      .catch(err => showErrorMsg(err, t))
  }, [selectedTimelineId, t])
  useEffect(() => {
    const connection = new HubConnectionBuilder().withUrl('/hub/admin').withAutomaticReconnect().configureLogging(SignalRLogLevel.Warning).build()
    let refresh: ReturnType<typeof setTimeout> | undefined
    // Live events refresh the open tab at most every 5 s (trailing), however many arrive.
    let lastRefresh = 0
    const refreshSoon = () => {
      if (refresh) return
      const wait = Math.max(750, 5000 - (Date.now() - lastRefresh))
      refresh = setTimeout(() => {
        refresh = undefined
        lastRefresh = Date.now()
        setReload(x => x + 1)
      }, wait)
    }
    connection.on('ReceivedSocUpdate', (update: { kind?: string; data?: unknown }) => {
      if (update.kind === 'snapshot') {
        const snapshot = normalizeSocPayload(update.data) as Partial<ResourceSnapshot>
        const { host, point, containerMetrics } = snapshot
        if (!host || !point) {
          refreshSoon()
          return
        }
        setOverview(current => current ? {
          ...current,
          host,
          history: [...current.history.filter(item => item.time !== point.time), point]
            .slice(-MAX_RESOURCE_HISTORY),
          containerMetrics: containerMetrics ?? current.containerMetrics,
        } : current)
        return
      }
      refreshSoon()
    })
    connection.on('ReceivedLog', (message: LogMessageModel) => {
      setSystemLogs(current => [message, ...(current ?? [])].slice(0, MAX_SOC_LOGS))
    })
    connection.onreconnecting(() => setLive(false)); connection.onreconnected(() => setLive(true)); connection.onclose(() => setLive(false))
    connection.start().then(() => setLive(true)).catch(() => setLive(false))
    return () => { clearTimeout(refresh); void connection.stop() }
  }, [])

  const chart = useMemo<EChartsOption>(() => {
    const theme = getZarrarChartTheme()
    return ({ ...zarrarChartBase(theme), legend: { show: true, textStyle: { color: theme.labelColor } },
      xAxis: { ...zarrarXAxis(theme), type: 'category', data: overview?.history.map(x => dayjs(x.time).format('HH:mm')) ?? [] },
      yAxis: { ...zarrarYAxis(theme), type: 'value', min: 0, max: 100, axisLabel: { color: theme.labelColor, formatter: '{value}%' } },
      series: [
        { name: 'CPU', type: 'line', showSymbol: false, smooth: true, data: overview?.history.map(x => x.cpuPercent) ?? [] },
        { name: 'Memory', type: 'line', showSymbol: false, smooth: true, data: overview?.history.map(x => x.memoryPercent) ?? [] },
        { name: 'Storage', type: 'line', showSymbol: false, smooth: true, data: overview?.history.map(x => x.storagePercent) ?? [] },
      ] }) as EChartsOption
  }, [overview])

  const filteredSystemLogs = useMemo(
    () => (systemLogs ?? []).filter(log => systemLogLevel === 'All' || log.level === systemLogLevel),
    [systemLogLevel, systemLogs]
  )

  const instanceColumns: DataTableColumn<Instance>[] = [
    { key: 'id', header: 'Instance', width: '9rem', sticky: true, rowHeader: true, render: x => <AdminMonoValue value={x.instanceId.slice(0, 12)} /> },
    { key: 'team', header: 'Team', width: '12rem', render: x => <AdminCellStack><AdminCellName>{x.team}</AdminCellName><AdminCellMeta>{x.game}</AdminCellMeta></AdminCellStack> },
    { key: 'challenge', header: 'Challenge', width: '12rem', render: x => <AdminCellStack><AdminCellName>{x.challenge}</AdminCellName><AdminCellMeta>{x.containerId.slice(0, 12)}</AdminCellMeta></AdminCellStack> },
    { key: 'node', header: 'Node', width: '11rem', render: x => x.node },
    { key: 'ttl', header: 'TTL remaining', width: '9rem', nowrap: true, render: x => duration(x.expiresAt) },
    { key: 'cpu', header: 'CPU', width: '6rem', numeric: true, render: x => percent(x.cpuPercent) },
    { key: 'ram', header: 'RAM', width: '11rem', numeric: true, render: x => `${bytes(x.memoryUsedBytes)} / ${bytes(x.memoryLimitBytes ?? (x.configuredMemoryMb ?? 0) * 1024 ** 2)}` },
    { key: 'network', header: 'Network', width: '8rem', render: x => <AdminCellStack><AdminCellName>{x.networkMode}</AdminCellName><AdminCellMeta>RX {bytes(x.networkRxBytes)} · TX {bytes(x.networkTxBytes)}</AdminCellMeta></AdminCellStack> },
    { key: 'status', header: 'Status', width: '7rem', render: x => <AdminStatusBadge label={x.status} tone={tone(x.status)} solid /> },
    { key: 'actions', header: '', headerLabel: 'Instance actions', width: '6rem', align: 'right', render: x => <Group gap={2} justify="flex-end" wrap="nowrap"><Tooltip label="Extend"><ActionIcon variant="subtle" color="blue" onClick={() => extendInstance(x.instanceId)}><Icon path={mdiTimerPlusOutline} size={0.75} /></ActionIcon></Tooltip><Tooltip label="Destroy"><ActionIcon variant="subtle" color="red" onClick={() => destroyInstance(x.instanceId)}><Icon path={mdiDeleteOutline} size={0.75} /></ActionIcon></Tooltip></Group> },
  ]
  const alertColumns: DataTableColumn<Alert>[] = [
    { key: 'time', header: 'Time', width: '10rem', nowrap: true, render: x => dayjs(x.createdAt).format('MM-DD HH:mm:ss') },
    { key: 'severity', header: 'Severity', width: '7rem', render: x => <AdminStatusBadge label={x.severity} tone={tone(x.severity)} solid /> },
    { key: 'event', header: 'Event', width: '18rem', rowHeader: true, render: x => <AdminCellStack><AdminCellName>{x.event}</AdminCellName><AdminCellMeta>{x.category}</AdminCellMeta></AdminCellStack> },
    { key: 'target', header: 'Target', width: '14rem', render: x => <AdminCellStack><AdminCellName>{x.team ?? 'Platform'}</AdminCellName><AdminCellMeta>{x.challenge ?? x.game ?? '—'}</AdminCellMeta></AdminCellStack> },
    { key: 'risk', header: 'Risk', width: '5rem', numeric: true, render: x => x.riskImpact ? `+${x.riskImpact}` : '—' },
    { key: 'status', header: 'Status', width: '8rem', render: x => <AdminStatusBadge label={x.status} tone={tone(x.status)} /> },
    { key: 'evidence', header: 'Evidence', width: '13rem', render: x => <AdminMonoValue value={x.evidenceFingerprint} /> },
    { key: 'actions', header: '', headerLabel: 'Alert actions', width: '9rem', align: 'right', render: x => <Select size="xs" value={x.status} onChange={value => value && updateAlert(x.id, value)} data={['Open','Investigating','Escalated','Resolved','Clean']} aria-label={`Change status for alert ${x.id}`} /> },
  ]
  const caseColumns: DataTableColumn<RiskCase>[] = [
    { key: 'case', header: 'Risk case', width: '19rem', sticky: true, rowHeader: true, render: x => <AdminCellStack><AdminCellName>{x.title}</AdminCellName><AdminMonoValue value={x.id} /></AdminCellStack> },
    { key: 'teams', header: 'Correlated teams', width: '16rem', render: x => <AdminCellStack><AdminCellName>{x.primaryTeam}</AdminCellName><AdminCellMeta>{x.relatedTeam ? `↔ ${x.relatedTeam}` : 'Team-level case'} · {x.game}</AdminCellMeta></AdminCellStack> },
    { key: 'risk', header: 'Risk / confidence', width: '10rem', numeric: true, render: x => <AdminCellStack><AdminCellName>{x.riskScore} / 100</AdminCellName><AdminCellMeta>{x.confidence}% confidence</AdminCellMeta></AdminCellStack> },
    { key: 'evidence', header: 'Evidence', width: '9rem', numeric: true, render: x => <AdminCellStack><AdminCellName>{x.evidenceCount} signals</AdminCellName><AdminCellMeta>{x.ruleHitCount} distinct rules</AdminCellMeta></AdminCellStack> },
    { key: 'summary', header: 'Latest signal', width: '25rem', render: x => x.summary },
    { key: 'seen', header: 'Last seen', width: '10rem', nowrap: true, render: x => dayjs(x.lastSeenAt).format('MM-DD HH:mm:ss') },
    { key: 'status', header: 'Status', width: '9rem', render: x => <Select size="xs" value={x.status} onChange={value => value && updateCase(x.id, value)} data={['Open','Investigating','Escalated','Resolved','Clean']} aria-label={`Change status for risk case ${x.id}`} /> },
    { key: 'actions', header: '', headerLabel: 'Risk case actions', width: '7rem', align: 'right', render: x => <Button size="xs" variant="default" onClick={() => setSelectedCaseId(x.id)}>Evidence</Button> },
  ]
  const teamColumns: DataTableColumn<TeamRisk>[] = [
    { key: 'team', header: 'Team', width: '15rem', rowHeader: true, sticky: true, render: x => <Link to={`/admin/soc/team/${x.teamId}`} className={classes.link}>{x.team}</Link> },
    { key: 'risk', header: 'Risk score', width: '8rem', numeric: true, render: x => <AdminCellStack><AdminCellName>{x.riskScore} / 100</AdminCellName><AdminStatusBadge label={x.classification} tone={tone(x.classification)} /></AdminCellStack> },
    { key: 'alerts', header: 'Open alerts', width: '8rem', numeric: true, render: x => x.openAlerts },
    { key: 'critical', header: 'Critical', width: '7rem', numeric: true, render: x => x.criticalAlerts },
    { key: 'submissions', header: 'Submissions', width: '8rem', numeric: true, render: x => x.submissions },
    { key: 'solves', header: 'Solves', width: '6rem', numeric: true, render: x => x.solves },
    { key: 'instances', header: 'Instances', width: '7rem', numeric: true, render: x => x.runningInstances },
    { key: 'reasons', header: 'Risk reasons', width: '22rem', render: x => x.reasons.join(' · ') || 'No active signals' },
  ]
  const flagColumns: DataTableColumn<FlagInfo>[] = [
    { key: 'fingerprint', header: 'Fingerprint', width: '18rem', sticky: true, rowHeader: true, render: x => <AdminMonoValue value={x.fingerprint} wide /> },
    { key: 'team', header: 'Owner', width: '12rem', render: x => x.team },
    { key: 'challenge', header: 'Challenge', width: '14rem', render: x => <AdminCellStack><AdminCellName>{x.challenge}</AdminCellName><AdminCellMeta>{x.game}</AdminCellMeta></AdminCellStack> },
    { key: 'instance', header: 'Instance', width: '12rem', render: x => <AdminMonoValue value={x.instanceId} /> },
    { key: 'generated', header: 'Generated', width: '10rem', nowrap: true, render: x => x.generatedAt ? dayjs(x.generatedAt).format('MM-DD HH:mm') : '—' },
    { key: 'artifact', header: 'Artifact SHA-256', width: '18rem', render: x => <AdminMonoValue value={x.artifactHash} wide /> },
    { key: 'assignment', header: 'Assignment', width: '12rem', render: x => <AdminCellStack><AdminMonoValue value={x.assignmentId} /><AdminCellMeta>{x.downloadCount} download{x.downloadCount === 1 ? '' : 's'}</AdminCellMeta></AdminCellStack> },
    { key: 'last-download', header: 'Last download', width: '10rem', nowrap: true, render: x => x.lastDownloadedAt ? dayjs(x.lastDownloadedAt).format('MM-DD HH:mm') : 'Never' },
    { key: 'status', header: 'Status', width: '7rem', render: x => <AdminStatusBadge label={x.status} tone={tone(x.status)} /> },
  ]
  const timelineColumns: DataTableColumn<Timeline>[] = [
    { key: 'time', header: 'Timestamp', width: '11rem', sticky: true, nowrap: true, render: x => dayjs(x.timestamp).format('MM-DD HH:mm:ss') },
    { key: 'severity', header: 'Severity', width: '7rem', render: x => <AdminStatusBadge label={x.severity} tone={tone(x.severity)} /> },
    { key: 'event', header: 'Event', width: '15rem', rowHeader: true, render: x => <AdminCellStack><AdminCellName>{x.event}</AdminCellName><AdminCellMeta>{x.category}</AdminCellMeta></AdminCellStack> },
    { key: 'source', header: 'Source', width: '12rem', render: x => <AdminCellStack><AdminCellName>{x.source ?? 'ZARRAR'}</AdminCellName><AdminCellMeta>{x.correlationId ? x.correlationId.slice(0, 16) : 'no correlation id'}</AdminCellMeta></AdminCellStack> },
    { key: 'team', header: 'Team', width: '12rem', render: x => <AdminCellStack><AdminCellName>{x.team ?? '—'}</AdminCellName><AdminCellMeta>{x.relatedTeam ? `Related: ${x.relatedTeam}` : '—'}</AdminCellMeta></AdminCellStack> },
    { key: 'challenge', header: 'Challenge', width: '13rem', render: x => x.challenge ?? x.game ?? '—' },
    { key: 'details', header: 'Details', width: '25rem', render: x => x.details },
    { key: 'actions', header: '', headerLabel: 'Timeline actions', width: '7rem', align: 'right', render: x => <Button size="xs" variant="default" onClick={() => setSelectedTimelineId(x.id)}>Inspect</Button> },
  ]
  const sharedRuntimeColumns: DataTableColumn<SharedRuntime>[] = [
    { key: 'challenge', header: 'Shared runtime', width: '15rem', rowHeader: true, render: x => <AdminCellStack><AdminCellName>{x.challenge}</AdminCellName><AdminCellMeta>Challenge {x.challengeId} · {x.stateMode}</AdminCellMeta></AdminCellStack> },
    { key: 'origin', header: 'Allowlisted origin', width: '19rem', render: x => <AdminMonoValue value={x.origin} wide /> },
    { key: 'capacity', header: 'Sessions', width: '9rem', numeric: true, render: x => `${x.activeSessions} / ${x.maxConcurrentSessions}` },
    { key: 'health', header: 'Health', width: '9rem', render: x => <AdminCellStack><AdminStatusBadge label={x.isEnabled ? x.healthStatus : 'Disabled'} tone={tone(x.isEnabled ? x.healthStatus : 'offline')} solid /><AdminCellMeta>{x.lastHealthCheckUtc ? dayjs(x.lastHealthCheckUtc).format('MM-DD HH:mm') : 'Not checked'}</AdminCellMeta></AdminCellStack> },
    { key: 'actions', header: '', headerLabel: 'Runtime actions', width: '6rem', align: 'right', render: x => <Tooltip label="Check health"><ActionIcon variant="subtle" onClick={() => checkSharedHealth(x.challengeId)}><Icon path={mdiRefresh} size={0.75} /></ActionIcon></Tooltip> },
  ]

  const systemLogColumns: DataTableColumn<LogMessageModel>[] = [
    {
      key: 'time', header: 'Time', width: '10rem', nowrap: true,
      render: log => <AdminCellStack><AdminCellMeta>{dayjs(log.time).format('MM-DD HH:mm:ss')}</AdminCellMeta>{log.level && <AdminStatusBadge tone={LOG_LEVEL_TONE[log.level] ?? 'neutral'} label={log.level.slice(0, 4)} />}</AdminCellStack>,
    },
    {
      key: 'who', header: 'User', width: '13rem', rowHeader: true,
      render: log => <AdminCellStack><AdminCellMeta title={log.name ?? undefined}>{log.name || 'Unknown'}</AdminCellMeta><AdminMonoValue value={log.ip} placeholder="Unknown" /></AdminCellStack>,
    },
    { key: 'message', header: 'Message', render: log => <span style={{ overflowWrap: 'anywhere' }}>{log.msg}</span> },
    {
      key: 'status', header: 'Status', width: '8rem',
      render: log => log.status ? <AdminStatusBadge solid label={log.status} tone={LOG_STATUS_TONE[TaskStatusColorMap.get(log.status as TaskStatus) ?? 'gray'] ?? 'neutral'} /> : null,
    },
  ]

  const commonPager = (total = 0) => <AdminPagination page={page} onChange={setPage} total={total} pageSize={PAGE_SIZE} />
  const filters = <><Select clearable placeholder="All categories" value={category} onChange={x => { setPage(1); setCategory(x) }} data={['AntiCheat', 'Infrastructure', 'Authentication', 'PlatformSecurity']} /><Select clearable placeholder="All severities" value={severity} onChange={x => { setPage(1); setSeverity(x) }} data={['Info', 'Low', 'Medium', 'High', 'Critical']} /></>
  const timelineFilters = <>{filters}<TextInput value={eventType} onChange={e => { setPage(1); setEventType(e.currentTarget.value) }} placeholder="Event type" /><TextInput value={source} onChange={e => { setPage(1); setSource(e.currentTarget.value) }} placeholder="Source" /></>

  return <AdminLayout
    title={t('admin.soc.title')}
    description={t('admin.soc.description')}
    breadcrumbs={[{ label: t('admin.nav.group.infrastructure', { defaultValue: 'Infrastructure' }) }]}
    actions={<Group gap="xs"><TextInput size="xs" value={gameFilter} onChange={e => { setPage(1); setGameFilter(e.currentTarget.value) }} placeholder="Game ID · all" w={120} /><AdminStatusBadge label={live ? 'Live' : 'Reconnecting'} tone={live ? 'success' : 'warning'} solid /><Tooltip label="Refresh"><ActionIcon variant="light" onClick={() => setReload(x => x + 1)}><Icon path={mdiRefresh} size={0.8} /></ActionIcon></Tooltip></Group>}
    isLoading={loading && !overview}
    subnav={<Tabs value={active} onChange={value => { setActive(value); setPage(1); setStatus(null) }} className={classes.tabs}><Tabs.List>{['overview','cases','alerts','teams','flags','instances','infrastructure','queues','timeline','logs'].map(key => <Tabs.Tab key={key} value={key}>{key === 'queues' ? 'Queues' : key === 'logs' ? 'Logs' : t(`admin.soc.tabs.${key}`)}</Tabs.Tab>)}</Tabs.List></Tabs>}
  >
    {Boolean(error) && <AdminOutageNotice message="Some SOC data is temporarily unavailable. Live data already loaded remains visible." onRetry={() => setReload(x => x + 1)} />}

    {active === 'overview' && <div className={classes.stack}>
      <AdminMetricGrid label="Competition security overview">
        <StatCard label="ACTIVE TEAMS" value={overview?.activeTeams ?? '—'} icon={<Icon path={mdiShieldSearch} size={0.9} />} />
        <StatCard label="ONLINE USERS" value={overview?.onlineUsers ?? '—'} />
        <StatCard label="OPEN INSTANCES" value={overview?.activeInstances ?? '—'} icon={<Icon path={mdiPackageVariantClosed} size={0.9} />} />
        <StatCard label="TOTAL SUBMISSIONS" value={overview?.totalSubmissions ?? '—'} />
        <StatCard label="CORRECT SOLVES" value={overview?.correctSolves ?? '—'} tone="success" icon={<Icon path={mdiCheckCircleOutline} size={0.9} />} />
        <StatCard label="FAILED ATTEMPTS" value={overview?.failedAttempts ?? '—'} />
        <StatCard label="OPEN ALERTS" value={overview?.openAlerts ?? '—'} tone={overview?.openAlerts ? 'warning' : 'neutral'} icon={<Icon path={mdiAlertOutline} size={0.9} />} />
        <StatCard label="RISK CASES" value={overview?.openRiskCases ?? '—'} tone={overview?.openRiskCases ? 'warning' : 'neutral'} icon={<Icon path={mdiShieldSearch} size={0.9} />} />
        <StatCard label="CRITICAL" value={overview?.criticalAlerts ?? '—'} tone={overview?.criticalAlerts ? 'critical' : 'neutral'} icon={<Icon path={mdiAlertOctagonOutline} size={0.9} />} />
        <StatCard label="CPU" value={percent(overview?.host.cpuPercent)} sublabel={`${overview?.host.cpuCores ?? '—'} cores`} icon={<Icon path={mdiCpu64Bit} size={0.9} />} />
        <StatCard label="MEMORY" value={percent(overview?.host.memoryPercent)} sublabel={`${bytes(overview?.host.memoryUsedBytes)} / ${bytes(overview?.host.memoryTotalBytes)}`} icon={<Icon path={mdiMemory} size={0.9} />} />
        <StatCard label="STORAGE" value={percent(overview?.host.storagePercent)} sublabel={`${bytes(overview?.host.storageUsedBytes)} / ${bytes(overview?.host.storageTotalBytes)}`} icon={<Icon path={mdiHarddisk} size={0.9} />} />
        <StatCard label="FAILED INSTANCES" value={overview?.failedInstances ?? '—'} tone={overview?.failedInstances ? 'critical' : 'neutral'} />
      </AdminMetricGrid>
      <div className={classes.twoColumns}>
        <Panel title="CPU / memory / storage history" actions={<SegmentedControl size="xs" value={range} onChange={x => setRange(x as Range)} data={['15m','1h','6h','24h','7d']} />}><EchartsContainer option={chart} className={classes.chart} /></Panel>
        <Panel title="Infrastructure health" icon={<Icon path={mdiLan} size={0.85} />}><div className={classes.healthList}>{Object.entries(overview?.host.services ?? {}).map(([name, state]) => <div className={classes.healthRow} key={name}><span>{name}</span><AdminStatusBadge label={state} tone={tone(state)} solid /></div>)}</div></Panel>
      </div>
      <div className={classes.twoColumns}>
        <Panel title="Top CPU consumers">{[...(overview?.containerMetrics ?? [])].sort((a,b) => (b.cpuPercent ?? -1) - (a.cpuPercent ?? -1)).slice(0,5).map(x => <div className={classes.rankRow} key={x.containerGuid}><AdminMonoValue value={x.containerGuid.slice(0,12)} /><strong>{percent(x.cpuPercent)}</strong></div>)}</Panel>
        <Panel title="Top memory consumers">{[...(overview?.containerMetrics ?? [])].sort((a,b) => (b.memoryUsedBytes ?? -1) - (a.memoryUsedBytes ?? -1)).slice(0,5).map(x => <div className={classes.rankRow} key={x.containerGuid}><AdminMonoValue value={x.containerGuid.slice(0,12)} /><strong>{bytes(x.memoryUsedBytes)}</strong></div>)}</Panel>
      </div>
      <Panel title="Open challenge instances" bare actions={<Button component={Link} to="#" variant="subtle" size="xs" onClick={() => setActive('instances')}>View all</Button>}><AdminTable caption="Open challenge instances" columns={instanceColumns} rows={(instances?.data ?? []).filter(x => x.status !== 'Destroyed').slice(0,8)} rowKey={x => x.instanceId} loading={loading && !instances} /></Panel>
      <Panel title="Recent security alerts" bare actions={<Button variant="subtle" size="xs" onClick={() => setActive('alerts')}>View all</Button>}><AdminTable caption="Recent security alerts" columns={alertColumns} rows={(alerts?.data ?? []).slice(0,8)} rowKey={x => x.id} loading={loading && !alerts} /></Panel>
    </div>}

    {active === 'cases' && <div className={classes.stack}><AdminToolbar filters={<Select clearable placeholder="All case statuses" value={status} onChange={x => { setPage(1); setStatus(x) }} data={['Open','Investigating','Escalated','Resolved','Clean']} />} trailing={commonPager(cases?.total)} /><Panel bare><AdminTable caption="Correlated anti-cheat risk cases" columns={caseColumns} rows={cases?.data ?? []} rowKey={x => x.id} loading={loading && !cases} maxHeight="calc(100vh - 21rem)" /></Panel></div>}
    {active === 'alerts' && <div className={classes.stack}><AdminToolbar filters={<>{filters}<Select clearable placeholder="All statuses" value={status} onChange={x => { setPage(1); setStatus(x) }} data={['Open','Investigating','Escalated','Resolved','Clean']} /><Button size="xs" variant="default" onClick={() => resolveShownAlerts('Resolved')}>Resolve shown</Button></>} trailing={commonPager(alerts?.total)} /><Panel bare><AdminTable caption="Security alerts" columns={alertColumns} rows={alerts?.data ?? []} rowKey={x => x.id} loading={loading && !alerts} maxHeight="calc(100vh - 21rem)" /></Panel></div>}
    {active === 'teams' && <div className={classes.stack}><AdminToolbar trailing={commonPager(teams?.total)} /><Panel bare><AdminTable caption="Team risk intelligence" columns={teamColumns} rows={teams?.data ?? []} rowKey={x => x.teamId} loading={loading && !teams} maxHeight="calc(100vh - 21rem)" /></Panel></div>}
    {active === 'flags' && <div className={classes.stack}><AdminOutageNotice message="Plaintext flags are never returned here. Fingerprints are keyed and safe for correlation." /><AdminToolbar filters={<TextInput value={artifactHash} onChange={e => { setPage(1); setArtifactHash(e.currentTarget.value.toLowerCase()) }} placeholder="Exact artifact SHA-256" />} trailing={commonPager(flags?.total)} /><Panel bare><AdminTable caption="Dynamic flag and personalized artifact intelligence" columns={flagColumns} rows={flags?.data ?? []} rowKey={socFlagRowKey} loading={loading && !flags} maxHeight="calc(100vh - 23rem)" /></Panel></div>}
    {active === 'instances' && <div className={classes.stack}><AdminToolbar filters={<><TextInput value={search} onChange={e => { setPage(1); setSearch(e.currentTarget.value) }} placeholder="Search team, challenge or container" /><Select clearable value={status} onChange={x => { setPage(1); setStatus(x) }} placeholder="All statuses" data={['Pending','Running','Destroyed']} /></>} trailing={commonPager(instances?.total)} /><Panel bare><AdminTable caption="All challenge instances" columns={instanceColumns} rows={instances?.data ?? []} rowKey={x => x.instanceId} loading={loading && !instances} maxHeight="calc(100vh - 21rem)" /></Panel></div>}
    {active === 'infrastructure' && <div className={classes.stack}><AdminMetricGrid><StatCard label="RUNTIME PROVIDER" value={overview?.provider.provider ?? '—'} sublabel={overview ? `${overview.provider.node} · ${overview.provider.status}` : 'Starting'} tone={tone(overview?.provider.status ?? 'pending')} /><StatCard label="RUNTIME CAPACITY" value={overview ? `${overview.provider.running} / ${overview.provider.capacity ?? '∞'}` : '—'} sublabel={`${overview?.provider.pending ?? 0} pending · ${overview?.provider.unavailable ?? 0} unavailable`} tone={overview?.provider.unavailable ? 'critical' : overview?.provider.pending ? 'warning' : 'success'} /><StatCard label="SOC OUTBOX" value={outbox?.pending ?? '—'} sublabel={`${outbox?.retrying ?? 0} retrying`} tone={outbox?.deadLettered ? 'critical' : outbox?.pending ? 'warning' : 'success'} /><StatCard label="DETECTION QUEUE" value={detectionQueue?.pending ?? '—'} sublabel={`${detectionQueue?.running ?? 0} running`} tone={detectionQueue?.failed ? 'critical' : detectionQueue?.pending ? 'warning' : 'success'} /><StatCard label="DETECTION FAILURES" value={detectionQueue?.failed ?? '—'} sublabel={detectionQueue?.oldestPending ? `Oldest ${dayjs(detectionQueue.oldestPending).format('MM-DD HH:mm')}` : 'Queue healthy'} tone={detectionQueue?.failed ? 'critical' : 'success'} /><StatCard label="DEAD LETTERS" value={outbox?.deadLettered ?? '—'} sublabel={outbox?.oldestPending ? `Oldest ${dayjs(outbox.oldestPending).format('MM-DD HH:mm')}` : 'Queue healthy'} tone={outbox?.deadLettered ? 'critical' : 'success'} /><StatCard label="SHARED RUNTIMES" value={sharedRuntimes.filter(x => x.isEnabled).length} sublabel={`${sharedRuntimes.reduce((sum, x) => sum + x.activeSessions, 0)} active sessions`} tone={sharedRuntimes.some(x => x.healthStatus === 'Unavailable') ? 'critical' : 'success'} /><StatCard label="VARIANT BUILDER" value={variantBuilder?.enabled ? (variantBuilder.configured ? 'READY' : 'MISCONFIGURED') : 'DISABLED'} sublabel={`${variantBuilder?.running ?? 0} running · ${variantBuilder?.pending ?? 0} queued`} tone={variantBuilder?.enabled && !variantBuilder.configured ? 'critical' : variantBuilder?.failed ? 'warning' : 'success'} /><StatCard label="DATASET CAPACITY" value={variantBuilder?.datasetAvailable ?? '—'} sublabel={`${variantBuilder?.validatedTemplates ?? 0} validated templates`} tone={variantBuilder?.datasetAvailable ? 'success' : 'neutral'} /><StatCard label="BUILD FAILURES" value={variantBuilder?.failed ?? '—'} sublabel={`${variantBuilder?.succeeded ?? 0} completed`} tone={variantBuilder?.failed ? 'critical' : 'success'} /><StatCard label="CPU" value={percent(overview?.host.cpuPercent)} sublabel={`${overview?.host.cpuCores ?? '—'} cores · load ${overview?.host.loadAverage?.toFixed(2) ?? '—'}`} icon={<Icon path={mdiCpu64Bit} size={0.9} />} /><StatCard label="MEMORY" value={percent(overview?.host.memoryPercent)} sublabel={`${bytes(overview?.host.memoryUsedBytes)} / ${bytes(overview?.host.memoryTotalBytes)}`} icon={<Icon path={mdiMemory} size={0.9} />} /><StatCard label="STORAGE" value={percent(overview?.host.storagePercent)} sublabel={`${bytes(overview?.host.storageUsedBytes)} / ${bytes(overview?.host.storageTotalBytes)}`} icon={<Icon path={mdiHarddisk} size={0.9} />} /><StatCard label="RUNNING INSTANCES" value={overview?.activeInstances ?? '—'} icon={<Icon path={mdiPackageVariantClosed} size={0.9} />} /></AdminMetricGrid><div className={classes.twoColumns}><Panel title={`${overview?.provider.provider ?? 'Runtime'} provider`} eyebrow={overview?.provider.status ?? 'Starting'}><div className={classes.healthList}><div className={classes.healthRow}><span>Node</span><strong>{overview?.provider.node ?? '—'}</strong></div><div className={classes.healthRow}><span>Isolation</span><strong>{overview?.provider.isolation ?? '—'}</strong></div><div className={classes.healthRow}><span>Version</span><strong>{overview?.provider.version ?? '—'}</strong></div><div className={classes.healthRow}><span>Sample age</span><strong>{overview ? `${Math.max(0, Math.floor((Date.now() - overview.provider.sampledAt) / 1000))}s` : '—'}</strong></div></div></Panel><Panel title={overview?.host.node ?? 'Primary Challenge Host'} eyebrow={overview?.host.status ?? 'Starting'}><div className={classes.healthList}><div className={classes.healthRow}><span>Last heartbeat</span><strong>{overview ? `${Math.max(0, Math.floor((Date.now() - overview.host.sampledAt) / 1000))}s ago` : '—'}</strong></div>{Object.entries(overview?.host.services ?? {}).map(([name,state]) => <div className={classes.healthRow} key={name}><span>{name}</span><AdminStatusBadge label={state} tone={tone(state)} solid /></div>)}</div></Panel></div><Panel title="Shared Web/API runtimes" bare><AdminTable caption="Shared runtime health and capacity" columns={sharedRuntimeColumns} rows={sharedRuntimes} rowKey={x => x.challengeId} loading={loading} /></Panel><Panel title="SOC ledger (tamper evidence)" eyebrow={ledger ? (ledger.intact ? 'INTACT' : 'BROKEN') : 'NOT CHECKED'}><div className={classes.healthList}><div className={classes.healthRow}><span>Every couple of minutes new timeline rows are sealed into a keyed hash chain. Verifying recomputes the chain; an edited, deleted or back-dated row breaks it.</span><Button size="xs" variant="default" loading={ledgerBusy} onClick={() => void verifyLedger()}>Verify now</Button></div>{ledger && <><div className={classes.healthRow}><span>Result</span><AdminStatusBadge label={ledger.intact ? 'Intact' : 'Broken'} tone={ledger.intact ? 'success' : 'critical'} solid /></div><div className={classes.healthRow}><span>Verified</span><strong>{ledger.verifiedEvents} events in {ledger.verifiedSeals} seals{ledger.prunedSeals ? ` (${ledger.prunedSeals} pruned)` : ''}</strong></div><div className={classes.healthRow}><span>Not sealed yet</span><strong>{ledger.unsealedEvents}</strong></div><div className={classes.healthRow}><span>Last seal</span><strong>{ledger.lastSealAt ? dayjs(ledger.lastSealAt).format('MM-DD HH:mm:ss') : '—'}</strong></div>{ledger.problem && <div className={classes.healthRow}><span>Problem (seal {ledger.brokenAtSealId})</span><strong>{ledger.problem}</strong></div>}</>}</div></Panel><Panel title="Resource history"><EchartsContainer option={chart} className={classes.chart} /></Panel></div>}
    {active === 'queues' && <SocOperations />}
    {active === 'timeline' && <div className={classes.stack}><AdminToolbar filters={timelineFilters} trailing={commonPager(timeline?.total)} /><Panel bare><AdminTable caption="SOC timeline and event search" columns={timelineColumns} rows={timeline?.data ?? []} rowKey={x => x.id} loading={loading && !timeline} maxHeight="calc(100vh - 21rem)" /></Panel></div>}
    {active === 'logs' && <div className={classes.stack}>
      {Boolean(logsError) && <AdminOutageNotice message="System logs are temporarily unavailable." onRetry={() => void loadSystemLogs()} />}
      <AdminToolbar
        filters={<SegmentedControl value={systemLogLevel} onChange={value => setSystemLogLevel(value as SystemLogLevel)} data={SYSTEM_LOG_LEVELS} />}
        trailing={<AdminResultCount shown={filteredSystemLogs.length} filtered={systemLogLevel !== 'All'} />}
      />
      <Panel bare><AdminTable caption="System and audit logs" columns={systemLogColumns} rows={filteredSystemLogs} rowKey={(log, index) => `${log.time}-${index}`} loading={logsLoading && !systemLogs} maxHeight="calc(100vh - 21rem)" emptyTitle="No log entries" emptyDescription="Nothing matched this level in the latest 300 records." /></Panel>
    </div>}
    <SocCaseDetailModal caseId={selectedCaseId} opened={Boolean(selectedCaseId)} onClose={() => setSelectedCaseId(undefined)} onChanged={() => setReload(x => x + 1)} />
    <Modal opened={selectedTimelineId !== undefined} onClose={() => { setSelectedTimelineId(undefined); setTimelineDetail(undefined) }} title="SOC event detail" size="lg">
      {!timelineDetail ? <Text c="dimmed">Loading event…</Text> : <Stack gap="sm">
        <Group justify="space-between"><Text fw={600}>{timelineDetail.event}</Text><AdminStatusBadge label={timelineDetail.severity} tone={tone(timelineDetail.severity)} /></Group>
        <Text>{timelineDetail.details}</Text>
        <Text size="sm" c="dimmed">{timelineDetail.source ?? 'ZARRAR'} · {timelineDetail.category} · {dayjs(timelineDetail.timestamp).format('YYYY-MM-DD HH:mm:ss')}</Text>
        {timelineDetail.eventId && <Code>{timelineDetail.eventId}</Code>}
        {timelineDetail.correlationId && <Code>{timelineDetail.correlationId}</Code>}
        {timelineDetail.payloadJson && <Code block>{timelineDetail.payloadJson}</Code>}
      </Stack>}
    </Modal>
  </AdminLayout>
}

export default Soc

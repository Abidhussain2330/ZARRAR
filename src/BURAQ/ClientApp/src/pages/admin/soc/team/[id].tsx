import { ActionIcon, Button, NumberInput, Tabs, Tooltip } from '@mantine/core'
import { mdiDeleteOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, useEffect, useMemo, useState } from 'react'
import { useParams } from 'react-router'
import api, { ContentType } from '@Api'
import {
  AdminCellMeta,
  AdminCellName,
  AdminCellStack,
  AdminLayout,
  AdminMetricGrid,
  AdminMonoValue,
  AdminOutageNotice,
  AdminStatusBadge,
  AdminTable,
  openAdminConfirm,
  showAdminSuccess,
} from '@Components/admin/shell'
import type { AdminTone } from '@Components/admin/shell'
import { Panel, StatCard } from '@Components/buraq'
import type { DataTableColumn } from '@Components/buraq'
import { showErrorMsg } from '@Utils/Shared'
import { formatSocMegabytes, formatSocPercent } from '@Utils/SocMetrics'
import { socFlagRowKey } from '@Utils/SocRowKeys'
import { useTranslation } from 'react-i18next'
import classes from '@Styles/admin/Soc.module.css'

interface Response<T> { data: T[]; total: number }
interface Risk { teamId: number; team: string; riskScore: number; classification: string; openAlerts: number; criticalAlerts: number; submissions: number; solves: number; runningInstances: number; reasons: string[] }
interface Alert { id: number; createdAt: number; severity: string; category: string; event: string; description: string; relatedTeam?: string; challenge?: string; instanceId?: string; riskImpact: number; status: string; evidenceFingerprint?: string }
interface Instance { instanceId: string; containerId: string; challenge: string; game: string; createdAt: number; expiresAt: number; status: string; networkMode: string; cpuPercent?: number | null; memoryUsedBytes?: number | null; memoryLimitBytes?: number | null }
interface Timeline { id: number; timestamp: number; severity: string; category: string; event: string; challenge?: string; instanceId?: string; details: string }
interface Flag { fingerprint: string; teamId?: number; challengeId?: number; challenge: string; game: string; instanceId?: string; assignmentId?: string; artifactHash?: string; generatedAt?: number; status: string }
interface InstanceLimit { participationId: number; gameId: number; game: string; runningInstances: number; defaultLimit: number; overrideLimit?: number; effectiveLimit: number }

const tone = (value: string): AdminTone => value === 'Critical' || value === 'High' ? 'critical' : value === 'Medium' || value === 'Investigating' ? 'warning' : value === 'Normal' || value === 'Running' || value === 'Resolved' || value === 'Clean' ? 'success' : 'neutral'
const TeamInvestigation: FC = () => {
  const { id } = useParams()
  const teamId = Number(id)
  const { t } = useTranslation()
  const [active, setActive] = useState<string | null>('overview')
  const [risk, setRisk] = useState<Risk>()
  const [alerts, setAlerts] = useState<Alert[]>([])
  const [instances, setInstances] = useState<Instance[]>([])
  const [timeline, setTimeline] = useState<Timeline[]>([])
  const [flags, setFlags] = useState<Flag[]>([])
  const [limits, setLimits] = useState<InstanceLimit[]>([])
  const [limitDrafts, setLimitDrafts] = useState<Record<number, number | string>>({})
  const [loading, setLoading] = useState(true)
  const [failed, setFailed] = useState(false)
  const [reload, setReload] = useState(0)

  useEffect(() => {
    if (!Number.isInteger(teamId) || teamId <= 0) { setFailed(true); setLoading(false); return }
    setLoading(true)
    Promise.all([
      api.request<Response<Risk>>({ path: '/api/soc/teams', method: 'GET', query: { teamId, count: 1 }, format: 'json' }),
      api.request<Response<Alert>>({ path: '/api/soc/alerts', method: 'GET', query: { teamId, count: 100 }, format: 'json' }),
      api.request<Response<Instance>>({ path: '/api/soc/instances', method: 'GET', query: { teamId, status: 'Running', count: 100 }, format: 'json' }),
      api.request<Response<Timeline>>({ path: '/api/soc/timeline', method: 'GET', query: { teamId, count: 100 }, format: 'json' }),
      api.request<Response<Flag>>({ path: '/api/soc/flags', method: 'GET', query: { teamId, count: 100 }, format: 'json' }),
      api.request<InstanceLimit[]>({ path: `/api/soc/teams/${teamId}/instanceLimits`, method: 'GET', format: 'json' }),
    ]).then(([r,a,i,tl,f,l]) => {
      setRisk((r.data as Response<Risk>).data[0]); setAlerts((a.data as Response<Alert>).data)
      setInstances((i.data as Response<Instance>).data); setTimeline((tl.data as Response<Timeline>).data)
      setFlags((f.data as Response<Flag>).data)
      const loadedLimits = l.data as InstanceLimit[]
      setLimits(loadedLimits)
      setLimitDrafts(Object.fromEntries(loadedLimits.map(x => [x.participationId, x.overrideLimit ?? ''])))
      setFailed(false)
    }).catch(err => { setFailed(true); showErrorMsg(err, t) }).finally(() => setLoading(false))
  }, [teamId, t, reload])

  const updateLimit = async (participationId: number) => {
    const draft = limitDrafts[participationId]
    const limit = draft === '' || draft === undefined ? null : Number(draft)
    try {
      await api.request({ path: `/api/soc/participations/${participationId}/instanceLimit`, method: 'PATCH',
        body: { limit }, type: ContentType.Json })
      showAdminSuccess(limit === null ? 'Team now inherits the game instance limit' : limit === 0 ? 'Team instance limit set to unlimited' : `Team instance limit set to ${limit}`)
      setReload(x => x + 1)
    } catch (err) { showErrorMsg(err, t) }
  }

  const destroyInstance = (instance: Instance) => openAdminConfirm({
    t,
    severity: 'danger',
    title: 'Close team instance',
    message: 'Permanently stop and remove this open challenge container?',
    subject: `${instance.challenge} · ${instance.instanceId.slice(0, 12)}`,
    onConfirm: async () => {
      try {
        await api.request({ path: `/api/soc/instances/${instance.instanceId}`, method: 'DELETE' })
        showAdminSuccess('Team instance closed')
        setReload(x => x + 1)
      } catch (err) { showErrorMsg(err, t) }
    },
  })

  const alertColumns: DataTableColumn<Alert>[] = [
    { key: 'time', header: 'Time', width: '10rem', render: x => dayjs(x.createdAt).format('MM-DD HH:mm:ss') },
    { key: 'severity', header: 'Severity', width: '7rem', render: x => <AdminStatusBadge label={x.severity} tone={tone(x.severity)} solid /> },
    { key: 'event', header: 'Event', width: '17rem', rowHeader: true, render: x => <AdminCellStack><AdminCellName>{x.event}</AdminCellName><AdminCellMeta>{x.challenge ?? x.category}</AdminCellMeta></AdminCellStack> },
    { key: 'risk', header: 'Risk impact', width: '7rem', numeric: true, render: x => x.riskImpact ? `+${x.riskImpact}` : '—' },
    { key: 'status', header: 'Status', width: '8rem', render: x => <AdminStatusBadge label={x.status} tone={tone(x.status)} /> },
    { key: 'evidence', header: 'Evidence', width: '15rem', render: x => <AdminMonoValue value={x.evidenceFingerprint} /> },
  ]
  const instanceColumns: DataTableColumn<Instance>[] = [
    { key: 'id', header: 'Instance', width: '10rem', rowHeader: true, render: x => <AdminMonoValue value={x.instanceId.slice(0, 12)} /> },
    { key: 'challenge', header: 'Challenge', width: '15rem', render: x => <AdminCellStack><AdminCellName>{x.challenge}</AdminCellName><AdminCellMeta>{x.game}</AdminCellMeta></AdminCellStack> },
    { key: 'cpu', header: 'CPU', width: '6rem', numeric: true, render: x => formatSocPercent(x.cpuPercent) },
    { key: 'ram', header: 'RAM', width: '10rem', numeric: true, render: x => `${formatSocMegabytes(x.memoryUsedBytes)} / ${formatSocMegabytes(x.memoryLimitBytes)}` },
    { key: 'network', header: 'Network', width: '8rem', render: x => x.networkMode },
    { key: 'status', header: 'Status', width: '7rem', render: x => <AdminStatusBadge label={x.status} tone={tone(x.status)} /> },
    { key: 'actions', header: '', headerLabel: 'Instance actions', width: '5rem', align: 'right', render: x => <Tooltip label="Close instance"><ActionIcon variant="subtle" color="red" onClick={() => destroyInstance(x)}><Icon path={mdiDeleteOutline} size={0.75} /></ActionIcon></Tooltip> },
  ]
  const limitColumns: DataTableColumn<InstanceLimit>[] = [
    { key: 'game', header: 'Game', width: '16rem', rowHeader: true, render: x => <AdminCellStack><AdminCellName>{x.game}</AdminCellName><AdminCellMeta>Game default: {x.defaultLimit === 0 ? 'Unlimited' : x.defaultLimit}</AdminCellMeta></AdminCellStack> },
    { key: 'running', header: 'Open now', width: '7rem', numeric: true, render: x => x.runningInstances },
    { key: 'effective', header: 'Effective limit', width: '8rem', numeric: true, render: x => x.effectiveLimit === 0 ? 'Unlimited' : x.effectiveLimit },
    { key: 'override', header: 'Team override', width: '12rem', render: x => <NumberInput value={limitDrafts[x.participationId] ?? ''} onChange={value => setLimitDrafts(current => ({ ...current, [x.participationId]: value }))} min={0} max={1000} allowDecimal={false} allowNegative={false} placeholder={`Inherit (${x.defaultLimit === 0 ? 'unlimited' : x.defaultLimit})`} aria-label={`Instance limit for ${x.game}`} /> },
    { key: 'save', header: '', headerLabel: 'Save limit', width: '6rem', align: 'right', render: x => <Button size="xs" onClick={() => updateLimit(x.participationId)}>Save</Button> },
  ]
  const timelineColumns: DataTableColumn<Timeline>[] = [
    { key: 'time', header: 'Timestamp', width: '11rem', render: x => dayjs(x.timestamp).format('MM-DD HH:mm:ss') },
    { key: 'event', header: 'Event', width: '15rem', rowHeader: true, render: x => <AdminCellStack><AdminCellName>{x.event}</AdminCellName><AdminCellMeta>{x.category}</AdminCellMeta></AdminCellStack> },
    { key: 'challenge', header: 'Challenge', width: '13rem', render: x => x.challenge ?? '—' },
    { key: 'details', header: 'Details', width: '24rem', render: x => x.details },
  ]
  const flagColumns: DataTableColumn<Flag>[] = [
    { key: 'fingerprint', header: 'Flag fingerprint', width: '18rem', rowHeader: true, render: x => <AdminMonoValue value={x.fingerprint} wide /> },
    { key: 'challenge', header: 'Challenge', width: '14rem', render: x => <AdminCellStack><AdminCellName>{x.challenge}</AdminCellName><AdminCellMeta>{x.game}</AdminCellMeta></AdminCellStack> },
    { key: 'instance', header: 'Instance', width: '12rem', render: x => <AdminMonoValue value={x.instanceId} /> },
    { key: 'status', header: 'Status', width: '7rem', render: x => <AdminStatusBadge label={x.status} tone={tone(x.status)} /> },
  ]
  const correlations = useMemo(() => Array.from(new Set(alerts.map(x => x.relatedTeam).filter(Boolean))) as string[], [alerts])

  return <AdminLayout title={risk?.team ?? 'Team investigation'} description="Explainable security evidence, submissions, instances and related activity."
    breadcrumbs={[{ label: 'SOC & Anti-Cheat', to: '/admin/soc' }, { label: 'Teams', to: '/admin/soc' }]}
    isLoading={loading}
    subnav={<Tabs value={active} onChange={setActive} className={classes.tabs}><Tabs.List>{['overview','instances','limits','submissions','alerts','correlated','sessions','timeline'].map(x => <Tabs.Tab key={x} value={x}>{x[0].toUpperCase()+x.slice(1)}</Tabs.Tab>)}</Tabs.List></Tabs>}>
    {failed && <AdminOutageNotice message="This team investigation could not be loaded." />}
    {active === 'overview' && <div className={classes.stack}><AdminMetricGrid><StatCard label="RISK SCORE" value={`${risk?.riskScore ?? '—'} / 100`} tone={risk ? tone(risk.classification) === 'critical' ? 'critical' : 'neutral' : 'neutral'} sublabel={risk?.classification} /><StatCard label="TOTAL SOLVES" value={risk?.solves ?? '—'} /><StatCard label="SUBMISSIONS" value={risk?.submissions ?? '—'} /><StatCard label="RUNNING INSTANCES" value={risk?.runningInstances ?? '—'} /><StatCard label="OPEN ALERTS" value={risk?.openAlerts ?? '—'} /><StatCard label="CRITICAL ALERTS" value={risk?.criticalAlerts ?? '—'} tone={risk?.criticalAlerts ? 'critical' : 'neutral'} /></AdminMetricGrid><Panel title="Risk breakdown">{risk?.reasons.length ? risk.reasons.map(x => <div className={classes.rankRow} key={x}><span>{x}</span></div>) : <p>No active risk signals.</p>}</Panel><Panel title="Dynamic flag ownership" bare><AdminTable caption="Team dynamic flags" columns={flagColumns} rows={flags} rowKey={socFlagRowKey} /></Panel></div>}
    {active === 'submissions' && <Panel bare><AdminTable caption="Submission activity" columns={timelineColumns} rows={timeline.filter(x => x.event === 'FlagSubmission')} rowKey={x => x.id} /></Panel>}
    {active === 'instances' && <Panel title={`Open instances (${instances.length})`} bare><AdminTable caption="Open team instances" columns={instanceColumns} rows={instances} rowKey={x => x.instanceId} /></Panel>}
    {active === 'limits' && <div className={classes.stack}><AdminOutageNotice message="Leave the override empty to inherit the game limit. Use 0 for unlimited. Lowering a limit does not close existing instances automatically." /><Panel title="Per-game instance allowance" bare><AdminTable caption="Team instance limits" columns={limitColumns} rows={limits} rowKey={x => x.participationId} /></Panel></div>}
    {active === 'alerts' && <Panel bare><AdminTable caption="Team alerts" columns={alertColumns} rows={alerts} rowKey={x => x.id} /></Panel>}
    {active === 'correlated' && <Panel title="Related teams">{correlations.length ? correlations.map(x => <div className={classes.rankRow} key={x}><strong>{x}</strong><span>Linked by cross-team flag evidence</span></div>) : <p>No correlated teams in current evidence.</p>}</Panel>}
    {active === 'sessions' && <AdminOutageNotice message="BURAQ does not currently maintain a reliable server-side session registry. IP overlap is intentionally not treated as cheating evidence." />}
    {active === 'timeline' && <Panel bare><AdminTable caption="Team SOC timeline" columns={timelineColumns} rows={timeline} rowKey={x => x.id} /></Panel>}
  </AdminLayout>
}

export default TeamInvestigation

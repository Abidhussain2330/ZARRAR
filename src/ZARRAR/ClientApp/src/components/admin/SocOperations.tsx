import { Button, Code, Select, Stack, Text } from '@mantine/core'
import { FC, useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import api from '@Api'
import { showErrorMsg } from '@Utils/Shared'
import { Panel } from '@Components/zarrar'
import { AdminCellMeta, AdminCellName, AdminCellStack, AdminStatusBadge, AdminTable, AdminToolbar, showAdminSuccess } from '@Components/admin/shell'
import type { DataTableColumn } from '@Components/zarrar'

interface ArrayResponse<T> { data: T[]; total: number }
interface DetectionJob { id: number; submissionId: number; status: string; attemptCount: number; createdAt: number; completedAt?: number; lastError?: string }
interface OutboxEntry { id: number; eventId: string; occurredAt: number; state: string; attemptCount: number; nextAttemptAt?: number; lastError?: string; eventType: string; source: string; severity: string; category: string; correlationId?: string }

export const SocOperations: FC = () => {
  const { t } = useTranslation()
  const [detectionState, setDetectionState] = useState('failed')
  const [outboxState, setOutboxState] = useState('dead')
  const [detectionJobs, setDetectionJobs] = useState<DetectionJob[]>([])
  const [outboxEntries, setOutboxEntries] = useState<OutboxEntry[]>([])
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState<string>()

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const [jobs, entries] = await Promise.all([
        api.request<ArrayResponse<DetectionJob>>({ path: '/api/soc/detectionqueue/jobs', method: 'GET', query: { count: 100, state: detectionState }, format: 'json' }),
        api.request<ArrayResponse<OutboxEntry>>({ path: '/api/soc/outbox/entries', method: 'GET', query: { count: 100, state: outboxState }, format: 'json' }),
      ])
      setDetectionJobs(jobs.data.data)
      setOutboxEntries(entries.data.data)
    } catch (error) {
      showErrorMsg(error, t)
    } finally {
      setLoading(false)
    }
  }, [detectionState, outboxState, t])

  useEffect(() => { void load() }, [load])

  const retry = async (kind: 'detectionqueue' | 'outbox', id: number) => {
    setBusy(`${kind}-${id}`)
    try {
      await api.request({ path: `/api/soc/${kind}/${id}/retry`, method: 'POST' })
      showAdminSuccess(kind === 'outbox' ? 'SOC outbox entry queued for retry' : 'Detection job queued for retry')
      await load()
    } catch (error) {
      showErrorMsg(error, t)
    } finally {
      setBusy(undefined)
    }
  }

  const detectionColumns: DataTableColumn<DetectionJob>[] = [
    { key: 'job', header: 'Detection job', width: '12rem', rowHeader: true, render: (item) => <AdminCellStack><AdminCellName>Job {item.id}</AdminCellName><AdminCellMeta>Submission {item.submissionId}</AdminCellMeta></AdminCellStack> },
    { key: 'status', header: 'Status', width: '8rem', render: (item) => <AdminStatusBadge label={item.status} /> },
    { key: 'attempts', header: 'Attempts', width: '6rem', numeric: true, render: (item) => item.attemptCount },
    { key: 'error', header: 'Last error', width: '28rem', render: (item) => item.lastError ?? '—' },
    { key: 'action', header: '', headerLabel: 'Detection job actions', width: '7rem', align: 'right', render: (item) => <Button size="xs" variant="default" disabled={Boolean(busy) || !['Failed', 'Pending'].includes(item.status)} onClick={() => void retry('detectionqueue', item.id)}>Retry</Button> },
  ]
  const outboxColumns: DataTableColumn<OutboxEntry>[] = [
    { key: 'event', header: 'SOC event', width: '18rem', rowHeader: true, render: (item) => <AdminCellStack><AdminCellName>{item.eventType}</AdminCellName><AdminCellMeta>{item.source} · {item.category}</AdminCellMeta></AdminCellStack> },
    { key: 'id', header: 'Event ID', width: '15rem', render: (item) => <Code>{item.eventId.slice(0, 18)}</Code> },
    { key: 'state', header: 'State', width: '8rem', render: (item) => <AdminStatusBadge label={item.state} /> },
    { key: 'attempts', header: 'Attempts', width: '6rem', numeric: true, render: (item) => item.attemptCount },
    { key: 'error', header: 'Last error', width: '28rem', render: (item) => <Text size="sm" c={item.lastError ? 'red' : 'dimmed'}>{item.lastError ?? '—'}</Text> },
    { key: 'action', header: '', headerLabel: 'Outbox entry actions', width: '7rem', align: 'right', render: (item) => <Button size="xs" variant="default" disabled={Boolean(busy) || item.state === 'Processed'} onClick={() => void retry('outbox', item.id)}>Retry</Button> },
  ]

  return <Stack gap="md">
    <Panel title="Detection queue" bare>
      <Stack gap="sm">
        <AdminToolbar filters={<Select value={detectionState} onChange={(value) => value && setDetectionState(value)} data={['failed', 'pending', 'running', 'completed']} />} trailing={<Button variant="default" onClick={() => void load()}>Refresh</Button>} />
        <AdminTable caption="SOC detection queue jobs" columns={detectionColumns} rows={detectionJobs} rowKey={(item) => item.id} loading={loading} />
      </Stack>
    </Panel>
    <Panel title="Durable SOC event outbox" bare>
      <Stack gap="sm">
        <AdminToolbar filters={<Select value={outboxState} onChange={(value) => value && setOutboxState(value)} data={['dead', 'retrying', 'pending', 'processed']} />} />
        <AdminTable caption="SOC durable outbox entries" columns={outboxColumns} rows={outboxEntries} rowKey={(item) => item.id} loading={loading} />
      </Stack>
    </Panel>
  </Stack>
}

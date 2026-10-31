import { Alert, Button, Code, Group, NumberInput, Select, SimpleGrid, Stack, Switch, Text, Textarea, TextInput } from '@mantine/core'
import { FC, useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import api, { ChallengeType, ContentType } from '@Api'
import { showErrorMsg } from '@Utils/Shared'
import { Panel } from '@Components/zarrar'
import { AdminFormGrid, AdminFormWide, AdminSection, AdminStatusBadge, openAdminConfirm, showAdminSuccess } from '@Components/admin/shell'

interface SharedRuntime {
  challengeId: number
  challenge: string
  origin: string
  audience: string
  healthPath: string
  stateMode: string
  isEnabled: boolean
  activeSessions: number
  maxConcurrentSessions: number
  healthStatus: string
  lastHealthCheckUtc?: number
  lastHealthError?: string
}

interface RuntimeDraft {
  origin: string
  audience: string
  healthPath: string
  stateMode: string
  isEnabled: boolean
  maxConcurrentSessions: number
}

interface BuilderStatus {
  enabled: boolean
  configured: boolean
  validatedTemplates: number
  datasetAvailable: number
  pending: number
  running: number
  succeeded: number
  failed: number
}

interface VariantTemplate {
  id: number
  challengeId: number
  version: string
  kind: string
  manifestHash: string
  builderImageDigest: string
  outputFileName: string
  status: string
  jobs: number
  succeeded: number
  failed: number
  datasetItems: number
}

interface VariantJob {
  id: string
  templateId: number
  team: string
  status: string
  attemptCount: number
  artifactHash?: string
  generatorVersion?: string
  lastError?: string
}

interface ValidationResult { isValid: boolean; manifestHash: string; errors: string[] }

const defaultRuntime: RuntimeDraft = {
  origin: '',
  audience: '',
  healthPath: '/healthz',
  stateMode: 'Stateless',
  isEnabled: true,
  maxConcurrentSessions: 1000,
}

const defaultManifest = JSON.stringify({ schemaVersion: 1, entrypoint: '/builder/run', network: false }, null, 2)

interface Props {
  challengeId: number
  challengeType?: ChallengeType
}

export const AdvancedChallengeRuntimeControls: FC<Props> = ({ challengeId, challengeType }) => {
  const { t } = useTranslation()
  const [runtime, setRuntime] = useState<SharedRuntime>()
  const [runtimeDraft, setRuntimeDraft] = useState<RuntimeDraft>(defaultRuntime)
  const [builder, setBuilder] = useState<BuilderStatus>()
  const [templates, setTemplates] = useState<VariantTemplate[]>([])
  const [jobs, setJobs] = useState<VariantJob[]>([])
  const [busy, setBusy] = useState(false)
  const [loadError, setLoadError] = useState(false)
  const [version, setVersion] = useState('v1')
  const [kind, setKind] = useState('Reverse')
  const [manifestJson, setManifestJson] = useState(defaultManifest)
  const [builderImageDigest, setBuilderImageDigest] = useState('')
  const [outputFileName, setOutputFileName] = useState('challenge.bin')

  const isDynamicAttachment = challengeType === ChallengeType.DynamicAttachment
  const challengeTemplates = useMemo(
    () => templates.filter((item) => item.challengeId === challengeId),
    [challengeId, templates]
  )
  const templateIds = useMemo(() => new Set(challengeTemplates.map((item) => item.id)), [challengeTemplates])
  const challengeJobs = useMemo(() => jobs.filter((item) => templateIds.has(item.templateId)), [jobs, templateIds])

  const load = useCallback(async () => {
    try {
      setLoadError(false)
      const requests = [
        api.request<SharedRuntime[]>({ path: '/api/admin/shared-runtimes', method: 'GET', format: 'json' }),
        api.request<BuilderStatus>({ path: '/api/admin/variant-builder/status', method: 'GET', format: 'json' }),
      ] as const
      const [runtimesResponse, builderResponse] = await Promise.all(requests)
      const found = runtimesResponse.data.find((item) => item.challengeId === challengeId)
      setRuntime(found)
      setRuntimeDraft(found ? {
        origin: found.origin,
        audience: found.audience,
        healthPath: found.healthPath,
        stateMode: found.stateMode,
        isEnabled: found.isEnabled,
        maxConcurrentSessions: found.maxConcurrentSessions,
      } : defaultRuntime)
      setBuilder(builderResponse.data)

      if (isDynamicAttachment) {
        const [templateResponse, jobResponse] = await Promise.all([
          api.request<VariantTemplate[]>({ path: '/api/admin/variant-builder/templates', method: 'GET', format: 'json' }),
          api.request<VariantJob[]>({ path: '/api/admin/variant-builder/jobs', method: 'GET', format: 'json' }),
        ])
        setTemplates(templateResponse.data)
        setJobs(jobResponse.data)
      }
    } catch (error) {
      setLoadError(true)
      showErrorMsg(error, t)
    }
  }, [challengeId, isDynamicAttachment, t])

  useEffect(() => { void load() }, [load])

  const run = async (action: () => Promise<unknown>, success: string) => {
    setBusy(true)
    try {
      await action()
      showAdminSuccess(success)
      await load()
    } catch (error) {
      showErrorMsg(error, t)
    } finally {
      setBusy(false)
    }
  }

  const saveRuntime = () => run(
    () => api.request({
      path: `/api/admin/shared-runtimes/${challengeId}`,
      method: 'PUT',
      body: runtimeDraft,
      type: ContentType.Json,
    }),
    'Shared runtime configuration saved'
  )

  const disableRuntime = () => openAdminConfirm({
    t,
    severity: 'danger',
    title: 'Disable shared runtime',
    message: 'Disable this shared runtime and revoke every active participant session?',
    subject: runtime?.challenge,
    onConfirm: () => run(
      () => api.request({ path: `/api/admin/shared-runtimes/${challengeId}/disable`, method: 'POST' }),
      'Shared runtime disabled and sessions revoked'
    ),
  })

  const createTemplate = () => {
    if (!version.trim() || !builderImageDigest.trim() || !outputFileName.trim()) {
      showErrorMsg(new Error('Version, pinned builder digest, and output file are required'), t)
      return
    }
    try {
      JSON.parse(manifestJson)
    } catch {
      showErrorMsg(new Error('Manifest must be valid JSON'), t)
      return
    }
    void run(
      () => api.request({
        path: `/api/admin/variant-builder/challenges/${challengeId}/templates`,
        method: 'PUT',
        body: { version: version.trim(), kind, manifestJson, builderImageDigest: builderImageDigest.trim(), outputFileName: outputFileName.trim() },
        type: ContentType.Json,
      }),
      'Variant template created'
    )
  }

  return <Stack gap="md">
    {loadError && <Alert color="red" title="Advanced runtime controls unavailable">Retry before changing this challenge runtime.</Alert>}

    <Panel>
      <AdminSection
        title="Shared Web/API runtime"
        description="Route this challenge through the authenticated gateway. The origin must also exist in the deployment allowlist."
      >
        <AdminFormGrid>
          <TextInput
            label="Allowlisted HTTPS origin"
            placeholder="https://challenge.internal.example/"
            value={runtimeDraft.origin}
            disabled={busy}
            onChange={(event) => setRuntimeDraft({ ...runtimeDraft, origin: event.currentTarget.value })}
          />
          <TextInput
            label="Token audience"
            placeholder="challenge-api-v1"
            value={runtimeDraft.audience}
            disabled={busy}
            onChange={(event) => setRuntimeDraft({ ...runtimeDraft, audience: event.currentTarget.value })}
          />
          <TextInput
            label="Health path"
            placeholder="/healthz"
            value={runtimeDraft.healthPath}
            disabled={busy}
            onChange={(event) => setRuntimeDraft({ ...runtimeDraft, healthPath: event.currentTarget.value })}
          />
          <Select
            label="State isolation"
            data={['Stateless', 'TeamNamespaced']}
            value={runtimeDraft.stateMode}
            disabled={busy}
            onChange={(value) => value && setRuntimeDraft({ ...runtimeDraft, stateMode: value })}
          />
          <NumberInput
            label="Maximum concurrent sessions"
            min={1}
            max={100000}
            value={runtimeDraft.maxConcurrentSessions}
            disabled={busy}
            onChange={(value) => typeof value === 'number' && setRuntimeDraft({ ...runtimeDraft, maxConcurrentSessions: value })}
          />
          <Switch
            label="Accept participant sessions"
            checked={runtimeDraft.isEnabled}
            disabled={busy}
            onChange={(event) => setRuntimeDraft({ ...runtimeDraft, isEnabled: event.currentTarget.checked })}
          />
          <AdminFormWide>
            <Group justify="space-between" align="center" wrap="wrap">
              <Group gap="xs">
                <AdminStatusBadge label={runtime ? (runtime.isEnabled ? runtime.healthStatus : 'Disabled') : 'Not configured'} />
                {runtime && <Text size="sm" c="dimmed">{runtime.activeSessions} active sessions</Text>}
                {runtime?.lastHealthError && <Text size="sm" c="red">{runtime.lastHealthError}</Text>}
              </Group>
              <Group gap="xs">
                <Button variant="default" disabled={busy} onClick={() => void load()}>Reload</Button>
                <Button variant="default" disabled={busy || !runtime} onClick={() => void run(
                  () => api.request({ path: `/api/admin/shared-runtimes/${challengeId}/health`, method: 'POST' }),
                  'Shared runtime health refreshed'
                )}>Check health</Button>
                <Button color="red" variant="outline" disabled={busy || !runtime?.isEnabled} onClick={disableRuntime}>Disable</Button>
                <Button disabled={busy || !runtimeDraft.origin.trim() || !runtimeDraft.audience.trim()} onClick={() => void saveRuntime()}>Save runtime</Button>
              </Group>
            </Group>
          </AdminFormWide>
        </AdminFormGrid>
      </AdminSection>
    </Panel>

    {isDynamicAttachment && <Panel>
      <AdminSection
        title="Deterministic variant builder"
        description="Create pinned, network-isolated build templates and schedule one immutable artifact per accepted participation."
      >
        <Stack gap="md">
          <Group gap="xs">
            <AdminStatusBadge label={!builder?.enabled ? 'Disabled' : builder.configured ? 'Ready' : 'Misconfigured'} />
            <Text size="sm" c="dimmed">
              {builder?.validatedTemplates ?? 0} validated · {builder?.datasetAvailable ?? 0} dataset items · {builder?.pending ?? 0} queued · {builder?.running ?? 0} running
            </Text>
          </Group>
          <AdminFormGrid>
            <TextInput label="Template version" value={version} disabled={busy} onChange={(event) => setVersion(event.currentTarget.value)} />
            <Select label="Challenge family" data={['Reverse', 'Pwn', 'Crypto', 'Stego']} value={kind} disabled={busy} onChange={(value) => value && setKind(value)} />
            <TextInput label="Pinned builder image" placeholder="registry/builder@sha256:…" value={builderImageDigest} disabled={busy} onChange={(event) => setBuilderImageDigest(event.currentTarget.value)} />
            <TextInput label="Output file name" value={outputFileName} disabled={busy} onChange={(event) => setOutputFileName(event.currentTarget.value)} />
            <AdminFormWide>
              <Textarea label="Build manifest JSON" autosize minRows={5} maxRows={14} value={manifestJson} disabled={busy} onChange={(event) => setManifestJson(event.currentTarget.value)} />
            </AdminFormWide>
            <AdminFormWide><Group justify="flex-end"><Button variant="default" disabled={busy} onClick={() => void run(
              () => api.request({ path: `/api/admin/variant-builder/challenges/${challengeId}/dataset/sync`, method: 'POST' }),
              'Existing attachment dataset synchronized'
            )}>Sync dataset</Button><Button disabled={busy} onClick={createTemplate}>Create template</Button></Group></AdminFormWide>
          </AdminFormGrid>

          {challengeTemplates.map((template) => <Stack key={template.id} gap="xs" p="sm" style={{ border: '1px solid var(--mantine-color-default-border)', borderRadius: 'var(--mantine-radius-md)' }}>
            <Group justify="space-between" align="flex-start">
              <div><Text fw={600}>{template.version} · {template.kind}</Text><Code>{template.manifestHash}</Code><Text size="xs" c="dimmed">{template.outputFileName} · {template.datasetItems} dataset items · {template.succeeded}/{template.jobs} succeeded · {template.failed} failed</Text></div>
              <AdminStatusBadge label={template.status} />
            </Group>
            <Group gap="xs" justify="flex-end">
              <Button size="xs" variant="default" disabled={busy || template.status === 'Disabled'} onClick={() => void run(async () => {
                const result = await api.request<ValidationResult>({ path: `/api/admin/variant-builder/templates/${template.id}/validate`, method: 'POST', format: 'json' })
                if (!result.data.isValid) throw new Error(result.data.errors.join('; ') || 'Template validation failed')
              }, 'Variant template validation passed')}>Validate</Button>
              <Button size="xs" disabled={busy || template.status !== 'Validated' || !builder?.enabled || !builder.configured} onClick={() => openAdminConfirm({
                t,
                title: 'Schedule variant builds',
                message: 'Create deterministic build jobs for every eligible accepted participation?',
                subject: template.version,
                onConfirm: () => run(() => api.request({ path: `/api/admin/variant-builder/templates/${template.id}/jobs`, method: 'POST' }), 'Variant build jobs scheduled'),
              })}>Schedule builds</Button>
              <Button size="xs" color="red" variant="outline" disabled={busy || template.status === 'Disabled'} onClick={() => openAdminConfirm({
                t,
                severity: 'danger',
                title: 'Disable variant template',
                message: 'Disable this template so it cannot schedule new assignments?',
                subject: template.version,
                onConfirm: () => run(() => api.request({ path: `/api/admin/variant-builder/templates/${template.id}/disable`, method: 'POST' }), 'Variant template disabled'),
              })}>Disable</Button>
            </Group>
          </Stack>)}

          {challengeJobs.length > 0 && <Stack gap="xs">
            <Text fw={600}>Recent build jobs</Text>
            {challengeJobs.slice(0, 25).map((job) => <SimpleGrid key={job.id} cols={{ base: 1, md: 4 }} spacing="xs" p="xs" style={{ borderBottom: '1px solid var(--mantine-color-default-border)' }}>
              <div><Text size="sm" fw={500}>{job.team}</Text><Code>{job.id.slice(0, 12)}</Code></div>
              <AdminStatusBadge label={job.status} />
              <Text size="sm" c={job.lastError ? 'red' : 'dimmed'}>{job.lastError ?? job.artifactHash ?? `Attempt ${job.attemptCount}`}</Text>
              <Button size="xs" variant="default" disabled={busy || !['Failed', 'Quarantined'].includes(job.status)} onClick={() => void run(
                () => api.request({ path: `/api/admin/variant-builder/jobs/${job.id}/retry`, method: 'POST' }),
                'Variant build job queued for retry'
              )}>Retry</Button>
            </SimpleGrid>)}
          </Stack>}
        </Stack>
      </AdminSection>
    </Panel>}
  </Stack>
}

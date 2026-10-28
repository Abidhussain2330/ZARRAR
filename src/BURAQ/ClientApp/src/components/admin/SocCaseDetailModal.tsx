import { Badge, Button, Code, Divider, Group, Modal, Select, SimpleGrid, Stack, Text, Textarea } from '@mantine/core'
import dayjs from 'dayjs'
import { FC, useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import api, { ContentType } from '@Api'
import { showErrorMsg } from '@Utils/Shared'
import { AdminStatusBadge, openAdminConfirm, showAdminSuccess } from '@Components/admin/shell'

interface Evidence { id: number; evidenceKey: string; kind: string; weight: number; confidence: number; occurredAt: number; teamId?: number; relatedTeamId?: number; challengeId?: number; submissionId?: number; alertId?: number; fingerprint?: string; metadataJson?: string }
interface CaseSummary { id: string; title: string; summary: string; status: string; riskScore: number; confidence: number; ruleHitCount: number; evidenceCount: number; game: string; primaryTeam: string; relatedTeam?: string; notes?: string }
interface CaseDetail { case: CaseSummary; evidence: Evidence[] }
interface MemberTarget { userId: string; userName: string; role: string }
interface TeamTarget { teamId: number; team: string; participationId: number; status: string; members: MemberTarget[] }
interface SubmissionTarget { id: number; teamId: number; team?: string; userName?: string; challenge?: string; submittedAt: number; status: string; scored: boolean }
interface Targets { teams: TeamTarget[]; submissions: SubmissionTarget[] }

type Action = 'suspend-team' | 'reinstate-team' | 'ban-user' | 'sign-out-user' | 'void-submission' | 'note'

interface Props {
  caseId?: string
  opened: boolean
  onClose: () => void
  onChanged: () => void
}

/** Proof-grade evidence (a foreign dynamic flag, a two-way exchange) never fades with time. */
const PROOF_CONFIDENCE = 95

/**
 * A risk case: its evidence chain and the response actions (S1). Every action is re-checked by the
 * server against this case's game and teams, needs a reason, and lands in the case notes and the
 * SOC timeline.
 */
export const SocCaseDetailModal: FC<Props> = ({ caseId, opened, onClose, onChanged }) => {
  const { t } = useTranslation()
  const [detail, setDetail] = useState<CaseDetail>()
  const [targets, setTargets] = useState<Targets>()
  const [busy, setBusy] = useState(false)
  const [note, setNote] = useState('')
  const [teamId, setTeamId] = useState<string | null>(null)
  const [userId, setUserId] = useState<string | null>(null)
  const [submissionId, setSubmissionId] = useState<string | null>(null)

  const loadAll = useCallback(async () => {
    if (!caseId) return
    try {
      const [d, tg] = await Promise.all([
        api.request<CaseDetail>({ path: `/api/soc/cases/${caseId}`, method: 'GET', format: 'json' }),
        api.request<Targets>({ path: `/api/soc/cases/${caseId}/targets`, method: 'GET', format: 'json' }),
      ])
      setDetail(d.data)
      setTargets(tg.data)
    } catch (error) {
      showErrorMsg(error, t)
    }
  }, [caseId, t])

  useEffect(() => {
    if (!opened || !caseId) return
    setDetail(undefined)
    setTargets(undefined)
    setNote('')
    setTeamId(null)
    setUserId(null)
    setSubmissionId(null)
    void loadAll()
  }, [caseId, opened, loadAll])

  const recalculate = async () => {
    if (!caseId) return
    setBusy(true)
    try {
      await api.request({ path: `/api/soc/cases/${caseId}/recalculate`, method: 'POST' })
      await loadAll()
      showAdminSuccess('Risk case recalculated from current evidence')
      onChanged()
    } catch (error) {
      showErrorMsg(error, t)
    } finally {
      setBusy(false)
    }
  }

  const members = useMemo(() => targets?.teams.flatMap((team) => team.members.map((m) => ({ ...m, team: team.team }))) ?? [], [targets])
  const selectedTeam = targets?.teams.find((x) => String(x.teamId) === teamId)

  const run = (action: Action, label: string, danger: boolean) => {
    if (!caseId) return
    if (action !== 'reinstate-team' && note.trim().length < 4) {
      showErrorMsg(new Error('Write a short reason first (at least 4 characters).'), t)
      return
    }

    const send = async () => {
      setBusy(true)
      try {
        await api.request({
          path: `/api/soc/cases/${caseId}/actions`,
          method: 'POST',
          type: ContentType.Json,
          body: {
            action,
            teamId: teamId ? Number(teamId) : undefined,
            userId: userId ?? undefined,
            submissionId: submissionId ? Number(submissionId) : undefined,
            note: note.trim() || undefined,
          },
        })
        showAdminSuccess(label)
        setNote('')
        await loadAll()
        onChanged()
      } catch (error) {
        showErrorMsg(error, t)
      } finally {
        setBusy(false)
      }
    }

    if (!danger) {
      void send()
      return
    }

    openAdminConfirm({
      t,
      severity: 'danger',
      title: label,
      message: 'This takes effect immediately and is recorded on the case and in the SOC timeline.',
      subject: note.trim() || label,
      onConfirm: send,
    })
  }

  return <Modal opened={opened} onClose={onClose} title={detail?.case.title ?? 'Risk case evidence'} size="xl">
    {!detail ? <Text c="dimmed">Loading evidence…</Text> : <Stack gap="md">
      <Group justify="space-between" align="flex-start">
        <div><Text fw={600}>{detail.case.primaryTeam}{detail.case.relatedTeam ? ` ↔ ${detail.case.relatedTeam}` : ''}</Text><Text size="sm" c="dimmed">{detail.case.game}</Text></div>
        <Group gap="xs"><AdminStatusBadge label={detail.case.status} /><Button size="xs" variant="default" loading={busy} onClick={() => void recalculate()}>Recalculate</Button></Group>
      </Group>
      <SimpleGrid cols={{ base: 2, sm: 4 }}><div><Text size="xs" c="dimmed">Risk now</Text><Text fw={700}>{detail.case.riskScore}/100</Text></div><div><Text size="xs" c="dimmed">Confidence</Text><Text fw={700}>{detail.case.confidence}%</Text></div><div><Text size="xs" c="dimmed">Rules</Text><Text fw={700}>{detail.case.ruleHitCount}</Text></div><div><Text size="xs" c="dimmed">Evidence</Text><Text fw={700}>{detail.case.evidenceCount}</Text></div></SimpleGrid>
      <Text>{detail.case.summary}</Text>

      {/* ---------------------------------------------------------------- respond */}
      <Divider label="Respond" labelPosition="left" />
      {!targets ? <Text size="sm" c="dimmed">Loading targets…</Text> : <Stack gap="sm">
        <Textarea
          label="Reason (recorded on the case)"
          placeholder="e.g. Flag from the other team's instance submitted at 14:02"
          autosize
          minRows={2}
          maxLength={500}
          value={note}
          onChange={(e) => setNote(e.currentTarget.value)}
        />
        <Group align="flex-end" gap="xs">
          <Select
            label="Team"
            placeholder="Pick a team"
            w={260}
            value={teamId}
            onChange={setTeamId}
            data={targets.teams.map((x) => ({ value: String(x.teamId), label: `${x.team} (${x.status})` }))}
          />
          <Button size="xs" color="red" disabled={!selectedTeam || selectedTeam.status === 'Suspended'} loading={busy}
            onClick={() => run('suspend-team', `Suspend ${selectedTeam?.team ?? 'team'}`, true)}>Suspend team</Button>
          <Button size="xs" variant="default" disabled={!selectedTeam || selectedTeam.status !== 'Suspended'} loading={busy}
            onClick={() => run('reinstate-team', `Reinstate ${selectedTeam?.team ?? 'team'}`, true)}>Reinstate</Button>
        </Group>
        <Group align="flex-end" gap="xs">
          <Select
            label="Player"
            placeholder="Pick a player"
            w={260}
            value={userId}
            onChange={setUserId}
            data={members.map((m) => ({ value: m.userId, label: `${m.userName} · ${m.team}${m.role === 'Banned' ? ' (banned)' : ''}` }))}
          />
          <Button size="xs" variant="default" disabled={!userId} loading={busy}
            onClick={() => run('sign-out-user', 'End all of this player’s sessions', true)}>Sign out everywhere</Button>
          <Button size="xs" color="red" disabled={!userId} loading={busy}
            onClick={() => run('ban-user', 'Ban this player', true)}>Ban player</Button>
        </Group>
        <Group align="flex-end" gap="xs">
          <Select
            label="Submission"
            placeholder="Pick a submission"
            w={420}
            searchable
            value={submissionId}
            onChange={setSubmissionId}
            data={targets.submissions.map((s) => ({
              value: String(s.id),
              label: `#${s.id} ${s.team ?? ''} · ${s.challenge ?? '?'} · ${s.userName ?? '?'} · ${dayjs(s.submittedAt).format('HH:mm:ss')}${s.scored ? ' · SCORED' : ` · ${s.status}`}`,
            }))}
          />
          <Button size="xs" color="red" disabled={!submissionId} loading={busy}
            onClick={() => run('void-submission', `Void submission #${submissionId}`, true)}>Void (remove points)</Button>
        </Group>
        <Group gap="xs">
          <Button size="xs" variant="light" disabled={note.trim().length < 4} loading={busy}
            onClick={() => run('note', 'Note added', false)}>Add note only</Button>
        </Group>
      </Stack>}
      {detail.case.notes && <Stack gap={2}><Text size="sm" fw={600}>Case log</Text><Code block>{detail.case.notes}</Code></Stack>}

      {/* --------------------------------------------------------------- evidence */}
      <Divider label="Evidence chain" labelPosition="left" />
      {detail.evidence.map((item) => <Stack key={item.id} gap={3} p="sm" style={{ border: '1px solid var(--mantine-color-default-border)', borderRadius: 'var(--mantine-radius-md)' }}>
        <Group justify="space-between"><div><Group gap={6}><Text fw={600}>{item.kind}</Text>{item.confidence >= PROOF_CONFIDENCE && <Badge size="xs" color="red" variant="light">proof · does not fade</Badge>}</Group><Code>{item.evidenceKey}</Code></div><Text size="sm">+{item.weight} risk · {item.confidence}% confidence · {dayjs(item.occurredAt).format('MM-DD HH:mm:ss')}</Text></Group>
        <Text size="xs" c="dimmed">Team {item.teamId ?? '—'}{item.relatedTeamId ? ` ↔ ${item.relatedTeamId}` : ''} · Challenge {item.challengeId ?? '—'} · Submission {item.submissionId ?? '—'} · Alert {item.alertId ?? 'grouped'}</Text>
        {item.fingerprint && <Code>{item.fingerprint}</Code>}
        {item.metadataJson && <Code block>{item.metadataJson}</Code>}
      </Stack>)}
    </Stack>}
  </Modal>
}

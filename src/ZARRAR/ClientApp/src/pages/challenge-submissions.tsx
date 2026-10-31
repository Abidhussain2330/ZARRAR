import { Alert, Badge, Button, Container, FileInput, Group, Paper, Stack, Table, Text, TextInput, Textarea, Title } from '@mantine/core'
import { mdiAlertCircleOutline, mdiCloudUploadOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, useCallback, useEffect, useState } from 'react'
import { useUser } from '@Hooks/useUser'

type Submission = {
  id: string
  title: string
  description: string
  packageName: string
  packageSize: number
  packageHash: string
  submittedUtc: string
  reviewedUtc?: string
  status: number
  reviewNote: string
  developerName?: string
}

const statusLabel = (status: number) => (status === 1 ? 'Approved' : status === 2 ? 'Rejected' : 'Pending')
const statusColor = (status: number) => (status === 1 ? 'green' : status === 2 ? 'red' : 'yellow')

const ChallengeSubmissions: FC = () => {
  const { user } = useUser()
  const [items, setItems] = useState<Submission[]>([])
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [packageFile, setPackageFile] = useState<File | null>(null)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)

  const load = useCallback(async () => {
    const endpoint = user?.role === 'Admin' ? '/api/admin/challenge-submissions' : '/api/challenge-submissions/mine'
    const response = await fetch(endpoint, { credentials: 'include' })
    if (!response.ok) throw new Error('Could not load challenge submissions.')
    setItems(await response.json())
  }, [user?.role])

  useEffect(() => {
    load().catch((error) => setMessage(error.message))
  }, [load])

  const submit = async () => {
    if (!title.trim() || !packageFile) return
    setBusy(true)
    setMessage(null)
    try {
      const body = new FormData()
      body.append('title', title.trim())
      body.append('description', description.trim())
      body.append('package', packageFile)
      const response = await fetch('/api/challenge-submissions', { method: 'POST', body, credentials: 'include' })
      if (!response.ok) throw new Error('The package could not be submitted.')
      setTitle('')
      setDescription('')
      setPackageFile(null)
      await load()
      setMessage('Package submitted for administrator review.')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'The package could not be submitted.')
    } finally {
      setBusy(false)
    }
  }

  const review = async (id: string, status: number) => {
    setBusy(true)
    try {
      const response = await fetch(`/api/admin/challenge-submissions/${id}/review`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ status }),
      })
      if (!response.ok) throw new Error('Review action failed.')
      await load()
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Review action failed.')
    } finally {
      setBusy(false)
    }
  }

  const isAdmin = user?.role === 'Admin'
  return (
    <Container size="lg" py="xl">
      <Stack gap="lg">
        <div>
          <Title order={2}>Challenge package submissions</Title>
          <Text c="dimmed">Upload a Docker challenge package for ZARRAR administrators to review.</Text>
        </div>
        {message && <Alert icon={<Icon path={mdiAlertCircleOutline} size={0.9} />}>{message}</Alert>}
        {!isAdmin && (
          <Paper withBorder p="lg">
            <Stack>
              <TextInput label="Challenge title" value={title} onChange={(event) => setTitle(event.currentTarget.value)} maxLength={128} required />
              <Textarea label="Description" value={description} onChange={(event) => setDescription(event.currentTarget.value)} maxLength={4000} />
              <FileInput label="Package" description="ZIP, TAR, or TAR.GZ; maximum 256 MB" value={packageFile} onChange={setPackageFile} required />
              <Group justify="flex-end">
                <Button leftSection={<Icon path={mdiCloudUploadOutline} size={0.9} />} loading={busy} disabled={!title.trim() || !packageFile} onClick={submit}>
                  Submit for review
                </Button>
              </Group>
            </Stack>
          </Paper>
        )}
        <Paper withBorder p="lg">
          <Table striped highlightOnHover withTableBorder>
            <Table.Thead><Table.Tr><Table.Th>Challenge</Table.Th>{isAdmin && <Table.Th>Developer</Table.Th>}<Table.Th>Package</Table.Th><Table.Th>Status</Table.Th><Table.Th>Submitted</Table.Th>{isAdmin && <Table.Th />}</Table.Tr></Table.Thead>
            <Table.Tbody>
              {items.map((item) => <Table.Tr key={item.id}>
                <Table.Td><Text fw={600}>{item.title}</Text>{item.description && <Text size="xs" c="dimmed">{item.description}</Text>}</Table.Td>
                {isAdmin && <Table.Td>{item.developerName ?? '—'}</Table.Td>}
                <Table.Td>{item.packageName}<Text size="xs" c="dimmed">{(item.packageSize / 1024 / 1024).toFixed(2)} MB</Text></Table.Td>
                <Table.Td><Badge color={statusColor(item.status)}>{statusLabel(item.status)}</Badge>{item.reviewNote && <Text size="xs" c="dimmed">{item.reviewNote}</Text>}</Table.Td>
                <Table.Td>{dayjs(item.submittedUtc).format('YYYY-MM-DD HH:mm')}</Table.Td>
                {isAdmin && <Table.Td><Group gap="xs"><Button size="xs" color="green" disabled={busy || item.status === 1} onClick={() => review(item.id, 1)}>Approve</Button><Button size="xs" color="red" variant="light" disabled={busy || item.status === 2} onClick={() => review(item.id, 2)}>Reject</Button></Group></Table.Td>}
              </Table.Tr>)}
            </Table.Tbody>
          </Table>
          {items.length === 0 && <Text c="dimmed" ta="center" py="lg">No submissions yet.</Text>}
        </Paper>
      </Stack>
    </Container>
  )
}

export default ChallengeSubmissions

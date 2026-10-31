import { Button, Modal, ModalProps, Select, Stack, Textarea, TextInput } from '@mantine/core'
import { FormEvent, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import api, { AdminTeamCreateModel, TeamInfoModel, UserInfoModel } from '@Api'
import { showAdminSuccess } from '@Components/admin/shell'
import { showErrorMsg } from '@Utils/Shared'

interface TeamCreateModalProps extends ModalProps {
  onCreated: (team: TeamInfoModel) => void
}

const emptyTeam = (): AdminTeamCreateModel => ({
  name: '',
  bio: '',
  captainId: '',
})

export const TeamCreateModal = ({ onCreated, ...modalProps }: TeamCreateModalProps) => {
  const { t } = useTranslation()
  const [team, setTeam] = useState<AdminTeamCreateModel>(emptyTeam)
  const [captainQuery, setCaptainQuery] = useState('')
  const [captains, setCaptains] = useState<UserInfoModel[]>([])
  const [searching, setSearching] = useState(false)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (!modalProps.opened || captainQuery.trim().length < 2) {
      setCaptains([])
      setSearching(false)
      return
    }

    let cancelled = false
    const timeout = window.setTimeout(async () => {
      setSearching(true)
      try {
        const response = await api.admin.adminSearchUsers({ hint: captainQuery.trim() })
        if (!cancelled) setCaptains(response.data.data ?? [])
      } catch (error) {
        if (!cancelled) {
          setCaptains([])
          showErrorMsg(error, t)
        }
      } finally {
        if (!cancelled) setSearching(false)
      }
    }, 250)

    return () => {
      cancelled = true
      window.clearTimeout(timeout)
    }
  }, [captainQuery, modalProps.opened, t])

  const close = () => {
    if (busy) return
    setTeam(emptyTeam())
    setCaptainQuery('')
    setCaptains([])
    modalProps.onClose()
  }

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!team.name.trim() || !team.captainId) return

    setBusy(true)
    try {
      const response = await api.admin.adminCreateTeam({
        name: team.name.trim(),
        bio: team.bio?.trim() || null,
        captainId: team.captainId,
      })
      showAdminSuccess(t('admin.notification.teams.created', { defaultValue: 'Team created successfully' }))
      setTeam(emptyTeam())
      setCaptainQuery('')
      setCaptains([])
      modalProps.onClose()
      onCreated(response.data)
    } catch (error) {
      showErrorMsg(error, t)
    } finally {
      setBusy(false)
    }
  }

  const captainOptions = captains
    .filter((user) => user.id && user.userName)
    .map((user) => ({
      value: user.id!,
      label: user.email ? `${user.userName} — ${user.email}` : user.userName!,
    }))

  return (
    <Modal
      {...modalProps}
      onClose={close}
      title={t('admin.button.teams.create', { defaultValue: 'New team' })}
      closeOnClickOutside={!busy}
      closeOnEscape={!busy}
    >
      <form onSubmit={submit}>
        <Stack gap="md">
          <TextInput
            required
            label={t('team.label.name')}
            maxLength={20}
            value={team.name}
            disabled={busy}
            autoComplete="off"
            onChange={(event) => setTeam({ ...team, name: event.currentTarget.value })}
          />
          <Textarea
            label={t('team.label.bio')}
            maxLength={72}
            value={team.bio ?? ''}
            disabled={busy}
            autosize
            minRows={2}
            maxRows={4}
            onChange={(event) => setTeam({ ...team, bio: event.currentTarget.value })}
          />
          <Select
            required
            searchable
            label={t('team.label.captain', { defaultValue: 'Captain' })}
            description={t('admin.content.teams.captain_hint', {
              defaultValue: 'Search for an existing user; the selected user becomes the first team member.',
            })}
            placeholder={t('admin.placeholder.users.search', { defaultValue: 'Type at least two characters' })}
            searchValue={captainQuery}
            onSearchChange={setCaptainQuery}
            value={team.captainId}
            onChange={(captainId) => setTeam({ ...team, captainId: captainId ?? '' })}
            data={captainOptions}
            disabled={busy}
            nothingFoundMessage={
              captainQuery.trim().length < 2
                ? t('admin.content.teams.captain_search_prompt', { defaultValue: 'Type at least two characters.' })
                : searching
                  ? t('common.state.loading', { defaultValue: 'Loading…' })
                  : t('admin.state.no_results', { defaultValue: 'No matching users' })
            }
          />
          <Button type="submit" loading={busy} disabled={!team.name.trim() || !team.captainId}>
            {t('admin.button.teams.create', { defaultValue: 'Create team' })}
          </Button>
        </Stack>
      </form>
    </Modal>
  )
}

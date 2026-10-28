import { Button, Modal, ModalProps, PasswordInput, SimpleGrid, Stack, TextInput } from '@mantine/core'
import { FormEvent, useState } from 'react'
import { useTranslation } from 'react-i18next'
import api, { UserCreateModel } from '@Api'
import { showAdminSuccess } from '@Components/admin/shell'
import { showErrorMsg } from '@Utils/Shared'

interface UserCreateModalProps extends ModalProps {
  onCreated: () => void
}

const emptyUser = (): UserCreateModel => ({
  userName: '',
  password: '',
  email: '',
  realName: '',
  stdNumber: '',
  phone: '',
  teamName: '',
})

export const UserCreateModal = ({ onCreated, ...modalProps }: UserCreateModalProps) => {
  const { t } = useTranslation()
  const [user, setUser] = useState<UserCreateModel>(emptyUser)
  const [busy, setBusy] = useState(false)

  const close = () => {
    if (busy) return
    setUser(emptyUser())
    modalProps.onClose()
  }

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setBusy(true)
    try {
      await api.admin.adminAddUsers([{
        ...user,
        userName: user.userName.trim(),
        email: user.email.trim(),
        realName: user.realName?.trim() || null,
        stdNumber: user.stdNumber?.trim() || null,
        phone: user.phone?.trim() || null,
        teamName: user.teamName?.trim() || null,
      }])
      showAdminSuccess(t('admin.notification.users.created', { defaultValue: 'User created successfully' }))
      setUser(emptyUser())
      modalProps.onClose()
      onCreated()
    } catch (error) {
      showErrorMsg(error, t)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      {...modalProps}
      onClose={close}
      title={t('admin.button.users.create', { defaultValue: 'Create user' })}
      closeOnClickOutside={!busy}
      closeOnEscape={!busy}
    >
      <form onSubmit={submit}>
        <Stack gap="md">
          <TextInput
            required
            minLength={3}
            maxLength={40}
            label={t('account.label.username')}
            value={user.userName}
            disabled={busy}
            autoComplete="off"
            onChange={(event) => setUser({ ...user, userName: event.currentTarget.value })}
          />
          <SimpleGrid cols={{ base: 1, xs: 2 }}>
            <TextInput
              required
              type="email"
              label={t('account.label.email')}
              value={user.email}
              disabled={busy}
              autoComplete="off"
              onChange={(event) => setUser({ ...user, email: event.currentTarget.value })}
            />
            <PasswordInput
              required
              minLength={10}
              maxLength={128}
              label={t('account.label.password')}
              description={t('admin.content.users.password_requirement', { defaultValue: 'At least 10 characters' })}
              value={user.password}
              disabled={busy}
              autoComplete="new-password"
              onChange={(event) => setUser({ ...user, password: event.currentTarget.value })}
            />
            <TextInput
              label={t('account.label.real_name')}
              maxLength={128}
              value={user.realName ?? ''}
              disabled={busy}
              onChange={(event) => setUser({ ...user, realName: event.currentTarget.value })}
            />
            <TextInput
              label={t('account.label.student_id')}
              maxLength={64}
              value={user.stdNumber ?? ''}
              disabled={busy}
              onChange={(event) => setUser({ ...user, stdNumber: event.currentTarget.value })}
            />
            <TextInput
              type="tel"
              label={t('account.label.phone')}
              value={user.phone ?? ''}
              disabled={busy}
              onChange={(event) => setUser({ ...user, phone: event.currentTarget.value })}
            />
            <TextInput
              label={t('admin.label.users.team', { defaultValue: 'Team' })}
              description={t('admin.content.users.team_optional', { defaultValue: 'Optional; creates the team if needed' })}
              maxLength={20}
              value={user.teamName ?? ''}
              disabled={busy}
              onChange={(event) => setUser({ ...user, teamName: event.currentTarget.value })}
            />
          </SimpleGrid>
          <Button type="submit" loading={busy}>
            {t('admin.button.users.create', { defaultValue: 'Create user' })}
          </Button>
        </Stack>
      </form>
    </Modal>
  )
}

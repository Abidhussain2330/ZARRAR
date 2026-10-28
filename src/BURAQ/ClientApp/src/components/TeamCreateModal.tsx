import { Button, Group, Modal, ModalProps, Text, Textarea, TextInput } from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiAccountMultiplePlus, mdiAlertCircleOutline, mdiCheck, mdiCloseCircleOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, FormEvent, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { EmptyState } from '@Components/buraq'
import { tryGetErrorMsg } from '@Utils/Shared'
import api, { TeamUpdateModel } from '@Api'
import styles from '@Styles/components/TeamCreateModal.module.css'

interface TeamCreateModalProps extends ModalProps {
  disallowCreate: boolean
  mutate: () => unknown | Promise<unknown>
}

export const TeamCreateModal: FC<TeamCreateModalProps> = ({ disallowCreate, mutate, onClose, ...modalProps }) => {
  const [createTeam, setCreateTeam] = useState<TeamUpdateModel>({ name: '', bio: '' })
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const { t } = useTranslation()

  const clear = () => {
    setCreateTeam({ name: '', bio: '' })
    setError(null)
  }

  const updateField = (field: keyof TeamUpdateModel, value: string) => {
    setCreateTeam((current) => ({ ...current, [field]: value }))
  }

  const close = () => {
    if (pending) return
    clear()
    onClose()
  }

  const onCreateTeam = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const name = createTeam.name?.trim() ?? ''

    if (!name) {
      setError('Enter a team name before creating the team.')
      return
    }

    setPending(true)
    setError(null)

    try {
      const response = await api.team.teamCreateTeam({ name, bio: createTeam.bio?.trim() ?? '' })
      showNotification({
        color: 'teal',
        title: t('team.notification.create.success.title'),
        message: t('team.notification.create.success.message', { team: response.data.name }),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      await mutate()
      clear()
      onClose()
    } catch (createError) {
      setError(tryGetErrorMsg(createError, t))
    } finally {
      setPending(false)
    }
  }

  return (
    <Modal
      {...modalProps}
      onClose={close}
      centered
      classNames={{ content: styles.content, header: styles.header }}
    >
      {disallowCreate ? (
        <EmptyState
          icon={<Icon path={mdiCloseCircleOutline} size={1.8} />}
          title={t('team.content.disallow_create.title')}
          description="You already captain the maximum number of teams. Disband or transfer one before creating another."
          action={
            <Button variant="default" onClick={close}>
              {t('common.modal.cancel')}
            </Button>
          }
        />
      ) : (
        <form className={styles.form} onSubmit={onCreateTeam}>
          <div className={styles.intro}>
            <span className={styles.icon} aria-hidden>
              <Icon path={mdiAccountMultiplePlus} size={1.1} />
            </span>
            <Text size="sm">Create a team, then use its private invitation code to add members.</Text>
          </div>

          <TextInput
            required
            label={t('team.label.name')}
            description={`${createTeam.name?.length ?? 0}/20`}
            maxLength={20}
            autoFocus
            value={createTeam.name ?? ''}
            disabled={pending}
            onChange={(event) => updateField('name', event.currentTarget.value)}
          />
          <Textarea
            label={t('team.label.bio')}
            placeholder={t('team.placeholder.bio')}
            description={`${createTeam.bio?.length ?? 0}/72`}
            maxLength={72}
            minRows={3}
            maxRows={5}
            autosize
            value={createTeam.bio ?? ''}
            disabled={pending}
            onChange={(event) => updateField('bio', event.currentTarget.value)}
          />

          {error && (
            <div className={styles.error} role="alert">
              <Icon path={mdiAlertCircleOutline} size={0.78} aria-hidden />
              {error}
            </div>
          )}

          <Group justify="flex-end" wrap="wrap" className={styles.actions}>
            <Button variant="default" disabled={pending} onClick={close}>
              {t('common.modal.cancel')}
            </Button>
            <Button type="submit" loading={pending} disabled={!createTeam.name?.trim() || pending}>
              {t('team.button.create')}
            </Button>
          </Group>
        </form>
      )}
    </Modal>
  )
}

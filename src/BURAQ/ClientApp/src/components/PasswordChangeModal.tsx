import { Button, Group, Modal, ModalProps, PasswordInput, Stack } from '@mantine/core'
import { useInputState } from '@mantine/hooks'
import { showNotification } from '@mantine/notifications'
import { mdiAlertCircleOutline, mdiCheck } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, FormEvent, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { StrengthPasswordInput } from '@Components/StrengthPasswordInput'
import { encryptApiData } from '@Utils/Crypto'
import { tryGetErrorMsg } from '@Utils/Shared'
import { useConfig } from '@Hooks/useConfig'
import api from '@Api'
import styles from '@Styles/components/PasswordChangeModal.module.css'

export const PasswordChangeModal: FC<ModalProps> = ({ onClose, ...modalProps }) => {
  const [oldPwd, setOldPwd] = useInputState('')
  const [pwd, setPwd] = useInputState('')
  const [retypedPwd, setRetypedPwd] = useInputState('')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const navigate = useNavigate()
  const { t } = useTranslation()
  const { config } = useConfig()

  const clearSecrets = () => {
    setOldPwd('')
    setPwd('')
    setRetypedPwd('')
    setError(null)
  }

  const close = () => {
    if (pending) return
    clearSecrets()
    onClose()
  }

  const onChangePwd = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setError(null)

    if (!oldPwd || !pwd || !retypedPwd) {
      setError(t('account.password.empty'))
      return
    }

    if (pwd !== retypedPwd) {
      setError(t('account.password.not_match'))
      return
    }

    setPending(true)

    try {
      await api.account.accountChangePassword({
        old: await encryptApiData(t, oldPwd, config.apiPublicKey),
        new: await encryptApiData(t, pwd, config.apiPublicKey),
      })
      showNotification({
        color: 'teal',
        message: t('account.notification.profile.password_updated'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      clearSecrets()
      onClose()
      await api.account.accountLogOut()
      navigate('/account/login', { replace: true })
    } catch (changeError) {
      setError(tryGetErrorMsg(changeError, t))
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
      <form className={styles.form} onSubmit={onChangePwd}>
        <Stack gap="md">
          <PasswordInput
            required
            label={t('account.label.password_old')}
            autoComplete="current-password"
            value={oldPwd}
            disabled={pending}
            onChange={setOldPwd}
          />
          <StrengthPasswordInput value={pwd} onChange={setPwd} disabled={pending} />
          <PasswordInput
            required
            label={t('account.label.password_retype')}
            autoComplete="new-password"
            value={retypedPwd}
            disabled={pending}
            onChange={setRetypedPwd}
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
            <Button
              type="submit"
              loading={pending}
              disabled={!oldPwd || !pwd || !retypedPwd || pending}
            >
              {t('common.modal.confirm_update')}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  )
}

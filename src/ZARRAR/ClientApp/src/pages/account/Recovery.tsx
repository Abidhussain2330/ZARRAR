import { useInputState } from '@mantine/hooks'
import { showNotification, updateNotification } from '@mantine/notifications'
import { mdiCheck, mdiClose } from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { Captcha, useCaptchaRef } from '@Components/Captcha'
import { GateField, GateHeading, GateShell, gateClasses as classes } from '@Components/player/GateShell'
import { tryGetErrorMsg } from '@Utils/Shared'
import { usePageTitle } from '@Hooks/usePageTitle'
import api from '@Api'

const Recovery: FC = () => {
  const [email, setEmail] = useInputState('')
  const [disabled, setDisabled] = useState(false)
  const [sent, setSent] = useState(false)
  const [error, setError] = useState<string>()
  const { captchaRef, getToken, cleanUp } = useCaptchaRef()

  const { t } = useTranslation()

  usePageTitle(t('account.title.recovery'))

  const onRecovery = async (event: React.SyntheticEvent) => {
    event.preventDefault()

    if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email)) {
      setError('Enter the email address on your account.')
      return
    }

    const { valid, token } = await getToken()

    if (!valid) {
      showNotification({
        color: 'orange',
        title: t('account.notification.captcha.not_valid'),
        message: t('common.error.try_later'),
        loading: true,
      })
      return
    }

    setDisabled(true)

    showNotification({
      color: 'orange',
      id: 'recovery-status',
      title: t('account.notification.captcha.request_sent.title'),
      message: t('account.notification.captcha.request_sent.message'),
      loading: true,
      autoClose: false,
    })

    try {
      await api.account.accountRecovery({ email, challenge: token })

      updateNotification({
        id: 'recovery-status',
        color: 'teal',
        title: t('common.email.sent.title'),
        message: t('common.email.sent.message'),
        icon: <Icon path={mdiCheck} size={1} />,
        loading: false,
        autoClose: true,
      })
      cleanUp(true)
      setSent(true)
    } catch (err: unknown) {
      updateNotification({
        id: 'recovery-status',
        color: 'red',
        title: t('common.error.encountered'),
        message: tryGetErrorMsg(err, t),
        icon: <Icon path={mdiClose} size={1} />,
        loading: false,
        autoClose: true,
      })
      cleanUp(false)
    } finally {
      setDisabled(false)
    }
  }

  if (sent) {
    return (
      <GateShell slim backTo="/account/login" backLabel="Back to sign in">
        <div className={`${classes.statusIcon} ${classes.statusOk}`} aria-hidden="true">
          ✓
        </div>
        <GateHeading
          title="Check your inbox"
          lede={`If an account uses ${email}, a reset link is on its way. The link expires shortly, so use it soon.`}
        />
        <Link className={`${classes.link} bqFocusRing`} to="/account/login">
          Back to sign in
        </Link>
      </GateShell>
    )
  }

  return (
    <GateShell slim backTo="/account/login" backLabel="Back to sign in">
      <GateHeading title="Reset your password" lede="We'll email you a link to set a new one." />

      <form className={classes.form} onSubmit={onRecovery} noValidate>
        <GateField
          label="Email"
          glyph="@"
          type="email"
          autoComplete="email"
          placeholder="you@example.com"
          value={email}
          error={error}
          disabled={disabled}
          onChange={(e) => {
            setEmail(e.currentTarget.value)
            setError(undefined)
          }}
        />

        <div className={classes.captchaSlot}>
          <Captcha action="recovery" ref={captchaRef} />
        </div>

        <button className={`${classes.submit} bqFocusRing`} type="submit" disabled={disabled}>
          {disabled ? 'SENDING…' : 'SEND RESET LINK'}
        </button>
      </form>
    </GateShell>
  )
}

export default Recovery

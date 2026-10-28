import { useInputState } from '@mantine/hooks'
import { showNotification } from '@mantine/notifications'
import { mdiCheck, mdiClose } from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation, useNavigate } from 'react-router'
import {
  GateField,
  GateHeading,
  GateShell,
  StrengthMeter,
  gateClasses as classes,
} from '@Components/player/GateShell'
import { encryptApiData } from '@Utils/Crypto'
import { showErrorMsg } from '@Utils/Shared'
import { useConfig } from '@Hooks/useConfig'
import { usePageTitle } from '@Hooks/usePageTitle'
import api from '@Api'

const Reset: FC = () => {
  const location = useLocation()
  const sp = new URLSearchParams(location.search)
  const token = sp.get('token')
  const email = sp.get('email')
  const navigate = useNavigate()

  const [pwd, setPwd] = useInputState('')
  const [retypedPwd, setRetypedPwd] = useInputState('')
  const [disabled, setDisabled] = useState(false)
  const [errors, setErrors] = useState<{ pwd?: string; retyped?: string }>({})

  const { t } = useTranslation()
  const { config } = useConfig()

  usePageTitle(t('account.title.reset'))

  const onReset = async (event: React.SyntheticEvent) => {
    event.preventDefault()

    const next: { pwd?: string; retyped?: string } = {}
    if (pwd.length < 10) next.pwd = 'Use at least 10 characters.'
    if (pwd !== retypedPwd) next.retyped = t('account.password.not_match')
    setErrors(next)
    if (Object.keys(next).length > 0) return

    if (!(token && email)) {
      showNotification({
        color: 'red',
        message: t('common.error.param_error'),
        icon: <Icon path={mdiClose} size={1} />,
      })
      return
    }

    setDisabled(true)

    try {
      await api.account.accountPasswordReset({
        rToken: token,
        email: email,
        password: await encryptApiData(t, pwd, config.apiPublicKey),
      })
      showNotification({
        color: 'teal',
        title: t('account.notification.reset.success.title'),
        message: t('account.notification.reset.success.message'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      navigate('/account/login')
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  // A missing token means the link was mistyped or truncated by a mail client.
  // Say so here rather than letting the player fill the form and fail on submit.
  if (!token || !email) {
    return (
      <GateShell slim backTo="/account/login" backLabel="Back to sign in">
        <div className={`${classes.statusIcon} ${classes.statusBad}`} aria-hidden="true">
          !
        </div>
        <GateHeading title={t('account.content.link_invalid')} lede={t('account.content.link_check')} />
        <Link className={`${classes.link} bqFocusRing`} to="/account/recovery">
          Request a new reset link
        </Link>
      </GateShell>
    )
  }

  return (
    <GateShell slim backTo="/account/login" backLabel="Back to sign in">
      <GateHeading title="Set a new password" lede="Choose something you haven't used here before." />

      <form className={classes.form} onSubmit={onReset} noValidate>
        <GateField
          label={t('account.label.password')}
          glyph="▭"
          revealable
          autoComplete="new-password"
          placeholder="New password"
          value={pwd}
          error={errors.pwd}
          disabled={disabled}
          onChange={(e) => {
            setPwd(e.currentTarget.value)
            setErrors((f) => ({ ...f, pwd: undefined }))
          }}
        />
        <StrengthMeter password={pwd} />

        <GateField
          label={t('account.label.password_retype')}
          glyph="▭"
          revealable
          autoComplete="new-password"
          placeholder="Repeat new password"
          value={retypedPwd}
          error={errors.retyped}
          disabled={disabled}
          onChange={(e) => {
            setRetypedPwd(e.currentTarget.value)
            setErrors((f) => ({ ...f, retyped: undefined }))
          }}
        />

        <button className={`${classes.submit} bqFocusRing`} type="submit" disabled={disabled}>
          {disabled ? 'SAVING…' : 'SAVE NEW PASSWORD'}
        </button>
      </form>
    </GateShell>
  )
}

export default Reset

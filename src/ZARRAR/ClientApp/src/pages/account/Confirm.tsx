import { showNotification } from '@mantine/notifications'
import { mdiCheck, mdiClose } from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation, useNavigate } from 'react-router'
import { GateHeading, GateShell, gateClasses as classes } from '@Components/player/GateShell'
import { usePageTitle } from '@Hooks/usePageTitle'
import api from '@Api'

const safeDecode = (value: string | null) => {
  if (!value) return null
  try {
    return window.atob(value)
  } catch {
    return null
  }
}

const Confirm: FC = () => {
  const navigate = useNavigate()
  const location = useLocation()
  const sp = new URLSearchParams(location.search)
  const token = sp.get('token')
  const email = sp.get('email')
  const decoded = safeDecode(email)
  const [disabled, setDisabled] = useState(false)
  const { t } = useTranslation()

  usePageTitle(t('account.title.confirm'))

  const verify = async (event: React.SyntheticEvent) => {
    event.preventDefault()

    if (!token || !email) {
      showNotification({
        color: 'red',
        title: t('account.notification.confirm.failed'),
        message: t('common.error.param_missing'),
        icon: <Icon path={mdiClose} size={1} />,
      })
      return
    }

    setDisabled(true)

    try {
      await api.account.accountMailChangeConfirm({ token, email })
      showNotification({
        color: 'teal',
        title: t('account.notification.confirm.success'),
        message: decoded ?? '',
        icon: <Icon path={mdiCheck} size={1} />,
      })
      navigate('/posts')
    } catch {
      showNotification({
        color: 'red',
        title: t('account.notification.confirm.failed'),
        message: t('common.error.param_error'),
        icon: <Icon path={mdiClose} size={1} />,
      })
    } finally {
      setDisabled(false)
    }
  }

  if (!token || !decoded) {
    return (
      <GateShell slim backTo="/account/login" backLabel="Back to sign in">
        <div className={`${classes.statusIcon} ${classes.statusBad}`} aria-hidden="true">
          !
        </div>
        <GateHeading title={t('account.content.link_invalid')} lede={t('account.content.link_check')} />
        <Link className={`${classes.link} bqFocusRing`} to="/account/settings">
          Back to settings
        </Link>
      </GateShell>
    )
  }

  return (
    <GateShell slim>
      <div className={`${classes.statusIcon} ${classes.statusOk}`} aria-hidden="true">
        ✓
      </div>
      <GateHeading
        title={t('account.content.welcome', { decodeEmail: decoded })}
        lede={t('account.content.confirm.message')}
      />
      <form className={classes.form} onSubmit={verify}>
        <button className={`${classes.submit} bqFocusRing`} type="submit" disabled={disabled}>
          {disabled ? 'CONFIRMING…' : 'CONFIRM EMAIL'}
        </button>
      </form>
    </GateShell>
  )
}

export default Confirm

import { useInputState } from '@mantine/hooks'
import { showNotification, updateNotification } from '@mantine/notifications'
import { mdiCheck, mdiClose } from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { Captcha, useCaptchaRef } from '@Components/Captcha'
import { AccessGranted, prefersSkipIntro } from '@Components/player/AccessGranted'
import { GateCheckbox, GateField, GateHeading, GateShell, gateClasses as classes } from '@Components/player/GateShell'
import { RequireRole } from '@Components/WithRole'
import { encryptApiData } from '@Utils/Crypto'
import { safeRedirect } from '@Utils/SafeRedirect'
import { tryGetClientError } from '@Utils/Shared'
import { useConfig } from '@Hooks/useConfig'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useUser } from '@Hooks/useUser'
import api, { Role } from '@Api'

/** Where a player lands after signing in, unless they were bounced here from somewhere else. */
const HOME = '/posts'
/** Where an admin lands after signing in, unless they were bounced here from somewhere else. */
const ADMIN_HOME = '/admin'
/** Where a ZARRAR monitor lands: the showdown event picker. */
const MONITOR_HOME = '/monitor'

/** The server answers a correct password on a two-step account with this 401 title. */
const needsTwoFactor = (err: unknown) => {
  const ax = err as { response?: { status?: number; data?: { title?: string } } } | undefined
  return ax?.response?.status === 401 && /two-step/i.test(ax.response?.data?.title ?? '')
}

const Login: FC = () => {
  const params = useSearchParams()[0]
  const navigate = useNavigate()

  const [pwd, setPwd] = useInputState('')
  const [uname, setUname] = useInputState('')
  // Present because the approved mockup has it. It does not currently reach the backend —
  // session lifetime is set by the auth cookie the server issues — so it is UI-only for now.
  const [remember, setRemember] = useState(true)
  // Two-step login (S3): shown after the server says the password was right but a code is needed.
  const [needCode, setNeedCode] = useState(false)
  const [code, setCode] = useState('')
  const [disabled, setDisabled] = useState(false)
  const [fieldErrors, setFieldErrors] = useState<{ uname?: string; pwd?: string }>({})

  // Set once the backend confirms the session. Drives the interstitial, or an
  // immediate redirect when the player has opted out of it.
  const [authenticated, setAuthenticated] = useState(false)
  const [introDone, setIntroDone] = useState(false)

  const { captchaRef, getToken, cleanUp } = useCaptchaRef()
  const { user, mutate } = useUser()
  const { config } = useConfig()

  const { t } = useTranslation()

  usePageTitle(t('account.title.login'))

  // `from` always wins when present. Otherwise land admins in the admin command
  // center and everyone else in the player experience — but only once the
  // profile has actually loaded, since `user` is undefined on the first render.
  const destination = safeRedirect(
    params.get('from'),
    RequireRole(Role.Admin, user?.role) ? ADMIN_HOME : RequireRole(Role.Monitor, user?.role) ? MONITOR_HOME : HOME
  )

  // Redirect only once the profile has actually loaded, so the arena never renders
  // before useUser has a user to hand it.
  useEffect(() => {
    if (authenticated && introDone && user) {
      navigate(destination, { replace: true })
    }
  }, [authenticated, introDone, user, destination, navigate])

  const validate = () => {
    const next: { uname?: string; pwd?: string } = {}
    if (uname.trim().length === 0) next.uname = 'Enter your username or email.'
    if (pwd.length < 6) next.pwd = 'Your password is at least 6 characters.'
    setFieldErrors(next)
    return Object.keys(next).length === 0
  }

  const onLogin = async (event: React.SyntheticEvent) => {
    event.preventDefault()

    if (!validate()) return

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
      id: 'login-status',
      title: t('account.notification.captcha.request_sent.title'),
      message: t('account.notification.captcha.request_sent.message'),
      loading: true,
      autoClose: false,
    })

    try {
      await api.account.accountLogIn({
        userName: uname,
        password: await encryptApiData(t, pwd, config.apiPublicKey),
        challenge: token,
        twoFactorCode: needCode ? code.trim() : undefined,
      } as Parameters<typeof api.account.accountLogIn>[0])

      updateNotification({
        id: 'login-status',
        color: 'teal',
        title: t('account.notification.login.success.title'),
        message: t('account.notification.login.success.message'),
        icon: <Icon path={mdiCheck} size={1} />,
        autoClose: true,
        loading: false,
      })

      cleanUp(true)
      mutate()

      // Opting out of the clip skips it here rather than rendering and immediately
      // tearing it down, so no part of the 1.2 MB file is fetched.
      setIntroDone(prefersSkipIntro())
      setAuthenticated(true)
    } catch (err: unknown) {
      if (needsTwoFactor(err)) {
        const first = !needCode
        setNeedCode(true)
        updateNotification({
          id: 'login-status',
          color: first ? 'orange' : 'red',
          title: first ? 'Two-step login' : 'Code not accepted',
          message: first
            ? 'Enter the 6-digit code from your authenticator app (or a recovery code).'
            : 'That code did not work. Check the time on your phone and try again.',
          autoClose: true,
          loading: false,
        })
        cleanUp(false)
        return
      }

      // No response, or a 5xx from the dev proxy, means the backend never answered — say so
      // plainly instead of a generic error, since the page itself still loads from Vite.
      const ax = err as { isAxiosError?: boolean; response?: { status?: number } } | undefined
      const unreachable = ax?.isAxiosError === true && (!ax.response || (ax.response.status ?? 0) >= 500)
      const { title, message } = unreachable
        ? {
            title: 'ZARRAR server unreachable',
            message: 'Sign-in got no valid answer from the backend. Check that the server is running and look at its console.',
          }
        : tryGetClientError(err, t)
      updateNotification({
        id: 'login-status',
        color: 'red',
        title,
        message,
        icon: <Icon path={mdiClose} size={1} />,
        autoClose: true,
        loading: false,
      })
      cleanUp(false)
    } finally {
      setDisabled(false)
    }
  }

  if (authenticated && !introDone) {
    return <AccessGranted onDone={() => setIntroDone(true)} />
  }

  return (
    <GateShell backTo="/" backLabel="Back to ZARRAR">
      <GateHeading title="Welcome back" lede="Sign in to your ZARRAR account." />

      <form className={classes.form} onSubmit={onLogin} noValidate>
        <GateField
          label="Email or username"
          glyph="●"
          autoComplete="username"
          placeholder="operator@zarrar.io"
          value={uname}
          error={fieldErrors.uname}
          disabled={disabled}
          onChange={(e) => {
            setUname(e.currentTarget.value)
            if (fieldErrors.uname) setFieldErrors((f) => ({ ...f, uname: undefined }))
          }}
        />

        <GateField
          label="Password"
          glyph="▭"
          revealable
          autoComplete="current-password"
          placeholder="••••••••••••"
          value={pwd}
          error={fieldErrors.pwd}
          disabled={disabled}
          onChange={(e) => {
            setPwd(e.currentTarget.value)
            if (fieldErrors.pwd) setFieldErrors((f) => ({ ...f, pwd: undefined }))
          }}
        />

        {needCode && (
          <GateField
            label="Two-step code"
            glyph="◆"
            autoComplete="one-time-code"
            inputMode="numeric"
            placeholder="123 456"
            value={code}
            disabled={disabled}
            autoFocus
            onChange={(e) => setCode(e.currentTarget.value)}
          />
        )}

        <div className={classes.rowBetween}>
          <GateCheckbox checked={remember} onToggle={() => setRemember((v) => !v)}>
            Keep me signed in
          </GateCheckbox>
          <Link className={`${classes.link} bqFocusRing`} to="/account/recovery">
            {t('account.anchor.recovery')}
          </Link>
        </div>

        <div className={classes.captchaSlot}>
          <Captcha action="login" ref={captchaRef} />
        </div>

        <button className={`${classes.submit} bqFocusRing`} type="submit" disabled={disabled}>
          {disabled ? 'SIGNING IN…' : 'SIGN IN'}
        </button>

        {config.allowRegister !== false && (
          <p className={classes.switchRow}>
            {t('account.anchor.register')}{' '}
            <Link className={`${classes.link} bqFocusRing`} to="/account/register">
              Create an account
            </Link>
          </p>
        )}
      </form>
    </GateShell>
  )
}

export default Login

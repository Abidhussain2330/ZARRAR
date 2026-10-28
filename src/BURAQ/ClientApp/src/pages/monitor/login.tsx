import { FC, FormEvent, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useSearchParams } from 'react-router'
import { Captcha, useCaptchaRef } from '@Components/Captcha'
import { RequireRole } from '@Components/WithRole'
import { encryptApiData } from '@Utils/Crypto'
import { safeRedirect } from '@Utils/SafeRedirect'
import { tryGetClientError } from '@Utils/Shared'
import { useConfig } from '@Hooks/useConfig'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useUser } from '@Hooks/useUser'
import api, { Role } from '@Api'
import classes from '@Styles/zarrar/Monitor.module.css'

/**
 * ZARRAR monitor sign-in. Same account system as players; only Monitor and Admin roles get past
 * the event picker.
 */
const MonitorLogin: FC = () => {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const { config } = useConfig()
  const { user, mutate } = useUser()
  const { captchaRef, getToken, cleanUp } = useCaptchaRef()

  const [uname, setUname] = useState('')
  const [pwd, setPwd] = useState('')
  // Two-step login (S3): asked for after the server accepts the password.
  const [needCode, setNeedCode] = useState(false)
  const [code, setCode] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  usePageTitle('Monitor sign in')

  const destination = safeRedirect(params.get('from'), '/monitor')

  // Already signed in as a monitor: skip the form.
  useEffect(() => {
    if (user && RequireRole(Role.Monitor, user.role)) navigate(destination, { replace: true })
  }, [user, destination, navigate])

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault()
    setError(null)

    if (!uname.trim() || !pwd) {
      setError('Enter the monitor username and password.')
      return
    }

    const { valid, token } = await getToken()
    if (!valid) {
      setError('The security check did not complete. Wait a moment and try again.')
      return
    }

    setBusy(true)
    try {
      await api.account.accountLogIn({
        userName: uname,
        password: await encryptApiData(t, pwd, config.apiPublicKey),
        challenge: token,
        twoFactorCode: needCode ? code.trim() : undefined,
      } as Parameters<typeof api.account.accountLogIn>[0])
      cleanUp(true)

      const profile = await mutate()
      if (profile && !RequireRole(Role.Monitor, profile.role)) {
        setError('Signed in, but this account is not a monitor. Ask an admin to give it the Monitor role.')
        return
      }
      navigate(destination, { replace: true })
    } catch (err: unknown) {
      const ax = err as { response?: { status?: number; data?: { title?: string } } } | undefined
      if (ax?.response?.status === 401 && /two-step/i.test(ax.response?.data?.title ?? '')) {
        setError(needCode ? 'That code did not work. Try the current one from your authenticator.' : null)
        setNeedCode(true)
        cleanUp(false)
        return
      }
      const { title, message } = tryGetClientError(err, t)
      setError(message ? `${title}: ${message}` : title)
      cleanUp(false)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className={classes.page}>
      <div className={classes.bg} style={{ backgroundImage: "url('/assets/zarrar/hacker-bg-red.jpg')" }} />
      <div className={classes.bgTint} />
      <div className={classes.grid} />
      <div className={classes.scan} />

      <main className={classes.content}>
        <img className={classes.logo} src="/assets/zarrar/zarrar-logo.png" alt="ZARRAR — 1v1 Showdown CTF" />
        <div className={classes.role}>MONITOR CONSOLE</div>

        <form className={classes.terminal} onSubmit={onSubmit} noValidate>
          <h1 className={classes.terminalTitle}>Monitor sign in</h1>
          <p className={classes.terminalSub}>Run the big screen for a live showdown.</p>

          {error && <p className={classes.error}>{error}</p>}

          <label className={classes.field}>
            <span className={classes.fieldLabel}>USERNAME OR EMAIL</span>
            <input
              className={classes.input}
              autoComplete="username"
              value={uname}
              disabled={busy}
              onChange={(e) => setUname(e.currentTarget.value)}
            />
          </label>

          <label className={classes.field}>
            <span className={classes.fieldLabel}>PASSWORD</span>
            <input
              className={classes.input}
              type="password"
              autoComplete="current-password"
              value={pwd}
              disabled={busy}
              onChange={(e) => setPwd(e.currentTarget.value)}
            />
          </label>

          {needCode && (
            <label className={classes.field}>
              <span className={classes.fieldLabel}>TWO-STEP CODE</span>
              <input
                className={classes.input}
                inputMode="numeric"
                autoComplete="one-time-code"
                placeholder="123 456"
                value={code}
                disabled={busy}
                autoFocus
                onChange={(e) => setCode(e.currentTarget.value)}
              />
            </label>
          )}

          <div className={classes.captcha}>
            <Captcha action="login" ref={captchaRef} />
          </div>

          <button className={classes.submit} type="submit" disabled={busy}>
            {busy ? 'SIGNING IN…' : 'SIGN IN'}
          </button>

          <p className={classes.footnote}>Players sign in on the main ZARRAR page, not here.</p>
        </form>
      </main>
    </div>
  )
}

export default MonitorLogin

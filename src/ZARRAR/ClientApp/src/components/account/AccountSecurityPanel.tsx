import { FC, FormEvent, useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { arena } from '@Components/player/Arena'
import classes from '@Styles/player/Security.module.css'

interface TwoFactorStatus {
  enabled: boolean
  recoveryCodesLeft: number
  sharedKey?: string | null
  authenticatorUri?: string | null
  recommended: boolean
}

const call = async <T,>(method: string, path: string, body?: unknown): Promise<T> => {
  const res = await fetch(path, {
    method,
    credentials: 'same-origin',
    headers: body === undefined ? { Accept: 'application/json' } : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  const text = await res.text().catch(() => '')
  let data: unknown
  try {
    data = text ? JSON.parse(text) : undefined
  } catch {
    data = undefined
  }
  if (!res.ok) throw new Error((data as { title?: string } | undefined)?.title ?? `HTTP ${res.status}`)
  return data as T
}

/**
 * S3 — account security: two-step login (authenticator app + one-time recovery codes) and
 * "sign out everywhere". Strongly recommended for Monitor and Admin accounts; the ZARRAR
 * pre-flight checklist lists staff accounts that don't have it yet.
 */
export const AccountSecurityPanel: FC = () => {
  const navigate = useNavigate()
  const [status, setStatus] = useState<TwoFactorStatus | null>(null)
  const [code, setCode] = useState('')
  const [codes, setCodes] = useState<string[] | null>(null)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null)

  const load = useCallback(async () => {
    try {
      setStatus(await call<TwoFactorStatus>('GET', '/api/account/twofactorstatus'))
    } catch (e) {
      setMessage({ ok: false, text: e instanceof Error ? e.message : 'Could not load two-step login.' })
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  const enable = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setMessage(null)
    try {
      const result = await call<{ recoveryCodes: string[] }>('POST', '/api/account/twofactorenable', { code })
      setCodes(result.recoveryCodes)
      setCode('')
      setMessage({ ok: true, text: 'Two-step login is on. Save the recovery codes below now — they are shown once.' })
      await load()
    } catch (err) {
      setMessage({ ok: false, text: err instanceof Error ? err.message : 'That code did not work.' })
    } finally {
      setBusy(false)
    }
  }

  const disable = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setMessage(null)
    try {
      await call('POST', '/api/account/twofactordisable', { code })
      setCode('')
      setCodes(null)
      setMessage({ ok: true, text: 'Two-step login is off.' })
      await load()
    } catch (err) {
      setMessage({ ok: false, text: err instanceof Error ? err.message : 'That code did not work.' })
    } finally {
      setBusy(false)
    }
  }

  const signOutEverywhere = async () => {
    if (!window.confirm('Sign out of ZARRAR on every device, including this one?')) return
    setBusy(true)
    try {
      await call('POST', '/api/account/signouteverywhere')
      navigate('/account/login', { replace: true })
    } catch (err) {
      setMessage({ ok: false, text: err instanceof Error ? err.message : 'Could not sign out everywhere.' })
      setBusy(false)
    }
  }

  return (
    <section className={`${arena.panel} ${arena.panelPad} ${classes.panel}`} aria-labelledby="security-title">
      <div className={classes.head}>
        <div>
          <div className={arena.eyebrow}>// SECURITY</div>
          <h2 id="security-title" className={classes.title}>Two-step login</h2>
        </div>
        <span className={`${classes.state} ${status?.enabled ? classes.stateOn : ''}`}>
          {status ? (status.enabled ? 'ON' : 'OFF') : '…'}
        </span>
      </div>

      {status?.recommended && !status.enabled && (
        <p className={classes.warn}>
          This is a staff account (Monitor or Admin). Turn on two-step login before the event — a stolen password
          alone should never be enough to run the match.
        </p>
      )}

      {status && !status.enabled && (
        <form className={classes.form} onSubmit={enable}>
          <ol className={classes.steps}>
            <li>Open an authenticator app (Google Authenticator, Microsoft Authenticator, Aegis, 1Password…).</li>
            <li>
              Add an account with this key:
              <code className={classes.key}>{status.sharedKey}</code>
              <span className={classes.hint}>(time-based, 6 digits)</span>
            </li>
            <li>Type the 6-digit code it shows:</li>
          </ol>
          <div className={classes.row}>
            <input
              className={arena.search}
              inputMode="numeric"
              autoComplete="one-time-code"
              placeholder="123 456"
              value={code}
              onChange={(e) => setCode(e.currentTarget.value)}
              aria-label="Authenticator code"
            />
            <button type="submit" className={`${arena.btn} ${arena.btnPrimary}`} disabled={busy || code.trim().length < 6}>
              Turn on
            </button>
          </div>
          {status.authenticatorUri && (
            <a className={classes.link} href={status.authenticatorUri}>
              Open in an authenticator on this device
            </a>
          )}
        </form>
      )}

      {status?.enabled && (
        <form className={classes.form} onSubmit={disable}>
          <p className={classes.body}>
            Signing in asks for a code from your authenticator. Recovery codes left: <b>{status.recoveryCodesLeft}</b>.
          </p>
          <div className={classes.row}>
            <input
              className={arena.search}
              placeholder="Code or recovery code"
              value={code}
              onChange={(e) => setCode(e.currentTarget.value)}
              aria-label="Code to turn two-step login off"
            />
            <button type="submit" className={`${arena.btn} ${arena.btnDanger}`} disabled={busy || code.trim().length < 6}>
              Turn off
            </button>
          </div>
        </form>
      )}

      {codes && (
        <div className={classes.codes} role="status">
          <b>Recovery codes — each works once if you lose your phone:</b>
          <ul>
            {codes.map((c) => (
              <li key={c}>
                <code>{c}</code>
              </li>
            ))}
          </ul>
        </div>
      )}

      {message && <p className={message.ok ? classes.ok : classes.error}>{message.text}</p>}

      <div className={classes.divider} />

      <div className={classes.head}>
        <div>
          <h2 className={classes.title}>Sign out everywhere</h2>
          <p className={classes.body}>Ends every session of this account on every device (others within a minute).</p>
        </div>
        <button type="button" className={`${arena.btn} ${arena.btnGhost}`} disabled={busy} onClick={() => void signOutEverywhere()}>
          Sign out everywhere
        </button>
      </div>
    </section>
  )
}

export default AccountSecurityPanel

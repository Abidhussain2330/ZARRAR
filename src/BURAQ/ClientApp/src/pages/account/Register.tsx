import { useInputState } from '@mantine/hooks'
import { showNotification, updateNotification } from '@mantine/notifications'
import { mdiCheck, mdiClose } from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router'
import { Captcha, useCaptchaRef } from '@Components/Captcha'
import {
  GateCheckbox,
  GateField,
  GateHeading,
  GateShell,
  StrengthMeter,
  gateClasses as classes,
} from '@Components/player/GateShell'
import { encryptApiData } from '@Utils/Crypto'
import { tryGetClientError } from '@Utils/Shared'
import { useConfig } from '@Hooks/useConfig'
import { usePageTitle } from '@Hooks/usePageTitle'
import api, { RegisterStatus } from '@Api'

interface FieldErrors {
  uname?: string
  email?: string
  pwd?: string
  retyped?: string
  agree?: string
}

const Register: FC = () => {
  const [pwd, setPwd] = useInputState('')
  const [retypedPwd, setRetypedPwd] = useInputState('')
  const [uname, setUname] = useInputState('')
  const [email, setEmail] = useInputState('')
  const [agree, setAgree] = useState(false)
  const [disabled, setDisabled] = useState(false)
  const [errors, setErrors] = useState<FieldErrors>({})

  const { config } = useConfig()
  const navigate = useNavigate()
  const { captchaRef, getToken, cleanUp } = useCaptchaRef()
  const { t } = useTranslation()

  usePageTitle(t('account.title.register'))

  const RegisterStatusMap = new Map([
    [RegisterStatus.LoggedIn, { message: t('account.notification.register.logged_in') }],
    [
      RegisterStatus.AdminConfirmationRequired,
      {
        title: t('account.notification.register.request_sent.title'),
        message: t('account.notification.register.request_sent.message'),
      },
    ],
    [
      RegisterStatus.EmailConfirmationRequired,
      { title: t('common.email.sent.title'), message: t('common.email.sent.message') },
    ],
    [undefined, undefined],
  ])

  if (config.allowRegister === false) {
    return (
      <GateShell backTo="/" backLabel="Back to ZARRAR">
        <GateHeading title="Registration is closed" lede="New account registration is not available right now." />
        <p className={classes.switchRow}>
          Already have an account?{' '}
          <Link className={`${classes.link} bqFocusRing`} to="/account/login">
            Sign in
          </Link>
        </p>
      </GateShell>
    )
  }

  const clearError = (key: keyof FieldErrors) =>
    setErrors((prev) => (prev[key] ? { ...prev, [key]: undefined } : prev))

  /**
   * Everything the client can know before the request goes out is checked here and
   * reported next to the offending field. The backend remains the authority on
   * uniqueness and on its own password policy; those come back as toasts.
   */
  const validate = () => {
    const next: FieldErrors = {}

    if (uname.trim().length < 3) next.uname = 'Pick a username of at least 3 characters.'
    if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email)) next.email = 'Enter an email address you can receive mail at.'
    if (pwd.length < 10) next.pwd = 'Use at least 10 characters.'
    if (retypedPwd !== pwd) next.retyped = 'This doesn\u2019t match the password above.'
    if (!agree) next.agree = 'Accept the terms to continue.'

    setErrors(next)
    return Object.keys(next).length === 0
  }

  const onRegister = async (event: React.SyntheticEvent) => {
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
      id: 'register-status',
      title: t('account.notification.captcha.request_sent.title'),
      message: t('account.notification.captcha.request_sent.message'),
      loading: true,
      autoClose: false,
    })

    try {
      const res = await api.account.accountRegister({
        userName: uname,
        password: await encryptApiData(t, pwd, config.apiPublicKey),
        email: email,
        challenge: token,
      })

      const data = RegisterStatusMap.get(res.data.data)
      if (data) {
        updateNotification({
          id: 'register-status',
          color: 'teal',
          title: data.title,
          message: data.message,
          icon: <Icon path={mdiCheck} size={1} />,
          loading: false,
          autoClose: true,
        })
        cleanUp(true)

        if (res.data.data === RegisterStatus.LoggedIn) navigate('/posts')
        else if (res.data.data === RegisterStatus.EmailConfirmationRequired)
          navigate('/account/pending', { state: { email } })
        else navigate('/account/login')
      }
    } catch (err: unknown) {
      const { title, message } = tryGetClientError(err, t)

      updateNotification({
        id: 'register-status',
        color: 'red',
        title,
        message,
        icon: <Icon path={mdiClose} size={1} />,
        loading: false,
        autoClose: true,
      })
      cleanUp(false)
    } finally {
      setDisabled(false)
    }
  }

  return (
    <GateShell backTo="/" backLabel="Back to ZARRAR">
      <GateHeading title="Create your account" lede="One account gets you into every ZARRAR showdown." />

      <form className={classes.form} onSubmit={onRegister} noValidate>
        <GateField
          label="Username"
          glyph="●"
          autoComplete="username"
          placeholder="Choose a username"
          maxLength={40}
          value={uname}
          error={errors.uname}
          disabled={disabled}
          onChange={(e) => {
            setUname(e.currentTarget.value)
            clearError('uname')
          }}
        />

        <GateField
          label="Email"
          glyph="@"
          type="email"
          autoComplete="email"
          placeholder="you@example.com"
          value={email}
          error={errors.email}
          disabled={disabled}
          onChange={(e) => {
            setEmail(e.currentTarget.value)
            clearError('email')
          }}
        />

        <GateField
          label="Password"
          glyph="▭"
          revealable
          autoComplete="new-password"
          placeholder="Create a strong password"
          value={pwd}
          error={errors.pwd}
          disabled={disabled}
          onChange={(e) => {
            setPwd(e.currentTarget.value)
            clearError('pwd')
          }}
        />
        <StrengthMeter password={pwd} />

        <GateField
          label="Confirm password"
          glyph="▭"
          revealable
          autoComplete="new-password"
          placeholder="Repeat your password"
          value={retypedPwd}
          error={errors.retyped}
          disabled={disabled}
          onChange={(e) => {
            setRetypedPwd(e.currentTarget.value)
            clearError('retyped')
          }}
        />

        <div>
          <GateCheckbox
            checked={agree}
            onToggle={() => {
              setAgree((v) => !v)
              clearError('agree')
            }}
          >
            I agree to the platform rules and to fair play during events.
          </GateCheckbox>
          {errors.agree && (
            <span className={classes.fieldError} role="alert">
              {errors.agree}
            </span>
          )}
        </div>

        <div className={classes.captchaSlot}>
          <Captcha action="register" ref={captchaRef} />
        </div>

        <button className={`${classes.submit} bqFocusRing`} type="submit" disabled={disabled}>
          {disabled ? 'CREATING…' : 'CREATE ACCOUNT'}
        </button>

        <p className={classes.switchRow}>
          {t('account.anchor.login')}{' '}
          <Link className={`${classes.link} bqFocusRing`} to="/account/login">
            Sign in
          </Link>
        </p>
      </form>
    </GateShell>
  )
}

export default Register

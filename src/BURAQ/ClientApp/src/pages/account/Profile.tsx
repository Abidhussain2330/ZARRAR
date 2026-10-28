import { Modal } from '@mantine/core'
import { Dropzone } from '@mantine/dropzone'
import { useModals } from '@mantine/modals'
import { showNotification } from '@mantine/notifications'
import { mdiCheck, mdiClose } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, FormEvent, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { PasswordChangeModal } from '@Components/PasswordChangeModal'
import { WithRole } from '@Components/WithRole'
import { Badge, ErrorState, Loading, PageHead, arena } from '@Components/player/Arena'
import { PlayerShell } from '@Components/player/PlayerShell'
import { encryptApiData } from '@Utils/Crypto'
import { IMAGE_MIME_TYPES, tryGetErrorMsg } from '@Utils/Shared'
import { useConfig } from '@Hooks/useConfig'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useTeams, useUser } from '@Hooks/useUser'
import api, { ProfileUpdateModel, Role } from '@Api'
import classes from '@Styles/player/Profile.module.css'

type Feedback = { tone: 'ok' | 'bad'; message: string }

const emptyProfile: ProfileUpdateModel = {
  userName: '',
  bio: '',
  stdNumber: '',
  phone: '',
  realName: '',
}

const clean = (value?: string | null) => value ?? ''

const Profile: FC = () => {
  const { user, error: userError, mutate } = useUser()
  const { teams } = useTeams()
  const { config } = useConfig()
  const { t } = useTranslation()
  const modals = useModals()

  const [profile, setProfile] = useState<ProfileUpdateModel>(emptyProfile)
  const [profilePending, setProfilePending] = useState(false)
  const [feedback, setFeedback] = useState<Feedback | null>(null)

  const [avatarOpened, setAvatarOpened] = useState(false)
  const [avatarFile, setAvatarFile] = useState<File | null>(null)
  const [avatarPreview, setAvatarPreview] = useState<string | null>(null)
  const [avatarPending, setAvatarPending] = useState(false)
  const [avatarError, setAvatarError] = useState<string | null>(null)

  const [mailOpened, setMailOpened] = useState(false)
  const [email, setEmail] = useState('')
  const [mailPwd, setMailPwd] = useState('')
  const [mailPending, setMailPending] = useState(false)
  const [mailError, setMailError] = useState<string | null>(null)
  const [emailChangePending, setEmailChangePending] = useState(false)

  const [pwdOpened, setPwdOpened] = useState(false)

  usePageTitle(t('account.title.profile'))

  useEffect(() => {
    if (!user) return
    setProfile({
      userName: clean(user.userName),
      bio: clean(user.bio),
      stdNumber: clean(user.stdNumber),
      phone: clean(user.phone),
      realName: clean(user.realName),
    })
  }, [user])

  // Object URLs leak unless revoked, and this one is replaced on every file pick.
  useEffect(() => {
    if (!avatarFile) {
      setAvatarPreview(null)
      return
    }
    const preview = URL.createObjectURL(avatarFile)
    setAvatarPreview(preview)
    return () => URL.revokeObjectURL(preview)
  }, [avatarFile])

  const hasChanges = useMemo(() => {
    if (!user) return false
    return (
      clean(profile.userName) !== clean(user.userName) ||
      clean(profile.bio) !== clean(user.bio) ||
      clean(profile.stdNumber) !== clean(user.stdNumber) ||
      clean(profile.phone) !== clean(user.phone) ||
      clean(profile.realName) !== clean(user.realName)
    )
  }, [profile, user])

  const setField = (field: keyof ProfileUpdateModel, value: string) =>
    setProfile((current) => ({ ...current, [field]: value }))

  const onSaveProfile = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!hasChanges) return

    setProfilePending(true)
    setFeedback(null)

    try {
      await api.account.accountUpdate(profile)
      await mutate()
      setFeedback({ tone: 'ok', message: t('account.notification.profile.profile_updated') })
      showNotification({
        color: 'teal',
        message: t('account.notification.profile.profile_updated'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (error) {
      setFeedback({ tone: 'bad', message: tryGetErrorMsg(error, t) })
    } finally {
      setProfilePending(false)
    }
  }

  const onSaveAvatar = async () => {
    if (!avatarFile) {
      setAvatarError('Choose an image first.')
      return
    }

    setAvatarPending(true)
    setAvatarError(null)

    try {
      await api.account.accountAvatar({ file: avatarFile })
      await mutate()
      showNotification({
        id: 'profile-avatar',
        color: 'teal',
        message: t('common.avatar.uploaded'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      setAvatarFile(null)
      setAvatarOpened(false)
    } catch (error) {
      const message = tryGetErrorMsg(error, t)
      setAvatarError(message)
      showNotification({
        id: 'profile-avatar',
        color: 'red',
        title: t('common.avatar.upload_failed'),
        message,
        icon: <Icon path={mdiClose} size={1} />,
      })
    } finally {
      setAvatarPending(false)
    }
  }

  const removeAvatar = async () => {
    setAvatarPending(true)
    setAvatarError(null)
    try {
      await api.account.accountRemoveAvatar()
      await mutate()
      showNotification({
        id: 'profile-avatar-remove',
        color: 'teal',
        message: 'Avatar removed',
        icon: <Icon path={mdiCheck} size={1} />,
      })
      setAvatarOpened(false)
    } catch (error) {
      setAvatarError(tryGetErrorMsg(error, t))
    } finally {
      setAvatarPending(false)
    }
  }

  const confirmRemoveAvatar = () =>
    modals.openConfirmModal({
      title: 'Remove your avatar?',
      children: <p className={classes.modalCopy}>Your account goes back to the initials avatar.</p>,
      labels: { confirm: 'Remove avatar', cancel: t('common.modal.cancel') },
      confirmProps: { color: 'red' },
      onConfirm: removeAvatar,
    })

  const onChangeEmail = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!email || !mailPwd) return

    setMailPending(true)
    setMailError(null)

    try {
      const response = await api.account.accountChangeEmail({
        newMail: email,
        password: await encryptApiData(t, mailPwd, config.apiPublicKey),
      })

      // A truthy payload means the backend sent a confirmation mail rather than
      // applying the change outright.
      if (response.data.data) {
        setEmailChangePending(true)
        showNotification({
          color: 'teal',
          title: t('common.email.sent.title'),
          message: t('common.email.sent.message'),
          icon: <Icon path={mdiCheck} size={1} />,
        })
      } else {
        setEmailChangePending(false)
        await mutate()
        showNotification({
          color: 'teal',
          message: 'Email address updated',
          icon: <Icon path={mdiCheck} size={1} />,
        })
      }

      setMailPwd('')
      setMailOpened(false)
    } catch (error) {
      setMailError(tryGetErrorMsg(error, t))
    } finally {
      setMailPending(false)
    }
  }

  const emailStatus = emailChangePending
    ? { label: 'CHANGE PENDING', color: 'var(--bq-warn)' }
    : user?.emailConfirmed
      ? { label: 'VERIFIED', color: 'var(--bq-live)' }
      : { label: 'UNVERIFIED', color: 'var(--bq-warn)' }

  return (
    <PlayerShell>
      <WithRole requiredRole={Role.User}>
        <PageHead
          eyebrow="ZARRAR / ACCOUNT"
          title="Your profile"
          subtitle="How you appear on scoreboards, and the details organisers use to reach you."
        />

        {!user && !userError ? (
          <Loading label="Loading profile…" />
        ) : userError ? (
          <ErrorState
            title="Profile unavailable"
            body={tryGetErrorMsg(userError, t)}
            action={
              <button type="button" className={`${arena.btn} ${arena.btnGhost} bqFocusRing`} onClick={() => mutate()}>
                Retry
              </button>
            }
          />
        ) : user ? (
          <div className={classes.layout}>
            <div className={classes.main}>
              <form className={classes.panel} onSubmit={onSaveProfile}>
                <h2 className={classes.panelTitle}>Identity</h2>

                <div className={classes.fieldGrid}>
                  <label className={classes.field}>
                    <span className={classes.label}>{t('account.label.username')}</span>
                    <input
                      className={`${classes.input} bqFocusRing`}
                      value={clean(profile.userName)}
                      maxLength={40}
                      disabled={profilePending}
                      onChange={(e) => setField('userName', e.currentTarget.value)}
                    />
                  </label>

                  <label className={classes.field}>
                    <span className={classes.label}>Real name</span>
                    <input
                      className={`${classes.input} bqFocusRing`}
                      value={clean(profile.realName)}
                      disabled={profilePending}
                      onChange={(e) => setField('realName', e.currentTarget.value)}
                    />
                  </label>

                  <label className={classes.field}>
                    <span className={classes.label}>Student number</span>
                    <input
                      className={`${classes.input} bqFocusRing`}
                      value={clean(profile.stdNumber)}
                      disabled={profilePending}
                      onChange={(e) => setField('stdNumber', e.currentTarget.value)}
                    />
                  </label>

                  <label className={classes.field}>
                    <span className={classes.label}>Phone</span>
                    <input
                      className={`${classes.input} bqFocusRing`}
                      value={clean(profile.phone)}
                      disabled={profilePending}
                      onChange={(e) => setField('phone', e.currentTarget.value)}
                    />
                  </label>
                </div>

                <label className={classes.field}>
                  <span className={classes.label}>Bio</span>
                  <textarea
                    className={`${classes.input} ${classes.textarea} bqFocusRing`}
                    rows={3}
                    maxLength={64}
                    value={clean(profile.bio)}
                    disabled={profilePending}
                    onChange={(e) => setField('bio', e.currentTarget.value)}
                  />
                </label>

                {feedback && (
                  <p className={feedback.tone === 'ok' ? classes.feedbackOk : classes.feedbackBad} role="status">
                    {feedback.message}
                  </p>
                )}

                <div className={classes.formActions}>
                  <button
                    type="submit"
                    className={`${arena.btn} ${arena.btnPrimary} bqFocusRing`}
                    disabled={!hasChanges || profilePending}
                  >
                    {profilePending ? 'SAVING…' : 'SAVE CHANGES'}
                  </button>
                </div>
              </form>

              <div className={classes.panel}>
                <h2 className={classes.panelTitle}>Sign-in and security</h2>

                <div className={classes.row}>
                  <div className={classes.rowMain}>
                    <p className={classes.rowLabel}>Email</p>
                    <p className={classes.rowValue}>{user.email}</p>
                  </div>
                  <Badge color={emailStatus.color} withDot={false}>
                    {emailStatus.label}
                  </Badge>
                  <button
                    type="button"
                    className={`${arena.btn} ${arena.btnGhost} bqFocusRing`}
                    onClick={() => {
                      setEmail(user.email ?? '')
                      setMailPwd('')
                      setMailError(null)
                      setMailOpened(true)
                    }}
                  >
                    Change
                  </button>
                </div>

                <div className={classes.row}>
                  <div className={classes.rowMain}>
                    <p className={classes.rowLabel}>Password</p>
                    <p className={classes.rowValue}>Last changed is not tracked</p>
                  </div>
                  <button
                    type="button"
                    className={`${arena.btn} ${arena.btnGhost} bqFocusRing`}
                    onClick={() => setPwdOpened(true)}
                  >
                    Change
                  </button>
                </div>
              </div>
            </div>

            <aside className={classes.side}>
              <div className={classes.panel}>
                <div className={classes.avatarBlock}>
                  {user.avatar ? (
                    <img className={classes.avatar} src={user.avatar} alt="" />
                  ) : (
                    <span className={classes.avatarFallback} aria-hidden="true">
                      {(user.userName ?? '0x').slice(0, 2).toUpperCase()}
                    </span>
                  )}
                  <p className={classes.avatarName}>{user.userName}</p>
                  {user.bio && <p className={classes.avatarBio}>{user.bio}</p>}
                  <button
                    type="button"
                    className={`${arena.btn} ${arena.btnGhost} bqFocusRing`}
                    onClick={() => setAvatarOpened(true)}
                  >
                    Change avatar
                  </button>
                </div>
              </div>

              <div className={classes.panel}>
                <h2 className={classes.panelTitle}>Teams</h2>
                {teams && teams.length > 0 ? (
                  <ul className={classes.teamList}>
                    {teams.map((team) => (
                      <li key={team.id} className={classes.teamItem}>
                        <span className={classes.teamName}>{team.name}</span>
                        {team.members?.some((m) => m.captain && m.id === user.userId) && (
                          <span className={classes.captainTag}>CAPTAIN</span>
                        )}
                      </li>
                    ))}
                  </ul>
                ) : (
                  <p className={classes.sideNote}>
                    You&rsquo;re not on a team yet. <Link to="/teams">Create or join one</Link> to enter events.
                  </p>
                )}
              </div>
            </aside>
          </div>
        ) : null}

        <Modal
          opened={avatarOpened}
          title="Change avatar"
          centered
          onClose={() => {
            if (avatarPending) return
            setAvatarFile(null)
            setAvatarError(null)
            setAvatarOpened(false)
          }}
        >
          <div className={classes.avatarModal}>
            <Dropzone
              onDrop={(files) => {
                setAvatarError(null)
                setAvatarFile(files[0])
              }}
              onReject={() => setAvatarError('That file is not a supported image.')}
              maxSize={3 * 1024 ** 2}
              accept={IMAGE_MIME_TYPES}
              disabled={avatarPending}
            >
              {avatarPreview ? (
                <img className={classes.dropPreview} src={avatarPreview} alt="" />
              ) : (
                <p className={classes.dropCopy}>Drop an image here, or click to pick one. Up to 3 MB.</p>
              )}
            </Dropzone>

            {avatarError && (
              <p className={classes.feedbackBad} role="alert">
                {avatarError}
              </p>
            )}

            <div className={classes.joinActions}>
              {user?.avatar && (
                <button
                  type="button"
                  className={`${arena.btn} ${arena.btnDanger} bqFocusRing`}
                  disabled={avatarPending}
                  onClick={confirmRemoveAvatar}
                >
                  Remove
                </button>
              )}
              <button
                type="button"
                className={`${arena.btn} ${arena.btnPrimary} bqFocusRing`}
                disabled={!avatarFile || avatarPending}
                onClick={onSaveAvatar}
              >
                {avatarPending ? 'UPLOADING…' : 'SAVE AVATAR'}
              </button>
            </div>
          </div>
        </Modal>

        <Modal
          opened={mailOpened}
          title="Change email"
          centered
          onClose={() => {
            if (mailPending) return
            setMailPwd('')
            setMailError(null)
            setMailOpened(false)
          }}
        >
          <form className={classes.avatarModal} onSubmit={onChangeEmail}>
            <label className={classes.field}>
              <span className={classes.label}>New email</span>
              <input
                type="email"
                required
                className={`${classes.input} bqFocusRing`}
                value={email}
                disabled={mailPending}
                onChange={(e) => {
                  setMailError(null)
                  setEmail(e.currentTarget.value)
                }}
              />
            </label>

            <label className={classes.field}>
              <span className={classes.label}>Confirm with your password</span>
              <input
                type="password"
                required
                autoComplete="current-password"
                className={`${classes.input} bqFocusRing`}
                value={mailPwd}
                disabled={mailPending}
                onChange={(e) => {
                  setMailError(null)
                  setMailPwd(e.currentTarget.value)
                }}
              />
            </label>

            {mailError && (
              <p className={classes.feedbackBad} role="alert">
                {mailError}
              </p>
            )}

            <div className={classes.joinActions}>
              <button
                type="submit"
                className={`${arena.btn} ${arena.btnPrimary} bqFocusRing`}
                disabled={!email || !mailPwd || mailPending}
              >
                {mailPending ? 'SAVING…' : 'CHANGE EMAIL'}
              </button>
            </div>
          </form>
        </Modal>

        <PasswordChangeModal
          opened={pwdOpened}
          onClose={() => setPwdOpened(false)}
          title={t('account.button.change_password')}
          withCloseButton={false}
        />
      </WithRole>
    </PlayerShell>
  )
}

export default Profile

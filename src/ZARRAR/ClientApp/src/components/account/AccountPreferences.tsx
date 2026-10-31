import { Button, Group, SegmentedControl, Switch, Text } from '@mantine/core'
import { mdiAlertCircleOutline, mdiCheckCircleOutline, mdiMotionPlayOutline, mdiTuneVariant } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, FormEvent, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ErrorState, LoadingState, Panel } from '@Components/zarrar'
import { useSyncedPreferences } from '@Hooks/zarrar/useSyncedPreferences'
import { tryGetErrorMsg } from '@Utils/Shared'
import { MotionPreference, UserPreferencesModel } from '@Api'
import styles from '@Styles/pages/AccountSettings.module.css'

type SaveFeedback = { tone: 'success' | 'error'; message: string }

const preferenceSignature = (value: UserPreferencesModel): string =>
  `${value.motion}|${value.lowEnergyMode}|${value.sidebarCollapsed}`

export interface AccountPreferencesProps {
  userId: string
}

export const AccountPreferences: FC<AccountPreferencesProps> = ({ userId }) => {
  const { t } = useTranslation()
  const { preferences, error, isLoading, reload, savePreferences } = useSyncedPreferences(userId)
  const [draft, setDraft] = useState<UserPreferencesModel | null>(null)
  const [saved, setSaved] = useState<UserPreferencesModel | null>(null)
  const [pending, setPending] = useState(false)
  const [feedback, setFeedback] = useState<SaveFeedback | null>(null)
  const loadedSignature = useRef<string | null>(null)

  useEffect(() => {
    if (!preferences) return

    const signature = `${userId}|${preferenceSignature(preferences)}`
    if (loadedSignature.current === signature) return

    loadedSignature.current = signature
    setDraft({ ...preferences })
    setSaved({ ...preferences })
    setFeedback(null)
  }, [preferences, userId])

  const changed = useMemo(
    () => Boolean(draft && saved && preferenceSignature(draft) !== preferenceSignature(saved)),
    [draft, saved]
  )

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!draft || !changed || pending) return

    setPending(true)
    setFeedback(null)

    try {
      const next = await savePreferences(draft)
      loadedSignature.current = `${userId}|${preferenceSignature(next)}`
      setDraft({ ...next })
      setSaved({ ...next })
      setFeedback({ tone: 'success', message: 'Preferences saved and applied to this session.' })
    } catch (saveError) {
      setFeedback({ tone: 'error', message: tryGetErrorMsg(saveError, t) })
    } finally {
      setPending(false)
    }
  }

  return (
    <Panel
      title="Interface preferences"
      eyebrow="PRESENTATION_PROFILE"
      icon={<Icon path={mdiTuneVariant} size={0.78} />}
      variant="raised"
    >
      {isLoading && !draft ? (
        <LoadingState variant="panel" count={4} label="Loading saved interface preferences" />
      ) : error && !draft ? (
        <ErrorState
          title="Preferences unavailable"
          message={tryGetErrorMsg(error, t)}
          onRetry={() => void reload()}
        />
      ) : draft ? (
        <form className={styles.preferenceForm} onSubmit={submit}>
          <fieldset className={styles.preferenceGroup} disabled={pending}>
            <legend>Motion</legend>
            <Text size="sm" c="dimmed">
              System follows your operating system. Reduced motion limits non-essential animation throughout ZARRAR.
            </Text>
            <SegmentedControl
              fullWidth
              aria-label="Motion preference"
              value={draft.motion}
              data={[
                { label: 'System', value: MotionPreference.System },
                { label: 'Full', value: MotionPreference.Full },
                { label: 'Reduced', value: MotionPreference.Reduced },
              ]}
              onChange={(motion) => {
                setFeedback(null)
                setDraft((current) => (current ? { ...current, motion: motion as MotionPreference } : current))
              }}
            />
            <div className={styles.accessibilityNote}>
              <Icon path={mdiMotionPlayOutline} size={0.76} aria-hidden />
              <span>
                Choosing System continues to respect <code>prefers-reduced-motion</code>; ZARRAR never forces motion over
                that accessibility setting.
              </span>
            </div>
          </fieldset>

          <div className={styles.preferenceSwitches}>
            <Switch
              checked={draft.lowEnergyMode}
              disabled={pending}
              label="Low-energy presentation"
              description="Reduce decorative effects and visual processing while keeping every control available."
              onChange={(event) => {
                setFeedback(null)
                setDraft((current) =>
                  current ? { ...current, lowEnergyMode: event.currentTarget.checked } : current
                )
              }}
            />
            <Switch
              checked={draft.sidebarCollapsed}
              disabled={pending}
              label="Collapse desktop navigation"
              description="Start the desktop shell with the navigation rail collapsed. Mobile navigation is unaffected."
              onChange={(event) => {
                setFeedback(null)
                setDraft((current) =>
                  current ? { ...current, sidebarCollapsed: event.currentTarget.checked } : current
                )
              }}
            />
          </div>

          {feedback && (
            <div className={styles.feedback} data-tone={feedback.tone} role={feedback.tone === 'error' ? 'alert' : 'status'}>
              <Icon
                path={feedback.tone === 'success' ? mdiCheckCircleOutline : mdiAlertCircleOutline}
                size={0.78}
                aria-hidden
              />
              <span>{feedback.message}</span>
            </div>
          )}

          <Group justify="flex-end" wrap="wrap" className={styles.formActions}>
            <Button
              type="button"
              variant="default"
              disabled={!changed || pending}
              onClick={() => {
                if (saved) setDraft({ ...saved })
                setFeedback(null)
              }}
            >
              Discard changes
            </Button>
            <Button type="submit" loading={pending} disabled={!changed || pending}>
              Save preferences
            </Button>
          </Group>
        </form>
      ) : null}
    </Panel>
  )
}

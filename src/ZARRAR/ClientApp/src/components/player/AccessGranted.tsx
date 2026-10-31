import { FC, useEffect, useRef, useState } from 'react'
import classes from '@Styles/player/AccessGranted.module.css'

/**
 * Local-storage key holding the player's choice to stop seeing the clip. Read
 * before the overlay ever mounts, so opting out takes effect from the next login.
 */
const SKIP_KEY = 'zarrar.skipAccessGranted'

export const prefersSkipIntro = () => {
  try {
    return localStorage.getItem(SKIP_KEY) === '1'
  } catch {
    // Storage can throw in private modes and under strict cookie policies. Losing
    // the preference is acceptable; failing the login over it is not.
    return false
  }
}

const setSkipIntro = (value: boolean) => {
  try {
    if (value) localStorage.setItem(SKIP_KEY, '1')
    else localStorage.removeItem(SKIP_KEY)
  } catch {
    /* preference simply won't persist */
  }
}

interface AccessGrantedProps {
  /** Fires when the clip ends, is skipped, or fails to play. Always fires exactly once. */
  onDone: () => void
}

/**
 * Plays after the backend confirms the session, then hands off to onDone.
 *
 * Every exit routes through finish(), so a player is never stranded here: the clip
 * ending, the skip button, Escape, a playback error and a 6-second watchdog all
 * lead to the same place. The watchdog matters because a decode failure on some
 * mobile browsers fires no event at all.
 */
export const AccessGranted: FC<AccessGrantedProps> = ({ onDone }) => {
  const videoRef = useRef<HTMLVideoElement>(null)
  const doneRef = useRef(false)
  const [dontShowAgain, setDontShowAgain] = useState(false)

  useEffect(() => {
    const finish = () => {
      if (doneRef.current) return
      doneRef.current = true
      onDone()
    }

    const video = videoRef.current
    const watchdog = window.setTimeout(finish, 6000)

    video?.play().catch(finish)

    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') finish()
    }
    window.addEventListener('keydown', onKey)

    return () => {
      window.clearTimeout(watchdog)
      window.removeEventListener('keydown', onKey)
    }
  }, [onDone])

  const finishNow = () => {
    if (doneRef.current) return
    doneRef.current = true
    onDone()
  }

  const onSkipChange = (next: boolean) => {
    setDontShowAgain(next)
    setSkipIntro(next)
  }

  return (
    <div className={classes.overlay} role="status" aria-live="polite">
      <video
        ref={videoRef}
        className={classes.video}
        muted
        playsInline
        preload="auto"
        src="/assets/login-success.mp4"
        onEnded={finishNow}
        onError={finishNow}
        aria-hidden="true"
      />
      <div className={classes.scrim} />

      <div className={`${classes.corner} ${classes.cTl}`} aria-hidden="true" />
      <div className={`${classes.corner} ${classes.cTr}`} aria-hidden="true" />
      <div className={`${classes.corner} ${classes.cBl}`} aria-hidden="true" />
      <div className={`${classes.corner} ${classes.cBr}`} aria-hidden="true" />

      <div className={classes.controls}>
        <button type="button" className={`${classes.skip} bqFocusRing`} onClick={finishNow}>
          Skip
        </button>
        <label className={classes.dontShow}>
          <input type="checkbox" checked={dontShowAgain} onChange={(e) => onSkipChange(e.currentTarget.checked)} />
          Don&rsquo;t play this again
        </label>
      </div>

      <div className={classes.caption}>
        <p className={classes.captionMain}>Access granted</p>
        <p className={classes.captionSub}>Entering the arena…</p>
      </div>
    </div>
  )
}

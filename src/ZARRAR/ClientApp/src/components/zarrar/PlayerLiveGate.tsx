import { FC, useCallback, useEffect, useRef, useState } from 'react'
import { DuelPlayerStatus, zarrarApi } from '@Utils/zarrar/api'
import { useZarrarHub } from '@Utils/zarrar/hub'
import { publishProblem, usePublisher, zarrarPublisher } from '@Utils/zarrar/live'
import classes from '@Styles/zarrar/PlayerGate.module.css'

/**
 * The player's side of a ZARRAR showdown, layered over the normal challenge page.
 *
 *  - Not streaming yet: a full-screen gate. The challenges stay locked (the server enforces this
 *    too) until the player shares their entire screen.
 *  - Streaming: a small LIVE strip so they always know they are on the big screen.
 *  - Paused / ended: an overlay explaining why flags are not being accepted.
 *  - Benched (tag-team): a teammate is on the keyboard. The player waits for the monitor to tag
 *    them in; when that happens the gate turns into a "you're tagged in, share your screen" call.
 *
 * Renders nothing in a normal jeopardy game.
 */
export const PlayerLiveGate: FC<{ gameId: number; onChange?: () => void }> = ({ gameId, onChange }) => {
  const [status, setStatus] = useState<DuelPlayerStatus | null>(null)
  // Set once this player has been on the bench, so being tagged in gets its own call to action.
  const [wasBenched, setWasBenched] = useState(false)
  const publisher = usePublisher()
  const onChangeRef = useRef(onChange)
  onChangeRef.current = onChange

  const load = useCallback(async () => {
    try {
      setStatus(await zarrarApi.me(gameId))
    } catch {
      /* not signed in or not a participant — the page handles that */
    }
  }, [gameId])

  // Poll briskly only in a showdown; an ordinary game just checks once a minute in case the
  // admin turns showdown mode on, so normal events don't spend players' request budget.
  const isDuel = !!status?.duel
  useEffect(() => {
    load()
    const timer = window.setInterval(load, isDuel ? 5000 : 60000)
    return () => window.clearInterval(timer)
  }, [load, isDuel])

  useZarrarHub(
    gameId,
    {
      onState: (state) => {
        setStatus((prev) =>
          prev ? { ...prev, duel: state.enabled, paused: state.paused, pauseReason: state.pauseReason, ended: state.ended } : prev
        )
        // Tag-ins arrive as state changes; the bench status itself comes from /me.
        load()
        onChangeRef.current?.()
      },
      onReconnected: load,
    },
    !!status?.duel
  )

  // The server's view of the feed lags the browser's by a second; refresh it when ours changes.
  useEffect(() => {
    if (publisher.status === 'live' || publisher.status === 'error') {
      const timer = window.setTimeout(() => {
        load()
        onChangeRef.current?.()
      }, 1200)
      return () => window.clearTimeout(timer)
    }
  }, [publisher.status, load])

  const benched = !!status?.benched

  useEffect(() => {
    if (!benched) return
    setWasBenched(true)
    // The server closes a benched stream itself; stop locally too so the share indicator goes away.
    if (zarrarPublisher.getSnapshot().gameId === gameId && zarrarPublisher.getSnapshot().status !== 'idle')
      zarrarPublisher.stop()
  }, [benched, gameId])

  if (!status?.duel) return null

  const mine = publisher.gameId === gameId
  const streaming = mine && publisher.status === 'live'
  const problem = publishProblem()
  const busy = mine && (publisher.status === 'requesting' || publisher.status === 'connecting' || publisher.status === 'reconnecting')

  if (status.ended) {
    return (
      <div className={classes.overlay} role="dialog" aria-modal="true" aria-label="Match over">
        <div className={classes.panel}>
          <img className={classes.logo} src="/assets/zarrar/zarrar-logo-sm.png" alt="ZARRAR" />
          <h2 className={classes.title}>Match over</h2>
          <p className={classes.body}>Flags are no longer accepted. Look at the big screen for the result.</p>
        </div>
      </div>
    )
  }

  if (benched) {
    return (
      <div className={classes.overlay} role="dialog" aria-modal="true" aria-label="On the bench">
        <div className={`${classes.panel} ${classes.panelBench}`}>
          <img className={classes.logo} src="/assets/zarrar/zarrar-logo-sm.png" alt="ZARRAR" />
          <span className={classes.benchKicker}>TAG TEAM{status.teamName ? ` // ${status.teamName}` : ''}</span>
          <h2 className={classes.title}>On the bench</h2>
          <p className={classes.body}>
            {status.operatorName ? (
              <>
                <b className={classes.benchName}>{status.operatorName}</b> is on the keyboard for your team.
              </>
            ) : (
              'A teammate is on the keyboard for your team.'
            )}{' '}
            Stay ready — when the monitor tags you in, this screen asks you to share yours and you take over.
          </p>
          <div className={classes.benchWait} aria-hidden="true">
            <i />
            <i />
            <i />
          </div>
        </div>
      </div>
    )
  }

  if (!streaming) {
    const taggedIn = wasBenched
    return (
      <div className={classes.overlay} role="dialog" aria-modal="true" aria-label="Share your screen">
        <div className={classes.panel}>
          <img className={classes.logo} src="/assets/zarrar/zarrar-logo-sm.png" alt="ZARRAR" />
          {taggedIn && !busy && <span className={classes.benchKicker}>TAG IN</span>}
          <h2 className={classes.title}>
            {busy ? 'Connecting your screen…' : taggedIn ? "You're tagged in" : 'Go live to play'}
          </h2>
          <p className={classes.body}>
            {taggedIn
              ? 'The monitor put you on the keyboard. Share your entire screen to take over for your team — the auditorium switches to your screen the moment you do.'
              : 'This is a 1v1 showdown. Your entire screen is shown live to the auditorium for the whole match. Share your entire screen to unlock the challenges.'}
          </p>

          <ul className={classes.rules}>
            <li>Pick the “Entire screen” option, not a window or a tab.</li>
            <li>Keep this tab open. Closing it, reloading it or stopping the share pauses the match for both players.</li>
            <li>Close anything private before you start.</li>
          </ul>

          {problem ? (
            <p className={classes.error}>{problem}</p>
          ) : (
            mine && publisher.message && <p className={classes.error}>{publisher.message}</p>
          )}

          <button
            type="button"
            className={classes.go}
            disabled={busy || !!problem}
            onClick={() => zarrarPublisher.start(gameId)}
          >
            {busy ? 'Connecting…' : 'Share entire screen'}
          </button>
        </div>
      </div>
    )
  }

  return (
    <>
      <div className={classes.liveStrip} role="status">
        <span className={classes.dot} aria-hidden="true" />
        <b>LIVE</b>
        <span>Your screen is on the big screen</span>
      </div>

      {status.paused && (
        <div className={classes.pausedBanner} role="alert">
          <b>MATCH PAUSED</b>
          <span>{status.pauseReason ?? 'The admin paused the match.'} Flags are not accepted until the admin resumes.</span>
        </div>
      )}
    </>
  )
}

export default PlayerLiveGate

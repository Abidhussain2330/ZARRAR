import { FC, useCallback, useEffect, useState } from 'react'
import { DuelPlayer, ZarrarApiError, zarrarApi } from '@Utils/zarrar/api'
import classes from '@Styles/zarrar/TagRoster.module.css'

/**
 * Tag-team control: which member of each team is on the keyboard.
 *
 * Tagging a member in closes their teammate's stream (that laptop shows the bench screen), and
 * the stage keeps the team's panel, switching to the new player's screen as soon as they share
 * it. Only the tagged-in member may stream and submit flags for the team.
 */
export const TagRoster: FC<{
  gameId: number
  players: DuelPlayer[]
  /** Called after a successful tag so the caller can refresh its snapshot. */
  onChanged?: () => void
}> = ({ gameId, players, onChanged }) => {
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const tag = async (participationId: number, userId: string | null) => {
    const key = `${participationId}:${userId ?? 'auto'}`
    setBusy(key)
    setError(null)
    try {
      await zarrarApi.setOperator(gameId, participationId, userId)
      onChanged?.()
    } catch (e) {
      setError(e instanceof ZarrarApiError ? e.message : 'Could not tag that player in.')
    } finally {
      setBusy(null)
    }
  }

  if (players.length === 0) return <p className={classes.empty}>No accepted teams yet.</p>

  return (
    <div className={classes.root}>
      {players.slice(0, 2).map((p, i) => {
        const side = i === 0 ? 'p1' : 'p2'
        const roster = p.roster ?? []
        const pendingSwap = !!p.operatorId && p.liveUserId !== p.operatorId

        return (
          <div key={p.participationId} className={`${classes.team} ${side === 'p2' ? classes.teamP2 : ''}`}>
            <div className={classes.teamHead}>
              <span className={classes.slot}>{side === 'p1' ? 'P1' : 'P2'}</span>
              <span className={classes.teamName}>{p.teamName}</span>
              <span className={`${classes.state} ${p.live && !pendingSwap ? classes.stateLive : pendingSwap ? classes.stateWait : ''}`}>
                {pendingSwap
                  ? `WAITING FOR ${(p.operatorName ?? 'PLAYER').toUpperCase()} TO SHARE`
                  : p.live
                    ? 'LIVE'
                    : 'NO SIGNAL'}
              </span>
            </div>

            <div className={classes.members}>
              {roster.map((m) => {
                const onKeyboard = p.operatorId ? p.operatorId === m.userId : p.liveUserId === m.userId
                const streaming = p.liveUserId === m.userId
                const key = `${p.participationId}:${m.userId}`
                return (
                  <button
                    key={m.userId}
                    type="button"
                    className={`${classes.member} ${onKeyboard ? classes.memberOn : ''}`}
                    disabled={!!busy || (onKeyboard && !!p.operatorId)}
                    aria-pressed={onKeyboard}
                    onClick={() => tag(p.participationId, m.userId)}
                    title={onKeyboard ? 'On the keyboard' : `Tag ${m.userName} in`}
                  >
                    <span className={classes.memberName}>{m.userName}</span>
                    <span className={classes.memberTag}>
                      {busy === key ? 'TAGGING…' : onKeyboard ? (streaming ? 'ON KEYBOARD · LIVE' : 'ON KEYBOARD') : 'TAG IN'}
                    </span>
                  </button>
                )
              })}
            </div>

            {roster.length <= 1 && (
              <p className={classes.hint}>
                Only {roster.length === 1 ? 'one player has' : 'nobody has'} joined this match for {p.teamName}. A
                teammate shows up here after they sign in with their own account and join the game with this team.
              </p>
            )}

            {p.operatorId && (
              <button
                type="button"
                className={classes.clear}
                disabled={!!busy}
                onClick={() => tag(p.participationId, null)}
              >
                Clear tag (whoever shares first plays)
              </button>
            )}
          </div>
        )
      })}

      {error && <p className={classes.error}>{error}</p>}
    </div>
  )
}

/**
 * The monitor's tag-in drawer, laid over the projector stage. Hidden by default so the audience
 * never sees it: press T (or the small TAG button that appears when the mouse moves) to open it.
 */
export const MonitorTagDrawer: FC<{ gameId: number }> = ({ gameId }) => {
  const [open, setOpen] = useState(false)
  const [players, setPlayers] = useState<DuelPlayer[]>([])
  const [awake, setAwake] = useState(false)

  const load = useCallback(async () => {
    try {
      setPlayers((await zarrarApi.state(gameId)).players)
    } catch {
      /* the stage shows the connection problem */
    }
  }, [gameId])

  // Refresh while open: a tagged-in player going live flips their row to LIVE.
  useEffect(() => {
    if (!open) return
    load()
    const timer = window.setInterval(load, 2500)
    return () => window.clearInterval(timer)
  }, [open, load])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement) return
      if (e.key === 't' || e.key === 'T') setOpen((v) => !v)
      else if (e.key === 'Escape') setOpen(false)
    }
    let timer = 0
    const onMove = () => {
      setAwake(true)
      window.clearTimeout(timer)
      timer = window.setTimeout(() => setAwake(false), 2500)
    }
    window.addEventListener('keydown', onKey)
    window.addEventListener('mousemove', onMove)
    return () => {
      window.removeEventListener('keydown', onKey)
      window.removeEventListener('mousemove', onMove)
      window.clearTimeout(timer)
    }
  }, [])

  return (
    <>
      {!open && (
        <button
          type="button"
          className={`${classes.fab} ${awake ? classes.fabOn : ''}`}
          onClick={() => setOpen(true)}
          aria-label="Tag a player in (T)"
        >
          TAG <kbd>T</kbd>
        </button>
      )}

      {open && (
        <aside className={classes.drawer} role="dialog" aria-label="Tag team">
          <div className={classes.drawerHead}>
            <div>
              <div className={classes.drawerKicker}>MONITOR CONTROL</div>
              <h2 className={classes.drawerTitle}>TAG TEAM</h2>
            </div>
            <button type="button" className={classes.close} onClick={() => setOpen(false)} aria-label="Close (Esc)">
              ✕
            </button>
          </div>
          <p className={classes.drawerNote}>
            Pick who is on the keyboard. The benched player's screen drops off; the projector switches to the new
            player the moment they share their screen. Only the tagged-in player can submit flags.
          </p>
          <TagRoster gameId={gameId} players={players} onChanged={load} />
        </aside>
      )}
    </>
  )
}

export default TagRoster

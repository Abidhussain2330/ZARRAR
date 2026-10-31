import { CSSProperties, FC, ReactNode, useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import {
  DuelPlayer,
  DuelSnapshot,
  DuelState,
  StageEvent,
  formatDuration,
  liveSecondsOf,
  zarrarApi,
} from '@Utils/zarrar/api'
import { useZarrarHub } from '@Utils/zarrar/hub'
import { ViewerStatus, attachViewer, viewProblem } from '@Utils/zarrar/live'
import { playSfx, preloadSfx, setSfxMuted, unlockSfx } from '@Utils/zarrar/sfx'
import classes from '@Styles/zarrar/Stage.module.css'

/* ============================================================================================ */
/*  Artboard scaling                                                                            */
/* ============================================================================================ */

const W = 1920
const H = 1080

/** Fits the fixed 1920×1080 artboard into whatever box the stage is given. */
const ScaledArtboard: FC<{ children: ReactNode; className?: string }> = ({ children, className }) => {
  const outer = useRef<HTMLDivElement>(null)
  const [box, setBox] = useState({ scale: 1, x: 0, y: 0 })

  useLayoutEffect(() => {
    const el = outer.current
    if (!el) return

    const measure = () => {
      const { width, height } = el.getBoundingClientRect()
      const scale = Math.min(width / W, height / H) || 1
      setBox({ scale, x: (width - W * scale) / 2, y: (height - H * scale) / 2 })
    }

    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(el)
    return () => observer.disconnect()
  }, [])

  return (
    <div ref={outer} className={classes.viewport}>
      <div
        className={`${classes.artboard} ${className ?? ''}`}
        style={{ transform: `translate(${box.x}px, ${box.y}px) scale(${box.scale})` }}
      >
        {children}
      </div>
    </div>
  )
}

/* ============================================================================================ */
/*  Data                                                                                        */
/* ============================================================================================ */

const useStageSnapshot = (gameId: number) => {
  const [snapshot, setSnapshot] = useState<DuelSnapshot | null>(null)
  const [error, setError] = useState<string | null>(null)
  const offset = useRef(0)
  const inflight = useRef(false)

  const refresh = useCallback(async () => {
    if (inflight.current) return
    inflight.current = true
    try {
      const data = await zarrarApi.state(gameId)
      offset.current = data.serverTimeUtc - Date.now()
      setSnapshot(data)
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Cannot reach the server')
    } finally {
      inflight.current = false
    }
  }, [gameId])

  useEffect(() => {
    refresh()
    const timer = window.setInterval(refresh, 8000)
    return () => window.clearInterval(timer)
  }, [refresh])

  // Events can arrive in bursts (capture + accepted, a run of flags): one fetch per burst.
  const soonTimer = useRef<number | null>(null)
  const refreshSoon = useCallback(() => {
    if (soonTimer.current !== null) return
    soonTimer.current = window.setTimeout(() => {
      soonTimer.current = null
      refresh()
    }, 600)
  }, [refresh])
  useEffect(
    () => () => {
      if (soonTimer.current !== null) window.clearTimeout(soonTimer.current)
    },
    []
  )

  const setDuel = useCallback((duel: DuelState) => {
    setSnapshot((prev) => (prev ? { ...prev, duel } : prev))
  }, [])

  return { snapshot, error, refresh, refreshSoon, setDuel, offset }
}

const useNow = (interval = 250) => {
  const [now, setNow] = useState(Date.now())
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), interval)
    return () => window.clearInterval(timer)
  }, [interval])
  return now
}

/** Counts a number up/down to its new value so score changes are felt, not just seen. */
const useTween = (value: number, duration = 900) => {
  const [shown, setShown] = useState(value)
  const from = useRef(value)

  useEffect(() => {
    const start = performance.now()
    const begin = from.current
    let frame = 0

    const step = (t: number) => {
      const k = Math.min(1, (t - start) / duration)
      const eased = 1 - Math.pow(1 - k, 3)
      const next = Math.round(begin + (value - begin) * eased)
      setShown(next)
      from.current = next
      if (k < 1) frame = requestAnimationFrame(step)
    }

    frame = requestAnimationFrame(step)
    return () => cancelAnimationFrame(frame)
  }, [value, duration])

  return shown
}

const pad = (n: number) => String(Math.max(0, Math.floor(n))).padStart(2, '0')

const formatClock = (ms: number) => {
  const total = Math.max(0, Math.floor(ms / 1000))
  return `${pad(total / 3600)}:${pad((total % 3600) / 60)}:${pad(total % 60)}`
}

const MASCOT: Record<string, string> = {
  p1: '/assets/zarrar/p1-blue.jpg',
  p2: '/assets/zarrar/p2-green.jpg',
}

/* ============================================================================================ */
/*  Feed panel                                                                                  */
/* ============================================================================================ */

interface PanelBox {
  left: number
  top: number
  width: number
  compact: boolean
  hidden: boolean
  /** Full-screen focus: this player's screen fills the whole stage, pixel for pixel. */
  full: boolean
}

/** Hooded-operative glyph for the PLAYER tag. */
const SlotIcon: FC = () => (
  <svg className={classes.slotIcon} viewBox="0 0 24 24" aria-hidden="true">
    <path d="M12 2.5 20.5 7v10L12 21.5 3.5 17V7z" fill="none" stroke="currentColor" strokeWidth="1.8" />
    <path d="M12 6.5c-2.6 0-4.2 2-4.2 4.6 0 1.3.4 2.4 1 3.2L12 16l3.2-1.7c.6-.8 1-1.9 1-3.2 0-2.6-1.6-4.6-4.2-4.6z" fill="currentColor" />
  </svg>
)

const FeedPanel: FC<{
  gameId: number
  player: DuelPlayer
  side: 'p1' | 'p2'
  box: PanelBox
  flash: 'win' | 'lose' | null
}> = ({ gameId, player, side, box, flash }) => {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const [status, setStatus] = useState<ViewerStatus>('connecting')
  const score = useTween(player.score)
  const [bump, setBump] = useState(false)
  const lastScore = useRef(player.score)

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    return attachViewer(canvas, gameId, player.participationId, setStatus)
  }, [gameId, player.participationId])

  useEffect(() => {
    if (player.score > lastScore.current) {
      setBump(true)
      const timer = window.setTimeout(() => setBump(false), 950)
      lastScore.current = player.score
      return () => window.clearTimeout(timer)
    }
    lastScore.current = player.score
  }, [player.score])

  const live = status === 'live'
  // Tag team: the handle is whoever is on the keyboard. A tagged-in player who has not shared
  // yet is a swap in progress, not a lost feed.
  const handle = player.operatorName ?? player.members.find((m) => m !== player.teamName) ?? player.members[0]
  const swapping = !!player.operatorId && player.liveUserId !== player.operatorId
  const tagTeam = (player.roster?.length ?? 0) > 1
  const hitRate = player.attempts > 0 ? Math.round((player.solvedCount / player.attempts) * 100) : null

  const panelClass = [
    classes.panel,
    side === 'p2' ? classes.panelP2 : '',
    box.compact ? classes.compact : '',
    box.full ? classes.full : '',
    status === 'offline' && !swapping ? classes.feedLost : '',
    flash === 'win' ? classes.flashWin : '',
    flash === 'lose' ? classes.flashLose : '',
  ].join(' ')

  const style: CSSProperties = {
    left: box.left,
    top: box.top,
    width: box.width,
    opacity: box.hidden ? 0 : 1,
    transform: box.hidden ? 'translateY(40px) scale(0.96)' : 'none',
  }

  return (
    <section className={panelClass} style={style} aria-label={`${side === 'p1' ? 'Player 1' : 'Player 2'} screen`}>
      <div className={classes.frame} aria-hidden="true" />

      <header className={classes.panelHead}>
        <span className={classes.slotTag}>
          <SlotIcon />
          {side === 'p1' ? 'PLAYER 1' : 'PLAYER 2'}
        </span>
        <span className={classes.headRight}>
          <span className={live ? classes.liveBadge : classes.offBadge}>
            <span className={classes.badgeDot} />
            {live ? 'LIVE' : 'OFFLINE'}
          </span>
          <span className={`${classes.uplink} ${live ? classes.uplinkOn : ''}`}>
            <span className={classes.bars} aria-hidden="true">
              <i />
              <i />
              <i />
              <i />
            </span>
            {live ? 'UPLINK SECURE' : swapping ? 'TAG IN' : status === 'offline' ? 'NO SIGNAL' : 'LINKING'}
          </span>
        </span>
      </header>

      {/* Nothing is drawn over the picture: the feed is the player's real screen, so it stays clean. */}
      <div className={classes.feedBox}>
        <canvas ref={canvasRef} className={classes.feedCanvas} width={1280} height={720} />
        {box.full && live && (
          <div className={classes.fullTag}>
            <span className={classes.fullSlot}>{side === 'p1' ? 'P1' : 'P2'}</span>
            <span className={classes.fullName}>{player.teamName}</span>
            {handle && <span className={classes.fullHandle}>@{handle}</span>}
            <span className={classes.fullScore}>{player.score}</span>
          </div>
        )}
        {!live &&
          (swapping ? (
            <div className={`${classes.feedStatus} ${classes.feedSwap}`}>
              <div className={classes.feedStatusTitle}>TAG IN</div>
              <div className={classes.feedStatusSub}>
                {player.operatorName ?? 'New player'} is taking the keyboard — waiting for their screen
              </div>
            </div>
          ) : (
            <div className={classes.feedStatus}>
              <div className={classes.feedStatusTitle}>
                {status === 'offline' ? 'FEED LOST' : status === 'waiting' ? 'SYNCING…' : 'CONNECTING…'}
              </div>
              <div className={classes.feedStatusSub}>
                {status === 'offline' ? `${player.teamName} is not sharing their screen` : 'Waiting for the first frame'}
              </div>
            </div>
          ))}
      </div>

      <div className={classes.card}>
        <div className={classes.avatar}>
          <img src={MASCOT[side]} alt="" />
        </div>
        <div className={classes.who}>
          <div className={classes.name}>{player.teamName}</div>
          <div className={classes.handle}>
            {handle ? `@${handle}` : '\u00a0'}
            {tagTeam && <span className={classes.tagChip}>TAG TEAM</span>}
          </div>
        </div>
        <div className={classes.stats}>
          {/* Hit rate on the outside, score next to the VS so the two scores face off. */}
          <div className={`${classes.stat} ${classes.statMinor}`}>
            <div className={classes.statLabel}>HIT RATE</div>
            <div className={classes.statValue}>{hitRate == null ? '—' : `${hitRate}%`}</div>
            <div className={classes.statSub}>
              ({player.solvedCount}/{player.attempts})
            </div>
          </div>
          <div className={classes.stat}>
            <div className={classes.statLabel}>SCORE</div>
            <div className={`${classes.score} ${bump ? classes.scoreBump : ''}`}>{score}</div>
          </div>
        </div>
      </div>
    </section>
  )
}

/** Tug-of-war bar: each side's share of the points, clashing at a white-hot seam. */
const Dominance: FC<{ p1?: DuelPlayer; p2?: DuelPlayer }> = ({ p1, p2 }) => {
  const a = p1?.score ?? 0
  const b = p2?.score ?? 0
  const share = a + b > 0 ? a / (a + b) : 0.5
  const pct = Math.round(share * 100)

  return (
    <div className={classes.dominance} aria-label={`Points share ${pct} to ${100 - pct}`}>
      <span className={classes.domPct} style={{ color: 'var(--p1)' }}>
        {pct}%
      </span>
      <div className={classes.domTrack}>
        <div className={classes.domP1} style={{ width: `${share * 100}%` }} />
        <div className={classes.domP2} style={{ width: `${(1 - share) * 100}%` }} />
        <div className={classes.domClash} style={{ left: `${share * 100}%` }} />
      </div>
      <span className={classes.domPct} style={{ color: 'var(--p2)' }}>
        {100 - pct}%
      </span>
    </div>
  )
}

/** The crackling seam between the two feeds. */
const Seam: FC<{ on: boolean }> = ({ on }) => (
  <div className={classes.seam} style={{ opacity: on ? 1 : 0 }} aria-hidden="true">
    <svg viewBox="0 0 40 560" preserveAspectRatio="none">
      <polyline className={classes.bolt} points="20,0 14,60 26,118 12,190 27,250 15,320 25,380 13,450 24,510 20,560" />
      <polyline
        className={`${classes.bolt} ${classes.bolt2}`}
        points="20,0 27,70 13,130 26,200 11,262 24,330 14,392 27,455 15,515 20,560"
      />
    </svg>
  </div>
)

/* ============================================================================================ */
/*  Takeovers                                                                                   */
/* ============================================================================================ */

type Takeover = StageEvent & { key: number; side: 'p1' | 'p2' | null }

const TAKEOVER_MS: Partial<Record<StageEvent['type'], number>> = {
  countdown: 4300,
  accepted: 4400,
  denied: 2400,
  released: 4000,
  feedLost: 2600,
  feedRestored: 2200,
  resumed: 2200,
  withdrawn: 2600,
  tagged: 3400,
}

const COUNT_STEPS = ['3', '2', '1', 'FIGHT']

/** Takeovers allowed to wait in line before low-value ones (wrong flags) are dropped. */
const MAX_QUEUED = 4

/** "Start match": 3, 2, 1 slam in one per second, then FIGHT floods the screen. */
const CountdownView: FC<{ names: [string, string] }> = ({ names }) => {
  const [step, setStep] = useState(0)

  useEffect(() => {
    playSfx('count')
    const timers = [1, 2, 3].map((i) =>
      window.setTimeout(() => {
        setStep(i)
        playSfx(i === 3 ? 'fight' : 'count')
      }, i * 1000)
    )
    return () => timers.forEach((t) => window.clearTimeout(t))
  }, [])

  const fight = step === COUNT_STEPS.length - 1

  return (
    <div className={`${classes.takeover} ${classes.countdown} ${fight ? classes.countdownFight : ''}`}>
      <div className={classes.countKicker}>{fight ? 'MATCH LIVE' : 'MATCH STARTS IN'}</div>
      <div key={step} className={fight ? classes.countFight : classes.countNum}>
        {COUNT_STEPS[step]}
      </div>
      <div className={classes.countVs}>
        <span style={{ color: 'var(--p1)' }}>{names[0]}</span>
        <span className={classes.countVsMid}>VS</span>
        <span style={{ color: 'var(--p2)' }}>{names[1]}</span>
      </div>
    </div>
  )
}

const TakeoverView: FC<{ item: Takeover; player?: DuelPlayer; names: [string, string] }> = ({
  item,
  player,
  names,
}) => {
  const name = player?.teamName ?? item.teamName ?? 'UNKNOWN'

  switch (item.type) {
    case 'countdown':
      return <CountdownView names={names} />
    case 'accepted':
      return (
        <div className={`${classes.takeover} ${classes.capture} ${item.side === 'p2' ? classes.captureP2 : ''}`}>
          <div className={classes.rays} />
          <div className={classes.ring} />
          <div className={`${classes.ring} ${classes.ring2}`} />
          <div className={`${classes.ring} ${classes.ring3}`} />
          <div className={classes.takeKicker}>{item.side === 'p2' ? 'PLAYER 2' : 'PLAYER 1'}</div>
          <div className={classes.takeTitle}>FLAG CAPTURED</div>
          <div className={classes.takeWho}>{name}</div>
          <div className={classes.takeDetail}>
            <span>{item.challengeTitle}</span>
            {item.seconds != null ? <span className={classes.takeTime}>IN {formatDuration(item.seconds)}</span> : null}
            {item.points ? <span className={classes.takePoints}>+{item.points}</span> : null}
          </div>
        </div>
      )
    case 'denied':
      return (
        <div className={`${classes.takeover} ${classes.denied}`}>
          <div className={classes.deniedBand}>
            <div className={classes.deniedTitle}>ACCESS DENIED</div>
            <div className={classes.deniedSub}>
              {name} — WRONG FLAG{item.challengeTitle ? ` ON ${item.challengeTitle.toUpperCase()}` : ''}
            </div>
          </div>
        </div>
      )
    case 'released':
      return (
        <div className={`${classes.takeover} ${classes.release}`}>
          <div className={classes.releaseKicker}>NEW TARGET UNLOCKED</div>
          <div className={classes.releaseTitle}>{item.challengeTitle}</div>
          {item.points ? <div className={classes.releaseMeta}>{item.points} POINTS ON THE LINE</div> : null}
        </div>
      )
    case 'tagged':
      return (
        <div className={`${classes.takeover} ${classes.tag} ${item.side === 'p2' ? classes.tagP2 : ''}`}>
          <div className={classes.tagSlab} />
          <div className={classes.tagKicker}>
            {item.side === 'p2' ? 'PLAYER 2' : 'PLAYER 1'} // {name}
          </div>
          <div className={classes.tagTitle}>TAG IN</div>
          <div className={classes.tagSwap}>
            {item.message && (
              <>
                <span className={classes.tagOut}>{item.message}</span>
                <span className={classes.tagArrow}>⇄</span>
              </>
            )}
            <span className={classes.tagIn}>{item.userName ?? 'NEW PLAYER'}</span>
          </div>
        </div>
      )
    case 'feedLost':
      return <div className={classes.banner}>FEED LOST — {name}</div>
    case 'feedRestored':
      return <div className={`${classes.banner} ${classes.bannerOk}`}>FEED RESTORED — {name}</div>
    case 'resumed':
      return <div className={`${classes.banner} ${classes.bannerOk}`}>MATCH RESUMED</div>
    case 'withdrawn':
      return (
        <div className={`${classes.banner} ${classes.bannerWarn}`}>
          TARGET WITHDRAWN{item.challengeTitle ? ` — ${item.challengeTitle.toUpperCase()}` : ''}
        </div>
      )
    default:
      return null
  }
}

const SFX_FOR: Partial<Record<StageEvent['type'], Parameters<typeof playSfx>[0]>> = {
  accepted: 'accepted',
  denied: 'denied',
  released: 'released',
  feedLost: 'alert',
  paused: 'alert',
  resumed: 'resume',
  ended: 'winner',
  tagged: 'fight',
}

/* ============================================================================================ */
/*  Stage                                                                                       */
/* ============================================================================================ */

export interface StageProps {
  gameId: number
  /** Embedded in the admin page: no "enter" gate, sound off until toggled, cursor visible. */
  embedded?: boolean
  /** Sound on/off (embedded only; the projector stage always has sound after entering). */
  sound?: boolean
}

const TICKER_TEXT = (e: StageEvent, name: string): [string, string] => {
  switch (e.type) {
    case 'countdown':
      return ['Match started — FIGHT', 'var(--red)']
    case 'accepted':
      return [
        `${name} captured ${e.challengeTitle ?? 'a challenge'}${e.points ? ` +${e.points}` : ''}${
          e.seconds != null ? ` in ${formatDuration(e.seconds)}` : ''
        }`,
        'var(--p2)',
      ]
    case 'withdrawn':
      return [`${e.challengeTitle ?? 'Target'} withdrawn — no winner`, 'var(--amber)']
    case 'denied':
      return [`${name} — wrong flag on ${e.challengeTitle ?? 'a challenge'}`, 'var(--red)']
    case 'released':
      return [`New target: ${e.challengeTitle ?? ''}${e.points ? ` (${e.points} pts)` : ''}`, 'var(--ink)']
    case 'feedLost':
      return [`${name} feed lost`, 'var(--amber)']
    case 'feedRestored':
      return [`${name} feed restored`, 'var(--p2)']
    case 'paused':
      return [`Match paused${e.message ? ` — ${e.message}` : ''}`, 'var(--amber)']
    case 'resumed':
      return [`Match resumed${e.message ? ` (${e.message})` : ''}`, 'var(--p2)']
    case 'ended':
      return [`Match over${e.teamName ? ` — ${e.teamName} wins` : ''}`, 'var(--red)']
    case 'tagged':
      return [`${name} tag in — ${e.message ? `${e.message} out, ` : ''}${e.userName ?? 'new player'} in`, 'var(--amber)']
    default:
      return ['', 'var(--ink)']
  }
}

export const Stage: FC<StageProps> = ({ gameId, embedded = false, sound = false }) => {
  const { snapshot, error, refresh, refreshSoon, setDuel, offset } = useStageSnapshot(gameId)
  const now = useNow(250)

  const [entered, setEntered] = useState(embedded)
  const [feedProblem] = useState(viewProblem)
  const [ticker, setTicker] = useState<StageEvent[]>([])
  const [queue, setQueue] = useState<Takeover[]>([])
  const [current, setCurrent] = useState<Takeover | null>(null)
  const [flash, setFlash] = useState<{ pid: number; kind: 'win' | 'lose'; key: number } | null>(null)
  const shakeRef = useRef<HTMLDivElement>(null)
  const [cursorIdle, setCursorIdle] = useState(false)
  const keyRef = useRef(0)

  const players = snapshot?.players ?? []
  const p1 = players[0]
  const p2 = players[1]

  const sideOf = useCallback(
    (pid?: number | null): 'p1' | 'p2' | null => (pid == null ? null : pid === p1?.participationId ? 'p1' : pid === p2?.participationId ? 'p2' : null),
    [p1?.participationId, p2?.participationId]
  )

  // Sound: the projector stage plays after "enter"; the admin preview follows its toggle.
  useEffect(() => {
    setSfxMuted(embedded ? !sound : !entered)
  }, [embedded, sound, entered])

  useEffect(() => {
    preloadSfx()
  }, [])

  const onEvent = useCallback(
    (e: StageEvent) => {
      if (e.gameId !== gameId) return

      if (e.type !== 'captured') setTicker((prev) => [e, ...prev].slice(0, 14))

      if (TAKEOVER_MS[e.type]) {
        keyRef.current += 1
        const item = { ...e, key: keyRef.current, side: sideOf(e.participationId) }
        setQueue((prev) => {
          // A run of wrong flags must not hold the projector hostage: at most one pending
          // ACCESS DENIED per player, and never more than a few takeovers waiting overall
          // (dropping wrong flags first). Captures, releases and tag-ins are never dropped.
          if (item.type === 'denied' && prev.some((q) => q.type === 'denied' && q.participationId === item.participationId))
            return prev
          const next = [...prev, item]
          while (next.length > MAX_QUEUED) {
            const drop = next.findIndex((q) => q.type === 'denied' || q.type === 'feedRestored')
            if (drop < 0) break
            next.splice(drop, 1)
          }
          return next
        })
      } else {
        // Persistent states (paused / ended) render from duel state; they only need their sound.
        const sfx = SFX_FOR[e.type]
        if (sfx) playSfx(sfx)
      }

      if (
        [
          'accepted',
          'captured',
          'released',
          'withdrawn',
          'countdown',
          'feedLost',
          'feedRestored',
          'resumed',
          'ended',
          'paused',
          'tagged',
        ].includes(
          e.type
        )
      )
        refreshSoon()
    },
    [gameId, refreshSoon, sideOf]
  )

  useZarrarHub(
    gameId,
    {
      onState: setDuel,
      onEvent,
      onReconnected: refresh,
    },
    true,
    !embedded
  )

  const shakeScreen = () => {
    const el = shakeRef.current
    if (!el) return
    el.classList.remove(classes.shake)
    void el.offsetWidth // restart the animation
    el.classList.add(classes.shake)
  }

  // Play takeovers one at a time so two quick flags never collide on screen.
  useEffect(() => {
    if (current || queue.length === 0) return
    const [next, ...rest] = queue
    setQueue(rest)
    setCurrent(next)

    const sfx = SFX_FOR[next.type]
    if (sfx) playSfx(sfx)

    if (next.type === 'accepted' || next.type === 'denied') {
      shakeScreen()
      if (next.participationId)
        setFlash({ pid: next.participationId, kind: next.type === 'accepted' ? 'win' : 'lose', key: next.key })
    }
  }, [current, queue])

  useEffect(() => {
    if (!current) return
    const timer = window.setTimeout(() => setCurrent(null), TAKEOVER_MS[current.type] ?? 2500)
    return () => window.clearTimeout(timer)
  }, [current])

  useEffect(() => {
    if (!flash) return
    const timer = window.setTimeout(() => setFlash(null), 1500)
    return () => window.clearTimeout(timer)
  }, [flash])

  // Hide the cursor on the projector when the mouse is still.
  useEffect(() => {
    if (embedded) return
    let timer = window.setTimeout(() => setCursorIdle(true), 2000)
    const onMove = () => {
      setCursorIdle(false)
      window.clearTimeout(timer)
      timer = window.setTimeout(() => setCursorIdle(true), 2000)
    }
    window.addEventListener('mousemove', onMove)
    return () => {
      window.removeEventListener('mousemove', onMove)
      window.clearTimeout(timer)
    }
  }, [embedded])

  const enter = async () => {
    await unlockSfx()
    setEntered(true)
    try {
      await document.documentElement.requestFullscreen?.()
    } catch {
      /* fullscreen refused: the stage still works in a maximised window */
    }
  }

  const duel = snapshot?.duel
  const layout = duel?.ended ? 'board' : (duel?.layout ?? 'split')

  /* ------------------------------------------------------------------ positions */

  // Panels hold header + 16:9 feed + player card. Split: two 900px columns with a 40px seam.
  const boxes = useMemo((): { p1: PanelBox; p2: PanelBox; timer: CSSProperties; vs: boolean } => {
    const show = (left: number, top: number, width: number, compact = false, full = false): PanelBox => ({
      left,
      top,
      width,
      compact,
      hidden: false,
      full,
    })
    const hide = (b: PanelBox): PanelBox => ({ ...b, hidden: true })

    switch (layout) {
      case 'p1':
        return {
          p1: show(40, 140, 1220),
          p2: show(1290, 330, 590, true),
          timer: { left: 1290, top: 112, width: 590 },
          vs: false,
        }
      case 'p2':
        return {
          p1: show(40, 330, 590, true),
          p2: show(660, 140, 1220),
          timer: { left: 40, top: 112, width: 590 },
          vs: false,
        }
      // Full screen: 1920×1080 on a 1920×1080 artboard, so a 1080p screen is shown pixel for pixel.
      case 'f1':
        return {
          p1: show(0, 0, 1920, false, true),
          p2: hide(show(980, 300, 900)),
          timer: { left: 460, top: 112, width: 1000 },
          vs: false,
        }
      case 'f2':
        return {
          p1: hide(show(40, 300, 900)),
          p2: show(0, 0, 1920, false, true),
          timer: { left: 460, top: 112, width: 1000 },
          vs: false,
        }
      case 'board':
        return {
          p1: hide(show(40, 300, 900)),
          p2: hide(show(980, 300, 900)),
          timer: { left: 460, top: 112, width: 1000 },
          vs: false,
        }
      default:
        return {
          p1: show(40, 300, 900),
          p2: show(980, 300, 900),
          timer: { left: 460, top: 112, width: 1000 },
          vs: true,
        }
    }
  }, [layout])

  /* ---------------------------------------------------------------------- clock */

  const serverNow = now + offset.current
  const challenges = snapshot?.challenges ?? []
  const released = challenges.filter((c) => c.status === 'live' || c.status === 'captured')
  const liveTarget =
    challenges.find((c) => c.id === duel?.currentChallengeId && c.status === 'live') ??
    challenges.find((c) => c.status === 'live')

  let clockMs = 0
  let clockLabel = 'MATCH TIME'
  if (snapshot) {
    if (duel?.paused && duel.pausedAtUtc) {
      clockMs = snapshot.endUtc - duel.pausedAtUtc
      clockLabel = 'PAUSED'
    } else if (serverNow < snapshot.startUtc) {
      clockMs = snapshot.startUtc - serverNow
      clockLabel = 'STARTS IN'
    } else {
      clockMs = snapshot.endUtc - serverNow
      clockLabel = clockMs <= 0 ? 'MATCH OVER' : 'TIME LEFT'
    }
  }
  const critical = !duel?.paused && clockLabel === 'TIME LEFT' && clockMs < 60_000
  const targetSeconds = liveTarget ? liveSecondsOf(duel, liveTarget.id, serverNow) : null

  const winner = useMemo(() => {
    if (!p1 || !p2) return p1 ?? null
    if (p1.score === p2.score) return null
    return p1.score > p2.score ? p1 : p2
  }, [p1, p2])

  const tickerItems = ticker.length
    ? ticker
    : [
        {
          type: 'released',
          gameId,
          challengeTitle: liveTarget?.title ?? 'Waiting for the first target',
          points: liveTarget?.score,
          timeUtc: 0,
        } as StageEvent,
      ]

  const nameOf = (pid?: number | null, fallback?: string | null) =>
    players.find((p) => p.participationId === pid)?.teamName ?? fallback ?? 'Player'

  const rootClass = !embedded && cursorIdle ? classes.hideCursor : ''

  return (
    <ScaledArtboard className={rootClass}>
      <div className={classes.bg} style={{ backgroundImage: "url('/assets/zarrar/vs-bg.jpg')" }} />
      <div className={classes.bgTint} />
      <div className={classes.floor} />

      <div ref={shakeRef} style={{ position: 'absolute', inset: 0 }}>
        {/* ---------------------------------------------------------------- header */}
        <header className={classes.header}>
          <img className={classes.logo} src="/assets/zarrar/zarrar-logo.png" alt="ZARRAR" />
          <div className={classes.titleBlock}>
            <div className={classes.kicker}>THE ARZENS PRESENT</div>
            <h1 className={classes.title}>CYBER SHOWDOWN</h1>
            <div className={classes.subtitle}>
              1V1 CTF<span className={classes.subSep}>//</span>
              {snapshot?.title ?? 'ZARRAR'}
            </div>
          </div>
          <div className={classes.headerRight}>
            <span className={classes.livePill}>
              <span className={classes.liveDot} />
              LIVE
            </span>
            <span className={classes.roundTag}>
              TARGET <b>{released.length}</b> / {challenges.length || '—'}
            </span>
          </div>
        </header>

        {/* ----------------------------------------------------------------- timer */}
        <div className={classes.timerBlock} style={boxes.timer}>
          <div
            className={`${classes.timerBox} ${critical ? classes.timerCritical : ''} ${duel?.paused ? classes.timerPaused : ''}`}
          >
            <span className={classes.timerLabel}>{clockLabel}</span>
            <span className={classes.timerDigits}>
              {formatClock(clockMs)
                .split('')
                .map((ch, i) => (
                  <span key={i} className={ch === ':' ? classes.colon : classes.digit}>
                    {ch}
                  </span>
                ))}
            </span>
          </div>
          <Dominance p1={p1} p2={p2} />
          <div className={classes.target}>
            <span className={classes.targetKey}>TARGET</span>
            <span className={classes.targetTitle}>{liveTarget?.title ?? 'STANDBY'}</span>
            {liveTarget && <span className={classes.targetPts}>{liveTarget.score} PTS</span>}
            {targetSeconds != null && <span className={classes.targetClock}>{formatDuration(targetSeconds)}</span>}
          </div>
        </div>

        <Seam on={boxes.vs && !!p1 && !!p2} />

        {/* ---------------------------------------------------------------- panels */}
        {p1 && (
          <FeedPanel
            gameId={gameId}
            player={p1}
            side="p1"
            box={boxes.p1}
            flash={flash?.pid === p1.participationId ? flash.kind : null}
          />
        )}
        {p2 && (
          <FeedPanel
            gameId={gameId}
            player={p2}
            side="p2"
            box={boxes.p2}
            flash={flash?.pid === p2.participationId ? flash.kind : null}
          />
        )}

        <div className={classes.vs} style={{ opacity: boxes.vs && p1 && p2 ? 1 : 0 }} aria-hidden="true">
          <span className={classes.vsSlash} />
          <span className={classes.vsText}>VS</span>
        </div>

        {/* ----------------------------------------------------------------- board */}
        <div className={`${classes.board} ${layout === 'board' ? classes.boardOn : ''}`}>
          {[p1, p2].map((p, i) =>
            p ? (
              <div key={p.participationId} className={`${classes.boardSide} ${i === 1 ? classes.boardSideP2 : ''}`}>
                <img className={classes.boardMascot} src={MASCOT[i === 0 ? 'p1' : 'p2']} alt="" />
                <div className={classes.boardName}>{p.teamName}</div>
                <div className={classes.boardScore}>{p.score}</div>
                <div className={classes.boardMeta}>{p.solvedCount} CAPTURED</div>
              </div>
            ) : (
              <div key={i} className={`${classes.boardSide} ${i === 1 ? classes.boardSideP2 : ''}`}>
                <div className={classes.boardName}>AWAITING PLAYER</div>
              </div>
            )
          )}
          <div className={classes.targets} style={{ order: 0, gridColumn: 2, gridRow: 1 }}>
            <div className={classes.targetsTitle}>TARGETS</div>
            {challenges.slice(0, 9).map((c) => {
              const side = sideOf(c.capturedBy)
              const rowClass =
                c.status === 'live'
                  ? classes.targetRowLive
                  : side === 'p1'
                    ? classes.targetRowP1
                    : side === 'p2'
                      ? classes.targetRowP2
                      : ''
              return (
                <div key={c.id} className={`${classes.targetRow} ${rowClass}`}>
                  <span>{c.status === 'queued' ? 'LOCKED' : c.title}</span>
                  <span className={classes.targetState}>
                    {c.status === 'captured'
                      ? (c.capturedByName ?? 'CAPTURED')
                      : c.status === 'live'
                        ? 'LIVE'
                        : c.status === 'withdrawn'
                          ? 'NO WINNER'
                          : `${c.score}`}
                  </span>
                </div>
              )
            })}
          </div>
        </div>

        {/* ---------------------------------------------------------------- ticker */}
        <div className={classes.ticker}>
          <div className={classes.tickerTag}>
            <span className={classes.liveDot} />
            LIVE EVENTS
          </div>
          <div className={classes.tickerTrack}>
            <div
              className={classes.tickerMove}
              style={{ ['--ticker-duration' as string]: `${Math.max(20, tickerItems.length * 9)}s` }}
            >
              {[...tickerItems, ...tickerItems].map((e, i) => {
                const [text, base] = TICKER_TEXT(e, nameOf(e.participationId, e.teamName))
                const side = sideOf(e.participationId)
                const color = e.type === 'accepted' && side ? `var(--${side})` : base
                return (
                  <span key={i} className={classes.tickerItem} style={{ ['--dot' as string]: color }}>
                    {text}
                  </span>
                )
              })}
            </div>
          </div>
        </div>
      </div>

      {/* ------------------------------------------------------- persistent layers */}
      {duel?.paused && !duel.ended && (
        <div className={classes.pausedLayer}>
          <div className={classes.pausedTitle}>MATCH PAUSED</div>
          {duel.pauseReason && <div className={classes.pausedReason}>{duel.pauseReason}</div>}
        </div>
      )}

      {duel?.ended && (
        <div
          className={`${classes.winnerLayer} ${winner ? (winner === p2 ? classes.winnerP2 : '') : classes.winnerDraw}`}
        >
          {winner && <img className={classes.winnerMascot} src={MASCOT[winner === p2 ? 'p2' : 'p1']} alt="" />}
          <div className={classes.winnerKicker}>{winner ? 'WINNER' : 'DRAW'}</div>
          <div className={classes.winnerName}>{winner ? winner.teamName : `${p1?.score ?? 0} : ${p2?.score ?? 0}`}</div>
          {winner && (
            <div className={classes.winnerScore}>
              {p1?.score ?? 0} — {p2?.score ?? 0}
            </div>
          )}
        </div>
      )}

      {current && (
        <TakeoverView
          key={current.key}
          item={current}
          player={players.find((p) => p.participationId === current.participationId)}
          names={[p1?.teamName ?? 'PLAYER 1', p2?.teamName ?? 'PLAYER 2']}
        />
      )}

      <div className={classes.vignette} />
      <div className={classes.scanlines} />

      {error && <div className={classes.connLost}>RECONNECTING TO SERVER…</div>}
      {feedProblem && <div className={classes.setupWarn}>{feedProblem}</div>}

      {!entered && (
        <div className={classes.gate}>
          <img className={classes.gateLogo} src="/assets/zarrar/zarrar-logo.png" alt="ZARRAR" />
          <button type="button" className={classes.gateButton} onClick={enter}>
            ENTER THE ARENA
          </button>
          <p className={classes.gateNote}>
            Starts full screen with sound. Share this screen to the projector. Press Esc to leave full screen.
          </p>
        </div>
      )}
    </ScaledArtboard>
  )
}

export default Stage

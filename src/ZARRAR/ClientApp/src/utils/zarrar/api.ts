/**
 * ZARRAR showdown API.
 *
 * These endpoints are new and not in the generated `Api.ts` (regenerating it needs the backend
 * running), so they are called with a small fetch wrapper. Same origin, so the auth cookie rides
 * along; mutations send JSON with an Origin header, which the CSRF middleware accepts.
 */

/** split: both screens · p1/p2: focus one · board: scores · f1/f2: one screen full size */
export type DuelLayout = 'split' | 'p1' | 'p2' | 'board' | 'f1' | 'f2'

export interface DuelState {
  gameId: number
  enabled: boolean
  paused: boolean
  pauseReason?: string | null
  pausedAtUtc?: number | null
  ended: boolean
  layout: DuelLayout
  currentChallengeId?: number | null
  withdrawn: number[]
  /** When the admin pressed "Start match" */
  startedUtc?: number | null
  /** Match clock (seconds, pauses excluded): banked seconds plus time since clockSinceUtc */
  clockSeconds: number
  clockSinceUtc?: number | null
  /** Match-clock reading when each challenge was released (keys are challenge IDs) */
  releasedAtClock: Record<string, number>
  /** Tag-team: participation ID → user ID of the member on the keyboard */
  operators?: Record<string, string>
  updatedUtc: number
}

export type StageEventType =
  | 'accepted'
  | 'denied'
  | 'released'
  | 'captured'
  | 'withdrawn'
  | 'countdown'
  | 'feedLost'
  | 'feedRestored'
  | 'paused'
  | 'resumed'
  | 'ended'
  | 'tagged'

export interface StageEvent {
  type: StageEventType
  gameId: number
  participationId?: number | null
  teamName?: string | null
  userName?: string | null
  challengeId?: number | null
  challengeTitle?: string | null
  points?: number | null
  message?: string | null
  /** Seconds the challenge was live before this capture (pauses excluded) */
  seconds?: number | null
  timeUtc: number
}

export interface DuelPlayer {
  slot: string
  participationId: number
  teamId: number
  teamName: string
  avatar?: string | null
  members: string[]
  score: number
  rank: number
  solvedCount: number
  /** Flags submitted in this match, right and wrong */
  attempts: number
  live: boolean
  /** Everyone on the team who joined the match (tag-in picker) */
  roster?: DuelMember[]
  /** Member the monitor tagged in; null means whoever streams plays */
  operatorId?: string | null
  /** Member streaming right now */
  liveUserId?: string | null
  /** Name of the player on the keyboard (tagged in, else streaming) */
  operatorName?: string | null
}

export interface DuelMember {
  userId: string
  userName: string
  avatar?: string | null
}

export interface DuelChallenge {
  id: number
  title: string
  category: string
  score: number
  status: 'queued' | 'live' | 'captured' | 'withdrawn'
  capturedBy?: number | null
  capturedByName?: string | null
  capturedAtUtc?: number | null
}

export interface DuelSnapshot {
  gameId: number
  title: string
  startUtc: number
  endUtc: number
  serverTimeUtc: number
  duel: DuelState
  players: DuelPlayer[]
  challenges: DuelChallenge[]
  /** Monitor stages (projector screens) currently showing this match */
  stageScreens: number
  /** Pre-flight checklist (admins only) */
  readiness?: DuelReadiness[] | null
}

export interface DuelReadiness {
  key: string
  label: string
  ok: boolean
  /** Blocks "Start match" and "Release" until fixed or overridden */
  critical: boolean
  detail: string
}

export interface DuelRecording {
  name: string
  bytes: number
  modifiedUtc: number
  participationId?: number | null
}

export interface DuelGame {
  id: number
  title: string
  startUtc: number
  endUtc: number
  duel: boolean
  paused: boolean
  ended: boolean
  players: number
}

export interface DuelPlayerStatus {
  duel: boolean
  paused: boolean
  pauseReason?: string | null
  ended: boolean
  live: boolean
  /** A teammate is tagged in: this player may not stream or submit */
  benched?: boolean
  operatorName?: string | null
  teamName?: string | null
}

export class ZarrarApiError extends Error {
  status: number

  constructor(message: string, status: number) {
    super(message)
    this.status = status
  }
}

/** A request that hangs (Wi-Fi blip, server restarting) fails after this instead of forever. */
const REQUEST_TIMEOUT_MS = 15_000

const request = async <T>(method: string, path: string, body?: unknown): Promise<T> => {
  const controller = new AbortController()
  const timer = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS)

  let res: Response
  try {
    res = await fetch(path, {
      method,
      credentials: 'same-origin',
      headers: body === undefined ? { Accept: 'application/json' } : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
      signal: controller.signal,
    })
  } catch {
    throw new ZarrarApiError('Cannot reach the server', 0)
  } finally {
    window.clearTimeout(timer)
  }

  const text = await res.text().catch(() => '')
  let data: unknown = undefined
  if (text) {
    try {
      data = JSON.parse(text)
    } catch {
      // A proxy error page (HTML) or a truncated body: report the status, not a parse error.
      data = undefined
    }
  }

  if (!res.ok) {
    const title =
      res.status === 429
        ? 'Too many requests — wait a few seconds'
        : ((data as { title?: string } | undefined)?.title ?? (res.statusText || `HTTP ${res.status}`))
    throw new ZarrarApiError(title, res.status)
  }

  return data as T
}

export const zarrarApi = {
  games: () => request<DuelGame[]>('GET', '/api/zarrar/games'),
  state: (gameId: number) => request<DuelSnapshot>('GET', `/api/zarrar/${gameId}/state`),
  me: (gameId: number) => request<DuelPlayerStatus>('GET', `/api/zarrar/${gameId}/me`),
  setMode: (gameId: number, enabled: boolean) => request<DuelState>('PUT', `/api/zarrar/${gameId}/mode`, { enabled }),
  setLayout: (gameId: number, layout: DuelLayout) =>
    request<DuelState>('PUT', `/api/zarrar/${gameId}/layout`, { layout }),
  pause: (gameId: number) => request<DuelState>('POST', `/api/zarrar/${gameId}/pause`),
  resume: (gameId: number) => request<DuelState>('POST', `/api/zarrar/${gameId}/resume`),
  end: (gameId: number) => request<DuelState>('POST', `/api/zarrar/${gameId}/end`),
  reopen: (gameId: number) => request<DuelState>('POST', `/api/zarrar/${gameId}/reopen`),
  announce: (gameId: number, challengeId: number) =>
    request<void>('POST', `/api/zarrar/${gameId}/announce`, { challengeId }),
  withdraw: (gameId: number, challengeId: number) =>
    request<DuelState>('POST', `/api/zarrar/${gameId}/withdraw`, { challengeId }),
  start: (gameId: number) => request<DuelState>('POST', `/api/zarrar/${gameId}/start`),
  /** Tag-in: put a team member on the keyboard (null clears it). Monitors and admins. */
  setOperator: (gameId: number, participationId: number, userId: string | null) =>
    request<DuelState>('PUT', `/api/zarrar/${gameId}/operator`, { participationId, userId }),
  timeline: (gameId: number) => request<StageEvent[]>('GET', `/api/zarrar/${gameId}/timeline`),
  recordings: (gameId: number) => request<DuelRecording[]>('GET', `/api/zarrar/${gameId}/recordings`),
  recordingUrl: (gameId: number, name: string) => `/api/zarrar/${gameId}/recordings/${encodeURIComponent(name)}`,
}

/** Match-clock reading in seconds at `serverNow` (ms). The clock stops while paused or ended. */
export const clockAt = (duel: DuelState | null | undefined, serverNow: number) => {
  if (!duel) return 0
  const running = duel.clockSinceUtc ? Math.max(0, (serverNow - duel.clockSinceUtc) / 1000) : 0
  return (duel.clockSeconds ?? 0) + running
}

/** Seconds a challenge has been live (pauses excluded), or null if it was never released. */
export const liveSecondsOf = (duel: DuelState | null | undefined, challengeId: number, serverNow: number) => {
  const at = duel?.releasedAtClock?.[String(challengeId)]
  return at === undefined ? null : Math.max(0, clockAt(duel, serverNow) - at)
}

/** 02:41 or 1:02:41 */
export const formatDuration = (seconds: number) => {
  const total = Math.max(0, Math.floor(seconds))
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  const sec = total % 60
  const mm = String(m).padStart(2, '0')
  const ss = String(sec).padStart(2, '0')
  return h > 0 ? `${h}:${mm}:${ss}` : `${mm}:${ss}`
}

/** ws(s):// URL on the current origin. */
export const wsUrl = (path: string) => `${window.location.protocol === 'https:' ? 'wss' : 'ws'}://${window.location.host}${path}`

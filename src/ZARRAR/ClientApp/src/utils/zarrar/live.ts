import { useSyncExternalStore } from 'react'
import { wsUrl } from '@Utils/zarrar/api'
import publisherWorkerScript from '@Utils/zarrar/publisherWorker'

/* -------------------------------------------------------------------------------------------- */
/*  Publisher (player side)                                                                     */
/* -------------------------------------------------------------------------------------------- */

export type PublisherStatus = 'idle' | 'requesting' | 'connecting' | 'live' | 'reconnecting' | 'error'

export interface PublisherState {
  status: PublisherStatus
  gameId: number | null
  message: string | null
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
type Any = any

let state: PublisherState = { status: 'idle', gameId: null, message: null }
const listeners = new Set<() => void>()

let worker: Worker | null = null
let stream: MediaStream | null = null

const setState = (next: Partial<PublisherState>) => {
  state = { ...state, ...next }
  listeners.forEach((listener) => listener())
}

const onBeforeUnload = (event: BeforeUnloadEvent) => {
  // Reloading or closing the tab ends the screen share and pauses the match.
  event.preventDefault()
}

const INSECURE_MESSAGE =
  'Screen streaming needs a secure (https://) address, and this page was opened over plain http://. ' +
  'Open ZARRAR at its https:// address. Ask the organisers if you are not sure which one.'

/**
 * Why this browser cannot publish its screen, or null when it can.
 * Chrome hides screen capture and WebCodecs entirely on insecure origins (http://<ip>), so that
 * case is checked first; otherwise it looks like an unsupported browser and the message misleads.
 */
export const publishProblem = (): string | null => {
  if (typeof window === 'undefined') return 'Not available here.'
  if (!window.isSecureContext) return INSECURE_MESSAGE
  if (
    !navigator.mediaDevices?.getDisplayMedia ||
    !('MediaStreamTrackProcessor' in window) ||
    !('VideoEncoder' in window) ||
    !('OffscreenCanvas' in window)
  )
    return 'This browser cannot stream your screen. Open ZARRAR in the latest Google Chrome or Microsoft Edge.'
  return null
}

/** Why this browser cannot show the live feeds (monitor / admin), or null when it can. */
export const viewProblem = (): string | null => {
  if (typeof window === 'undefined') return null
  if (!window.isSecureContext)
    return 'LIVE FEEDS NEED HTTPS: this page was opened over http://, so the browser blocks the video decoder. Reopen it at the https:// address.'
  if (!('VideoDecoder' in window))
    return 'LIVE FEEDS NEED CHROME OR EDGE: this browser has no video decoder. Reopen the stage in the latest Chrome or Edge.'
  return null
}

/** Whether this browser can publish (Chrome / Edge on a secure origin). */
export const canPublish = () => publishProblem() === null

const teardown = () => {
  window.removeEventListener('beforeunload', onBeforeUnload)

  if (worker) {
    worker.postMessage({ type: 'stop' })
    worker.terminate()
    worker = null
  }

  stream?.getTracks().forEach((track) => track.stop())
  stream = null
}

/**
 * Module-level singleton: the share survives navigation between the challenge list, the
 * scoreboard and back. Only a full page reload ends it.
 */
export const zarrarPublisher = {
  subscribe: (listener: () => void) => {
    listeners.add(listener)
    return () => listeners.delete(listener)
  },

  getSnapshot: () => state,

  async start(gameId: number) {
    const problem = publishProblem()
    if (problem) {
      setState({ status: 'error', gameId, message: problem })
      return
    }

    if (state.gameId === gameId && (state.status === 'live' || state.status === 'connecting')) return

    teardown()
    setState({ status: 'requesting', gameId, message: null })

    let captured: MediaStream
    try {
      captured = await navigator.mediaDevices.getDisplayMedia({
        video: { displaySurface: 'monitor', frameRate: { ideal: 15, max: 30 } },
        audio: false,
        // Chrome-specific hints: offer whole screens, keep this tab available, no mid-share switching.
        monitorTypeSurfaces: 'include',
        selfBrowserSurface: 'include',
        surfaceSwitching: 'exclude',
      } as Any)
    } catch {
      setState({ status: 'error', message: 'Screen sharing was cancelled. You must share your screen to play.' })
      return
    }

    const track = captured.getVideoTracks()[0]
    const surface = (track?.getSettings() as Any)?.displaySurface as string | undefined

    if (!track || (surface && surface !== 'monitor')) {
      captured.getTracks().forEach((t) => t.stop())
      setState({
        status: 'error',
        message: 'Share your ENTIRE SCREEN — not a window or a tab. Pick the "Entire screen" option and try again.',
      })
      return
    }

    try {
      // Screen content: Chrome keeps full resolution and sharp text rather than smooth motion.
      track.contentHint = 'text'
    } catch {
      /* not supported */
    }

    stream = captured

    track.addEventListener('ended', () => {
      teardown()
      setState({ status: 'error', message: 'Screen sharing stopped. The match is paused until you share again.' })
    })

    const ProcessorCtor = (window as Any).MediaStreamTrackProcessor
    const processor = new ProcessorCtor({ track })

    worker = new Worker(publisherWorkerScript)
    worker.onmessage = (event: MessageEvent) => {
      const message = event.data as { type: string; status?: PublisherStatus; message?: string }

      if (message.type === 'status' && message.status) {
        setState({ status: message.status, message: null })
      } else if (message.type === 'error') {
        setState({ message: message.message ?? 'Streaming error' })
      } else if (message.type === 'ended') {
        teardown()
        setState({ status: 'error', message: 'Screen sharing stopped. The match is paused until you share again.' })
      } else if (message.type === 'benched') {
        // A teammate was tagged in: stop sharing quietly; the gate shows the bench screen.
        teardown()
        setState({ status: 'idle', message: null })
      }
    }

    worker.postMessage({ type: 'start', url: wsUrl(`/api/zarrar/${gameId}/live/publish`), readable: processor.readable }, [
      processor.readable,
    ])

    window.addEventListener('beforeunload', onBeforeUnload)
    setState({ status: 'connecting' })
  },

  stop() {
    teardown()
    setState({ status: 'idle', message: null })
  },
}

export const usePublisher = () => useSyncExternalStore(zarrarPublisher.subscribe, zarrarPublisher.getSnapshot)

/* -------------------------------------------------------------------------------------------- */
/*  Viewer (monitor / admin side)                                                               */
/* -------------------------------------------------------------------------------------------- */

export type ViewerStatus = 'connecting' | 'waiting' | 'live' | 'offline'

/** No message at all (frames, config or the server's 4 s keepalive) for this long: the socket is dead. */
const VIEWER_SILENCE_MS = 12000

/**
 * Plays one player's feed into a canvas. Returns a disposer.
 *
 * Decoding is WebCodecs (VP9 or VP8). When the decoder falls behind it skips to a fresh key frame
 * (and asks the player's browser for one), so the projector always shows "now", never a backlog.
 *
 * The canvas is drawn at the size it actually occupies on screen, in device pixels, with
 * high-quality downscaling and letterboxing. Letting the browser shrink a full-size canvas with
 * CSS (and the stage's scale transform) is blurrier and shimmers on small text.
 */
export const attachViewer = (
  canvas: HTMLCanvasElement,
  gameId: number,
  participationId: number,
  onStatus: (status: ViewerStatus) => void
) => {
  let socket: WebSocket | null = null
  let decoder: Any = null
  let closed = false
  let waitingKey = true
  let lastConfig: { codec: string; width: number; height: number } | null = null
  let reconnect: ReturnType<typeof setTimeout> | null = null
  let lastFrameAt = 0
  let lastMessageAt = performance.now()
  let lastKeyAskAt = 0
  let lastFrame: Any = null
  let status: ViewerStatus = 'connecting'
  // Reconnect back-off: 1.5 s doubling to 15 s, reset once a socket opens. A refused socket
  // (server restarting, rate limit) must not turn into a request storm.
  let retryMs = 1500

  const ctx = canvas.getContext('2d', { alpha: false })

  const setStatus = (next: ViewerStatus) => {
    if (next === status) return
    status = next
    onStatus(next)
  }

  /** Asks the player's browser for a fresh key frame (rate-limited). */
  const askKey = () => {
    const now = performance.now()
    if (now - lastKeyAskAt < 500) return
    lastKeyAskAt = now
    try {
      if (socket?.readyState === WebSocket.OPEN) socket.send('{"type":"keyframe"}')
    } catch {
      /* the reconnect logic handles a broken socket */
    }
  }

  /** Target size in device pixels: what the canvas really covers on screen right now. */
  const targetSize = (frame: Any) => {
    const rect = canvas.getBoundingClientRect()
    const dpr = window.devicePixelRatio || 1
    const w = Math.round(rect.width * dpr)
    const h = Math.round(rect.height * dpr)
    return w > 0 && h > 0 ? { w, h } : { w: frame.displayWidth, h: frame.displayHeight }
  }

  const paint = (frame: Any) => {
    if (!ctx) return
    const { w, h } = targetSize(frame)

    if (canvas.width !== w || canvas.height !== h) {
      canvas.width = w
      canvas.height = h
    }
    // Resizing a canvas resets its state, so set this on every paint.
    ctx.imageSmoothingEnabled = true
    ctx.imageSmoothingQuality = 'high'

    const fw = frame.displayWidth
    const fh = frame.displayHeight
    const scale = Math.min(w / fw, h / fh)
    const dw = Math.round(fw * scale)
    const dh = Math.round(fh * scale)
    const dx = Math.floor((w - dw) / 2)
    const dy = Math.floor((h - dh) / 2)

    // Letterbox (a 16:10 laptop screen in a 16:9 frame): black bars, never a stretched picture.
    if (dw !== w || dh !== h) {
      ctx.fillStyle = '#000'
      ctx.fillRect(0, 0, w, h)
    }
    ctx.drawImage(frame, dx, dy, dw, dh)
  }

  const configure = (config: { codec: string; width: number; height: number }) => {
    lastConfig = config

    if (decoder) {
      try {
        decoder.close()
      } catch {
        /* already closed */
      }
    }

    if (!('VideoDecoder' in window)) {
      setStatus('offline')
      return
    }

    waitingKey = true

    const DecoderCtor = (window as Any).VideoDecoder
    decoder = new DecoderCtor({
      output: (frame: Any) => {
        // Keep the newest frame so a layout change can redraw it at the new size straight away.
        lastFrame?.close()
        lastFrame = frame
        paint(frame)
        lastFrameAt = performance.now()
        setStatus('live')
      },
      error: () => {
        decoder = null
        waitingKey = true
        askKey()
        if (lastConfig && !closed) configure(lastConfig)
      },
    })

    decoder.configure({
      codec: config.codec,
      codedWidth: config.width,
      codedHeight: config.height,
      optimizeForLatency: true,
    })

    // A mid-stream change (the player's browser stepped quality up or down) keeps the last
    // picture on screen; only a fresh feed shows SYNCING.
    if (status !== 'live') setStatus('waiting')
    askKey()
  }

  const open = () => {
    if (closed) return
    setStatus('connecting')
    lastMessageAt = performance.now()

    const ws = new WebSocket(wsUrl(`/api/zarrar/${gameId}/live/watch/${participationId}`))
    ws.binaryType = 'arraybuffer'
    socket = ws
    ws.onopen = () => {
      retryMs = 1500
    }

    ws.onmessage = (event: MessageEvent) => {
      lastMessageAt = performance.now()

      if (typeof event.data === 'string') {
        try {
          const message = JSON.parse(event.data)
          if (message.type === 'config') configure(message)
          else if (message.type === 'offline') {
            waitingKey = true
            setStatus('offline')
          }
        } catch {
          /* ignore */
        }
        return
      }

      const buffer = event.data as ArrayBuffer
      if (buffer.byteLength < 10 || !decoder || decoder.state !== 'configured') return

      const bytes = new Uint8Array(buffer)
      const isKey = bytes[0] === 1

      if (waitingKey && !isKey) {
        askKey()
        return
      }

      if (decoder.decodeQueueSize > 8 && !isKey) {
        waitingKey = true
        askKey()
        return
      }

      waitingKey = false
      const timestamp = Number(new DataView(buffer).getBigInt64(1, true))
      const ChunkCtor = (window as Any).EncodedVideoChunk

      try {
        decoder.decode(new ChunkCtor({ type: isKey ? 'key' : 'delta', timestamp, data: bytes.subarray(9) }))
      } catch {
        waitingKey = true
        askKey()
      }
    }

    ws.onclose = () => {
      if (socket === ws) socket = null
      if (closed) return
      setStatus('connecting')
      reconnect = setTimeout(open, retryMs)
      retryMs = Math.min(retryMs * 2, 15000)
    }
  }

  open()

  const watchdog = setInterval(() => {
    const now = performance.now()
    // A feed that stops producing frames without saying so is shown as syncing.
    if (status === 'live' && now - lastFrameAt > 5000) setStatus('waiting')
    // Total silence (not even the server's keepalive): the socket died without closing — a
    // sleeping laptop, a Wi-Fi hop. Drop it and reconnect instead of waiting for TCP.
    if (socket && socket.readyState === WebSocket.OPEN && now - lastMessageAt > VIEWER_SILENCE_MS) {
      try {
        socket.close()
      } catch {
        /* onclose reconnects */
      }
    }
  }, 1000)

  // The stage animates panels between layouts; redraw the last picture at the new size so the feed
  // stays sharp (and never blank) while a static screen sends only one frame a second.
  let lastBox = ''
  const resizer = setInterval(() => {
    if (!lastFrame) return
    const rect = canvas.getBoundingClientRect()
    const box = `${Math.round(rect.width)}x${Math.round(rect.height)}x${window.devicePixelRatio}`
    if (box !== lastBox) {
      lastBox = box
      paint(lastFrame)
    }
  }, 120)

  return () => {
    closed = true
    clearInterval(watchdog)
    clearInterval(resizer)
    if (reconnect) clearTimeout(reconnect)
    socket?.close()
    try {
      decoder?.close()
    } catch {
      /* ignore */
    }
    try {
      lastFrame?.close()
    } catch {
      /* ignore */
    }
    lastFrame = null
  }
}

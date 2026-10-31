/**
 * ZARRAR screen publisher — runs in a dedicated worker.
 *
 * The player's tab is in the background for the whole match (they are in a terminal, Burp, an
 * editor…). Background tabs get their timers throttled, so the capture → encode → send loop runs
 * in a worker, which is not throttled. The main thread only owns `getDisplayMedia` and hands the
 * video track's frame stream over.
 *
 * Built from a function body into a Blob URL, the same way `PowWorker` is, so it needs no bundler
 * worker support. Everything the worker uses must therefore be defined inside `workerFunction`.
 *
 * Quality: up to 1920×1080 (a 1080p screen is sent pixel for pixel, so terminal text stays sharp),
 * VP9 when the browser can encode it (much clearer text per bit than VP8), VP8 otherwise. The
 * stream adapts: when the Wi-Fi can't keep up it steps the bitrate (and then the size) down, and
 * steps back up after a quiet spell; when the laptop's CPU can't keep up it falls back from VP9
 * to VP8, then to a smaller size.
 *
 * Messages in:  { type: 'start', url, readable }   { type: 'stop' }
 * Messages out: { type: 'status', status: 'connecting' | 'live' | 'reconnecting' }
 *               { type: 'ended' }   { type: 'error', message }
 *               { type: 'benched' }  (a teammate was tagged in; the worker has stopped)
 *               { type: 'quality', codec, width, height, bitrate }  (informational)
 */

const workerFunction = function () {
  const FPS = 15
  const FRAME_MS = 1000 / FPS
  const KEY_EVERY = FPS * 2

  // The codec strings the server relays (it allow-lists them).
  // VP9 profile 0, level 4.0 (enough for 1080p), 8-bit.
  const VP9 = 'vp09.00.40.08'
  const VP8 = 'vp8'

  // Quality ladder, best first. Bitrates are for the full size; smaller screens scale down.
  const LADDER = [
    { maxW: 1920, maxH: 1080, vp9: 3_000_000, vp8: 4_000_000 },
    { maxW: 1920, maxH: 1080, vp9: 2_000_000, vp8: 2_600_000 },
    { maxW: 1600, maxH: 900, vp9: 1_400_000, vp8: 1_800_000 },
    { maxW: 1280, maxH: 720, vp9: 900_000, vp8: 1_200_000 },
  ]

  // Network: a send backlog bigger than this many seconds of video is dropped (a stale picture is
  // worse than a skipped one), and repeated drops step the quality down.
  const MAX_BACKLOG_SECONDS = 0.75
  const CONGESTION_STEPS_DOWN = 3 // drops within CONGESTION_WINDOW_MS that trigger a step down
  const CONGESTION_WINDOW_MS = 5000
  const RECOVER_AFTER_MS = 20000 // a clean spell this long steps back up one level…
  const RECOVER_MAX_MS = 160000 // …doubling (up to this) each time a step up fails soon after
  const WARMUP_MS = 3000 // a fresh encoder's first frames queue up; don't call that overload
  // CPU: frames skipped because the encoder is still busy.
  const OVERLOAD_STEPS_DOWN = 15
  const OVERLOAD_WINDOW_MS = 10000

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  type Any = any

  let ws: WebSocket | null = null
  let url = ''
  let running = false
  let encoder: Any = null
  let canvas: Any = null
  let ctx: Any = null
  let width = 0
  let height = 0
  let forceKey = true
  let frameCount = 0
  let lastEncodeAt = 0
  let reader: Any = null
  let heartbeat: ReturnType<typeof setInterval> | null = null
  // Reconnect back-off: 1.5 s doubling to 6 s (a player must come back fast), reset on open.
  let retryMs = 1500
  let idleRefresh: ReturnType<typeof setInterval> | null = null

  let codec = VP8
  let level = 0
  let bitrate = LADDER[0].vp8
  let lastFrameSize = { w: 0, h: 0 }
  let congestion: number[] = []
  let overload: number[] = []
  let lastTroubleAt = 0
  let recoverMs = RECOVER_AFTER_MS
  let lastStepUpAt = -Infinity
  let encoderSince = 0

  const post = (message: Record<string, unknown>) => (self as Any).postMessage(message)

  const sendConfig = () => {
    if (ws && ws.readyState === 1 && width > 0) ws.send(JSON.stringify({ type: 'config', codec, width, height }))
  }

  const announce = () => post({ type: 'quality', codec, width, height, bitrate })

  /** Picks VP9 when this browser can encode it in real time at full size, else VP8. */
  const chooseCodec = async () => {
    const VideoEncoderCtor = (self as Any).VideoEncoder
    try {
      const support = await VideoEncoderCtor.isConfigSupported({
        codec: VP9,
        width: 1920,
        height: 1080,
        bitrate: LADDER[0].vp9,
        framerate: FPS,
        latencyMode: 'realtime',
      })
      codec = support?.supported ? VP9 : VP8
    } catch {
      codec = VP8
    }
  }

  const connect = () => {
    if (!running) return
    post({ type: 'status', status: 'connecting' })

    const socket = new WebSocket(url)
    socket.binaryType = 'arraybuffer'
    ws = socket

    socket.onopen = () => {
      retryMs = 1500
      forceKey = true
      sendConfig()
      post({ type: 'status', status: 'live' })
    }

    socket.onmessage = (event: MessageEvent) => {
      if (typeof event.data !== 'string') return
      try {
        const message = JSON.parse(event.data)
        if (message.type === 'keyframe') forceKey = true
        else if (message.type === 'benched') benched()
      } catch {
        /* ignore */
      }
    }

    socket.onclose = (event: CloseEvent) => {
      if (ws === socket) ws = null
      if (!running) return
      // 4001: tagged out. Retrying would only be refused again.
      if (event.code === 4001) {
        benched()
        return
      }
      post({ type: 'status', status: 'reconnecting' })
      setTimeout(connect, retryMs)
      retryMs = Math.min(retryMs * 2, 6000)
    }

    socket.onerror = () => {
      /* onclose follows */
    }
  }

  /** Bitrate for the current level, codec and frame size. */
  const targetBitrate = () => {
    const step = LADDER[level]
    const full = codec === VP9 ? step.vp9 : step.vp8
    const share = (width * height) / (step.maxW * step.maxH)
    return Math.max(400_000, Math.round(full * Math.min(1, Math.max(share, 0.35))))
  }

  const encoderConfig = () => ({
    codec,
    width,
    height,
    bitrate,
    framerate: FPS,
    latencyMode: 'realtime',
    // Screen content: favour crisp text over smooth motion (ignored where unsupported).
    contentHint: 'text',
  })

  const setupEncoder = () => {
    if (encoder) {
      try {
        encoder.close()
      } catch {
        /* already closed */
      }
    }

    bitrate = targetBitrate()
    encoderSince = performance.now()
    const VideoEncoderCtor = (self as Any).VideoEncoder
    encoder = new VideoEncoderCtor({
      output: (chunk: Any) => {
        const socket = ws
        if (!socket || socket.readyState !== 1) return

        // The Wi-Fi is not keeping up: drop this chunk and restart from a key frame rather than
        // building an ever-growing delay.
        if (socket.bufferedAmount > (bitrate / 8) * MAX_BACKLOG_SECONDS) {
          forceKey = true
          // Deferred: never reconfigure the encoder from inside its own output callback.
          trouble(congestion, CONGESTION_STEPS_DOWN, CONGESTION_WINDOW_MS, () => setTimeout(stepDown, 0))
          return
        }

        const buffer = new ArrayBuffer(9 + chunk.byteLength)
        const view = new DataView(buffer)
        view.setUint8(0, chunk.type === 'key' ? 1 : 0)
        view.setBigInt64(1, BigInt(Math.round(chunk.timestamp)), true)
        chunk.copyTo(new Uint8Array(buffer, 9))
        socket.send(buffer)
      },
      error: (e: Any) => {
        post({ type: 'error', message: String(e?.message ?? e) })
        encoder = null
        // A VP9 encoder that fails mid-stream: carry on with VP8.
        if (codec === VP9) setTimeout(() => switchCodec(VP8), 0)
      },
    })

    try {
      encoder.configure(encoderConfig())
    } catch (e) {
      encoder = null
      if (codec === VP9) {
        codec = VP8
        setupEncoder()
        sendConfig()
      } else {
        post({ type: 'error', message: String(e) })
      }
      return
    }

    forceKey = true
    announce()
  }

  /** Records a problem; `act` runs once `limit` problems land inside `windowMs`. */
  const trouble = (log: number[], limit: number, windowMs: number, act: () => void) => {
    const now = performance.now()
    lastTroubleAt = now
    log.push(now)
    while (log.length && now - log[0] > windowMs) log.shift()
    if (log.length >= limit) {
      log.length = 0
      act()
    }
  }

  /** Re-applies the ladder: a new size means a new canvas, encoder and viewer config. */
  const applyLevel = () => {
    const before = { w: width, h: height }
    resize(lastFrameSize.w, lastFrameSize.h)
    if (width === before.w && height === before.h && encoder && encoder.state === 'configured') {
      bitrate = targetBitrate()
      try {
        encoder.configure(encoderConfig())
        forceKey = true
        announce()
      } catch {
        setupEncoder()
      }
    }
  }

  const stepDown = () => {
    if (level >= LADDER.length - 1) return
    // Stepping back down within a minute of stepping up means that level doesn't hold here:
    // wait twice as long before trying it again, so the stream settles instead of see-sawing.
    const now = performance.now()
    recoverMs = now - lastStepUpAt < 60000 ? Math.min(recoverMs * 2, RECOVER_MAX_MS) : RECOVER_AFTER_MS
    level++
    applyLevel()
  }

  const stepUp = () => {
    if (level === 0) return
    lastStepUpAt = performance.now()
    level--
    applyLevel()
  }

  const switchCodec = (next: string) => {
    if (codec === next) return
    codec = next
    setupEncoder()
    sendConfig()
  }

  /** Sizes the canvas for a captured frame of fw×fh at the current level. */
  const resize = (fw: number, fh: number) => {
    if (!fw || !fh) return
    const step = LADDER[level]
    const scale = Math.min(1, step.maxW / fw, step.maxH / fh)
    const w = Math.max(16, Math.round((fw * scale) / 2) * 2)
    const h = Math.max(16, Math.round((fh * scale) / 2) * 2)

    if (w !== width || h !== height || !canvas) {
      width = w
      height = h
      const OffscreenCanvasCtor = (self as Any).OffscreenCanvas
      canvas = new OffscreenCanvasCtor(w, h)
      ctx = canvas.getContext('2d', { alpha: false })
      ctx.imageSmoothingEnabled = true
      ctx.imageSmoothingQuality = 'high'
      setupEncoder()
      sendConfig()
    }
  }

  const encodeCanvas = () => {
    if (!canvas) return
    if (!encoder || encoder.state !== 'configured') setupEncoder()
    if (!encoder || encoder.state !== 'configured') return
    if (encoder.encodeQueueSize > 3) {
      forceKey = true
      // The laptop can't encode this fast: VP9 → VP8 first (much cheaper), then smaller frames.
      // A fresh encoder's first frames always queue a little, so that doesn't count.
      if (performance.now() - encoderSince > WARMUP_MS)
        trouble(overload, OVERLOAD_STEPS_DOWN, OVERLOAD_WINDOW_MS, () =>
          codec === VP9 ? switchCodec(VP8) : stepDown()
        )
      return
    }

    const now = performance.now()
    const VideoFrameCtor = (self as Any).VideoFrame
    const frame = new VideoFrameCtor(canvas, { timestamp: Math.round(now * 1000) })
    const keyFrame = forceKey || frameCount % KEY_EVERY === 0
    forceKey = false

    try {
      encoder.encode(frame, { keyFrame })
    } finally {
      frame.close()
    }

    frameCount++
    lastEncodeAt = now

    // A long clean spell: try one level better.
    if (level > 0 && now - lastTroubleAt > recoverMs) {
      lastTroubleAt = now
      stepUp()
    }
  }

  const draw = (frame: Any) => {
    const fw = frame.displayWidth || frame.codedWidth
    const fh = frame.displayHeight || frame.codedHeight
    lastFrameSize = { w: fw, h: fh }
    resize(fw, fh)
    ctx.drawImage(frame, 0, 0, width, height)
  }

  const pump = async () => {
    try {
      while (running && reader) {
        const { value: frame, done } = await reader.read()
        if (done || !frame) break

        try {
          if (performance.now() - lastEncodeAt >= FRAME_MS * 0.9) {
            draw(frame)
            encodeCanvas()
          }
        } finally {
          frame.close()
        }
      }
    } catch (e) {
      post({ type: 'error', message: String(e) })
    }

    if (running) post({ type: 'ended' })
  }

  const stop = () => {
    running = false
    if (heartbeat) clearInterval(heartbeat)
    if (idleRefresh) clearInterval(idleRefresh)
    heartbeat = null
    idleRefresh = null

    try {
      reader?.cancel()
    } catch {
      /* ignore */
    }
    reader = null

    try {
      encoder?.close()
    } catch {
      /* ignore */
    }
    encoder = null

    try {
      ws?.close()
    } catch {
      /* ignore */
    }
    ws = null
  }

  // Tag-team: the server says a teammate is on the keyboard now. Stop for good.
  function benched() {
    if (!running) return
    stop()
    post({ type: 'benched' })
  }

  self.onmessage = async (event: MessageEvent) => {
    const message = event.data
    if (message?.type === 'start') {
      stop()
      running = true
      url = message.url
      reader = message.readable.getReader()
      frameCount = 0
      lastEncodeAt = 0
      forceKey = true
      level = 0
      width = 0
      height = 0
      canvas = null
      congestion = []
      overload = []
      lastTroubleAt = performance.now()
      recoverMs = RECOVER_AFTER_MS
      lastStepUpAt = -Infinity

      await chooseCodec()
      if (!running) return

      connect()
      pump()

      // Heartbeat so the server can tell "static screen" from "dead player".
      heartbeat = setInterval(() => {
        if (ws && ws.readyState === 1) ws.send('{"type":"ping"}')
      }, 2000)

      // A static screen produces no new frames; re-send the last image once a second so a viewer
      // that just joined still gets a picture.
      idleRefresh = setInterval(() => {
        if (running && canvas && performance.now() - lastEncodeAt > 1000) encodeCanvas()
      }, 500)
    } else if (message?.type === 'stop') {
      stop()
    }
  }
}

const codeStr = workerFunction.toString()
const mainCode = codeStr.substring(codeStr.indexOf('{') + 1, codeStr.lastIndexOf('}'))
const blob = new Blob([mainCode], { type: 'application/javascript' })
const publisherWorkerScript = URL.createObjectURL(blob)

export default publisherWorkerScript

/**
 * Stage sound effects.
 *
 * Drop real files into `src/BURAQ/wwwroot/assets/zarrar/sfx/` with these names and they are used
 * automatically; until then each effect falls back to a synthesized placeholder:
 *
 *   accepted.mp3   flag accepted / challenge captured
 *   denied.mp3     wrong flag
 *   released.mp3   new challenge released
 *   alert.mp3      feed lost / match paused
 *   resume.mp3     match resumed
 *   winner.mp3     match over, winner reveal
 *   count.mp3      each 3-2-1 beat of the start countdown
 *   fight.mp3      "FIGHT" at the end of the countdown
 *
 * Browsers only allow audio after a user gesture, so the stage calls `unlockSfx()` from its
 * "enter the arena" click.
 */

export type SfxName = 'accepted' | 'denied' | 'released' | 'alert' | 'resume' | 'winner' | 'count' | 'fight'

const SFX_BASE = '/assets/zarrar/sfx'

let audioCtx: AudioContext | null = null
let muted = false
const fileState = new Map<SfxName, 'unknown' | 'ok' | 'missing'>()
const buffers = new Map<SfxName, AudioBuffer>()

const context = () => {
  if (!audioCtx) {
    const Ctor = window.AudioContext ?? (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext
    audioCtx = new Ctor()
  }
  return audioCtx
}

export const unlockSfx = async () => {
  try {
    await context().resume()
  } catch {
    /* ignore */
  }
}

export const setSfxMuted = (value: boolean) => {
  muted = value
}

export const isSfxMuted = () => muted

const loadFile = async (name: SfxName) => {
  const known = fileState.get(name)
  if (known === 'missing') return null
  if (known === 'ok') return buffers.get(name) ?? null

  try {
    const res = await fetch(`${SFX_BASE}/${name}.mp3`, { cache: 'force-cache' })
    const type = res.headers.get('content-type') ?? ''
    if (!res.ok || type.includes('text/html')) {
      fileState.set(name, 'missing')
      return null
    }
    const buffer = await context().decodeAudioData(await res.arrayBuffer())
    buffers.set(name, buffer)
    fileState.set(name, 'ok')
    return buffer
  } catch {
    fileState.set(name, 'missing')
    return null
  }
}

/** Preload real files so the first flag does not wait on the network. */
export const preloadSfx = () => {
  ;(['accepted', 'denied', 'released', 'alert', 'resume', 'winner', 'count', 'fight'] as SfxName[]).forEach(
    (n) => void loadFile(n)
  )
}

const tone = (
  ctx: AudioContext,
  start: number,
  freq: number,
  duration: number,
  type: OscillatorType,
  gain: number,
  slideTo?: number
) => {
  const osc = ctx.createOscillator()
  const amp = ctx.createGain()
  osc.type = type
  osc.frequency.setValueAtTime(freq, start)
  if (slideTo) osc.frequency.exponentialRampToValueAtTime(slideTo, start + duration)
  amp.gain.setValueAtTime(0.0001, start)
  amp.gain.exponentialRampToValueAtTime(gain, start + 0.012)
  amp.gain.exponentialRampToValueAtTime(0.0001, start + duration)
  osc.connect(amp).connect(ctx.destination)
  osc.start(start)
  osc.stop(start + duration + 0.05)
}

const noiseBurst = (ctx: AudioContext, start: number, duration: number, gain: number) => {
  const length = Math.floor(ctx.sampleRate * duration)
  const buffer = ctx.createBuffer(1, length, ctx.sampleRate)
  const data = buffer.getChannelData(0)
  for (let i = 0; i < length; i++) data[i] = (Math.random() * 2 - 1) * (1 - i / length)
  const src = ctx.createBufferSource()
  const amp = ctx.createGain()
  amp.gain.value = gain
  src.buffer = buffer
  src.connect(amp).connect(ctx.destination)
  src.start(start)
}

/** Synthesized placeholders — aggressive, short, unmistakable. */
const synth = (name: SfxName) => {
  const ctx = context()
  const t = ctx.currentTime + 0.01

  switch (name) {
    case 'accepted':
      noiseBurst(ctx, t, 0.25, 0.25)
      tone(ctx, t, 110, 0.6, 'sawtooth', 0.35, 55)
      ;[523.25, 659.25, 783.99, 1046.5].forEach((f, i) => tone(ctx, t + 0.12 + i * 0.09, f, 0.35, 'square', 0.12))
      tone(ctx, t + 0.5, 1046.5, 0.9, 'triangle', 0.2)
      break
    case 'denied':
      tone(ctx, t, 220, 0.22, 'sawtooth', 0.3, 110)
      tone(ctx, t + 0.24, 180, 0.4, 'sawtooth', 0.3, 70)
      noiseBurst(ctx, t, 0.12, 0.2)
      break
    case 'released':
      tone(ctx, t, 300, 0.5, 'sawtooth', 0.18, 1200)
      tone(ctx, t + 0.5, 880, 0.18, 'square', 0.14)
      tone(ctx, t + 0.68, 880, 0.3, 'square', 0.14)
      break
    case 'alert':
      for (let i = 0; i < 3; i++) {
        tone(ctx, t + i * 0.42, 880, 0.18, 'square', 0.18)
        tone(ctx, t + i * 0.42 + 0.2, 660, 0.18, 'square', 0.18)
      }
      break
    case 'resume':
      tone(ctx, t, 440, 0.15, 'square', 0.14)
      tone(ctx, t + 0.16, 880, 0.3, 'square', 0.14)
      break
    case 'count':
      tone(ctx, t, 98, 0.35, 'sawtooth', 0.4, 49)
      tone(ctx, t, 784, 0.12, 'square', 0.16)
      noiseBurst(ctx, t, 0.06, 0.2)
      break
    case 'fight':
      noiseBurst(ctx, t, 0.5, 0.35)
      tone(ctx, t, 55, 1.1, 'sawtooth', 0.45, 30)
      tone(ctx, t, 220, 0.5, 'square', 0.2, 880)
      ;[659.25, 880, 1318.5].forEach((f, i) => tone(ctx, t + 0.08 + i * 0.06, f, 0.5, 'square', 0.12))
      break
    case 'winner':
      noiseBurst(ctx, t, 0.4, 0.25)
      tone(ctx, t, 65, 1.4, 'sawtooth', 0.35, 40)
      ;[392, 523.25, 659.25, 783.99, 1046.5].forEach((f, i) => tone(ctx, t + 0.2 + i * 0.14, f, 0.6, 'square', 0.12))
      break
  }
}

export const playSfx = async (name: SfxName) => {
  if (muted) return
  try {
    const ctx = context()
    if (ctx.state === 'suspended') return
    const buffer = await loadFile(name)
    if (buffer) {
      const src = ctx.createBufferSource()
      src.buffer = buffer
      src.connect(ctx.destination)
      src.start()
    } else {
      synth(name)
    }
  } catch {
    /* audio is decoration; never break the stage */
  }
}

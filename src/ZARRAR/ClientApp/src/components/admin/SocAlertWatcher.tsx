import { HubConnectionBuilder, LogLevel as SignalRLogLevel } from '@microsoft/signalr'
import { showNotification } from '@mantine/notifications'
import { FC, useEffect } from 'react'
import { RequireRole } from '@Components/WithRole'
import { useUser } from '@Hooks/useUser'
import { Role } from '@Api'

interface AlertUpdate {
  kind?: string
  data?: { id?: number; event?: string; severity?: string; category?: string; teamId?: number; riskImpact?: number }
}

/** At most one toast this often; anything in between is counted into the next one. */
const TOAST_GAP_MS = 8000

/** Short two-tone chime, synthesised so no asset is needed. Silently skipped if audio is blocked. */
const chime = (critical: boolean) => {
  try {
    const Ctx = window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext
    if (!Ctx) return
    const ctx = new Ctx()
    const tones = critical ? [880, 660, 880] : [660, 880]
    tones.forEach((freq, i) => {
      const osc = ctx.createOscillator()
      const gain = ctx.createGain()
      osc.type = 'square'
      osc.frequency.value = freq
      gain.gain.setValueAtTime(0.0001, ctx.currentTime + i * 0.16)
      gain.gain.exponentialRampToValueAtTime(0.12, ctx.currentTime + i * 0.16 + 0.02)
      gain.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + i * 0.16 + 0.14)
      osc.connect(gain).connect(ctx.destination)
      osc.start(ctx.currentTime + i * 0.16)
      osc.stop(ctx.currentTime + i * 0.16 + 0.15)
    })
    window.setTimeout(() => void ctx.close(), 1200)
  } catch {
    /* audio unavailable */
  }
}

/**
 * S1 — critical SOC alerts reach the admin wherever they are (ZARRAR Live, game settings, …), not
 * only on the SOC page: a toast plus a chime for High and Critical alerts. Bursts are folded into
 * one toast with a count so a flood can't bury the screen.
 */
export const SocAlertWatcher: FC = () => {
  const { user } = useUser()
  const isAdmin = RequireRole(Role.Admin, user?.role)

  useEffect(() => {
    if (!isAdmin) return

    const connection = new HubConnectionBuilder()
      .withUrl('/hub/admin')
      .withAutomaticReconnect()
      .configureLogging(SignalRLogLevel.None)
      .build()

    let lastToast = 0
    let pending: { event: string; severity: string; count: number } | null = null
    let timer: ReturnType<typeof setTimeout> | undefined

    const flush = () => {
      timer = undefined
      if (!pending) return
      const { event, severity, count } = pending
      pending = null
      lastToast = Date.now()
      const critical = severity === 'Critical'
      chime(critical)
      showNotification({
        color: critical ? 'red' : 'orange',
        title: `${critical ? 'CRITICAL' : 'High'} SOC alert${count > 1 ? ` (+${count - 1} more)` : ''}`,
        message: `${event} — open Administration → SOC to triage.`,
        autoClose: critical ? 20000 : 10000,
      })
    }

    connection.on('ReceivedSocUpdate', (update: AlertUpdate) => {
      if (update.kind !== 'alert') return
      const severity = update.data?.severity ?? ''
      if (severity !== 'Critical' && severity !== 'High') return

      const event = update.data?.event ?? 'Security alert'
      pending = pending
        ? { event: severity === 'Critical' ? event : pending.event, severity: pending.severity === 'Critical' ? 'Critical' : severity, count: pending.count + 1 }
        : { event, severity, count: 1 }

      if (timer) return
      timer = setTimeout(flush, Math.max(0, TOAST_GAP_MS - (Date.now() - lastToast)))
    })

    connection.start().catch(() => {
      /* not reachable right now; the SOC page itself still works */
    })

    return () => {
      if (timer) clearTimeout(timer)
      void connection.stop()
    }
  }, [isAdmin])

  return null
}

export default SocAlertWatcher

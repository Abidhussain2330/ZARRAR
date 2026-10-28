import { Badge, Button, Checkbox, Group, SegmentedControl, Switch } from '@mantine/core'
import { useModals } from '@mantine/modals'
import { showNotification } from '@mantine/notifications'
import { FC, useCallback, useEffect, useRef, useState } from 'react'
import { useParams } from 'react-router'
import { AdminLayout } from '@Components/admin/shell/AdminLayout'
import { Stage } from '@Components/zarrar/Stage'
import { TagRoster } from '@Components/zarrar/TagRoster'
import {
  DuelLayout,
  DuelRecording,
  DuelSnapshot,
  StageEvent,
  formatDuration,
  liveSecondsOf,
  zarrarApi,
} from '@Utils/zarrar/api'
import { useZarrarHub } from '@Utils/zarrar/hub'
import { unlockSfx } from '@Utils/zarrar/sfx'
import api from '@Api'
import classes from '@Styles/zarrar/AdminLive.module.css'

const errorText = (e: unknown) => {
  const err = e as { response?: { data?: { title?: string } }; message?: string }
  return err?.response?.data?.title ?? err?.message ?? 'Request failed'
}

const sleep = (ms: number) => new Promise((resolve) => window.setTimeout(resolve, ms))

/** The stage countdown runs 3-2-1-FIGHT over about four seconds; the first target lands after it. */
const COUNTDOWN_MS = 4200

const EVENT_LABEL: Partial<Record<StageEvent['type'], string>> = {
  countdown: 'Match started',
  released: 'Released',
  denied: 'Wrong flag',
  captured: 'Captured',
  withdrawn: 'Withdrawn',
  feedLost: 'Feed lost',
  feedRestored: 'Feed restored',
  paused: 'Paused',
  resumed: 'Resumed',
  ended: 'Match ended',
  tagged: 'Tag in',
}

const EVENT_COLOR: Partial<Record<StageEvent['type'], string>> = {
  captured: '#1ff28c',
  denied: '#ff2a3d',
  feedLost: '#ffb020',
  paused: '#ffb020',
  withdrawn: '#ffb020',
  countdown: '#ff2a3d',
  ended: '#ff2a3d',
  tagged: '#ffbe3c',
}

const LAYOUT_KEYS: Record<string, DuelLayout> = {
  Digit1: 'split',
  Digit2: 'p1',
  Digit3: 'p2',
  Digit4: 'board',
  Digit5: 'f1',
  Digit6: 'f2',
}

const csvCell = (value: unknown) => {
  let text = value == null ? '' : String(value)
  // Team names, user names and titles come from players. A cell starting with = + - @ (or a
  // tab / CR) runs as a formula in Excel; prefix it so it stays text (CSV injection).
  if (/^[=+\-@\t\r]/.test(text)) text = `'${text}`
  return /[",\n\r]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text
}

const formatBytes = (bytes: number) =>
  bytes >= 1024 * 1024 * 1024
    ? `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GB`
    : bytes >= 1024 * 1024
      ? `${(bytes / 1024 / 1024).toFixed(1)} MB`
      : `${Math.max(1, Math.round(bytes / 1024))} KB`

const timeOf = (ms: number) => new Date(ms).toLocaleTimeString([], { hour12: false })

/** Ignore shortcuts while typing, while a dialog is open, or with a modifier other than Shift. */
const shortcutAllowed = (e: KeyboardEvent) => {
  if (e.ctrlKey || e.metaKey || e.altKey || e.repeat) return false
  const target = e.target as HTMLElement | null
  if (target && (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName))) return false
  if (document.querySelector('[role="dialog"]')) return false
  return true
}

/**
 * ZARRAR Live — the admin's control room for one showdown.
 *
 * Left: the exact stage the auditorium sees (the same component, synced through the server, not
 * a video of the monitor's screen), then the match timeline and the recordings.
 * Right: the pre-flight checklist and everything that changes the match.
 */
const AdminLive: FC = () => {
  const { id } = useParams()
  const gameId = Number.parseInt(id ?? '-1', 10)
  const modals = useModals()

  const [snapshot, setSnapshot] = useState<DuelSnapshot | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [sound, setSound] = useState(false)
  const [override, setOverride] = useState(false)
  const [timeline, setTimeline] = useState<StageEvent[]>([])
  const [recordings, setRecordings] = useState<DuelRecording[] | null>(null)
  const [now, setNow] = useState(Date.now())
  const offset = useRef(0)

  const load = useCallback(async () => {
    try {
      const data = await zarrarApi.state(gameId)
      offset.current = data.serverTimeUtc - Date.now()
      setSnapshot(data)
    } catch (e) {
      showNotification({ color: 'red', title: 'Could not load the match', message: errorText(e) })
    }
  }, [gameId])

  const loadTimeline = useCallback(async () => {
    try {
      setTimeline(await zarrarApi.timeline(gameId))
    } catch {
      /* the timeline is secondary; the next event retries */
    }
  }, [gameId])

  const loadRecordings = useCallback(async () => {
    try {
      setRecordings(await zarrarApi.recordings(gameId))
    } catch (e) {
      showNotification({ color: 'red', title: 'Could not list recordings', message: errorText(e) })
    }
  }, [gameId])

  useEffect(() => {
    load()
    loadTimeline()
    loadRecordings()
    const timer = window.setInterval(load, 4000)
    const slow = window.setInterval(loadTimeline, 15000)
    return () => {
      window.clearInterval(timer)
      window.clearInterval(slow)
    }
  }, [load, loadTimeline, loadRecordings])

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [])

  // Coalesce event-driven reloads: a burst of events (a player spamming wrong flags) becomes
  // one snapshot + one timeline fetch, so the admin never burns their request budget and gets
  // locked out of Pause / Release by the rate limiter.
  const pending = useRef<{ timer: number | null; recordings: boolean }>({ timer: null, recordings: false })
  const reloadSoon = useCallback(
    (recordings: boolean) => {
      pending.current.recordings ||= recordings
      if (pending.current.timer !== null) return
      pending.current.timer = window.setTimeout(() => {
        const withRecordings = pending.current.recordings
        pending.current = { timer: null, recordings: false }
        load()
        loadTimeline()
        if (withRecordings) loadRecordings()
      }, 1200)
    },
    [load, loadTimeline, loadRecordings]
  )
  useEffect(
    () => () => {
      if (pending.current.timer !== null) window.clearTimeout(pending.current.timer)
    },
    []
  )

  useZarrarHub(gameId, {
    onState: (duel) => setSnapshot((prev) => (prev ? { ...prev, duel } : prev)),
    onEvent: (e) => reloadSoon(e.type === 'ended' || e.type === 'feedLost' || e.type === 'feedRestored'),
    onReconnected: () => {
      load()
      loadTimeline()
    },
  })

  const run = async (label: string, action: () => Promise<unknown>, done?: string) => {
    setBusy(label)
    try {
      await action()
      if (done) showNotification({ color: 'teal', message: done })
      await load()
    } catch (e) {
      showNotification({ color: 'red', title: `${label} failed`, message: errorText(e) })
    } finally {
      setBusy(null)
    }
  }

  const duel = snapshot?.duel
  const players = snapshot?.players ?? []
  const challenges = snapshot?.challenges ?? []
  const readiness = snapshot?.readiness ?? []
  const nextQueued = challenges.find((c) => c.status === 'queued')
  const serverNow = now + offset.current

  const blocking = readiness.filter((r) => r.critical && !r.ok)
  const warnings = readiness.filter((r) => !r.critical && !r.ok)
  const blocked = blocking.length > 0 && !override

  const releaseNow = async (challengeId: number) => {
    await api.edit.editUpdateGameChallenge(gameId, challengeId, { isEnabled: true })
    await zarrarApi.announce(gameId, challengeId)
  }

  const release = (challengeId: number, title: string) =>
    run('Release', () => releaseNow(challengeId), `${title} is live for both players.`)

  const startMatch = () => {
    const first = nextQueued
    return run(
      'Start',
      async () => {
        await zarrarApi.start(gameId)
        if (first) {
          await sleep(COUNTDOWN_MS)
          await releaseNow(first.id)
        }
      },
      first ? `Match started. ${first.title} is live.` : 'Match started.'
    )
  }

  const withdraw = (challengeId: number, title: string) =>
    modals.openConfirmModal({
      title: 'Withdraw this challenge?',
      children: `${title} disappears for both players and nobody scores it. Use this when nobody can solve it.`,
      labels: { confirm: 'Withdraw', cancel: 'Keep it live' },
      confirmProps: { color: 'red' },
      onConfirm: () =>
        run(
          'Withdraw',
          async () => {
            await api.edit.editUpdateGameChallenge(gameId, challengeId, { isEnabled: false })
            await zarrarApi.withdraw(gameId, challengeId)
          },
          `${title} withdrawn.`
        ),
    })

  const endMatch = () =>
    modals.openConfirmModal({
      title: 'End the match now?',
      children:
        'The clock stops, flags are no longer accepted and the stage shows the winner. You can reopen it, but the end time has to be fixed in game settings.',
      labels: { confirm: 'End match', cancel: 'Cancel' },
      confirmProps: { color: 'red' },
      onConfirm: () => run('End match', () => zarrarApi.end(gameId), 'Match ended.'),
    })

  const togglePause = () =>
    duel?.paused
      ? run('Resume', () => zarrarApi.resume(gameId), 'Match resumed.')
      : run('Pause', () => zarrarApi.pause(gameId), 'Match paused.')

  const canStart = !!duel?.enabled && !duel.startedUtc && !duel.ended && !duel.paused
  const canRelease = !!duel?.enabled && !duel.ended && !blocked

  /* -------------------------------------------------------------- shortcuts */

  const keys = useRef({ busy, duel, canStart, canRelease, nextQueued, blocked, startMatch, release, togglePause })
  keys.current = { busy, duel, canStart, canRelease, nextQueued, blocked, startMatch, release, togglePause }

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (!shortcutAllowed(e)) return
      const k = keys.current
      if (k.busy || !k.duel?.enabled) return

      if (!e.shiftKey && LAYOUT_KEYS[e.code]) {
        e.preventDefault()
        run('Layout', () => zarrarApi.setLayout(gameId, LAYOUT_KEYS[e.code]))
      } else if (!e.shiftKey && e.code === 'KeyP' && !k.duel.ended) {
        e.preventDefault()
        k.togglePause()
      } else if (e.shiftKey && e.code === 'KeyN') {
        e.preventDefault()
        if (k.blocked) showNotification({ color: 'yellow', message: 'Fix the blocking checks first, or override them.' })
        else if (k.canRelease && k.nextQueued) k.release(k.nextQueued.id, k.nextQueued.title)
      } else if (e.shiftKey && e.code === 'KeyS') {
        e.preventDefault()
        if (k.blocked) showNotification({ color: 'yellow', message: 'Fix the blocking checks first, or override them.' })
        else if (k.canStart) k.startMatch()
      }
    }

    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [gameId])

  /* ----------------------------------------------------------------- export */

  const nameOf = (pid?: number | null, fallback?: string | null) =>
    players.find((p) => p.participationId === pid)?.teamName ?? fallback ?? ''

  const exportCsv = () => {
    const header = ['time_utc', 'local_time', 'event', 'player', 'user', 'challenge', 'points', 'seconds_live', 'note']
    const rows = timeline.map((e) => [
      new Date(e.timeUtc).toISOString(),
      timeOf(e.timeUtc),
      EVENT_LABEL[e.type] ?? e.type,
      nameOf(e.participationId, e.teamName),
      e.userName ?? '',
      e.challengeTitle ?? '',
      e.points ?? '',
      e.seconds ?? '',
      e.message ?? '',
    ])
    const csv = [header, ...rows].map((r) => r.map(csvCell).join(',')).join('\r\n')
    const url = URL.createObjectURL(new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8' }))
    const a = document.createElement('a')
    a.href = url
    a.download = `zarrar-match-${gameId}-timeline.csv`
    a.click()
    window.setTimeout(() => URL.revokeObjectURL(url), 1000)
  }

  /* ----------------------------------------------------------------- render */

  const status = !duel?.enabled
    ? { label: 'NOT A SHOWDOWN', color: '#6b7280' }
    : duel.ended
      ? { label: 'MATCH OVER', color: '#9aa1ad' }
      : duel.paused
        ? { label: 'PAUSED', color: '#ffb020' }
        : snapshot && serverNow < snapshot.startUtc
          ? { label: 'NOT STARTED', color: '#9aa1ad' }
          : snapshot && serverNow > snapshot.endUtc
            ? { label: 'TIME UP', color: '#9aa1ad' }
            : !duel.startedUtc
              ? { label: 'READY TO START', color: '#9aa1ad' }
              : { label: 'LIVE', color: '#ff2a3d' }

  const recent = [...timeline].reverse()

  return (
    <AdminLayout
      title={snapshot?.title ? `ZARRAR Live · ${snapshot.title}` : 'ZARRAR Live'}
      documentTitle="ZARRAR Live"
      breadcrumbs={[
        { label: 'ZARRAR Live', to: '/admin/live' },
        ...(snapshot ? [{ label: snapshot.title, to: `/admin/games/${gameId}/info` }] : []),
      ]}
      isLoading={!snapshot}
      actions={
        <Button component="a" href={`/monitor/${gameId}`} target="_blank" rel="noreferrer" variant="default">
          Open monitor stage
        </Button>
      }
    >
      <div className={classes.layout}>
        {/* ============================================================ left */}
        <div>
          <div className={classes.preview}>
            {Number.isFinite(gameId) && gameId > 0 && <Stage gameId={gameId} embedded sound={sound} />}
          </div>

          <div className={classes.previewBar}>
            <SegmentedControl
              value={duel?.layout ?? 'split'}
              disabled={!duel?.enabled || !!busy}
              onChange={(value) => run('Layout', () => zarrarApi.setLayout(gameId, value as DuelLayout))}
              data={[
                { label: 'Both', value: 'split' },
                { label: 'Focus P1', value: 'p1' },
                { label: 'Focus P2', value: 'p2' },
                { label: 'Full P1', value: 'f1' },
                { label: 'Full P2', value: 'f2' },
                { label: 'Scores', value: 'board' },
              ]}
            />
            <Switch
              label="Preview sound"
              checked={sound}
              onChange={async (e) => {
                const on = e.currentTarget.checked
                if (on) await unlockSfx()
                setSound(on)
              }}
            />
          </div>
          <p className={classes.hint}>
            This preview is the same stage the auditorium sees: layout, takeovers and pause screens are pushed from the
            server to every screen at once.
          </p>
          <p className={classes.shortcuts}>
            Shortcuts: <kbd className={classes.kbd}>1</kbd>–<kbd className={classes.kbd}>6</kbd> layout (5 / 6: one screen full size) ·{' '}
            <kbd className={classes.kbd}>P</kbd> pause / resume · <kbd className={classes.kbd}>Shift</kbd>+
            <kbd className={classes.kbd}>N</kbd> release next · <kbd className={classes.kbd}>Shift</kbd>+
            <kbd className={classes.kbd}>S</kbd> start match
          </p>

          {/* ----------------------------------------------------- timeline */}
          <section className={classes.section} style={{ marginTop: 14 }}>
            <div className={classes.sectionHead}>
              <h2 className={classes.sectionTitle}>TIMELINE</h2>
              <Group gap="xs">
                <Button size="xs" variant="default" onClick={loadTimeline}>
                  Refresh
                </Button>
                <Button size="xs" variant="light" color="red" disabled={timeline.length === 0} onClick={exportCsv}>
                  Export CSV
                </Button>
              </Group>
            </div>
            {recent.length === 0 ? (
              <p className={classes.hint} style={{ marginTop: 0 }}>
                Nothing yet. Releases, wrong flags, captures, pauses and lost feeds appear here as they happen.
              </p>
            ) : (
              <div className={classes.timeline}>
                {recent.map((e, i) => (
                  <div key={`${e.timeUtc}-${i}`} className={classes.tlRow}>
                    <span className={classes.tlTime}>{timeOf(e.timeUtc)}</span>
                    <span className={classes.tlType} style={{ color: EVENT_COLOR[e.type] }}>
                      {EVENT_LABEL[e.type] ?? e.type}
                    </span>
                    <span className={classes.tlText}>
                      {[
                        nameOf(e.participationId, e.teamName),
                        e.type === 'tagged' ? `${e.message ? `${e.message} → ` : ''}${e.userName ?? ''}` : null,
                        e.challengeTitle,
                        e.points ? `+${e.points}` : null,
                        e.seconds != null && e.type === 'captured' ? `in ${formatDuration(e.seconds)}` : null,
                        e.seconds != null && e.type === 'withdrawn' ? `after ${formatDuration(e.seconds)}` : null,
                        e.type === 'tagged' ? null : e.message,
                      ]
                        .filter(Boolean)
                        .join(' · ')}
                    </span>
                  </div>
                ))}
              </div>
            )}
          </section>

          {/* --------------------------------------------------- recordings */}
          <section className={classes.section}>
            <div className={classes.sectionHead}>
              <h2 className={classes.sectionTitle}>RECORDINGS</h2>
              <Button size="xs" variant="default" onClick={loadRecordings}>
                Refresh
              </Button>
            </div>
            {!recordings || recordings.length === 0 ? (
              <p className={classes.hint} style={{ marginTop: 0 }}>
                No recordings yet. Each time a player starts sharing, their screen is recorded to a new file.
              </p>
            ) : (
              recordings.map((r) => (
                <div key={r.name} className={classes.row}>
                  <div className={classes.rowMain}>
                    <div className={classes.rowTitle}>{nameOf(r.participationId) || r.name}</div>
                    <div className={classes.rowSub}>
                      {formatBytes(r.bytes)} · last written {new Date(r.modifiedUtc).toLocaleString()} · {r.name}
                    </div>
                  </div>
                  <Button
                    size="xs"
                    variant="light"
                    component="a"
                    href={zarrarApi.recordingUrl(gameId, r.name)}
                    download={r.name}
                  >
                    Download
                  </Button>
                </div>
              ))
            )}
            <p className={classes.hint}>
              IVF video: opens in VLC, or convert with <code>ffmpeg -i file.ivf -c copy file.webm</code>. A file still
              being written downloads up to the current moment.
            </p>
          </section>
        </div>

        {/* =========================================================== right */}
        <div>
          {/* ---------------------------------------------------- pre-flight */}
          <section className={classes.section}>
            <div className={classes.sectionHead}>
              <h2 className={classes.sectionTitle}>PRE-FLIGHT</h2>
              <Badge
                variant="filled"
                color={blocking.length ? 'red' : warnings.length ? 'yellow' : 'teal'}
                styles={{ root: { color: blocking.length ? '#fff' : '#0b0b0d' } }}
              >
                {blocking.length
                  ? `${blocking.length} blocking`
                  : warnings.length
                    ? `${warnings.length} warning${warnings.length === 1 ? '' : 's'}`
                    : 'Ready'}
              </Badge>
            </div>

            {readiness.map((r) => (
              <div key={r.key} className={classes.check}>
                <span
                  className={`${classes.checkMark} ${r.ok ? classes.checkOk : r.critical ? classes.checkBad : classes.checkWarn}`}
                  aria-hidden="true"
                >
                  {r.ok ? '✓' : r.critical ? '✕' : '!'}
                </span>
                <div className={classes.rowMain}>
                  <div className={classes.checkLabel}>
                    {r.label}
                    {!r.ok && r.critical && <span className={classes.checkTag}>BLOCKING</span>}
                  </div>
                  <div className={classes.rowSub} style={{ whiteSpace: 'normal' }}>
                    {r.detail}
                  </div>
                </div>
              </div>
            ))}

            {blocking.length > 0 && (
              <Checkbox
                mt="sm"
                color="red"
                label="Override: allow Start and Release anyway"
                checked={override}
                onChange={(e) => setOverride(e.currentTarget.checked)}
              />
            )}
          </section>

          {/* --------------------------------------------------------- match */}
          <section className={classes.section}>
            <h2 className={classes.sectionTitle}>MATCH</h2>
            <div className={classes.status}>
              <span className={classes.statusDot} style={{ background: status.color }} />
              {status.label}
            </div>
            {duel?.paused && duel.pauseReason && <p className={classes.hint}>{duel.pauseReason}</p>}
            {duel?.startedUtc && <p className={classes.hint}>Started at {timeOf(duel.startedUtc)}.</p>}

            <Switch
              mb="md"
              label="1v1 showdown mode"
              description="Screen sharing required, first flag captures, auto-pause on a lost feed"
              checked={!!duel?.enabled}
              disabled={!!busy}
              onChange={(e) => {
                const on = e.currentTarget.checked
                run('Showdown mode', () => zarrarApi.setMode(gameId, on), on ? 'Showdown mode on.' : 'Showdown mode off.')
              }}
            />

            {canStart && (
              <Button
                fullWidth
                mb="sm"
                size="md"
                color="red"
                loading={busy === 'Start'}
                disabled={blocked || !!busy}
                onClick={startMatch}
              >
                {nextQueued ? `Start match · first target: ${nextQueued.title}` : 'Start match'}
              </Button>
            )}

            <Group gap="xs" grow>
              {duel?.paused ? (
                <Button
                  color="teal"
                  loading={busy === 'Resume'}
                  disabled={!duel?.enabled || !!duel?.ended}
                  onClick={togglePause}
                >
                  Resume
                </Button>
              ) : (
                <Button
                  color="yellow"
                  variant="light"
                  loading={busy === 'Pause'}
                  disabled={!duel?.enabled || !!duel?.ended}
                  onClick={togglePause}
                >
                  Pause
                </Button>
              )}

              {duel?.ended ? (
                <Button
                  variant="default"
                  loading={busy === 'Reopen'}
                  onClick={() => run('Reopen', () => zarrarApi.reopen(gameId), 'Match reopened.')}
                >
                  Reopen
                </Button>
              ) : (
                <Button color="red" variant="light" disabled={!duel?.enabled || !!busy} onClick={endMatch}>
                  End match
                </Button>
              )}
            </Group>
          </section>

          {/* ------------------------------------------------------- players */}
          <section className={classes.section}>
            <h2 className={classes.sectionTitle}>PLAYERS</h2>
            {players.map((p, i) => (
              <div key={p.participationId} className={classes.row}>
                <div className={classes.rowMain}>
                  <div className={classes.rowTitle}>
                    <span className={classes.feedDot} style={{ background: p.live ? '#1ff28c' : '#ff2a3d' }} />
                    {i < 2 ? `P${i + 1} · ` : ''}
                    {p.teamName}
                  </div>
                  <div className={classes.rowSub}>
                    {p.live ? `Screen live${p.operatorName ? ` — ${p.operatorName}` : ''}` : 'Not sharing'} ·{' '}
                    {p.members.join(', ') || 'no members'}
                  </div>
                </div>
                <Badge variant="light" color={i === 0 ? 'blue' : 'green'} size="lg">
                  {p.score}
                </Badge>
              </div>
            ))}
            {players.length === 0 && (
              <p className={classes.hint} style={{ marginTop: 0 }}>
                No accepted players yet.
              </p>
            )}
            {players.length > 0 && (
              <>
                <h3 className={classes.sectionTitle} style={{ marginTop: 16, fontSize: 12 }}>
                  TAG TEAM — ON THE KEYBOARD
                </h3>
                <TagRoster gameId={gameId} players={players} onChanged={load} />
              </>
            )}
          </section>

          {/* ---------------------------------------------------- challenges */}
          <section className={classes.section}>
            <h2 className={classes.sectionTitle}>CHALLENGES</h2>
            <Button
              fullWidth
              mb="sm"
              color="red"
              disabled={!nextQueued || !canRelease || !!busy}
              loading={busy === 'Release'}
              onClick={() => nextQueued && release(nextQueued.id, nextQueued.title)}
            >
              {nextQueued ? `Release next: ${nextQueued.title}` : 'No challenge left to release'}
            </Button>

            {challenges.map((c) => {
              const live = c.status === 'live' ? liveSecondsOf(duel, c.id, serverNow) : null
              return (
                <div key={c.id} className={classes.row}>
                  <div className={classes.rowMain}>
                    <div className={classes.rowTitle}>{c.title}</div>
                    <div className={classes.rowSub}>
                      {c.category} · {c.score} pts
                      {live != null && ` · live ${formatDuration(live)}`}
                      {c.status === 'captured' && ` · captured by ${c.capturedByName ?? 'unknown'}`}
                      {c.status === 'withdrawn' && ' · withdrawn'}
                    </div>
                  </div>
                  {(c.status === 'queued' || c.status === 'withdrawn') && (
                    <Button
                      size="xs"
                      variant="light"
                      disabled={!!busy || !canRelease}
                      onClick={() => release(c.id, c.title)}
                    >
                      Release
                    </Button>
                  )}
                  {c.status === 'live' && (
                    <Button size="xs" color="red" variant="subtle" disabled={!!busy} onClick={() => withdraw(c.id, c.title)}>
                      Withdraw
                    </Button>
                  )}
                  {c.status === 'captured' && (
                    <Badge color="gray" variant="light">
                      Captured
                    </Badge>
                  )}
                </div>
              )
            })}
            <p className={classes.hint}>
              Release order is top to bottom. Only the first correct flag on a challenge scores, so each capture is worth
              exactly the listed points (unless a first-blood bonus is on — see Pre-flight).
            </p>
          </section>
        </div>
      </div>
    </AdminLayout>
  )
}

export default AdminLive

import dayjs, { Dayjs } from 'dayjs'
import { FC, useEffect, useMemo, useRef } from 'react'
import { Link } from 'react-router'
import { useLanguage } from '@Utils/I18n'
import classes from '@Styles/player/EventTimeline.module.css'

/** Pixels per day column. Matches the original Gantt so the density is unchanged. */
const DAY_WIDTH = 40
/** Weeks shown: 3 before today, 4 after — the original's window. */
const WEEKS_BEFORE = 3
const TOTAL_WEEKS = 7

export interface TimelineEvent {
  id: number
  title: string
  start: Dayjs
  end: Dayjs
  color: string
  colorHi: string
}

interface EventTimelineProps {
  events: TimelineEvent[]
}

/**
 * Horizontal 3D timeline of events across a seven-week window.
 *
 * Restores the original GanttTimeline that the card-grid rewrite dropped, keeping its window,
 * day/month headers, weekend shading and now-marker, and adding depth: the scene is tilted in a
 * single shared perspective so headers and bars stay aligned, and bars are extruded off that
 * plane. Hovering the timeline flattens it back to 0deg for precise date reading.
 */
export const EventTimeline: FC<EventTimelineProps> = ({ events }) => {
  const viewportRef = useRef<HTMLDivElement>(null)
  const { locale } = useLanguage()

  const model = useMemo(() => {
    const now = dayjs().startOf('hour')
    const start = now.startOf('week').subtract(WEEKS_BEFORE, 'week')
    const end = start.add(TOTAL_WEEKS, 'week').subtract(1, 'second')
    const totalDays = end.diff(start, 'day')
    const durationSeconds = end.diff(start, 'second')
    const width = totalDays * DAY_WIDTH

    const days: { i: number; label: string; isToday: boolean }[] = []
    const months: { i: number; label: string }[] = []
    const weekends: number[] = []

    for (let i = 0; i <= totalDays; i++) {
      const current = start.add(i, 'day').locale(locale)
      days.push({ i, label: current.format('DD'), isToday: current.isSame(now, 'day') })
      if (current.date() === 1) months.push({ i, label: current.format('MMM') })
      // Saturday starts the weekend block; the shading spans Sat+Sun, hence 2x width in CSS.
      if (current.day() === 6) weekends.push(i)
    }

    const nowOffset = (now.diff(start, 'second') / durationSeconds) * width

    return { now, start, end, totalDays, durationSeconds, width, days, months, weekends, nowOffset }
  }, [locale])

  // Open scrolled to "now" rather than to the start of the window, so today is on screen.
  useEffect(() => {
    const el = viewportRef.current
    if (!el) return
    el.scrollTo({ left: Math.max(0, model.nowOffset - el.clientWidth / 2) })
  }, [model.nowOffset])

  const positioned = useMemo(
    () =>
      events.map((event) => {
        const clippedLeft = event.start.isBefore(model.start)
        const clippedRight = event.end.isAfter(model.end)

        const left = clippedLeft
          ? 0
          : (event.start.diff(model.start, 'second') / model.durationSeconds) * model.width
        const right = clippedRight
          ? model.width
          : (event.end.diff(model.start, 'second') / model.durationSeconds) * model.width

        return {
          event,
          left,
          width: Math.max(right - left, 6),
          clippedRight,
          // Fully outside the window — rendered as an empty row so row heights stay aligned
          // with any list rendered beside the timeline.
          offscreen: event.end.isBefore(model.start) || event.start.isAfter(model.end),
        }
      }),
    [events, model]
  )

  if (events.length === 0) {
    return (
      <div className={classes.wrap}>
        <div className={classes.head}>
          <h2 className={classes.title}>Event timeline</h2>
        </div>
        <p className={classes.empty}>No events scheduled in this window.</p>
      </div>
    )
  }

  return (
    <section className={classes.wrap} aria-label="Event timeline">
      <div className={classes.head}>
        <h2 className={classes.title}>Event timeline</h2>
        <p className={classes.range}>
          {model.start.locale(locale).format('D MMM')} — {model.end.locale(locale).format('D MMM YYYY')}
        </p>
      </div>

      <div className={classes.viewport} ref={viewportRef}>
        <div className={classes.scene} style={{ width: model.width }}>
          <div className={classes.dateRow}>
            {model.months.map((m) => (
              <span key={`m${m.i}`} className={classes.month} style={{ left: m.i * DAY_WIDTH }}>
                {m.label}
              </span>
            ))}
            {model.days.map((d) => (
              <span
                key={`d${d.i}`}
                className={`${classes.day} ${d.isToday ? classes.today : ''}`}
                style={{ left: d.i * DAY_WIDTH }}
              >
                {d.label}
              </span>
            ))}
          </div>

          <div className={classes.grid}>
            <div className={classes.gridLines} />

            {model.weekends.map((i) => (
              <span key={`w${i}`} className={classes.weekend} style={{ left: i * DAY_WIDTH }} />
            ))}

            <div className={classes.nowLine} style={{ left: model.nowOffset }}>
              <span className={classes.nowFlag}>NOW</span>
            </div>

            {positioned.map(({ event, left, width, clippedRight, offscreen }, index) =>
              offscreen ? (
                <div key={event.id} className={classes.row} />
              ) : (
                <div key={event.id} className={classes.row}>
                  <Link
                    to={`/games/${event.id}`}
                    className={`${classes.bar} ${clippedRight ? classes.overflowRight : ''} bqFocusRing`}
                    style={{
                      left,
                      width,
                      // Staggered entrance, capped so a long list doesn't take seconds to draw.
                      animationDelay: `${Math.min(index * 60, 600)}ms`,
                      ['--bar' as string]: event.color,
                      ['--bar-hi' as string]: event.colorHi,
                    }}
                    title={`${event.title} — ${event.start.format('D MMM HH:mm')} to ${event.end.format('D MMM HH:mm')}`}
                  >
                    <span className={classes.barShadow} aria-hidden="true" />
                    <span className={classes.barTop} aria-hidden="true" />
                    <span className={classes.barFace} aria-hidden="true" />
                    {width > 90 && <span className={classes.barLabel}>{event.title}</span>}
                  </Link>

                  {width <= 90 && (
                    <span className={classes.barLabelOutside} style={{ left: left + width + 10 }}>
                      {event.title}
                    </span>
                  )}
                </div>
              )
            )}
          </div>
        </div>
      </div>

      <div className={classes.legend}>
        <span className={classes.legendItem}>
          <span className={classes.legendSwatch} style={{ background: 'var(--bq-live)' }} />
          LIVE
        </span>
        <span className={classes.legendItem}>
          <span className={classes.legendSwatch} style={{ background: 'var(--bq-solved)' }} />
          UPCOMING
        </span>
        <span className={classes.legendItem}>
          <span className={classes.legendSwatch} style={{ background: 'var(--bq-locked)' }} />
          ENDED
        </span>
        <span className={classes.legendItem}>HOVER TO FLATTEN · CLICK A BAR TO OPEN</span>
      </div>
    </section>
  )
}

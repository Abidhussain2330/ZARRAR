import dayjs from 'dayjs'
import { FC, ReactNode } from 'react'
import classes from '@Styles/player/Arena.module.css'

/* ------------------------------------------------------------------ header */

interface PageHeadProps {
  eyebrow?: string
  title: string
  subtitle?: string
  actions?: ReactNode
}

export const PageHead: FC<PageHeadProps> = ({ eyebrow, title, subtitle, actions }) => (
  <div className={classes.head}>
    <div className={classes.headText}>
      {eyebrow && <span className={`${classes.eyebrow} zr-cursor`}>// {eyebrow}</span>}
      <h1 className={classes.title}>
        <span className="zr-glitch" data-text={title}>
          {title}
        </span>
      </h1>
      {subtitle && <p className={classes.subtitle}>{subtitle}</p>}
    </div>
    {actions && <div className={classes.headActions}>{actions}</div>}
  </div>
)

/* ------------------------------------------------------------------ states */

export const Loading: FC<{ label?: string }> = ({ label = 'Loading…' }) => (
  <div className={classes.state} role="status" aria-live="polite">
    <span className={classes.spinner} aria-hidden="true" />
    <p className={classes.stateBody}>{label}</p>
  </div>
)

interface StateProps {
  title: string
  body?: string
  action?: ReactNode
}

export const Empty: FC<StateProps> = ({ title, body, action }) => (
  <div className={classes.state}>
    <p className={classes.stateTitle}>{title}</p>
    {body && <p className={classes.stateBody}>{body}</p>}
    {action}
  </div>
)

export const ErrorState: FC<StateProps> = ({ title, body, action }) => (
  <div className={`${classes.state} ${classes.stateError}`} role="alert">
    <p className={classes.stateTitle}>{title}</p>
    {body && <p className={classes.stateBody}>{body}</p>}
    {action}
  </div>
)

/* ------------------------------------------------------------------ badge */

interface BadgeProps {
  color: string
  children: ReactNode
  /** Adds the pulsing dot. Reserved for states that change while you watch. */
  live?: boolean
  withDot?: boolean
}

/**
 * Colour is always paired with a text label — never the only signal, so the badge
 * still reads for colour-blind players and in high-contrast modes.
 */
export const Badge: FC<BadgeProps> = ({ color, children, live, withDot = true }) => (
  <span
    className={classes.badge}
    style={{
      color,
      borderColor: `color-mix(in srgb, ${color} 35%, transparent)`,
      background: `color-mix(in srgb, ${color} 10%, transparent)`,
    }}
  >
    {children}
    {withDot && (
      <span
        className={`${classes.dot} ${live ? classes.dotLive : ''}`}
        style={{ background: color }}
        aria-hidden="true"
      />
    )}
  </span>
)

/* ------------------------------------------------------------ game status */

export type GameStatus = 'upcoming' | 'live' | 'ended'

export const gameStatus = (start?: string | null, end?: string | null): GameStatus => {
  const now = dayjs()
  if (start && now.isBefore(dayjs(start))) return 'upcoming'
  if (end && now.isAfter(dayjs(end))) return 'ended'
  return 'live'
}

export const STATUS_META: Record<GameStatus, { label: string; color: string }> = {
  live: { label: 'LIVE', color: 'var(--bq-live)' },
  upcoming: { label: 'UPCOMING', color: 'var(--bq-solved)' },
  ended: { label: 'ENDED', color: 'var(--bq-locked)' },
}

export const GameStatusBadge: FC<{ start?: string | null; end?: string | null }> = ({ start, end }) => {
  const status = gameStatus(start, end)
  const meta = STATUS_META[status]
  return (
    <Badge color={meta.color} live={status === 'live'}>
      {meta.label}
    </Badge>
  )
}

/* ------------------------------------------------------------- categories */

/**
 * Category colours resolve through the token sheet. Anything the backend adds
 * later that isn't listed falls back to the neutral hue rather than breaking.
 */
export const categoryColor = (category?: string | null) => {
  const key = (category ?? '').toLowerCase()
  const map: Record<string, string> = {
    web: 'var(--bq-cat-web)',
    reverse: 'var(--bq-cat-reverse)',
    crypto: 'var(--bq-cat-crypto)',
    pwn: 'var(--bq-cat-pwn)',
    forensics: 'var(--bq-cat-forensics)',
    misc: 'var(--bq-cat-misc)',
    osint: 'var(--bq-cat-osint)',
    stego: 'var(--bq-cat-stego)',
    hardware: 'var(--bq-cat-hardware)',
    blockchain: 'var(--bq-cat-blockchain)',
    ai: 'var(--bq-cat-ai)',
    mobile: 'var(--bq-cat-mobile)',
    ppc: 'var(--bq-cat-ppc)',
    pentest: 'var(--bq-cat-pentest)',
  }
  return map[key] ?? 'var(--bq-cat-hardware)'
}

/* ------------------------------------------------------------- pagination */

interface PagerProps {
  page: number
  pageCount: number
  onChange: (page: number) => void
}

export const Pager: FC<PagerProps> = ({ page, pageCount, onChange }) => {
  if (pageCount <= 1) return null

  // Windowed so a long event history doesn't produce a hundred buttons.
  const windowSize = 5
  let start = Math.max(1, page - Math.floor(windowSize / 2))
  const end = Math.min(pageCount, start + windowSize - 1)
  start = Math.max(1, end - windowSize + 1)

  const pages = Array.from({ length: end - start + 1 }, (_, i) => start + i)

  return (
    <nav className={classes.pager} aria-label="Pagination">
      <button
        type="button"
        className={`${classes.pageBtn} bqFocusRing`}
        disabled={page <= 1}
        onClick={() => onChange(page - 1)}
        aria-label="Previous page"
      >
        ‹
      </button>
      {pages.map((p) => (
        <button
          key={p}
          type="button"
          className={`${classes.pageBtn} bqFocusRing ${p === page ? classes.pageBtnOn : ''}`}
          aria-current={p === page ? 'page' : undefined}
          onClick={() => onChange(p)}
        >
          {p}
        </button>
      ))}
      <button
        type="button"
        className={`${classes.pageBtn} bqFocusRing`}
        disabled={page >= pageCount}
        onClick={() => onChange(page + 1)}
        aria-label="Next page"
      >
        ›
      </button>
    </nav>
  )
}

export { classes as arena }

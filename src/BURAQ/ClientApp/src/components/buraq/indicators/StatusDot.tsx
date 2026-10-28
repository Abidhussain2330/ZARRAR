import { FC, ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import classes from '@Styles/buraq/Indicators.module.css'

export type BuraqStatus = 'live' | 'active' | 'solved' | 'locked' | 'offline'

const STATUS_LABEL_DEFAULTS: Record<BuraqStatus, string> = {
  live: 'Live',
  active: 'Active',
  solved: 'Solved',
  locked: 'Locked',
  offline: 'Offline',
}

/**
 * Each status draws a different silhouette — ring, diamond, tick, padlock,
 * struck-through circle — so the state survives greyscale printing and
 * forced-colours mode, where every dot would otherwise collapse to one colour.
 */
const STATUS_SHAPE: Record<BuraqStatus, ReactNode> = {
  live: (
    <>
      <circle cx="8" cy="8" r="5" className={classes.statusRing} />
      <circle cx="8" cy="8" r="2.5" />
    </>
  ),
  active: <path d="M8 1.5 14.5 8 8 14.5 1.5 8Z" />,
  solved: <path d="M2.5 8.4 6.2 12 13.5 4.4" className={classes.statusStroke} />,
  locked: (
    <>
      <rect x="3" y="7" width="10" height="7" rx="1.5" />
      <path d="M5.5 7V5.2a2.5 2.5 0 0 1 5 0V7" className={classes.statusStroke} />
    </>
  ),
  offline: (
    <>
      <circle cx="8" cy="8" r="5.5" className={classes.statusStroke} />
      <path d="M4.2 11.8 11.8 4.2" className={classes.statusStroke} />
    </>
  ),
}

export interface StatusDotProps {
  status: BuraqStatus
  /** Visible text beside the glyph. Preferred — it needs no ARIA at all. */
  label?: ReactNode
  /** Accessible name used when no visible `label` is given. */
  srLabel?: string
  size?: 'sm' | 'md'
  className?: string
}

export const StatusDot: FC<StatusDotProps> = ({ status, label, srLabel, size = 'md', className }) => {
  const { t } = useTranslation()
  const hasVisibleLabel = label !== undefined && label !== null && label !== false
  const accessibleName =
    srLabel ?? t(`common.indicator.status.${status}`, { defaultValue: STATUS_LABEL_DEFAULTS[status] })

  return (
    <span className={[classes.status, className ?? ''].filter(Boolean).join(' ')} data-status={status} data-size={size}>
      <svg
        className={classes.statusGlyph}
        viewBox="0 0 16 16"
        role={hasVisibleLabel ? 'presentation' : 'img'}
        aria-hidden={hasVisibleLabel || undefined}
        aria-label={hasVisibleLabel ? undefined : accessibleName}
        focusable="false"
      >
        {!hasVisibleLabel && <title>{accessibleName}</title>}
        {STATUS_SHAPE[status]}
      </svg>
      {hasVisibleLabel && <span className={classes.statusLabel}>{label}</span>}
    </span>
  )
}

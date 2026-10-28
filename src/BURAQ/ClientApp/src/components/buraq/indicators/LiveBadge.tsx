import { FC } from 'react'
import { useTranslation } from 'react-i18next'
import classes from '@Styles/buraq/Indicators.module.css'

export interface LiveBadgeProps {
  /** Badge text. Override to translate; it is never hidden. */
  label?: string
  /**
   * Set false while a stream is reconnecting: the badge keeps its footprint but
   * stops claiming the data is live.
   */
  active?: boolean
  /** Replaces the label while `active` is false. */
  idleLabel?: string
  size?: 'sm' | 'md'
  className?: string
}

/**
 * The pulse is a plain CSS animation on purpose. `[data-buraq-energy='low']`
 * pauses every animation in the document, and this module stops it outright,
 * so nothing here keeps a timer running on a scoreboard left open for days.
 */
export const LiveBadge: FC<LiveBadgeProps> = ({ label, active = true, idleLabel, size = 'md', className }) => {
  const { t } = useTranslation()
  const resolvedLabel = label ?? t('common.indicator.live_badge.label', { defaultValue: 'LIVE' })
  const resolvedIdleLabel = idleLabel ?? t('common.indicator.live_badge.paused', { defaultValue: 'PAUSED' })

  return (
    <span
      className={[classes.live, className ?? ''].filter(Boolean).join(' ')}
      data-size={size}
      data-idle={!active || undefined}
    >
      <span className={classes.liveDot} aria-hidden />
      <span className={classes.liveLabel}>{active ? resolvedLabel : resolvedIdleLabel}</span>
    </span>
  )
}

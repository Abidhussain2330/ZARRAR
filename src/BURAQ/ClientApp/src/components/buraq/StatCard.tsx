import { FC, ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import classes from '@Styles/buraq/StatCard.module.css'

export type StatDeltaDirection = 'up' | 'down' | 'flat'

/** Whether the movement is good news. A falling error rate is `good`, not `bad`. */
export type StatDeltaPolarity = 'good' | 'bad' | 'neutral'

export interface StatDelta {
  value: ReactNode
  direction: StatDeltaDirection
  /** Defaults to up=good, down=bad. Set explicitly when lower is better. */
  polarity?: StatDeltaPolarity
  /** Comparison context, e.g. 'vs. last hour'. */
  context?: ReactNode
  /** Direction word for screen readers. Override to translate. */
  srDirection?: string
}

export interface StatCardProps {
  label: string
  value: ReactNode
  icon?: ReactNode
  delta?: StatDelta
  sublabel?: ReactNode
  loading?: boolean
  /** Announced while `loading`. Override to translate. */
  loadingLabel?: string
  /** Tints the icon and the value. Use sparingly — a wall of red reads as noise. */
  tone?: 'neutral' | 'accent' | 'success' | 'warning' | 'critical'
  className?: string
}

const DELTA_GLYPH: Record<StatDeltaDirection, string> = {
  up: '▲',
  down: '▼',
  flat: '—',
}

const DELTA_SR_DIRECTION: Record<StatDeltaDirection, string> = {
  up: 'increase',
  down: 'decrease',
  flat: 'no change',
}

const DEFAULT_POLARITY: Record<StatDeltaDirection, StatDeltaPolarity> = {
  up: 'good',
  down: 'bad',
  flat: 'neutral',
}

/**
 * A single metric tile.
 *
 * Direction is carried by an arrow glyph as well as colour, so the trend
 * survives greyscale, colour-blindness and forced-colours mode.
 */
export const StatCard: FC<StatCardProps> = ({
  label,
  value,
  icon,
  delta,
  sublabel,
  loading = false,
  loadingLabel,
  tone = 'neutral',
  className,
}) => {
  const { t } = useTranslation()
  const resolvedLoadingLabel = loadingLabel ?? t('common.state.loading', { defaultValue: 'Loading' })

  return (
    <div
      className={[classes.card, className ?? ''].filter(Boolean).join(' ')}
      data-tone={tone}
      aria-busy={loading || undefined}
    >
      <div className={classes.head}>
        {icon && (
          <span className={classes.icon} aria-hidden>
            {icon}
          </span>
        )}
        <p className={classes.label}>{label}</p>
      </div>

      {loading ? (
        <>
          <span className={classes.srOnly} role="status">
            {resolvedLoadingLabel}
          </span>
          <div className={classes.skeleton} aria-hidden>
            <span className={`${classes.bar} ${classes.barValue}`} />
            <span className={`${classes.bar} ${classes.barMeta}`} />
          </div>
        </>
      ) : (
        <>
          <p className={classes.value}>{value}</p>

          {(delta || sublabel) && (
            <div className={classes.meta}>
              {delta && (
                <span className={classes.delta} data-polarity={delta.polarity ?? DEFAULT_POLARITY[delta.direction]}>
                  <span className={classes.deltaGlyph} aria-hidden>
                    {DELTA_GLYPH[delta.direction]}
                  </span>
                  <span className={classes.deltaValue}>{delta.value}</span>
                  <span className={classes.srOnly}>{delta.srDirection ?? DELTA_SR_DIRECTION[delta.direction]}</span>
                  {delta.context && <span className={classes.deltaContext}>{delta.context}</span>}
                </span>
              )}
              {sublabel && <span className={classes.sublabel}>{sublabel}</span>}
            </div>
          )}
        </>
      )}
    </div>
  )
}

import { FC } from 'react'
import { useTranslation } from 'react-i18next'
import classes from '@Styles/zarrar/States.module.css'

export type LoadingVariant = 'panel' | 'table' | 'card-grid' | 'chart'

export interface LoadingStateProps {
  variant?: LoadingVariant
  /** Lines for `panel`, rows for `table`, tiles for `card-grid`, bars for `chart`. */
  count?: number
  /** Columns per skeleton row. Ignored outside `table`. */
  columns?: number
  /** Announced while the region is busy. Override to translate. */
  label?: string
  className?: string
}

const DEFAULT_COUNT: Record<LoadingVariant, number> = {
  panel: 4,
  table: 6,
  'card-grid': 6,
  chart: 12,
}

/*
 * Fixed rather than random: a randomised skeleton reshuffles on every render
 * and visibly flickers under React's development double-render.
 */
const LINE_WIDTHS = [92, 74, 84, 61, 88, 70]
const BAR_HEIGHTS = [42, 68, 55, 88, 34, 76, 60, 95, 48, 72, 38, 84]

const at = (values: number[], index: number): number => values[index % values.length]

const range = (count: number): number[] => Array.from({ length: Math.max(1, count) }, (_, index) => index)

const PanelSkeleton: FC<{ count: number }> = ({ count }) => (
  <div className={classes.skelStack}>
    <span className={`${classes.skel} ${classes.skelTitle}`} />
    {range(count).map((index) => (
      <span key={index} className={classes.skel} style={{ width: `${at(LINE_WIDTHS, index)}%` }} />
    ))}
  </div>
)

const TableSkeleton: FC<{ count: number; columns: number }> = ({ count, columns }) => (
  <div className={classes.skelTable}>
    <div className={classes.skelRow} data-head>
      {range(columns).map((column) => (
        <span key={column} className={classes.skelCell}>
          <span className={classes.skel} />
        </span>
      ))}
    </div>
    {range(count).map((row) => (
      <div key={row} className={classes.skelRow}>
        {range(columns).map((column) => (
          <span key={column} className={classes.skelCell}>
            <span className={classes.skel} style={{ width: `${at(LINE_WIDTHS, row + column)}%` }} />
          </span>
        ))}
      </div>
    ))}
  </div>
)

const CardGridSkeleton: FC<{ count: number }> = ({ count }) => (
  <div className={classes.skelGrid}>
    {range(count).map((index) => (
      <div key={index} className={classes.skelCard}>
        <span className={`${classes.skel} ${classes.skelChip}`} />
        <span className={`${classes.skel} ${classes.skelTitle}`} />
        <span className={classes.skel} style={{ width: `${at(LINE_WIDTHS, index)}%` }} />
      </div>
    ))}
  </div>
)

const ChartSkeleton: FC<{ count: number }> = ({ count }) => (
  <div className={classes.skelChart}>
    <div className={classes.skelBars}>
      {range(count).map((index) => (
        <span
          key={index}
          className={`${classes.skel} ${classes.skelBar}`}
          style={{ height: `${at(BAR_HEIGHTS, index)}%` }}
        />
      ))}
    </div>
    <span className={classes.skelAxis} />
  </div>
)

/**
 * Skeleton placeholder for a region that is still loading.
 *
 * The shapes are `aria-hidden`; assistive tech gets one polite announcement
 * instead of a stream of meaningless boxes.
 */
export const LoadingState: FC<LoadingStateProps> = ({
  variant = 'panel',
  count,
  columns = 5,
  label,
  className,
}) => {
  const { t } = useTranslation()
  const resolvedLabel = label ?? t('common.state.loading', { defaultValue: 'Loading' })
  const size = count ?? DEFAULT_COUNT[variant]

  return (
    <div
      className={[classes.loading, className ?? ''].filter(Boolean).join(' ')}
      role="status"
      aria-live="polite"
      aria-busy
    >
      <span className={classes.srOnly}>{resolvedLabel}</span>
      <div aria-hidden>
        {variant === 'panel' && <PanelSkeleton count={size} />}
        {variant === 'table' && <TableSkeleton count={size} columns={columns} />}
        {variant === 'card-grid' && <CardGridSkeleton count={size} />}
        {variant === 'chart' && <ChartSkeleton count={size} />}
      </div>
    </div>
  )
}

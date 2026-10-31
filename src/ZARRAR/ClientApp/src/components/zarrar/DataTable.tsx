import { CSSProperties, ReactElement, ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { EmptyState } from '@Components/zarrar/states/EmptyState'
import classes from '@Styles/zarrar/DataTable.module.css'

export interface DataTableColumn<T> {
  /** Stable identity for the column. Also used as the React key. */
  key: string
  header: ReactNode
  render: (row: T, rowIndex: number) => ReactNode
  /**
   * Text equivalent when `header` is an icon or a glyph. Without it a
   * non-textual header leaves its whole column unlabelled for screen readers.
   */
  headerLabel?: string
  align?: 'left' | 'center' | 'right'
  /** Any CSS width. Applied through <col> so it does not fight cell padding. */
  width?: string
  /** Mono digits with tabular figures, for scores, ranks and timings. */
  numeric?: boolean
  /** Pins the column while the table scrolls sideways. Use on the first column only. */
  sticky?: boolean
  /** Renders the cell as `<th scope="row">` — the label for its row. */
  rowHeader?: boolean
  nowrap?: boolean
}

export interface DataTableProps<T> {
  /**
   * Describes the table for screen readers. Required: a bare grid of numbers is
   * meaningless without it. Rendered visually only when `captionVisible`.
   */
  caption: string
  captionVisible?: boolean
  columns: DataTableColumn<T>[]
  rows: T[]
  rowKey: (row: T, rowIndex: number) => string | number
  /** Marks one row as the viewer's own — the current team on a scoreboard. */
  highlightRow?: (row: T, rowIndex: number) => boolean
  loading?: boolean
  loadingRows?: number
  loadingLabel?: string
  /** Replaces the default empty message. */
  empty?: ReactNode
  emptyTitle?: string
  emptyDescription?: ReactNode
  stickyHeader?: boolean
  /**
   * Caps the scroll viewport, which is what gives the sticky header something
   * to stick to. It is a max, so a short table still shrinks to its content.
   */
  maxHeight?: number | string
  className?: string
}

const SKELETON_WIDTHS = [88, 64, 76, 52, 92, 70]

/**
 * Themed table wrapper.
 *
 * Generic in the row type, so it is declared as a generic function rather than
 * `FC<Props>` — `FC` cannot carry a type parameter.
 */
export const DataTable = <T,>({
  caption,
  captionVisible = false,
  columns,
  rows,
  rowKey,
  highlightRow,
  loading = false,
  loadingRows = 6,
  loadingLabel,
  empty,
  emptyTitle,
  emptyDescription,
  stickyHeader = true,
  maxHeight = '70vh',
  className,
}: DataTableProps<T>): ReactElement => {
  const { t } = useTranslation()
  const resolvedLoadingLabel = loadingLabel ?? t('common.state.loading', { defaultValue: 'Loading' })
  const resolvedEmptyTitle = emptyTitle ?? t('common.state.no_results', { defaultValue: 'No results' })
  const showEmpty = !loading && rows.length === 0
  const scrollStyle: CSSProperties = { maxHeight }

  return (
    <div className={[classes.root, className ?? ''].filter(Boolean).join(' ')}>
      {/*
        tabIndex makes the scroll container reachable by keyboard: a region that
        only scrolls with a pointer is a WCAG 2.1.1 failure.
      */}
      <div
        className={classes.scroll}
        style={scrollStyle}
        role="region"
        aria-label={caption}
        aria-busy={loading || undefined}
        tabIndex={0}
      >
        <table className={classes.table}>
          <caption className={captionVisible ? classes.caption : classes.srOnly}>{caption}</caption>

          <colgroup>
            {columns.map((column) => (
              <col key={column.key} style={column.width ? { width: column.width } : undefined} />
            ))}
          </colgroup>

          <thead className={stickyHeader ? classes.stickyHead : undefined}>
            <tr>
              {columns.map((column) => (
                <th
                  key={column.key}
                  scope="col"
                  className={classes.th}
                  data-align={column.align ?? (column.numeric ? 'right' : 'left')}
                  data-sticky={column.sticky || undefined}
                >
                  {column.headerLabel ? (
                    <>
                      <span aria-hidden>{column.header}</span>
                      <span className={classes.srOnly}>{column.headerLabel}</span>
                    </>
                  ) : (
                    column.header
                  )}
                </th>
              ))}
            </tr>
          </thead>

          <tbody>
            {loading &&
              Array.from({ length: Math.max(1, loadingRows) }, (_, rowIndex) => (
                <tr key={`skeleton-${rowIndex}`} className={classes.tr}>
                  {columns.map((column, columnIndex) => (
                    <td key={column.key} className={classes.td} data-sticky={column.sticky || undefined}>
                      <span
                        className={classes.skeleton}
                        style={{ width: `${SKELETON_WIDTHS[(rowIndex + columnIndex) % SKELETON_WIDTHS.length]}%` }}
                        aria-hidden
                      />
                    </td>
                  ))}
                </tr>
              ))}

            {!loading &&
              rows.map((row, rowIndex) => {
                const highlighted = highlightRow?.(row, rowIndex) ?? false

                return (
                  <tr
                    key={rowKey(row, rowIndex)}
                    className={classes.tr}
                    data-highlight={highlighted || undefined}
                    aria-current={highlighted || undefined}
                  >
                    {columns.map((column) =>
                      column.rowHeader ? (
                        <th
                          key={column.key}
                          scope="row"
                          className={`${classes.td} ${classes.rowHeader}`}
                          data-align={column.align ?? 'left'}
                          data-numeric={column.numeric || undefined}
                          data-sticky={column.sticky || undefined}
                          data-nowrap={column.nowrap || undefined}
                        >
                          {column.render(row, rowIndex)}
                        </th>
                      ) : (
                        <td
                          key={column.key}
                          className={classes.td}
                          data-align={column.align ?? (column.numeric ? 'right' : 'left')}
                          data-numeric={column.numeric || undefined}
                          data-sticky={column.sticky || undefined}
                          data-nowrap={column.nowrap || undefined}
                        >
                          {column.render(row, rowIndex)}
                        </td>
                      )
                    )}
                  </tr>
                )
              })}

            {showEmpty && (
              <tr>
                <td className={classes.emptyCell} colSpan={Math.max(1, columns.length)}>
                  {empty ?? (
                    <EmptyState title={resolvedEmptyTitle} description={emptyDescription} headingLevel={4} compact />
                  )}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {loading && (
        <span className={classes.srOnly} role="status" aria-live="polite">
          {resolvedLoadingLabel}
        </span>
      )}
    </div>
  )
}

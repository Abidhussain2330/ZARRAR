import { Checkbox } from '@mantine/core'
import { ReactElement, ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { AdminSelection } from '@Hooks/admin/useAdminSelection'
import { DataTable, DataTableColumn, DataTableProps } from '@Components/buraq'
import classes from '@Styles/admin/AdminControls.module.css'

export interface AdminBulkBarProps {
  count: number
  onClear: () => void
  children: ReactNode
}

/**
 * Bulk-action strip shown once rows are selected.
 *
 * `role="status"` because selecting rows changes what the page's destructive
 * actions will act on, and that has to be announced, not just drawn.
 */
export const AdminBulkBar = ({ count, onClear, children }: AdminBulkBarProps): ReactElement => {
  const { t } = useTranslation()

  return (
    <div className={classes.bulkBar} role="status" aria-live="polite">
      <span className={classes.bulkCount}>
        {t('admin.table.selected', { defaultValue: '{{count}} selected', count })}
      </span>
      <div className={classes.bulkActions}>
        {children}
        <button type="button" className={classes.copyable} onClick={onClear}>
          {t('admin.table.clear_selection', { defaultValue: 'Clear selection' })}
        </button>
      </div>
    </div>
  )
}

export interface AdminTableProps<T, K> extends Omit<DataTableProps<T>, 'columns'> {
  columns: DataTableColumn<T>[]
  /** Enables the leading checkbox column. Omit for a read-only table. */
  selection?: AdminSelection<K>
  /** Required with `selection`: the selection key for a row. */
  selectionKey?: (row: T) => K
  /** Rows that cannot be selected — the signed-in operator's own account, say. */
  selectionDisabled?: (row: T) => boolean
  /** Accessible name for a row's checkbox. */
  selectionLabel?: (row: T) => string
}

/**
 * `DataTable` plus the admin bulk-selection column.
 *
 * The BURAQ `DataTable` already owns skeletons, the empty state, the sticky
 * header and internal horizontal scrolling — a table may scroll sideways, the
 * page may not. This adds only what administration needs on top of it, so the
 * two cannot drift apart.
 */
export const AdminTable = <T, K>({
  columns,
  selection,
  selectionKey,
  selectionDisabled,
  selectionLabel,
  rows,
  ...rest
}: AdminTableProps<T, K>): ReactElement => {
  const { t } = useTranslation()
  const selectableKeys = selectionKey
    ? rows.filter((row) => !selectionDisabled?.(row)).map(selectionKey)
    : []

  const withSelection: DataTableColumn<T>[] =
    selection && selectionKey
      ? [
          {
            key: '__select',
            width: '2.75rem',
            align: 'center',
            header: (
              <Checkbox
                size="xs"
                aria-label={t('admin.table.select_all', { defaultValue: 'Select all rows' })}
                checked={selection.allSelected(selectableKeys)}
                indeterminate={selection.someSelected(selectableKeys)}
                disabled={selectableKeys.length === 0}
                onChange={() => selection.toggleAll(selectableKeys)}
              />
            ),
            render: (row) => (
              <Checkbox
                size="xs"
                aria-label={
                  selectionLabel?.(row) ?? t('admin.table.select_row', { defaultValue: 'Select this row' })
                }
                checked={selection.isSelected(selectionKey(row))}
                disabled={selectionDisabled?.(row)}
                onChange={() => selection.toggle(selectionKey(row))}
              />
            ),
          },
          ...columns,
        ]
      : columns

  return <DataTable {...rest} rows={rows} columns={withSelection} />
}

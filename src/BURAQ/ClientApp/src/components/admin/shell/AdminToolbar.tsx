import { CloseButton, TextInput } from '@mantine/core'
import { mdiMagnify } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, FormEvent, PropsWithChildren, ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import classes from '@Styles/admin/AdminControls.module.css'

export interface AdminToolbarProps extends PropsWithChildren {
  /** Filter controls — selects, segmented controls. Rendered beside the search. */
  filters?: ReactNode
  /** Right-hand cluster: result count, pagination, view toggles. */
  trailing?: ReactNode
}

/**
 * The single filter row used above every admin collection.
 *
 * Search, filters and the result count sit in one wrapping flex row so the same
 * markup survives 1440, 834 and 360 without per-screen media queries.
 */
export const AdminToolbar: FC<AdminToolbarProps> = ({ children, filters, trailing }) => (
  <div className={classes.toolbar}>
    <div className={classes.toolbarLead}>
      {children}
      {filters}
    </div>
    {trailing && <div className={classes.toolbarTrail}>{trailing}</div>}
  </div>
)

export interface AdminSearchProps {
  value: string
  onChange: (value: string) => void
  /** Fired on Enter and on the search button. Omit for live filtering. */
  onSubmit?: () => void
  placeholder?: string
  label?: string
  disabled?: boolean
}

/**
 * Search box with an explicit accessible label and a clear button.
 *
 * Submitting is a form submit rather than a keydown handler, so Enter works the
 * way the browser and assistive technology already expect.
 */
export const AdminSearch: FC<AdminSearchProps> = ({ value, onChange, onSubmit, placeholder, label, disabled }) => {
  const { t } = useTranslation()
  const resolvedLabel = label ?? t('admin.toolbar.search', { defaultValue: 'Search' })

  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    onSubmit?.()
  }

  return (
    <form className={classes.search} onSubmit={handleSubmit} role="search">
      <TextInput
        aria-label={resolvedLabel}
        placeholder={placeholder ?? resolvedLabel}
        value={value}
        disabled={disabled}
        onChange={(event) => onChange(event.currentTarget.value)}
        leftSection={<Icon path={mdiMagnify} size={0.9} />}
        rightSection={
          value ? (
            <CloseButton
              size="sm"
              aria-label={t('admin.toolbar.clear_search', { defaultValue: 'Clear search' })}
              onClick={() => {
                onChange('')
                onSubmit?.()
              }}
            />
          ) : null
        }
      />
    </form>
  )
}

export interface AdminResultCountProps {
  /** Rows currently rendered. */
  shown: number
  /** Rows the server reports in total. Omit when the endpoint does not report one. */
  total?: number
  /** Set while a filter is applied, so "no results" can be distinguished from "empty". */
  filtered?: boolean
}

/** Live region: the count is how a screen-reader user knows a filter did anything. */
export const AdminResultCount: FC<AdminResultCountProps> = ({ shown, total, filtered }) => {
  const { t } = useTranslation()

  return (
    <p className={classes.count} role="status" aria-live="polite">
      <span className={classes.countValue}>{shown.toLocaleString()}</span>
      {total !== undefined && total > 0 && (
        <>
          {' / '}
          {total.toLocaleString()}
        </>
      )}{' '}
      {filtered
        ? t('admin.toolbar.count_filtered', { defaultValue: 'matching' })
        : t('admin.toolbar.count', { defaultValue: 'shown' })}
    </p>
  )
}

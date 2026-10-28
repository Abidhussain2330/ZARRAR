import { Tooltip } from '@mantine/core'
import { mdiAlertOutline, mdiRefresh } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, PropsWithChildren, ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import classes from '@Styles/admin/AdminControls.module.css'

/* ------------------------------------------------------------------ layout */

export interface AdminMetricGridProps extends PropsWithChildren {
  /** Labels the group of tiles for assistive technology. */
  label?: string
}

/** Auto-fitting tile row. Never demands a track wider than the viewport. */
export const AdminMetricGrid: FC<AdminMetricGridProps> = ({ children, label }) => (
  <div className={classes.metricGrid} role={label ? 'group' : undefined} aria-label={label}>
    {children}
  </div>
)

export interface AdminSectionProps extends PropsWithChildren {
  title: string
  description?: ReactNode
  actions?: ReactNode
}

/** A titled block inside a settings or editor screen. */
export const AdminSection: FC<AdminSectionProps> = ({ title, description, actions, children }) => (
  <section className={classes.section}>
    <div className={classes.sectionHead}>
      <div>
        <h2 className={classes.sectionTitle}>{title}</h2>
        {description && <p className={classes.sectionDescription}>{description}</p>}
      </div>
      {actions}
    </div>
    {children}
  </section>
)

export interface AdminFormGridProps extends PropsWithChildren {
  /** `2` widens the minimum track, for forms whose fields need more room. */
  columns?: 'auto' | '2'
}

/**
 * The responsive field grid every admin editor uses.
 *
 * Replaces the fixed `SimpleGrid cols={4}` / `Grid columns={3}` layouts, which
 * produced four 200px columns on a tablet and overflowed on a phone.
 */
export const AdminFormGrid: FC<AdminFormGridProps> = ({ children, columns = 'auto' }) => (
  <div className={classes.formGrid} data-columns={columns === '2' ? '2' : undefined}>
    {children}
  </div>
)

/** Spans every track of the enclosing `AdminFormGrid`. */
export const AdminFormWide: FC<PropsWithChildren> = ({ children }) => (
  <div className={classes.formWide}>{children}</div>
)

/* -------------------------------------------------------------- table cells */

export type AdminCellProps = PropsWithChildren

export const AdminCellStack: FC<AdminCellProps> = ({ children }) => (
  <div className={classes.cellStack}>{children}</div>
)

export const AdminCellIdentity: FC<AdminCellProps> = ({ children }) => (
  <div className={classes.cellIdentity}>{children}</div>
)

export const AdminCellName: FC<AdminCellProps & { title?: string }> = ({ children, title }) => (
  <span className={classes.cellName} title={title}>
    {children}
  </span>
)

export const AdminCellMeta: FC<AdminCellProps & { title?: string }> = ({ children, title }) => (
  <span className={classes.cellMeta} title={title}>
    {children}
  </span>
)

export const AdminCellActions: FC<AdminCellProps> = ({ children }) => (
  <div className={classes.cellActions}>{children}</div>
)

export interface AdminMonoValueProps {
  value?: string | null
  /** Fallback shown when the value is absent. */
  placeholder?: string
  wide?: boolean
  /** Copies `value` when the cell is activated. Renders as a button when set. */
  onCopy?: (value: string) => void
  copyLabel?: string
}

/**
 * A long technical value — GUID, hash, fingerprint, IP:port — clamped to one
 * line with the full text in `title`. Without the clamp a container GUID sets
 * the column width and pushes the row actions off-screen.
 */
export const AdminMonoValue: FC<AdminMonoValueProps> = ({ value, placeholder, wide, onCopy, copyLabel }) => {
  const { t } = useTranslation()
  const text = value ?? placeholder ?? '—'
  const className = wide ? `${classes.cellMono} ${classes.cellMonoWide}` : classes.cellMono

  if (!onCopy || !value) {
    return (
      <span className={className} title={value ?? undefined}>
        {text}
      </span>
    )
  }

  return (
    <Tooltip label={copyLabel ?? t('common.button.copy')} withArrow position="top-start">
      <button type="button" className={`${classes.copyable} ${className}`} onClick={() => onCopy(value)}>
        {text}
      </button>
    </Tooltip>
  )
}

/* ------------------------------------------------------------------ states */

export interface AdminOutageNoticeProps {
  message: ReactNode
  onRetry?: () => void
  retryLabel?: string
}

/**
 * Partial-service notice.
 *
 * Used where the page still has usable data but one dependency is degraded —
 * the log SignalR stream dropped, live submissions stopped arriving. Distinct
 * from `ErrorState`, which replaces the content entirely.
 */
export const AdminOutageNotice: FC<AdminOutageNoticeProps> = ({ message, onRetry, retryLabel }) => {
  const { t } = useTranslation()

  return (
    <div className={classes.outage} role="status" aria-live="polite">
      <span className={classes.outageIcon} aria-hidden>
        <Icon path={mdiAlertOutline} size={0.8} />
      </span>
      <p className={classes.outageText}>{message}</p>
      {onRetry && (
        <div className={classes.outageActions}>
          <button type="button" className={classes.copyable} onClick={onRetry}>
            <Icon path={mdiRefresh} size={0.7} aria-hidden /> {retryLabel ?? t('common.button.reset')}
          </button>
        </div>
      )}
    </div>
  )
}

export interface AdminPendingProps {
  label?: string
}

/** Inline "working…" marker for an action already in flight. */
export const AdminPending: FC<AdminPendingProps> = ({ label }) => {
  const { t } = useTranslation()

  return (
    <span className={classes.pending} role="status" aria-live="polite">
      {label ?? t('admin.state.pending', { defaultValue: 'Working…' })}
    </span>
  )
}

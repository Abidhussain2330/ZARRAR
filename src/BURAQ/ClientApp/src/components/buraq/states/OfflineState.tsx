import { mdiRefresh, mdiWifiOff } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { StatusDot } from '@Components/buraq/indicators/StatusDot'
import { BuraqHeadingLevel, StateShell } from '@Components/buraq/states/StateShell'
import classes from '@Styles/buraq/States.module.css'

export interface OfflineStateProps {
  /**
   * `block` replaces the content of a panel. `inline` is the strip that sits in
   * a header while the scoreboard socket is down and the data is still on screen.
   */
  variant?: 'block' | 'inline'
  title?: string
  description?: ReactNode
  /** Reconnection progress, e.g. 'retrying in 5s'. */
  status?: ReactNode
  onRetry?: () => void
  retryLabel?: string
  headingLevel?: BuraqHeadingLevel
  compact?: boolean
  className?: string
}

/**
 * Real-time disconnection notice.
 *
 * Polite rather than assertive: losing a socket is not worth interrupting a
 * screen-reader user mid-sentence, but they must still learn the figures on
 * screen have stopped updating.
 */
export const OfflineState: FC<OfflineStateProps> = ({
  variant = 'block',
  title,
  description,
  status,
  onRetry,
  retryLabel,
  headingLevel = 3,
  compact = false,
  className,
}) => {
  const { t } = useTranslation()
  const resolvedTitle = title ?? t('common.state.offline.title', { defaultValue: 'Connection lost' })
  const resolvedDescription =
    description ??
    t('common.state.offline.description', {
      defaultValue: 'Live updates are paused. The data below may be out of date.',
    })
  const resolvedRetryLabel = retryLabel ?? t('common.state.offline.retry', { defaultValue: 'Reconnect' })

  const retry = onRetry && (
    <button type="button" className={classes.action} onClick={onRetry}>
      <Icon path={mdiRefresh} size={0.75} aria-hidden />
      {resolvedRetryLabel}
    </button>
  )

  if (variant === 'inline') {
    return (
      <div
        className={[classes.offlineInline, className ?? ''].filter(Boolean).join(' ')}
        role="status"
        aria-live="polite"
      >
        <StatusDot status="offline" label={resolvedTitle} size="sm" />
        {status && <span className={classes.offlineStatus}>{status}</span>}
        {retry}
      </div>
    )
  }

  return (
    <StateShell
      icon={<Icon path={mdiWifiOff} size={1.6} />}
      title={resolvedTitle}
      description={resolvedDescription}
      detail={status}
      tone="warning"
      role="status"
      ariaLive="polite"
      headingLevel={headingLevel}
      action={retry}
      compact={compact}
      className={className}
    />
  )
}

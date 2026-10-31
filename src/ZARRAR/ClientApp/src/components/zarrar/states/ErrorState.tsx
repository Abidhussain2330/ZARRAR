import { mdiAlertOctagonOutline, mdiRefresh } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { ZarrarHeadingLevel, StateShell } from '@Components/zarrar/states/StateShell'
import classes from '@Styles/zarrar/States.module.css'

export interface ErrorStateProps {
  /** What went wrong, in the user's terms. */
  message: ReactNode
  title?: string
  /** Correlation id, status code or stack — shown in smaller technical type. */
  detail?: ReactNode
  onRetry?: () => void
  retryLabel?: string
  icon?: ReactNode
  headingLevel?: ZarrarHeadingLevel
  compact?: boolean
  className?: string
}

/**
 * `role="alert"` because an error usually replaces content the user was already
 * looking at, and they need to know without discovering it by tabbing.
 */
export const ErrorState: FC<ErrorStateProps> = ({
  message,
  title,
  detail,
  onRetry,
  retryLabel,
  icon,
  headingLevel = 3,
  compact = false,
  className,
}) => {
  const { t } = useTranslation()
  const resolvedTitle = title ?? t('common.state.error.title', { defaultValue: 'Something went wrong' })
  const resolvedRetryLabel = retryLabel ?? t('common.state.error.retry', { defaultValue: 'Try again' })

  return (
    <StateShell
      icon={icon ?? <Icon path={mdiAlertOctagonOutline} size={1.6} />}
      title={resolvedTitle}
      description={message}
      detail={detail}
      tone="critical"
      role="alert"
      headingLevel={headingLevel}
      compact={compact}
      className={className}
      action={
        onRetry && (
          <button type="button" className={classes.action} onClick={onRetry}>
            <Icon path={mdiRefresh} size={0.75} aria-hidden />
            {resolvedRetryLabel}
          </button>
        )
      }
    />
  )
}

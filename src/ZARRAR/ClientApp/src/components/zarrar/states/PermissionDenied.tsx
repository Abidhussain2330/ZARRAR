import { mdiShieldLockOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { ZarrarHeadingLevel, StateShell } from '@Components/zarrar/states/StateShell'

export interface PermissionDeniedProps {
  title?: string
  description?: ReactNode
  /** The role or capability the view needs, shown as technical detail. */
  required?: ReactNode
  /** Usually a link back to somewhere the user can reach. */
  action?: ReactNode
  icon?: ReactNode
  headingLevel?: ZarrarHeadingLevel
  compact?: boolean
  className?: string
}

/**
 * No live region: this is rendered on navigation, where the heading already
 * carries the announcement, and an alert would fight the route change.
 */
export const PermissionDenied: FC<PermissionDeniedProps> = ({
  title,
  description,
  required,
  action,
  icon,
  headingLevel = 2,
  compact = false,
  className,
}) => {
  const { t } = useTranslation()
  const resolvedTitle = title ?? t('common.state.permission_denied.title', { defaultValue: 'Access denied' })
  const resolvedDescription =
    description ??
    t('common.state.permission_denied.description', {
      defaultValue: 'Your account does not have permission to view this page.',
    })

  return (
    <StateShell
      icon={icon ?? <Icon path={mdiShieldLockOutline} size={1.6} />}
      title={resolvedTitle}
      description={resolvedDescription}
      detail={required}
      tone="warning"
      headingLevel={headingLevel}
      action={action}
      compact={compact}
      className={className}
    />
  )
}

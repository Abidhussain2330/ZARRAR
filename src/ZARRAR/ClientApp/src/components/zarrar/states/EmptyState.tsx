import { mdiInboxOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, ReactNode } from 'react'
import { ZarrarHeadingLevel, StateShell } from '@Components/zarrar/states/StateShell'

export interface EmptyStateProps {
  title: string
  description?: ReactNode
  /** Defaults to an outlined inbox. Pass an `<Icon>` or any node. */
  icon?: ReactNode
  /** A button or link that gets the user out of the empty state. */
  action?: ReactNode
  headingLevel?: ZarrarHeadingLevel
  /** Tightens the padding for use inside a table cell or a small panel. */
  compact?: boolean
  className?: string
}

export const EmptyState: FC<EmptyStateProps> = ({
  title,
  description,
  icon,
  action,
  headingLevel = 3,
  compact = false,
  className,
}) => (
  <StateShell
    icon={icon ?? <Icon path={mdiInboxOutline} size={1.6} />}
    title={title}
    description={description}
    action={action}
    headingLevel={headingLevel}
    compact={compact}
    className={className}
  />
)
